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
        /// LiteRT-LM's for a model on the device, the <see cref="ChatModelOption.Remote"/>'s for a remote model. A derived
        /// page overrides it for a remote model without one.
        /// </summary>
        /// <param name="option">The model chosen</param>
        /// <param name="model">Its instance on the device, or null for a remote model</param>
        protected virtual IChatClient CreateChatClient(ChatModelOption option, LanguageModel model)
        {
            if (option?.Remote != null)
                return option.Remote.CreateChatClient();
            if (model == null)
                throw new InvalidOperationException(option?.IsRemote == true
                    ? "A remote model needs a client: set the option's Remote, or override CreateChatClient."
                    : "The model isn't loaded.");
            return new LiteRtLmChatClient(model) { EnableContextManagement = true };
        }

        /// <summary>
        /// Connect to a remote model before its first message: the <see cref="ChatModelOption.Remote"/>'s
        /// <see cref="IRemoteChatModel.ConnectAsync"/>, its problem shown as the status; nothing to do without one. A
        /// derived page overrides it to reach a server of its own, and returns false (with a status) when it can't.
        /// </summary>
        /// <param name="option">The remote model</param>
        /// <param name="cancellationToken">Cancels it</param>
        protected virtual async Task<bool> ConnectRemoteAsync(ChatModelOption option, CancellationToken cancellationToken)
        {
            if (option?.Remote == null)
                return true;
            string problem = await option.Remote.ConnectAsync(cancellationToken);
            if (problem == null)
                return true;
            SetStatus(problem);
            return false;
        }

        /// <summary>
        /// A request to the model failed: whether to send the message again with the options changed - a remote model
        /// may take the reasoning option out after its server rejected it (<see cref="IRemoteChatModel.ShouldResend"/>).
        /// The view's <see cref="ChatView.DefaultSendAsync(ChatSendRequest, CancellationToken)"/> asks it; a derived page's own send loop should too.
        /// </summary>
        /// <param name="error">What the request threw</param>
        /// <param name="options">The message's options, changed when it returns true</param>
        /// <returns>True to send the message again into the same reply</returns>
        protected bool ShouldResend(Exception error, ChatOptions options)
        {
            IRemoteChatModel remote = _model?.Remote;
            if (remote == null || error == null || options == null)
                return false;
            try { return remote.ShouldResend(error, options); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Send a message once the model is ready (the view's <see cref="ChatView.Sender"/>): by default the view's
        /// <see cref="ChatView.DefaultSendAsync(ChatSendRequest, CancellationToken)"/> - the options from <see cref="ComposeOptionsAsync"/>, the reply streamed,
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
            ChatSendRequest prepared = await PrepareRequestAsync(request, cancellationToken);
            foreach (string step in PendingStepLines)
                AddCardToNextReply(StepLine(step));
            await _chatView.DefaultSendAsync(prepared, cancellationToken, CreateMessageClient(prepared));
        }

        // The features' additions to the message being sent, and the request they were made for.
        private readonly Dictionary<ChatFeature, ChatMessageAddition> _additions = new Dictionary<ChatFeature, ChatMessageAddition>();
        private ChatSendRequest _preparedRequest;

        /// <summary>The step lines the features gave for the message being sent (<see cref="ChatMessageAddition.StepLine"/>).</summary>
        protected IReadOnlyList<string> PendingStepLines =>
            _additions.Values.Select(a => a.StepLine).Where(s => !string.IsNullOrEmpty(s)).ToList();

        /// <summary>
        /// Ask every feature what it adds to a message (<see cref="ChatFeature.PrepareMessageAsync"/>): the request with
        /// their context joined after the request's own, and their attachments after its. A derived page with its own
        /// send calls it first, then sends what it returns.
        /// </summary>
        /// <param name="request">The message as the user sent it</param>
        /// <param name="cancellationToken">Stops the send</param>
        protected async Task<ChatSendRequest> PrepareRequestAsync(ChatSendRequest request, CancellationToken cancellationToken)
        {
            _additions.Clear();
            _preparedRequest = request;
            var attachments = request.Attachments.ToList();
            foreach (ChatFeature feature in _features)
            {
                ChatMessageAddition addition = await feature.PrepareMessageAsync(request, cancellationToken);
                if (addition == null)
                    continue;
                _additions[feature] = addition;
                if (addition.Attachments != null)
                    attachments.AddRange(addition.Attachments);
            }
            return new ChatSendRequest(request.Text, attachments, JoinedContext(request.Context)) { IsVoiceMessage = request.IsVoiceMessage };
        }

        private string JoinedContext(string requestContext)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(requestContext))
                parts.Add(requestContext);
            parts.AddRange(_additions.Values.Select(a => a.Context).Where(c => !string.IsNullOrEmpty(c)));
            return parts.Count == 0 ? null : string.Join("\n\n", parts);
        }

        // Before the request goes out: the features whose additions no longer fit what the model holds give new ones.
        private async Task RefreshAdditionsAsync(ChatMessageItem message, CancellationToken cancellationToken)
        {
            if (_additions.Count == 0)
                return;
            bool changed = false;
            foreach (ChatFeature feature in _additions.Keys.ToList())
            {
                ChatMessageAddition again = await feature.RefreshMessageAsync(message, cancellationToken);
                if (again == null)
                    continue;
                _additions[feature] = again;
                changed = true;
            }
            if (changed)
                message.Context = JoinedContext(_preparedRequest?.Context);
        }

        /// <summary>Put a card above the next reply, when it starts (for what is known before the reply exists).</summary>
        /// <param name="card">The card</param>
        protected void AddCardToNextReply(View card)
        {
            ChatConversation conversation = Conversation;
            if (card == null || conversation == null)
                return;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler handler = null;
            handler = (s, e) =>
            {
                ChatMessageItem reply = e.NewItems?.OfType<ChatMessageItem>().FirstOrDefault(m => m.Role == ChatMessageRole.Assistant);
                if (reply == null)
                    return;
                conversation.Messages.CollectionChanged -= handler;
                reply.Decorations.Add(card);
            };
            conversation.Messages.CollectionChanged += handler;
        }

        /// <summary>
        /// A client of a feature's to run the message through instead of the model's own (<see cref="ChatFeature.CreateMessageClient"/>;
        /// e.g. task mode's), or null: the first feature's. A derived page's own send asks it after preparing the message.
        /// </summary>
        /// <param name="request">The message, as prepared</param>
        protected IChatClient CreateMessageClient(ChatSendRequest request)
        {
            foreach (ChatFeature feature in _features)
            {
                IChatClient client;
                try { client = feature.CreateMessageClient(request); }
                catch (Exception) { client = null; }
                if (client != null)
                    return client;
            }
            return null;
        }

        /// <summary>A grey step line for above a reply, like the tool calls' ("Looked in report.pdf: pages 2 and 5").</summary>
        /// <param name="text">The line</param>
        public static Label StepLine(string text)
        {
            var label = new Label { Text = "\U0001F50E " + text, Margin = new Thickness(6, 0) };
            ChatResources.ApplyStyle(label, "ChatDecorationTextStyle");
            return label;
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
            // What a remote model doesn't take (e.g. the reasoning option its server rejected) goes.
            _model?.Remote?.AdjustOptions(options);
            return options;
        }

        /// <summary>
        /// One question to the model in a request of its own (not part of the conversation); its answer. A remote model
        /// answers through its <see cref="ChatModelOption.Remote"/>.
        /// </summary>
        /// <param name="prompt">The question</param>
        /// <param name="maxTokens">The longest answer</param>
        /// <param name="cancellationToken">Cancels it</param>
        public virtual async Task<string> AskModelOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default)
        {
            if (_model?.Remote != null)
                return await _model.Remote.AskOnceAsync(prompt, maxTokens, cancellationToken);
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
