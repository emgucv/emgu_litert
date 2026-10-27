//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// A model that runs somewhere else - on a server of the user's own, or a cloud service - behind a
    /// <see cref="ChatModelOption"/> with no model on the device (<see cref="ChatModelOption.IsRemote"/>,
    /// <see cref="ChatModelOption.Remote"/>): how a chat page connects to it, talks to it, and copes with what it
    /// doesn't take. A chat page uses it for the remote option's client, its summaries and its one-off questions.
    /// </summary>
    public interface IRemoteChatModel
    {
        /// <summary>
        /// Connect to the model, or check it still answers, before the first message (and again after
        /// the connection was reset): learn what it takes - set the option's <see cref="ChatModelOption.AcceptsImages"/>
        /// and <see cref="ChatModelOption.ContextWindow"/> - and say what is wrong when it can't be reached.
        /// </summary>
        /// <param name="cancellationToken">Cancels it</param>
        /// <returns>Null when the model is ready, else why it isn't, in words for the user (shown as the page's status)</returns>
        Task<string> ConnectAsync(CancellationToken cancellationToken);

        /// <summary>A client that talks to the model, before the page adds function invocation (made again for each connection).</summary>
        IChatClient CreateChatClient();

        /// <summary>
        /// Make a message's options into what the model takes: e.g. drop the reasoning option a server rejected. Called
        /// with the options the page composed, before the message is sent.
        /// </summary>
        /// <param name="options">The message's options</param>
        void AdjustOptions(ChatOptions options);

        /// <summary>
        /// A request failed: whether it should be sent again, with the options changed (e.g. the server's 400 named the
        /// reasoning option, which it doesn't take: it is dropped, for this and the messages after). Asked once per message.
        /// </summary>
        /// <param name="error">What the request threw</param>
        /// <param name="options">The message's options, changed by this call when it returns true</param>
        /// <returns>True to send the message again</returns>
        bool ShouldResend(Exception error, ChatOptions options);

        /// <summary>The options for the conversation's summaries when it compacts, or null for the conversation's defaults.</summary>
        ChatOptions SummaryOptions { get; }

        /// <summary>One question to the model in a request of its own (not part of the conversation); its answer.</summary>
        /// <param name="prompt">The question</param>
        /// <param name="maxTokens">The longest answer</param>
        /// <param name="cancellationToken">Cancels it</param>
        Task<string> AskOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken);
    }
}
