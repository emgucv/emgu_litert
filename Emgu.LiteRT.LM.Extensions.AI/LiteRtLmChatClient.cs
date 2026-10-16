//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using LmChatMessage = Emgu.LiteRT.LM.Models.ChatMessage;

namespace Emgu.LiteRT.LM.Extensions.AI
{
    /// <summary>
    /// An IChatClient (Microsoft.Extensions.AI) backed by a LiteRT-LM language model running on the device, so the model
    /// can be used by code written against Microsoft.Extensions.AI and Microsoft Agent Framework: tool calling through
    /// ChatClientBuilder.UseFunctionInvocation(), agents (ChatClientAgent) and so on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IChatClient is stateless (each request carries the whole conversation) while a LiteRT-LM Conversation keeps its
    /// history. For models that support multi-turn conversations (LanguageModel.SupportsMultiTurnConversation), the
    /// client keeps one Conversation open and, when a request only adds a message to the messages already in it,
    /// sends just the new message(s), so earlier turns aren't processed again. Otherwise it creates a new Conversation
    /// seeded with the earlier messages. Changing the system message, the tools or the other options also starts a new
    /// Conversation.
    /// </para>
    /// <para>
    /// Supported content: text, images and audio (DataContent, for models that accept them), tool calls and tool
    /// results (FunctionCallContent / FunctionResultContent, for models with LanguageModel.SupportsToolCalling).
    /// Tool results containing AIContent or IEnumerable&lt;AIContent&gt; can return image/audio DataContent for the model.
    /// Thinking is reported as TextReasoningContent; ChatOptions.Reasoning turns it on (any effort) or off
    /// (ReasoningEffort.None) for models that can think. ChatOptions.MaxOutputTokens is supported; the other sampling
    /// options are ignored. One request runs at a time; dispose the client before the model, or let the model's
    /// EngineReleasing event release the open Conversation.
    /// </para>
    /// </remarks>
    public class LiteRtLmChatClient : IChatClient
    {
        private readonly LanguageModel _model;
        private readonly ChatClientMetadata _metadata;
        private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);
        // The function name of each tool call this client reported, by call id: LiteRT-LM's tool responses are
        // matched to calls by name, and FunctionResultContent only carries the call id.
        private readonly ConcurrentDictionary<String, String> _callNames = new ConcurrentDictionary<String, String>();
        private OpenConversation _open;
        private bool _disposed;

        /// <summary>
        /// Create a chat client for an initialized language model.
        /// </summary>
        /// <param name="model">The model; LanguageModel.Init must have completed</param>
        public LiteRtLmChatClient(LanguageModel model)
        {
            if (model == null)
                throw new ArgumentNullException("model");
            if (!model.Initialized)
                throw new InvalidOperationException("The model is not initialized; call Init first");
            _model = model;
            _metadata = new ChatClientMetadata("litert-lm", null, Path.GetFileName(model.ModelPath));
            _model.EngineReleasing += OnEngineReleasing;
        }

        /// <summary>Budget each tool round and rebuild low-capacity native caches without completed reasoning.
        /// Old completed exchanges may be removed; the latest user request and all its tool calls/results are retained.
        /// Configure before use. Off by default for compatibility.</summary>
        public bool EnableContextManagement { get; set; }
        /// <summary>Maximum reasoning tokens per round when context management is enabled. Thinking stays enabled.</summary>
        public int MaxThinkingTokens { get; set; } = 256;
        /// <summary>Raised at native send boundaries; contains counts only. Observers must marshal UI work.</summary>
        public event Action<LiteRtLmContextInfo> ContextChanged;

        /// <summary>
        /// Whether to constrain generation when tools are given, so the model can only produce well-formed calls to the
        /// given tools (or plain text): LiteRT-LM's constrained decoding, using its Gemma model constraint provider
        /// (libGemmaModelConstraintProvider). On by default. Only applied with LiteRT-LM v0.18.0 or later
        /// (LiteRtLmInvoke.IsV018OrLater): v0.17.1's prebuilt constraint provider crashes the process, so with an older
        /// liblitert-lm the tools are used without constraints.
        /// </summary>
        public bool EnableConstrainedDecoding { get; set; } = true;

        /// <summary>
        /// The language model this client runs
        /// </summary>
        public LanguageModel Model
        {
            get { return _model; }
        }

