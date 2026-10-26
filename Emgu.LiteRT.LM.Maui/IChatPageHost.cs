//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// What a <see cref="ChatPage"/> offers its features (<see cref="ChatFeature.Host"/>): the conversation and the model,
    /// the page's state, its status line, cards on the reply, alerts, prompts and the bottom sheet.
    /// </summary>
    public interface IChatPageHost
    {
        /// <summary>The page.</summary>
        Page Page { get; }

        /// <summary>The conversation shown (a new one for each new chat or model).</summary>
        ChatConversation Conversation { get; }

        /// <summary>The chat client with the tools (the model's, through the page's function invocation), for requests of a feature's own.</summary>
        IChatClient ChatClient { get; }

        /// <summary>The model chosen, or null.</summary>
        ChatModelOption Model { get; }

        /// <summary>True once the model is loaded and ready.</summary>
        bool ModelReady { get; }

        /// <summary>True while a reply is being written, or the page is busy with a feature's work (<see cref="SetBusy"/>).</summary>
        bool IsBusy { get; }

        /// <summary>True while a reply is being written.</summary>
        bool IsReplying { get; }

        /// <summary>True in a private chat: nothing is saved, nothing saved is read.</summary>
        bool IsPrivate { get; }

        /// <summary>True when the user lets the agent remember things about them (and the chat isn't private).</summary>
        bool MemoryOn { get; }

        /// <summary>Settings > Developer mode: show what the model is given.</summary>
        bool DeveloperMode { get; }

        /// <summary>The model's context window in tokens.</summary>
        int ContextWindow { get; }

        /// <summary>A token counter for the model (call it off the UI thread).</summary>
        Func<string, int> TokenCounter { get; }

        /// <summary>The page's size.</summary>
        Size PageSize { get; }

        /// <summary>Where the settings are, in words for the model ("this chat's settings (the Settings button at the top)").</summary>
        string SettingsLocation { get; }

        /// <summary>The memories about the user, or null when the page has none.</summary>
        IAgentMemoryStore Memory { get; }

        /// <summary>The page's settings.</summary>
        IChatPageSettings Settings { get; }

        /// <summary>The skills (the features), composed into each message.</summary>
        ChatSkillSet Skills { get; }

        /// <summary>Services the features offer each other.</summary>
        ChatFeatureServices Services { get; }

        /// <summary>The status line under the conversation; null hides it.</summary>
        void SetStatus(string message);

        /// <summary>Mark the page busy with a feature's work (the message box waits), or done.</summary>
        void SetBusy(bool busy);

        /// <summary>Show a card above the reply being written (or the latest reply); on the UI thread.</summary>
        void AddCardToReply(View card);

        /// <summary>Add a notice to the transcript (the model never sees it); on the UI thread.</summary>
        void AddNotice(string text);

        /// <summary>One question to the model in a request of its own (not part of the conversation); its answer.</summary>
        Task<string> AskModelOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default);

        /// <summary>Send a message as if the user had typed it.</summary>
        Task SendMessageAsync(string text);

        /// <summary>An alert: true for accept; with cancel null it has one button.</summary>
        Task<bool> AskAsync(string title, string message, string accept, string cancel = null);

        /// <summary>A text prompt: the text entered, or null when cancelled.</summary>
        Task<string> PromptAsync(string title, string message, string accept, string cancel, string initialValue = null, int maxLength = -1);

        /// <summary>The page's bottom sheet: shows a view (the result is what <see cref="DismissSheet"/> passed, or null).</summary>
        Task<string> ShowSheetAsync(string title, View content, string closeText = "Close");

        /// <summary>Close the bottom sheet with a result.</summary>
        void DismissSheet(string result);

        /// <summary>Build the settings and the chips again (e.g. after a feature's state changed what it shows).</summary>
        void RefreshSettings();
    }
}
