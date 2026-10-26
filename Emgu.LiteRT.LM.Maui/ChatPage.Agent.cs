//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Extensions.AI;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatReply = Emgu.LiteRT.LM.Models.ChatReply;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui
{
    // The agent: the chat client with function invocation, and each message's tools and instructions from the skills.
    public partial class ChatPage
    {
        private ChatSkillComposition _lastComposition;

        // The model's client with function invocation for the skills' tools, made on first use (the model is loaded by
        // then: ChatView waits for it before sending) and again for another model.
        private IChatClient CreateInnerClient()
        {
            IChatClient inner = CreateChatClient(_model, _chatView?.Model);
            if (_options.ConfigureClient != null)
                inner = _options.ConfigureClient(inner) ?? inner;
            return new ChatClientBuilder(inner).UseFunctionInvocation().Build();
        }

        /// <summary>
        /// The client that talks to a model, before the page adds function invocation and <see cref="ChatPageOptions.ConfigureClient"/>:
        /// LiteRT-LM's for a model on the device. A derived page overrides it for a remote model (a server's client).
        /// </summary>
        /// <param name="option">The model chosen</param>
        /// <param name="model">Its instance on the device, or null for a remote model</param>
        protected virtual IChatClient CreateChatClient(ChatModelOption option, LanguageModel model)
        {
            if (model == null)
                throw new InvalidOperationException(option?.IsRemote == true
                    ? "A remote model needs a client: override CreateChatClient."
                    : "The model isn't loaded.");
            return new LiteRtLmChatClient(model) { EnableContextManagement = true };
        }

        /// <summary>
        /// Connect to a remote model before its first message (nothing to do by default): a derived page overrides it to
        /// reach the server, and returns false (with a status) when it can't.
        /// </summary>
        /// <param name="option">The remote model</param>
        /// <param name="cancellationToken">Cancels it</param>
        protected virtual Task<bool> ConnectRemoteAsync(ChatModelOption option, CancellationToken cancellationToken) => Task.FromResult(true);

        /// <summary>
        /// Send a message once the model is ready (the view's <see cref="ChatView.Sender"/>): by default the view's
        /// <see cref="ChatView.DefaultSendAsync"/> - the options from <see cref="ComposeOptionsAsync"/>, the reply streamed,
        /// a rerun when a tool group asks for one - after connecting a remote model. A derived page overrides it to do
        /// more around the send (e.g. give the model document excerpts as context, or save the exchange with its own
        /// extras), on the view's cancellation token.
        /// </summary>
        /// <param name="request">The message</param>
        /// <param name="cancellationToken">Stops the reply (the Stop button)</param>
        protected virtual async Task SendCoreAsync(ChatSendRequest request, CancellationToken cancellationToken)
        {
            if (!await EnsureRemoteAsync(cancellationToken))
                return;
            await _chatView.DefaultSendAsync(request, cancellationToken);
        }

        /// <summary>Connect a remote model if it isn't yet (<see cref="ConnectRemoteAsync"/>); true when the model is ready.</summary>
        /// <param name="cancellationToken">Cancels it</param>
        protected async Task<bool> EnsureRemoteAsync(CancellationToken cancellationToken)
        {
            ChatModelOption option = _model;
            if (option == null || !option.IsRemote || _remoteReady)
                return true;
            _chatView.SetStatus("Connecting...");
            _remoteReady = await ConnectRemoteAsync(option, cancellationToken);
            if (_remoteReady)
                _chatView.SetStatus(null);
            return _remoteReady;
        }

        /// <summary>Forget a remote model's connection, so the next message connects again (e.g. after its settings changed).</summary>
        protected void ResetRemote()
        {
            _remoteReady = false;
            _client.Reset();
        }

        // The step line for a tool call: the features' (e.g. an MCP server's tools), else the Agents package's.
        private string DescribeToolCall(FunctionCallContent call)
        {
            foreach (ChatFeature feature in _features)
            {
                string text;
                try { text = feature.DescribeToolCall(call); }
                catch (Exception) { text = null; }
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
            return AgentTools.Describe(call);
        }

        /// <summary>
        /// The context of a message for the skills: what the page knows about the chat and the model.
        /// </summary>
        /// <param name="request">The message</param>
        protected ChatMessageContext CreateMessageContext(ChatSendRequest request) => new ChatMessageContext
        {
            Prompt = request.Text,
            Attachments = request.Attachments,
            IsPrivate = IsPrivate,
            MemoryOn = MemoryOn,
            ModelCanUseTools = _model?.CanUseTools == true,
            ModelAcceptsImages = _model?.AcceptsImages == true,
            HandsFree = HandsFree
        };

        /// <summary>
        /// The options of a message: the skills' tools (for a model that can use them), the instructions, and the
        /// reasoning effort (the Think chip, for a model that can think, not hands-free). A derived page may add to them.
        /// </summary>
        /// <param name="request">The message</param>
        protected virtual async Task<ChatOptions> ComposeOptionsAsync(ChatSendRequest request)
        {
            ChatMessageContext context = CreateMessageContext(request);
            // A model without tools isn't told about them; a private chat is told nothing is saved.
            string instructions = !context.ModelCanUseTools ? _options.InstructionsWithoutTools
                : IsPrivate ? _options.PrivateInstructions : _options.Instructions;
            _lastComposition = await _skills.ComposeAsync(context, instructions);
            var options = new ChatOptions
            {
                Instructions = _lastComposition.Instructions,
                Reasoning = new ReasoningOptions
                {
                    Effort = _chatView.ThinkingEnabled && _model?.CanThink != false && !HandsFree ? ReasoningEffort.Medium : ReasoningEffort.None
                }
            };
            if (_lastComposition.Tools.Count > 0)
            {
                options.Tools = _lastComposition.Tools;
                options.ToolMode = ChatToolMode.Auto;
            }
            return options;
        }

        /// <summary>One question to the model in a request of its own (not part of the conversation); its answer.</summary>
        /// <param name="prompt">The question</param>
        /// <param name="maxTokens">The longest answer</param>
        /// <param name="cancellationToken">Cancels it</param>
        public virtual async Task<string> AskModelOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default)
        {
            ChatResponse response = await _client.GetResponseAsync(new[] { new AIChatMessage(ChatRole.User, prompt) },
                new ChatOptions { MaxOutputTokens = maxTokens, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } }, cancellationToken);
            return new ChatReply(response.Text ?? string.Empty).Text.Trim();
        }

        // The client the view is given once; the model's client behind it is made on first use and again after Reset.
        private sealed class ChatPageClient : IChatClient
        {
            private readonly Func<IChatClient> _create;
            private IChatClient _current;

            public ChatPageClient(Func<IChatClient> create)
            {
                _create = create;
            }

            private IChatClient Current => _current ??= _create();

            public void Reset()
            {
                _current?.Dispose();
                _current = null;
            }

            public Task<ChatResponse> GetResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default) =>
                Current.GetResponseAsync(messages, options, cancellationToken);

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await foreach (ChatResponseUpdate update in Current.GetStreamingResponseAsync(messages, options, cancellationToken))
                    yield return update;
            }

            public object GetService(Type serviceType, object serviceKey = null) =>
                serviceKey == null && serviceType.IsInstanceOfType(this) ? this : _current?.GetService(serviceType, serviceKey);

            public void Dispose() => Reset();
        }
    }
}
