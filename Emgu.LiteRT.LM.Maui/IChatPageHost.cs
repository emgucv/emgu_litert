//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Where a <see cref="ChatPage"/>'s features put their own views: overlays over the chat area (a camera preview, a
    /// monitor panel) and layers over the whole page under the bottom sheet (a hands-free voice screen).
    /// </summary>
    public interface IChatOverlays
    {
        /// <summary>Put a view over the chat area (the transcript and the message box); it lays out as the Grid's child it is.</summary>
        void AddOverlay(View view);

        /// <summary>Take an overlay away.</summary>
        void RemoveOverlay(View view);

        /// <summary>Put a view over the whole page, under the bottom sheet.</summary>
        void ShowLayer(View view);

        /// <summary>Take a layer away.</summary>
        void HideLayer(View view);

        /// <summary>The chat area's size, for an overlay to size itself by.</summary>
        Size ChatSize { get; }

        /// <summary>Raised when <see cref="ChatSize"/> changes.</summary>
        event EventHandler ChatSizeChanged;
    }

    /// <summary>
    /// What a <see cref="ChatPage"/> offers its features (<see cref="ChatFeature.Host"/>): the conversation and the model,
    /// the page's state, its status line, cards on the reply, alerts, prompts, the bottom sheet, and places for views of
    /// their own.
    /// </summary>
    public interface IChatPageHost
    {
        /// <summary>The page.</summary>
        Page Page { get; }

        /// <summary>Where the features put overlays and layers.</summary>
        IChatOverlays Overlays { get; }

        /// <summary>The saved chat being shown (null until its first exchange is saved, or without saved chats).</summary>
        int? CurrentChatId { get; }

        /// <summary>Open a saved chat (when nothing is being answered).</summary>
        Task OpenChatAsync(int chatId);

        /// <summary>True while a model is being reloaded: no new work starts meanwhile.</summary>
        bool IsReloadingModel { get; }

        /// <summary>Load the model again (e.g. after a thread count change); the chat goes on.</summary>
        Task<bool> ReloadModelAsync();

        /// <summary>
        /// Run work during which the models count as reloading (<see cref="IsReloadingModel"/>: nothing new starts, the
        /// settings wait) - another search model, a storage deletion; a page that closes waits for it.
        /// </summary>
        /// <param name="work">The work</param>
        Task RunReloadAsync(Func<Task> work);

        /// <summary>Free the chat model's engine (its file is being deleted); the page offers to download or load it again.</summary>
        Task ReleaseModelAsync();

        /// <summary>Forget a remote model's connection, so the next message connects again (its settings changed).</summary>
        void ResetRemote();

        /// <summary>
        /// The chosen model's abilities changed (a remote model's, learned after connecting: <see cref="ChatModelOption.AcceptsImages"/>,
        /// <see cref="ChatModelOption.ContextWindow"/>): what can be attached follows, attachments it doesn't take are
        /// dropped from the next message, and the conversation's context window is set.
        /// </summary>
        void ModelCapabilitiesChanged();

        /// <summary>List the models again (their rows changed: a remote model's detail, a model's file deleted).</summary>
        void RefreshModelList();

        /// <summary>Start a new chat (the model's conversation anew; nothing while a reply is being written).</summary>
        void StartNewChat();

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

        /// <summary>True in a hands-free chat (replies spoken, nothing on the screen looked at; <see cref="Emgu.LiteRT.LM.Agents.ChatMessageContext.HandsFree"/>).</summary>
        bool HandsFree { get; }

        /// <summary>Start or end a hands-free chat (a voice feature's).</summary>
        void SetHandsFree(bool handsFree);

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

        /// <summary>
        /// Show a card above the reply being written (or the latest reply) and keep a file with it: the chats' store
        /// saves the file with the reply (<see cref="ChatMessageItem.SavedFiles"/>), and when the chat is opened again
        /// the feature is asked for the card (<see cref="ChatFeature.RestoreSavedFile"/>). On the UI thread.
        /// </summary>
        /// <param name="card">The card, or null to keep the file only (the card was added before, e.g. while the picture was being drawn)</param>
        /// <param name="file">The file</param>
        void AddCardToReply(View card, ChatSavedFile file);

        /// <summary>Take a card away from whichever message it is on; on the UI thread.</summary>
        void RemoveCard(View card);

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

        /// <summary>List the saved chats again (e.g. after a feature changed a chat's title).</summary>
        void RefreshChatList();

        /// <summary>Show the chips above the message box again as they are now (a feature's chip changed state on its own).</summary>
        void RefreshChips();
    }
}
