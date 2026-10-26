//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Emgu.LiteRT.LM.Agents;
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
            LanguageModel model = _chatView?.Model;
            if (model == null)
                throw new InvalidOperationException("The model isn't loaded.");
            IChatClient inner = new LiteRtLmChatClient(model) { EnableContextManagement = true };
            if (_options.ConfigureClient != null)
                inner = _options.ConfigureClient(inner) ?? inner;
            return new ChatClientBuilder(inner).UseFunctionInvocation().Build();
        }

        // The options of a message: the skills' tools (for a model that can use them) and the instructions.
        private async Task<ChatOptions> ComposeOptionsAsync(ChatSendRequest request)
        {
            var context = new ChatMessageContext
            {
                Prompt = request.Text,
                Attachments = request.Attachments,
                IsPrivate = IsPrivate,
                MemoryOn = MemoryOn,
                ModelCanUseTools = _model?.CanUseTools == true,
                ModelAcceptsImages = _model?.AcceptsImages == true
            };
            // A model without tools isn't told about them; a private chat is told nothing is saved.
            string instructions = !context.ModelCanUseTools ? _options.InstructionsWithoutTools
                : IsPrivate ? _options.PrivateInstructions : _options.Instructions;
            _lastComposition = await _skills.ComposeAsync(context, instructions);
            var options = new ChatOptions { Instructions = _lastComposition.Instructions };
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
        public async Task<string> AskModelOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default)
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