        /// <summary>
        /// Send the messages and wait for the reply.
        /// </summary>
        /// <param name="messages">The conversation so far, ending with the new message(s)</param>
        /// <param name="options">The options, or null</param>
        /// <param name="cancellationToken">Cancels the generation</param>
        /// <returns>The reply</returns>
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<AIChatMessage> messages,
            ChatOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
        }

        /// <summary>
        /// Send the messages and stream the reply as it is generated.
        /// </summary>
        /// <param name="messages">The conversation so far, ending with the new message(s)</param>
        /// <param name="options">The options, or null</param>
        /// <param name="cancellationToken">Cancels the generation</param>
        /// <returns>The reply, as updates</returns>
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AIChatMessage> messages,
            ChatOptions options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default(CancellationToken))
        {
            if (messages == null)
                throw new ArgumentNullException("messages");
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            List<AIChatMessage> original = messages.ToList();
            List<AIChatMessage> all = ExpandToolMedia(original);
            String systemMessage = GetSystemMessage(all, options);
            List<AIChatMessage> turns = all.Where(m => m.Role != ChatRole.System).ToList();
            if (turns.Count == 0)
                throw new ArgumentException("No message to send", "messages");
            String toolsJson = GetToolsJson(options);
            bool? enableThinking = GetEnableThinking(options);
            int maxOutputTokens = options != null && options.MaxOutputTokens.HasValue ? options.MaxOutputTokens.Value : 0;
            if (EnableContextManagement && enableThinking == true && MaxThinkingTokens <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxThinkingTokens), "Thinking needs a positive token budget.");
            bool constrained = EnableConstrainedDecoding && toolsJson != null && LiteRtLmInvoke.IsV018OrLater;
            String settingsKey = String.Join("\u0001", new[] { systemMessage ?? "", toolsJson ?? "", enableThinking.ToString(), maxOutputTokens.ToString(), constrained.ToString(), EnableContextManagement.ToString(), MaxThinkingTokens.ToString() });
            List<String> keys = turns.Select(GetMessageKey).ToList();

            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            OpenConversation conversation = null;
            bool retainConversation = false;
            try
            {
                // Reuse an unchanged prefix. v0.18 also accepts a batch of appended messages: a media tool result
                // needs both its acknowledgement and an image turn, without re-reading the whole conversation.
                conversation = _open;
                if (conversation != null
                    && (conversation.SettingsKey != settingsKey
                        || keys.Count <= conversation.MessageKeys.Count
                        || (!LiteRtLmInvoke.IsV018OrLater && keys.Count != conversation.MessageKeys.Count + 1)
                        || !conversation.MessageKeys.SequenceEqual(keys.Take(conversation.MessageKeys.Count))))
                {
                    CloseConversation();
                    conversation = null;
                }
                int inputEstimate = 0, outputBudget = maxOutputTokens, thinkingBudget = 0;
                bool compacted = false;
                if (EnableContextManagement && conversation != null)
                {
                    int cached = CachedTokens(conversation);
                    int pending = turns.Skip(conversation.MessageKeys.Count).Sum(EstimateMessageTokens);
                    if (cached < 0 || cached + pending + Math.Min(maxOutputTokens > 0 ? maxOutputTokens : 256, 256) + 64 > ContextCapacity)
                    {
                        CloseConversation(); conversation = null; compacted = true;
                    }
                }
                while (conversation == null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<AIChatMessage> history = turns.Take(turns.Count - 1).ToList();
                    conversation = await Task.Run(() => CreateConversation(
                        history, systemMessage, toolsJson, constrained, enableThinking, maxOutputTokens)).ConfigureAwait(false);
                    conversation.SettingsKey = settingsKey;
                    conversation.MessageKeys.AddRange(keys.Take(keys.Count - 1));
                    if (!EnableContextManagement) break;
                    // Rendered native template includes actual tool schemas, instructions and retained history.
                    inputEstimate = _model.CountTokens(conversation.Conversation.RenderPrefaceToString()) +
                        history.Sum(m => m.Contents.OfType<DataContent>().Sum(EstimateMediaTokens)) + EstimateMessageTokens(turns.Last());
                    if (inputEstimate + Math.Min(maxOutputTokens > 0 ? maxOutputTokens : 64, 64) + 64 <= ContextCapacity) break;
                    ReportContext("compact", conversation, inputEstimate, 0, 0, completion: "input_too_large");
                    conversation.Dispose(); conversation = null;
                    // Remove only complete older exchanges, never current tool calls/results or media expansions.
                    if (!TrimOldestExchange(original))
                        throw new LiteRtLmContextException("This request and its tools do not fit in the current context window. Start a new chat, reduce attachments/tools, or choose a larger context window in Settings.");
                    turns = ExpandToolMedia(original).Where(m => m.Role != ChatRole.System).ToList();
                    keys = turns.Select(GetMessageKey).ToList(); compacted = true;
                }
                if (EnableContextManagement)
                {
                    if (inputEstimate == 0)
                        inputEstimate = CachedTokens(conversation) + turns.Skip(conversation.MessageKeys.Count).Sum(EstimateMessageTokens);
                    int available = ContextCapacity - inputEstimate - 64;
                    outputBudget = Math.Min(maxOutputTokens > 0 ? maxOutputTokens : 1024, available);
                    if (outputBudget <= 0) throw new LiteRtLmContextException("Too little space remains to answer. Start a new chat or choose a larger context window in Settings.");
                    thinkingBudget = enableThinking == true ? Math.Max(1, Math.Min(MaxThinkingTokens, outputBudget / 3)) : 0;
                }
                var pendingMessages = turns.Skip(conversation.MessageKeys.Count).Select(ToLiteRtLmJson).ToList();
                String messageJson = pendingMessages.Count == 1 ? pendingMessages[0] : "[" + String.Join(",", pendingMessages) + "]";
                ReportContext(compacted ? "compact" : "prepare", conversation, inputEstimate, outputBudget, thinkingBudget);
                String responseId = Guid.NewGuid().ToString("N");
                String messageId = Guid.NewGuid().ToString("N");
                DateTimeOffset createdAt = DateTimeOffset.UtcNow;

                // Bridge the native streaming callback (a LiteRT-LM thread) to this async enumerator.
                // The linked source also cancels the generation if the caller stops enumerating early.
                ConcurrentQueue<String> chunks = new ConcurrentQueue<String>();
                SemaphoreSlim chunkReady = new SemaphoreSlim(0);
                using var generation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var optional = EnableContextManagement ? new ConversationOptionalArgs { MaxOutputTokens = outputBudget } : null;
                using var roundThinking = EnableContextManagement && enableThinking == true ? new ThinkingConfig { EnableThinking = true, ThinkingTokenBudget = thinkingBudget } : null;
                if (optional != null && roundThinking != null) optional.ThinkingConfig = roundThinking;
                Task<String[]> send = conversation.Conversation.SendMessageStreamAsync(
                    messageJson,
                    chunk => { chunks.Enqueue(chunk); chunkReady.Release(); },
                    optional, null, generation.Token);
                Task signalCompletion = send.ContinueWith(t => chunkReady.Release(), TaskScheduler.Default);

                ThinkSplitter splitter = new ThinkSplitter();
                StringBuilder replyText = new StringBuilder();
                List<FunctionCallContent> calls = new List<FunctionCallContent>();
                bool succeeded = false;
                try
                {
                    while (true)
                    {
                        await chunkReady.WaitAsync().ConfigureAwait(false);
                        String chunk;
                        while (chunks.TryDequeue(out chunk))
                        {
                            List<AIContent> contents = ParseChunk(chunk, splitter, replyText, calls);
                            if (contents.Count > 0)
                                yield return CreateUpdate(contents, responseId, messageId, createdAt);
                        }
                        if (send.IsCompleted && chunks.IsEmpty)
                            break;
                    }
                    // Throws if the generation failed or was cancelled.
                    await CompleteSendAsync(send).ConfigureAwait(false);

                    List<AIContent> rest = splitter.Flush(replyText);
                    ChatResponseUpdate last = CreateUpdate(rest, responseId, messageId, createdAt);
                    int cachedAfter = CachedTokens(conversation);
                    int decoded = -1;
                    try { var benchmark = conversation.Conversation.GetBenchmarkInfo();
                        if (benchmark?.DecodeTurns.Length > 0) decoded = benchmark.DecodeTurns.Last().TokenCount; } catch { }
                    conversation.LastDecodedTokens = decoded;
                    bool atLimit = cachedAfter >= ContextCapacity - 1 || (outputBudget > 0 && decoded >= outputBudget);
                    bool empty = calls.Count == 0 && replyText.Length == 0;
                    last.FinishReason = calls.Count > 0 ? ChatFinishReason.ToolCalls : atLimit ? ChatFinishReason.Length : ChatFinishReason.Stop;
                    ReportContext("complete", conversation, inputEstimate, outputBudget, thinkingBudget, calls.Count,
                        atLimit ? "length" : empty ? "empty" : calls.Count > 0 ? "tool_calls" : "stop");
                    if (EnableContextManagement && empty)
                        throw new LiteRtLmContextException("The model stopped before answering or completing the tools. Try a larger context window in Settings or a shorter request.");
                    succeeded = true;

                    // The conversation now also holds this message and the reply.
                    if (_model.SupportsMultiTurnConversation)
                    {
                        conversation.MessageKeys.AddRange(keys.Skip(conversation.MessageKeys.Count));
                        conversation.MessageKeys.Add(GetAssistantKey(replyText.ToString(), calls));
                        _open = conversation;
                        retainConversation = true;
                    }
                    else
                    {
                        conversation.Dispose();
                    }
                    yield return last;
                }
                finally
                {
                    if (!succeeded)
                    {
                        ReportContext("failed", conversation, inputEstimate, outputBudget, thinkingBudget, calls.Count, "error_or_cancelled");
                        // LiteRT-LM doesn't support reusing a conversation after a failed or cancelled send. Stop the
                        // generation and wait for the native call to finish before releasing it.
                        generation.Cancel();
                        try { signalCompletion.Wait(); } catch { }
                        if (_open == conversation)
                            _open = null;
                        conversation.Dispose();
                    }
                }
            }
            finally
            {
                if (!retainConversation && conversation != null)
                {
                    if (ReferenceEquals(_open, conversation)) _open = null;
                    conversation.Dispose();
                }
                _requestLock.Release();
            }
        }

        // Context maintenance happens only before a native send. Calls/results already executed by the
        // function-invoking layer stay in the supplied history; rebuilding never invokes host tools.
        private async Task CompleteSendAsync(Task<string[]> send)
        {
            try { await send.ConfigureAwait(false); }
            catch (LiteRtLmException ex) when (EnableContextManagement &&
                (ex.Message.IndexOf("exceeds available state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 ex.Message.IndexOf("too long", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 ex.Message.IndexOf("context window out of bounds", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                throw new LiteRtLmContextException("The model ran out of context while processing the current tools. Completed actions were not replayed. Start a new chat or choose a larger context window in Settings.", ex);
            }
        }

        internal static bool TrimOldestExchange(List<AIChatMessage> messages)
        {
            int first = messages.FindIndex(m => m.Role == ChatRole.User);
            if (first < 0) return false;
            int next = messages.FindIndex(first + 1, m => m.Role == ChatRole.User);
            if (next < 0) return false;
            messages.RemoveRange(0, next);
            return true;
        }

        private int EstimateMessageTokens(AIChatMessage message)
        {
            // Tokenize payload text, not JSON transport escaping (which can multiply catalog sizes).
            int tokens = 24;
            foreach (var content in message.Contents)
            {
                if (content is TextContent text) tokens += _model.CountTokens(text.Text ?? String.Empty);
                else if (content is FunctionResultContent result)
                {
                    object value = result.Exception != null ? "Error: " + result.Exception.Message : result.Result;
                    string payload = value is JsonElement element ? element.ToString() : value is string str ? str :
                        value == null ? "null" : JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value.GetType()));
                    tokens += _model.CountTokens(payload) + 16;
                }
                else if (content is FunctionCallContent call)
                    tokens += _model.CountTokens(ToLiteRtLmJson(new AIChatMessage(ChatRole.Assistant, new AIContent[] { call }))) + 16;
                else if (content is DataContent media)
                    tokens += EstimateMediaTokens(media);
            }
            return tokens;
        }
        private static int EstimateMediaTokens(DataContent media)
        {
            if (media.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return 300;
            var data = media.Data.Span;
            if (data.Length >= 44 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')
            {
                int byteRate = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.Slice(28, 4));
                if (byteRate > 0) return (int)Math.Min(int.MaxValue, Math.Ceiling((data.Length - 44) * 25.0 / byteRate) + 64);
            }
            return 1600; // Conservatively covers the demo's bounded recordings; unknown compressed audio may need more.
        }

        private int ContextCapacity => _model.Engine?.ConfiguredContextTokens > 0
            ? _model.Engine.ConfiguredContextTokens : 4096;
        private int CachedTokens(OpenConversation conversation)
        { try { return conversation.Conversation.TokenCount; } catch { return -1; } }
        private void ReportContext(string stage, OpenConversation conversation, int input, int output, int thinking, int calls = 0, string completion = null)
        {
            var info = new LiteRtLmContextInfo { Stage = stage, Capacity = ContextCapacity,
                CachedTokens = conversation == null ? -1 : CachedTokens(conversation), InputTokens = input,
                OutputBudget = output, ThinkingBudget = thinking, ToolCalls = calls, Completion = completion,
                DecodedTokens = conversation?.LastDecodedTokens ?? -1 };
            System.Diagnostics.Trace.WriteLine("LiteRT context " + info);
            try { ContextChanged?.Invoke(info); } catch { /* Diagnostics must not interrupt native cleanup. */ }
        }

        /// <summary>
        /// Get a service: the ChatClientMetadata, the LanguageModel, or this client.
        /// </summary>
        /// <param name="serviceType">The type of the service</param>
        /// <param name="serviceKey">The service key; only null is supported</param>
        /// <returns>The service, or null</returns>
        public object GetService(Type serviceType, object serviceKey = null)
        {
            if (serviceType == null)
                throw new ArgumentNullException("serviceType");
            if (serviceKey != null)
                return null;
            if (serviceType == typeof(ChatClientMetadata))
                return _metadata;
            if (serviceType.IsInstanceOfType(_model))
                return _model;
            if (serviceType.IsInstanceOfType(this))
                return this;
            return null;
        }

        /// <summary>
        /// Release the open LiteRT-LM Conversation. The model is not disposed.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _model.EngineReleasing -= OnEngineReleasing;
            CloseConversation();
        }

        private void OnEngineReleasing(object sender, EventArgs e)
        {
            CloseConversation();
        }

        private void CloseConversation()
        {
            OpenConversation open = _open;
            _open = null;
            if (open != null)
                open.Dispose();
        }

        // ---------- Conversations ----------

        private OpenConversation CreateConversation(
            List<AIChatMessage> history, String systemMessage, String toolsJson, bool constrained, bool? enableThinking, int maxOutputTokens)
        {
            Engine engine = _model.Engine;
            if (engine == null)
                throw new InvalidOperationException("The model's engine has been released");
            OpenConversation open = new OpenConversation();
            try
            {
                open.Config = new ConversationConfig();
                if (maxOutputTokens > 0)
                {
                    open.SessionConfig = new SessionConfig();
                    open.SessionConfig.MaxOutputTokens = maxOutputTokens;
                    open.Config.SessionConfig = open.SessionConfig;
                }
                if (enableThinking.HasValue)
                {
                    open.ThinkingConfig = new ThinkingConfig();
                    open.ThinkingConfig.EnableThinking = enableThinking.Value;
                    open.Config.ThinkingConfig = open.ThinkingConfig;
                }
                if (!String.IsNullOrEmpty(systemMessage))
                    open.Config.SystemMessage = systemMessage;
                else if (EnableContextManagement)
                    // Gemma's preface renderer needs a message even before the first user turn.
                    open.Config.SystemMessage = "\"\"";
                if (toolsJson != null)
                    open.Config.ToolsJson = toolsJson;
                if (constrained)
                    open.Config.EnableConstrainedDecoding = true;
                if (history.Count > 0)
                {
                    open.Config.MessagesJson = "[" + String.Join(",", history.Select(ToLiteRtLmJson)) + "]";
                    // LiteRT-LM v0.18.0 drops the images and audio of the initial messages when it re-reads them to
                    // remove the previous reply's thinking from the KV cache, and the next message fails ("Provided
                    // less images than expected in the prompt"): keep the thinking in the KV cache instead.
                    if (history.Any(m => m.Contents.OfType<DataContent>().Any()))
                        open.Config.FilterChannelContentFromKvCache = false;
                }
                open.Conversation = engine.CreateConversation(open.Config);
                return open;
            }
            catch
            {
                open.Dispose();
                throw;
            }
        }

        /// <summary>
        /// A LiteRT-LM conversation, its native settings, and the keys of the messages it holds.
        /// </summary>
        private class OpenConversation : IDisposable
        {
            public SessionConfig SessionConfig;
            public ThinkingConfig ThinkingConfig;
            public ConversationConfig Config;
            public Conversation Conversation;
            public String SettingsKey;
            public int LastDecodedTokens = -1;
            public readonly List<String> MessageKeys = new List<String>();

            public void Dispose()
            {
                Conversation?.Dispose(); Conversation = null;
                Config?.Dispose(); Config = null;
                ThinkingConfig?.Dispose(); ThinkingConfig = null;
                SessionConfig?.Dispose(); SessionConfig = null;
            }
        }

        // ---------- Options ----------

        private static String GetSystemMessage(List<AIChatMessage> messages, ChatOptions options)
        {
            List<String> parts = new List<String>();
            if (options != null && !String.IsNullOrEmpty(options.Instructions))
                parts.Add(options.Instructions);
            foreach (AIChatMessage message in messages)
                if (message.Role == ChatRole.System && !String.IsNullOrEmpty(message.Text))
                    parts.Add(message.Text);
            return parts.Count > 0 ? String.Join("\n\n", parts) : null;
        }

        // The tools in LiteRT-LM's format: [{"type": "function", "function": {"name", "description", "parameters"}}].
        private String GetToolsJson(ChatOptions options)
        {
            if (options == null || options.Tools == null || options.Tools.Count == 0 || options.ToolMode is NoneChatToolMode)
                return null;
            if (!_model.SupportsToolCalling)
                throw new NotSupportedException(String.Format("{0} doesn't support tool calling", _model.GetType().Name));

            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();
                    foreach (AITool tool in options.Tools)
                    {
                        AIFunctionDeclaration function = tool as AIFunctionDeclaration;
                        if (function == null)
                            throw new NotSupportedException(String.Format(
                                "Tool {0} ({1}) is not supported: only functions (AIFunctionDeclaration) run on the device",
                                tool.Name, tool.GetType().Name));
                        writer.WriteStartObject();
                        writer.WriteString("type", "function");
                        writer.WritePropertyName("function");
                        writer.WriteStartObject();
                        writer.WriteString("name", function.Name);
                        if (!String.IsNullOrEmpty(function.Description))
                            writer.WriteString("description", function.Description);
                        writer.WritePropertyName("parameters");
                        function.JsonSchema.WriteTo(writer);
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static bool? GetEnableThinking(ChatOptions options)
        {
            if (options == null || options.Reasoning == null || !options.Reasoning.Effort.HasValue)
                return null;
            return options.Reasoning.Effort.Value != ReasoningEffort.None;
        }

        // ---------- Messages to LiteRT-LM ----------

        // Gemma's tool-response template renders response text, not images. Keep the tool acknowledgement and
        // follow it with a user media turn, so the native preprocessor actually receives the bytes.
        // Expand the complete history deterministically on every request, including restored agent sessions.
        internal static List<AIChatMessage> ExpandToolMedia(IEnumerable<AIChatMessage> messages)
        {
            var expanded = new List<AIChatMessage>();
            foreach (AIChatMessage message in messages)
            {
                var contents = new List<AIContent>();
                var media = new List<AIContent>();
                foreach (AIContent content in message.Contents)
                {
                    if (content is FunctionResultContent result && result.Exception == null &&
                        (result.Result is AIContent || result.Result is IEnumerable<AIContent>))
                    {
                        var items = result.Result is AIContent single ? new[] { single } : ((IEnumerable<AIContent>)result.Result).ToArray();
                        string text = String.Join("\n", items.OfType<TextContent>().Select(t => t.Text));
                        var data = items.OfType<DataContent>().ToList();
                        contents.Add(new FunctionResultContent(result.CallId,
                            text + (data.Count > 0 ? "\nThe captured media follows in the next message. Answer the user's question using it." : "")));
                        if (data.Count > 0)
                        {
                            media.Add(new TextContent(text));
                            media.AddRange(data);
                        }
                    }
                    else contents.Add(content);
                }
                expanded.Add(new AIChatMessage(message.Role, contents));
                if (media.Count > 0) expanded.Add(new AIChatMessage(ChatRole.User, media));
            }
            return expanded;
        }

        // A message in LiteRT-LM's format, e.g. {"role": "user", "content": [{"type": "text", "text": "Hello"}]}.
        private String ToLiteRtLmJson(AIChatMessage message)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", message.Role.Value);
                    writer.WritePropertyName("content");
                    writer.WriteStartArray();
                    foreach (AIContent content in message.Contents)
                    {
                        TextContent text = content as TextContent;
                        DataContent data = content as DataContent;
                        FunctionResultContent result = content as FunctionResultContent;
                        if (text != null)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("type", "text");
                            writer.WriteString("text", text.Text ?? String.Empty);
                            writer.WriteEndObject();
                        }
                        else if (data != null)
                        {
                            WriteData(writer, data);
                        }
                        else if (result != null)
                        {
                            // The tool response format Gemma 4's chat template reads (the one LiteRT-LM's Swift API
                            // sends): {"type": "tool_response", "name": ..., "response": ...}.
                            String name;
                            if (!_callNames.TryGetValue(result.CallId ?? String.Empty, out name))
                                throw new InvalidOperationException(String.Format(
                                    "Unknown tool call id {0}: the result must answer a call made through this client", result.CallId));
                            writer.WriteStartObject();
                            writer.WriteString("type", "tool_response");
                            writer.WriteString("name", name);
                            writer.WritePropertyName("response");
                            WriteValue(writer, result.Exception != null ? "Error: " + result.Exception.Message : result.Result);
                            writer.WriteEndObject();
                        }
                        // Reasoning, tool calls (below) and other content types are not sent.
                    }
                    writer.WriteEndArray();

                    List<FunctionCallContent> calls = message.Contents.OfType<FunctionCallContent>().ToList();
                    if (calls.Count > 0)
                    {
                        writer.WritePropertyName("tool_calls");
                        writer.WriteStartArray();
                        foreach (FunctionCallContent call in calls)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("type", "function");
                            writer.WritePropertyName("function");
                            writer.WriteStartObject();
                            writer.WriteString("name", call.Name);
                            writer.WritePropertyName("arguments");
                            writer.WriteStartObject();
                            if (call.Arguments != null)
                            {
                                foreach (KeyValuePair<String, object> argument in call.Arguments)
                                {
                                    writer.WritePropertyName(argument.Key);
                                    WriteValue(writer, argument.Value);
                                }
                            }
                            writer.WriteEndObject();
                            writer.WriteEndObject();
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private void WriteData(Utf8JsonWriter writer, DataContent data)
        {
            String type;
            if (data.HasTopLevelMediaType("image"))
            {
                if (!_model.SupportsImages)
                    throw new NotSupportedException(String.Format("{0} doesn't accept images", _model.GetType().Name));
                type = "image";
            }
            else if (data.HasTopLevelMediaType("audio"))
            {
                if (!_model.SupportsAudio)
                    throw new NotSupportedException(String.Format("{0} doesn't accept audio", _model.GetType().Name));
                type = "audio";
            }
            else
            {
                throw new NotSupportedException(String.Format("Data of type {0} is not supported", data.MediaType));
            }
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteString("blob", Convert.ToBase64String(data.Data.ToArray()));
            writer.WriteEndObject();
        }

        private static void WriteValue(Utf8JsonWriter writer, object value)
        {
            if (value == null)
                writer.WriteNullValue();
            else if (value is JsonElement)
                ((JsonElement)value).WriteTo(writer);
            else if (value is String)
                writer.WriteStringValue((String)value);
            else
                JsonSerializer.Serialize(writer, value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value.GetType()));
        }

        // ---------- Message keys ----------

        // What identifies a message when comparing a request with the messages already in the open conversation: the
        // role, the text (without thinking), data, tool calls and tool results.
        private static String GetMessageKey(AIChatMessage message)
        {
            StringBuilder key = new StringBuilder(message.Role.Value).Append('\u0001');
            foreach (AIContent content in message.Contents)
            {
                TextContent text = content as TextContent;
                DataContent data = content as DataContent;
                FunctionCallContent call = content as FunctionCallContent;
                FunctionResultContent result = content as FunctionResultContent;
                if (text != null)
                    key.Append(text.Text);
                else if (data != null)
                    key.Append("\u0002data:").Append(data.MediaType).Append(':').Append(data.Data.Length)
                        .Append(':').Append(GetHash(data.Data.Span)).Append('\u0002');
                else if (call != null)
                    key.Append(GetCallKey(call));
                else if (result != null)
                    key.Append("\u0002result:").Append(result.CallId).Append('\u0002');
            }
            return key.ToString();
        }

        private static String GetAssistantKey(String text, List<FunctionCallContent> calls)
        {
            StringBuilder key = new StringBuilder(ChatRole.Assistant.Value).Append('\u0001').Append(text);
            foreach (FunctionCallContent call in calls)
                key.Append(GetCallKey(call));
            return key.ToString();
        }

        private static String GetCallKey(FunctionCallContent call)
        {
            return "\u0002call:" + call.CallId + ":" + call.Name + "\u0002";
        }

        private static ulong GetHash(ReadOnlySpan<byte> data)
        {
            // FNV-1a
            ulong hash = 14695981039346656037UL;
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        // ---------- Replies from LiteRT-LM ----------

        private List<AIContent> ParseChunk(String chunkJson, ThinkSplitter splitter, StringBuilder replyText, List<FunctionCallContent> calls)
        {
            List<AIContent> contents = new List<AIContent>();
            // Thinking in a separate channel (e.g. Gemma 4).
            String reasoning = LmChatMessage.GetReasoning(chunkJson);
            if (reasoning.Length > 0)
                contents.Add(new TextReasoningContent(reasoning));
            // Text, which may contain a leading <think>...</think> block (e.g. Qwen3).
            String text = LmChatMessage.GetText(chunkJson);
            if (text.Length > 0)
                splitter.Add(text, contents, replyText);

            using (JsonDocument document = JsonDocument.Parse(chunkJson))
            {
                JsonElement toolCalls;
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("tool_calls", out toolCalls)
                    && toolCalls.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement toolCall in toolCalls.EnumerateArray())
                    {
                        JsonElement function;
                        JsonElement name;
                        if (!toolCall.TryGetProperty("function", out function)
                            || !function.TryGetProperty("name", out name)
                            || name.ValueKind != JsonValueKind.String)
                            continue;
                        Dictionary<String, object> arguments = new Dictionary<String, object>();
                        JsonElement argumentsElement;
                        if (function.TryGetProperty("arguments", out argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object)
                            foreach (JsonProperty argument in argumentsElement.EnumerateObject())
                                arguments[argument.Name] = argument.Value.Clone();
                        String callId = "call_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                        FunctionCallContent call = new FunctionCallContent(callId, name.GetString(), arguments);
                        _callNames[callId] = call.Name;
                        calls.Add(call);
                        contents.Add(call);
                    }
                }
            }
            return contents;
        }

        private ChatResponseUpdate CreateUpdate(List<AIContent> contents, String responseId, String messageId, DateTimeOffset createdAt)
        {
            return new ChatResponseUpdate(ChatRole.Assistant, contents)
            {
                ResponseId = responseId,
                MessageId = messageId,
                CreatedAt = createdAt,
                ModelId = _metadata.DefaultModelId
            };
        }

        /// <summary>
        /// Splits a leading &lt;think&gt;...&lt;/think&gt; block, streamed in pieces, off the text into
        /// TextReasoningContent. A piece that could be the start of a tag is held back until the next one shows what it
        /// is.
        /// </summary>
        private class ThinkSplitter
        {
            private const String StartTag = "<think>";
            private const String EndTag = "</think>";
            private enum State { Start, Thinking, Answer }
            private State _state = State.Start;
            private readonly StringBuilder _pending = new StringBuilder();

            public void Add(String text, List<AIContent> contents, StringBuilder replyText)
            {
                _pending.Append(text);
                while (true)
                {
                    String pending = _pending.ToString();
                    if (_state == State.Start)
                    {
                        String trimmed = pending.TrimStart();
                        if (trimmed.StartsWith(StartTag, StringComparison.Ordinal))
                        {
                            _pending.Clear().Append(trimmed.Substring(StartTag.Length));
                            _state = State.Thinking;
                            continue;
                        }
                        if (trimmed.Length == 0 || StartTag.StartsWith(trimmed, StringComparison.Ordinal))
                            return; // Not enough text yet to tell.
                        _state = State.Answer;
                        continue;
                    }
                    if (_state == State.Thinking)
                    {
                        int end = pending.IndexOf(EndTag, StringComparison.Ordinal);
                        if (end >= 0)
                        {
                            if (end > 0)
                                contents.Add(new TextReasoningContent(pending.Substring(0, end)));
                            _pending.Clear().Append(pending.Substring(end + EndTag.Length).TrimStart());
                            _state = State.Answer;
                            continue;
                        }
                        // Keep back what could be the start of the end tag.
                        int keep = PartialTagLength(pending, EndTag);
                        if (pending.Length > keep)
                        {
                            contents.Add(new TextReasoningContent(pending.Substring(0, pending.Length - keep)));
                            _pending.Clear().Append(pending.Substring(pending.Length - keep));
                        }
                        return;
                    }
                    // Answer
                    if (pending.Length > 0)
                    {
                        contents.Add(new TextContent(pending));
                        replyText.Append(pending);
                        _pending.Clear();
                    }
                    return;
                }
            }

            public List<AIContent> Flush(StringBuilder replyText)
            {
                List<AIContent> contents = new List<AIContent>();
                String pending = _pending.ToString();
                _pending.Clear();
                if (pending.Length == 0)
                    return contents;
                if (_state == State.Thinking)
                {
                    contents.Add(new TextReasoningContent(pending));
                }
                else
                {
                    contents.Add(new TextContent(pending));
                    replyText.Append(pending);
                }
                return contents;
            }

            private static int PartialTagLength(String text, String tag)
            {
                for (int length = Math.Min(tag.Length - 1, text.Length); length > 0; length--)
                    if (text.EndsWith(tag.Substring(0, length), StringComparison.Ordinal))
                        return length;
                return 0;
            }
        }
    }

    /// <summary>
    /// Microsoft.Extensions.AI extensions for the LiteRT-LM language models.
    /// </summary>
    public static class LanguageModelChatClientExtensions
    {
        /// <summary>
        /// Create an IChatClient (Microsoft.Extensions.AI) for an initialized language model.
        /// </summary>
        /// <param name="model">The model; LanguageModel.Init must have completed</param>
        /// <returns>The chat client; dispose it before the model</returns>
        public static IChatClient AsIChatClient(this LanguageModel model)
        {
            return new LiteRtLmChatClient(model);
        }
    }
}
