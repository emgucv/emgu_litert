//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Extensions.AI;
using Microsoft.Extensions.AI;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;
using ModelsChatReply = Emgu.LiteRT.LM.Models.ChatReply;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// How a conversation that has outgrown the model's context window makes room.
    /// </summary>
    public enum ChatCompaction
    {
        /// <summary>The model summarizes the earlier messages, and is given the summary instead (needs a model that summarizes well)</summary>
        Summarize,
        /// <summary>The earlier messages are dropped (for small models, whose summaries are poor)</summary>
        Drop
    }

    /// <summary>
    /// An estimate of how much of the model's context window a conversation uses.
    /// </summary>
    public sealed class ChatContextUsage
    {
        internal ChatContextUsage(int usedTokens, int windowTokens)
        {
            UsedTokens = usedTokens;
            WindowTokens = windowTokens;
        }

        /// <summary>The estimated tokens used: the messages the model sees, the summary and the overhead (instructions, tools).</summary>
        public int UsedTokens { get; }

        /// <summary>The size of the context window.</summary>
        public int WindowTokens { get; }

        /// <summary>The part used, 0 to 1.</summary>
        public double Fraction => WindowTokens > 0 ? Math.Min(1.0, UsedTokens / (double)WindowTokens) : 0;
    }

    /// <summary>
    /// A completed exchange: the user's message and the reply.
    /// </summary>
    public sealed class ChatExchangeEventArgs : EventArgs
    {
        internal ChatExchangeEventArgs(ChatMessageItem message, ChatMessageItem reply)
        {
            Message = message;
            Reply = reply;
        }

        /// <summary>The user's message.</summary>
        public ChatMessageItem Message { get; }

        /// <summary>The reply (done or interrupted).</summary>
        public ChatMessageItem Reply { get; }
    }

    /// <summary>
    /// The conversation was compacted.
    /// </summary>
    public sealed class ChatCompactedEventArgs : EventArgs
    {
        internal ChatCompactedEventArgs(int contextStart, string summary, bool summarized)
        {
            ContextStart = contextStart;
            Summary = summary;
            Summarized = summarized;
        }

        /// <summary>The index in <see cref="ChatConversation.Messages"/> of the first message the model still sees
        /// (<see cref="ChatConversation.Messages"/>' count if it sees none).</summary>
        public int ContextStart { get; }

        /// <summary>The summary of the messages before it (and of earlier summaries), or null if there is none.</summary>
        public string Summary { get; }

        /// <summary>True if the messages were summarized now, false if they were dropped.</summary>
        public bool Summarized { get; }
    }

    /// <summary>
    /// A chat's conversation, without UI, over any <see cref="IChatClient"/>: the messages (<see cref="Messages"/>, which a
    /// UI shows), sending a message and streaming the reply into a <see cref="ChatMessageItem"/> - its thinking, its text
    /// and its tool steps - and keeping the conversation within the model's context window. Before a message that would
    /// make the conversation too long, the earlier messages are summarized by the model (<see cref="ChatCompaction.Summarize"/>)
    /// or dropped, keeping the latest exchanges up to a quarter of the window; the model is then given the summary and the
    /// messages since, while <see cref="Messages"/> keeps everything. Sizes are estimates: text with
    /// <see cref="CountTokens"/>, plus measured costs of images, audio and tool calls.
    /// <para>
    /// Use it from one thread (the UI thread): <see cref="SendAsync"/> continues on the caller's synchronization context,
    /// so the messages change on it, and counts tokens on the thread pool.
    /// </para>
    /// </summary>
    public class ChatConversation
    {
        private const string SummaryIntro = "Here is a summary of our conversation so far:\n";
        private const string SummaryAcknowledgement = "Thanks, I have the summary and will continue from there.";
        private const string SummaryInstructions =
            "Summarize the conversation below between a user and an assistant, so the assistant can continue it " +
            "without seeing it again. Keep what may matter later: facts about the user, names, numbers, decisions, " +
            "code and file names, and open questions. Write short bullet points that are full sentences starting " +
            "with who is meant (\"The user ...\" or \"The assistant ...\"), at most 150 words, and write only the " +
            "summary.";

        private string _summary;
        private int _summaryTokens;
        private bool _busy;

        /// <summary>
        /// Create a conversation over a chat client.
        /// </summary>
        /// <param name="client">The chat client. For tools, give one that calls them (e.g. built with Microsoft.Extensions.AI's
        /// <c>UseFunctionInvocation()</c>); the tool calls then show as <see cref="ChatMessageItem.Steps"/>.</param>
        /// <param name="model">The on-device model the client runs, if any: its tokenizer counts tokens
        /// (<see cref="CountTokens"/>) and its <see cref="LanguageModel.MaxNumTokens"/> is the context window</param>
        public ChatConversation(IChatClient client, LanguageModel model = null)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            if (model != null)
            {
                CountTokens = model.CountTokens;
                if (model.MaxNumTokens > 0)
                    ContextWindow = model.MaxNumTokens;
            }
        }

        /// <summary>
        /// Create a conversation with an on-device model, through its <see cref="LiteRtLmChatClient"/> (without tool
        /// calling: for tools, build a client with function invocation and use the other constructor). The model may still
        /// be loading: its client is created with the first message, which needs it loaded (<see cref="LanguageModel.Init"/>).
        /// </summary>
        /// <param name="model">The model</param>
        public ChatConversation(LanguageModel model)
            : this(new ModelChatClient(model ?? throw new ArgumentNullException(nameof(model))), model)
        {
        }

        /// <summary>The chat client.</summary>
        public IChatClient Client { get; }

        /// <summary>Every message of the chat, in order, including those the model no longer sees.</summary>
        public ObservableCollection<ChatMessageItem> Messages { get; } = new ObservableCollection<ChatMessageItem>();

        /// <summary>The system instructions given with every message, or null.</summary>
        public string Instructions { get; set; }

        /// <summary>Counts the tokens of a text; called on the thread pool. Defaults to an estimate of about 3.5
        /// characters a token (for a server's model).</summary>
        public Func<string, int> CountTokens { get; set; } = ApproximateTokens;

        /// <summary>The size of the model's context window, in tokens.</summary>
        public int ContextWindow { get; set; } = 4096;

        /// <summary>How the conversation makes room when it outgrows the context window.</summary>
        public ChatCompaction Compaction { get; set; } = ChatCompaction.Summarize;

        /// <summary>Room left in the context window for the reply.</summary>
        public int ReplyReserveTokens { get; set; } = 1024;

        /// <summary>What takes room besides the messages: the chat template, and the instructions and tool definitions
        /// (about 900 tokens for an agent with a dozen tools).</summary>
        public int OverheadTokens { get; set; } = 50;

        /// <summary>The estimated size of a message on top of its text (role markers).</summary>
        public int MessageOverheadTokens { get; set; } = 8;

        /// <summary>The estimated size of an image (measured: Gemma 4 and Qwen3.5 VL, ~260).</summary>
        public int ImageTokens { get; set; } = 270;

        /// <summary>The estimated size of a second of audio (measured: Gemma 4, ~25).</summary>
        public int AudioTokensPerSecond { get; set; } = 25;

        /// <summary>The length assumed for audio that isn't a WAV file (MP3, FLAC...), whose length isn't read.</summary>
        public TimeSpan DefaultAudioLength { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>The estimated size of a tool call and its result, until the conversation is rebuilt.</summary>
        public int ToolStepTokens { get; set; } = 450;

        /// <summary>The longest summary, in tokens.</summary>
        public int SummaryMaxTokens { get; set; } = 400;

        /// <summary>Describes a tool call for its step line; defaults to "Calling {name}".</summary>
        public Func<FunctionCallContent, string> DescribeToolCall { get; set; } = call => "Calling " + call.Name;

        /// <summary>
        /// True if an error means the conversation didn't fit in the context window (then the earlier messages are dropped
        /// and the message sent once more). Defaults to LiteRT-LM's errors: "Input token ids are too long..." for a new
        /// conversation, "Prefill input length exceeds available state entries..." for a continued one.
        /// </summary>
        public Func<Exception, bool> IsContextFullError { get; set; } = DefaultIsContextFull;

        /// <summary>The index in <see cref="Messages"/> of the first message the model sees; the ones before it are covered
        /// by <see cref="Summary"/>, if any.</summary>
        public int ContextStart { get; private set; }

        /// <summary>The summary of the messages before <see cref="ContextStart"/>, or null.</summary>
        public string Summary => _summary;

        /// <summary>True while a message is being sent (or the conversation compacted).</summary>
        public bool IsBusy => _busy;

        /// <summary>Raised when an exchange is complete (the reply done or interrupted), e.g. to save it.</summary>
        public event EventHandler<ChatExchangeEventArgs> ExchangeCompleted;

        /// <summary>Raised when the conversation was compacted, e.g. to show where the model's memory starts and to save the summary.</summary>
        public event EventHandler<ChatCompactedEventArgs> Compacted;

        /// <summary>Raised when the conversation starts and stops summarizing earlier messages (true while it does), e.g. for a status line.</summary>
        public event EventHandler<bool> Summarizing;

        /// <summary>
        /// Send a message and stream the reply. The message and the reply are added to <see cref="Messages"/> straight away;
        /// the reply's text, thinking and steps change as it streams, on the caller's synchronization context.
        /// </summary>
        /// <param name="text">The message</param>
        /// <param name="attachments">Images and audio for models that take them, or null</param>
        /// <param name="options">Options for this message: tools, thinking (<see cref="ChatOptions.Reasoning"/>), the
        /// longest reply... <see cref="Instructions"/> is added to its instructions. Null for none.</param>
        /// <param name="context">Text given to the model before the message (e.g. document excerpts), not shown; or null</param>
        /// <param name="cancellationToken">Stops the reply: it keeps what was written so far (<see cref="ChatMessageState.Interrupted"/>)</param>
        /// <returns>The reply. If sending fails, the reply is marked <see cref="ChatMessageState.Failed"/> and the exception thrown.</returns>
        public async Task<ChatMessageItem> SendAsync(string text, IEnumerable<DataContent> attachments = null, ChatOptions options = null,
            string context = null, CancellationToken cancellationToken = default)
        {
            if (_busy)
                throw new InvalidOperationException("A message is already being sent.");
            _busy = true;
            ChatMessageItem message = new ChatMessageItem(ChatMessageRole.User, text, attachments, context);
            ChatMessageItem reply = new ChatMessageItem(ChatMessageRole.Assistant, string.Empty) { State = ChatMessageState.Streaming };
            try
            {
                Messages.Add(message);
                try
                {
                    await PrepareContextAsync(message, cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // Sent anyway; if it's too long, the context-full retry below drops the earlier messages.
                }
                Messages.Add(reply);
                ChatOptions requestOptions = WithInstructions(options);
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        await StreamReplyAsync(message, reply, requestOptions, cancellationToken);
                        reply.State = ChatMessageState.Done;
                        break;
                    }
                    catch (Exception) when (cancellationToken.IsCancellationRequested)
                    {
                        // Interrupted: the reply keeps what was written so far; the client is given it as text from now on
                        // (LiteRT-LM can't continue a cancelled conversation, and the client rebuilds it).
                        reply.ModelMessages = null;
                        reply.State = ChatMessageState.Interrupted;
                        break;
                    }
                    catch (Exception ex) when (attempt == 0 && IsContextFullError(ex))
                    {
                        // The estimate was short, so don't rely on it: drop the earlier messages (a summary made before
                        // stays) and try once more.
                        reply.Text = string.Empty;
                        reply.Thinking = string.Empty;
                        reply.ThinkingDuration = null;
                        reply.Steps.Clear();
                        await CompactAsync(ChatCompaction.Drop, 0, message, cancellationToken);
                    }
                }
                await CountReplyAsync(reply);
                ExchangeCompleted?.Invoke(this, new ChatExchangeEventArgs(message, reply));
                return reply;
            }
            catch (Exception ex)
            {
                reply.Error = ex.Message;
                reply.State = ChatMessageState.Failed;
                message.Excluded = reply.Excluded = true;
                throw;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Compact the conversation now: summarize (or, for <see cref="ChatCompaction.Drop"/>, drop) everything but the
        /// latest exchange.
        /// </summary>
        /// <param name="cancellationToken">Stops the summary; the messages are then dropped instead</param>
        /// <returns>False if there was nothing to compact</returns>
        public async Task<bool> CompactAsync(CancellationToken cancellationToken = default)
        {
            if (_busy)
                throw new InvalidOperationException("A message is being sent.");
            _busy = true;
            try
            {
                await CountTokensAsync(null);
                List<ChatMessageItem> items = ContextItems();
                int keep = items.Count >= 2 ? Cost(items[items.Count - 2]) + Cost(items[items.Count - 1]) : 0;
                return await CompactAsync(Compaction, keep, null, cancellationToken);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// The estimated part of the context window the conversation uses now, counting the messages not counted yet.
        /// </summary>
        public async Task<ChatContextUsage> GetContextUsageAsync()
        {
            await CountTokensAsync(null);
            return new ChatContextUsage(ContextTokens() + OverheadTokens, ContextWindow);
        }

        /// <summary>
        /// Replace the conversation with a saved one: its messages, and how it was compacted.
        /// </summary>
        /// <param name="messages">The messages, in order: user messages and replies, alternating</param>
        /// <param name="summary">The summary of the messages before <paramref name="contextStart"/>, or null</param>
        /// <param name="contextStart">The index of the first message the model saw (0 if the conversation was never compacted)</param>
        public void Load(IEnumerable<ChatMessageItem> messages, string summary = null, int contextStart = 0)
        {
            if (_busy)
                throw new InvalidOperationException("A message is being sent.");
            Messages.Clear();
            foreach (ChatMessageItem message in messages ?? Array.Empty<ChatMessageItem>())
            {
                message.ModelMessages = null;
                message.ExtraTokens = 0;
                Messages.Add(message);
            }
            _summary = summary;
            _summaryTokens = 0;
            ContextStart = Math.Max(0, Math.Min(contextStart, Messages.Count));
            MarkContextStart();
        }

        // The message the model's memory starts with, after a compaction (none before the first one).
        private void MarkContextStart()
        {
            for (int i = 0; i < Messages.Count; i++)
                Messages[i].StartsContext = i == ContextStart && ContextStart > 0;
        }

        /// <summary>
        /// Start again: no messages and no summary.
        /// </summary>
        public void Clear()
        {
            Load(null);
        }

        // ---------- Sending ----------

        // A model's LiteRtLmChatClient, created on first use: LiteRtLmChatClient needs the model loaded.
        private sealed class ModelChatClient : IChatClient
        {
            private readonly LanguageModel _model;
            private LiteRtLmChatClient _client;

            public ModelChatClient(LanguageModel model)
            {
                _model = model;
            }

            private IChatClient Client
            {
                get
                {
                    if (_client == null)
                        _client = new LiteRtLmChatClient(_model);
                    return _client;
                }
            }

            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null,
                CancellationToken cancellationToken = default)
                => Client.GetResponseAsync(messages, options, cancellationToken);

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
                ChatOptions options = null, CancellationToken cancellationToken = default)
                => Client.GetStreamingResponseAsync(messages, options, cancellationToken);

            public object GetService(Type serviceType, object serviceKey = null)
                => _client?.GetService(serviceType, serviceKey) ?? (serviceType.IsInstanceOfType(this) ? this : null);

            public void Dispose()
            {
                _client?.Dispose();
                _client = null;
            }
        }

        private ChatOptions WithInstructions(ChatOptions options)
        {
            if (string.IsNullOrEmpty(Instructions))
                return options;
            ChatOptions copy = options?.Clone() ?? new ChatOptions();
            copy.Instructions = string.IsNullOrEmpty(copy.Instructions) ? Instructions : Instructions + "\n\n" + copy.Instructions;
            return copy;
        }

        // The request: the summary (as a user/assistant pair), the messages the model sees, then the new message.
        private List<ChatMessage> RequestMessages(ChatMessageItem next)
        {
            List<ChatMessage> messages = new List<ChatMessage>();
            if (_summary != null)
            {
                messages.Add(new ChatMessage(ChatRole.User, SummaryIntro + _summary));
                messages.Add(new ChatMessage(ChatRole.Assistant, SummaryAcknowledgement));
            }
            foreach (ChatMessageItem item in ContextItems())
            {
                if (item == next)
                    continue;
                if (item.Role == ChatMessageRole.Assistant && item.ModelMessages != null)
                    messages.AddRange(item.ModelMessages);
                else
                    messages.Add(ToChatMessage(item));
            }
            messages.Add(ToChatMessage(next));
            return messages;
        }

        private static ChatMessage ToChatMessage(ChatMessageItem item)
        {
            List<AIContent> contents = new List<AIContent>();
            foreach (DataContent attachment in item.Attachments)
                contents.Add(attachment);
            string text = item.ModelText ?? string.Empty;
            // A reply interrupted before it wrote anything: an empty assistant message would confuse the model.
            if (item.Role == ChatMessageRole.Assistant && text.Length == 0)
                text = "…";
            contents.Add(new TextContent(text));
            return new ChatMessage(item.Role == ChatMessageRole.User ? ChatRole.User : ChatRole.Assistant, contents);
        }

        // Stream the reply into its item. Continues on the caller's context, so the item changes there.
        private async Task StreamReplyAsync(ChatMessageItem message, ChatMessageItem reply, ChatOptions options,
            CancellationToken cancellationToken)
        {
            List<ChatMessage> request = RequestMessages(message);
            List<ChatResponseUpdate> updates = new List<ChatResponseUpdate>();
            StringBuilder thinking = new StringBuilder();
            StringBuilder answer = new StringBuilder();
            Dictionary<string, ChatToolStep> steps = new Dictionary<string, ChatToolStep>();
            DateTime? thinkingStarted = null;
            await foreach (ChatResponseUpdate update in Client.GetStreamingResponseAsync(request, options, cancellationToken))
            {
                updates.Add(update);
                foreach (AIContent content in update.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent reasoning:
                            thinking.Append(reasoning.Text);
                            break;
                        case TextContent textContent:
                            answer.Append(textContent.Text);
                            break;
                        case FunctionCallContent call:
                            ChatToolStep step = new ChatToolStep(call, DescribeToolCall(call));
                            if (call.CallId != null)
                                steps[call.CallId] = step;
                            reply.Steps.Add(step);
                            break;
                        case FunctionResultContent result:
                            if (result.CallId != null && steps.TryGetValue(result.CallId, out ChatToolStep done))
                            {
                                done.Result = result;
                                done.Error = result.Exception?.Message;
                                done.IsRunning = false;
                            }
                            break;
                    }
                }
                // A client that writes the thinking into the text ("<think>...</think>...") is split the same way.
                string thinkingText = thinking.ToString();
                string answerText = answer.ToString();
                if (thinkingText.Length == 0 && answerText.StartsWith("<think>", StringComparison.Ordinal))
                {
                    ModelsChatReply split = new ModelsChatReply(answerText);
                    thinkingText = split.Thinking;
                    answerText = split.Text;
                }
                // After thinking, models start the answer with line breaks.
                if (thinkingText.Length > 0)
                    answerText = answerText.TrimStart();
                if (thinkingText.Length > 0 && thinkingStarted == null)
                    thinkingStarted = DateTime.UtcNow;
                if (answerText.Length > 0 && thinkingStarted != null && reply.ThinkingDuration == null)
                    reply.ThinkingDuration = DateTime.UtcNow - thinkingStarted.Value;
                reply.Thinking = thinkingText;
                reply.Text = answerText;
            }
            foreach (ChatToolStep step in reply.Steps)
                step.IsRunning = false;
            reply.ModelMessages = updates.ToChatResponse().Messages;
        }

        // ---------- Context window ----------

        private int ContextBudget => ContextWindow - ReplyReserveTokens - OverheadTokens;

        // The messages the model sees: from ContextStart, without failed exchanges.
        private List<ChatMessageItem> ContextItems()
        {
            return Messages.Skip(ContextStart).Where(m => !m.Excluded && m.State != ChatMessageState.Streaming).ToList();
        }

        private static int Cost(ChatMessageItem item) => Math.Max(0, item.Tokens) + item.ExtraTokens;

        // The size of what the model sees now, without the next message.
        private int ContextTokens()
        {
            int tokens = _summary != null ? _summaryTokens : 0;
            foreach (ChatMessageItem item in ContextItems())
                tokens += Cost(item);
            return tokens;
        }

        // Count the messages the model sees that aren't counted yet (e.g. of a restored conversation), the summary, and the
        // next message.
        private async Task CountTokensAsync(ChatMessageItem next)
        {
            Func<string, int> countTokens = CountTokens;
            List<ChatMessageItem> items = ContextItems().Where(i => i.Tokens < 0).ToList();
            if (next != null && next.Tokens < 0 && !items.Contains(next))
                items.Add(next);
            string summary = _summary != null && _summaryTokens == 0 ? _summary : null;
            if (items.Count == 0 && summary == null)
                return;
            int summaryTokens = await Task.Run(() =>
            {
                foreach (ChatMessageItem item in items)
                    item.Tokens = EstimateTokens(countTokens, item);
                return summary == null ? 0
                    : countTokens(SummaryIntro + summary + SummaryAcknowledgement) + 2 * MessageOverheadTokens;
            });
            if (summary != null && summary == _summary)
                _summaryTokens = summaryTokens;
        }

        // The reply's size, and what the model's own conversation holds on top until it is rebuilt: its thinking and tool
        // calls.
        private async Task CountReplyAsync(ChatMessageItem reply)
        {
            Func<string, int> countTokens = CountTokens;
            int steps = reply.Steps.Count;
            string thinking = reply.Thinking;
            try
            {
                await Task.Run(() =>
                {
                    reply.Tokens = EstimateTokens(countTokens, reply);
                    reply.ExtraTokens = (string.IsNullOrEmpty(thinking) ? 0 : countTokens(thinking)) + steps * ToolStepTokens;
                });
            }
            catch (Exception)
            {
                reply.Tokens = reply.Text.Length / 3 + MessageOverheadTokens;
            }
        }

        private int EstimateTokens(Func<string, int> countTokens, ChatMessageItem item)
        {
            int tokens = countTokens(item.ModelText ?? string.Empty) + MessageOverheadTokens;
            foreach (DataContent attachment in item.Attachments)
                tokens += attachment.HasTopLevelMediaType("image") ? ImageTokens : AudioTokens(attachment.Data.ToArray());
            return tokens;
        }

        // Audio by its length, from the WAV header; other formats count as DefaultAudioLength.
        private int AudioTokens(byte[] data)
        {
            double seconds = DefaultAudioLength.TotalSeconds;
            if (data != null && data.Length > 44
                && Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && Encoding.ASCII.GetString(data, 8, 4) == "WAVE")
            {
                int byteRate = BitConverter.ToInt32(data, 28);
                if (byteRate > 0)
                    seconds = (data.Length - 44) / (double)byteRate;
            }
            return (int)Math.Ceiling(seconds * AudioTokensPerSecond) + 10;
        }

        /// <summary>
        /// About 3.5 characters a token: the estimate when the model's tokenizer isn't available.
        /// </summary>
        public static int ApproximateTokens(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 3.5);
        }

        private static bool DefaultIsContextFull(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e.Message.IndexOf("too long", StringComparison.OrdinalIgnoreCase) >= 0
                    || e.Message.IndexOf("exceeds available state", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // Before a message is sent: compact the conversation if the message would make it too long for the model.
        private async Task PrepareContextAsync(ChatMessageItem next, CancellationToken cancellationToken)
        {
            await CountTokensAsync(next);
            int budget = ContextBudget - next.Tokens;
            if (ContextTokens() - next.Tokens <= budget)
                return;
            // Summarizing keeps the latest exchanges up to a quarter of the window, so it doesn't happen again soon;
            // dropping keeps as many as fit.
            bool summarize = Compaction == ChatCompaction.Summarize;
            int keep = summarize
                ? Math.Min(budget - SummaryMaxTokens - 2 * MessageOverheadTokens, ContextWindow / 4)
                : budget - _summaryTokens;
            await CompactAsync(Compaction, keep, next, cancellationToken);
        }

        // Make room: the messages before the latest exchanges that fit keepTokens are summarized - into one summary with the
        // previous one - or dropped. next is the message being sent (not compacted), or null. Returns false if there was
        // nothing to compact.
        private async Task<bool> CompactAsync(ChatCompaction compaction, int keepTokens, ChatMessageItem next,
            CancellationToken cancellationToken)
        {
            await CountTokensAsync(null);
            List<ChatMessageItem> items = ContextItems().Where(i => i != next).ToList();
            int keepStart = items.Count;
            int kept = 0;
            while (keepStart - 2 >= 0)
            {
                int exchange = Cost(items[keepStart - 2]) + Cost(items[keepStart - 1]);
                if (keepTokens <= 0 || kept + exchange > keepTokens)
                    break;
                kept += exchange;
                keepStart -= 2;
            }
            if (keepStart <= 0)
                return false;

            List<ChatMessageItem> older = items.GetRange(0, keepStart);
            string summary = _summary;
            bool summarized = false;
            if (compaction == ChatCompaction.Summarize)
            {
                Summarizing?.Invoke(this, true);
                try
                {
                    summary = await SummarizeAsync(_summary, older, cancellationToken);
                    summarized = true;
                }
                catch (Exception)
                {
                    // Dropped instead.
                }
                finally
                {
                    Summarizing?.Invoke(this, false);
                }
            }
            _summary = summary;
            _summaryTokens = 0;
            ChatMessageItem firstKept = keepStart < items.Count ? items[keepStart] : next;
            ContextStart = firstKept != null ? Messages.IndexOf(firstKept) : Messages.Count;
            MarkContextStart();
            // The model is given the conversation again, as text: what its own conversation held on top is gone.
            foreach (ChatMessageItem item in Messages)
            {
                item.ExtraTokens = 0;
                item.ModelMessages = null;
            }
            await CountTokensAsync(null);
            Compacted?.Invoke(this, new ChatCompactedEventArgs(ContextStart, _summary, summarized));
            return true;
        }

        // Have the model summarize the messages (and the summary of those before them), in a request of its own.
        private async Task<string> SummarizeAsync(string previous, List<ChatMessageItem> messages, CancellationToken cancellationToken)
        {
            Func<string, int> countTokens = CountTokens;
            int inputBudget = ContextWindow - SummaryMaxTokens - 200;
            string prompt = await Task.Run(() =>
            {
                string head = SummaryInstructions
                    + (previous != null ? "\n\nSummary of the conversation before this part:\n" + previous : string.Empty)
                    + "\n\nConversation:\n";
                List<string> lines = messages.Select(DescribeForSummary).ToList();
                // If it's still too long for the model, the oldest messages go.
                while (lines.Count > 1 && countTokens(head + string.Join("\n", lines)) > inputBudget)
                    lines.RemoveAt(0);
                return head + string.Join("\n", lines);
            });
            ChatOptions options = new ChatOptions
            {
                MaxOutputTokens = SummaryMaxTokens,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
            };
            ChatResponse response = await Client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, prompt) }, options, cancellationToken);
            string text = response.Text ?? string.Empty;
            if (text.StartsWith("<think>", StringComparison.Ordinal))
                text = new ModelsChatReply(text).Text;
            text = text.Trim();
            if (text.Length == 0)
                throw new InvalidOperationException("The summary is empty");
            return text;
        }

        private static string DescribeForSummary(ChatMessageItem item)
        {
            string text = item.Text ?? string.Empty;
            if (text.Length > 2000)
                text = text.Substring(0, 2000) + "...";
            string media = string.Concat(item.Attachments.Select(a => a.HasTopLevelMediaType("image") ? "[image] " : "[voice message] "));
            return (item.Role == ChatMessageRole.User ? "User: " : "Assistant: ") + media + text;
        }
    }
}
