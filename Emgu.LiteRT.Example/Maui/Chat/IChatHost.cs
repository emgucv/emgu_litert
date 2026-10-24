// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using Emgu.LiteRT.LM.Agents;

namespace Maui.Demo.Lite;

// What the chat page offers the features that are classes of their own (Chat/Features/*Feature.cs), instead of each feature being a partial
// of the page with access to all of its state. LiteRtLmChatPage implements it (LiteRtLmChatPage.Host.cs). Add members here only as a feature
// needs them.
internal interface IChatHost
{
    // Shows a card above the reply being written (call on the UI thread).
    void AddCardToReply(View card);

    // The status line under the conversation; null hides it.
    void SetStatus(string message);

    // One question to the model in a request of its own (not part of the conversation); its answer.
    Task<string> AskModelOnceAsync(string prompt, int maxTokens);

    // The model's context window in tokens, and a token counter for it (call the counter off the UI thread).
    int ContextWindow { get; }
    Func<string, int> TokenCounter { get; }

    // Saves a document with the reply being written and shows its card (Open, Share).
    Task SaveDocumentAsync(GeneratedDocument document);

    // Attaches a text to the chat as its document, so later messages get the passages they need (UI thread; nothing if document search
    // isn't available).
    void AttachDocument(byte[] bytes, string fileName);

    // Starts, updates or stops the Android foreground service to match what is recording or listening.
    Task SyncBackgroundServiceAsync();

    // The live camera (null when closed); opening it (its result text for the model), and closing it unless camera monitors need it.
    LiveCamera Camera { get; }
    Task<string> OpenCameraAsync(CancellationToken token);
    Task CloseCameraIfUnusedAsync();

    // Whether the camera preview fills the conversation's area, and the count shown over it there (null hides it; UI thread).
    bool CameraMaximized { get; }
    void SetCameraOverlay(string count, string detail);

    // The sound monitors stop listening while the app itself makes a sound (counted: every Begin needs an End).
    void BeginSoundMute();
    void EndSoundMute();

    // The user's goals, or null when they aren't kept (memories off, or a private chat).
    IAgentGoalStore Goals { get; }

    // Settings > Developer mode: show what the models see.
    bool DeveloperMode { get; }

    // Waits while the device is too hot or its battery too low for heavy work (WorkloadPolicy), telling status why (null when it goes on).
    Task WaitForWorkAsync(bool photos, Action<string> status, CancellationToken cancel);

    // Settings > Storage.
    Task OpenStorageAsync();

    // An alert: true for accept; with cancel null it has one button.
    Task<bool> AskAsync(string title, string message, string accept, string cancel);

    // Where the settings are, in words for the model ("this chat's settings (the gear button at the top)").
    string SettingsLocation { get; }

    // The page's bottom sheet: shows a view (the result is the closing button's text or what DismissSheet passed), and closes it.
    Task<string> ShowSheetAsync(string title, View content, string closeText);
    void DismissSheet(string result);

    Size PageSize { get; }

    // Whether a message is being answered; and sending one as if the user had typed it.
    bool IsBusy { get; }
    Task SendMessageAsync(string text);

    // The conversation's chat client (the model's or the server's, with the tools), for requests of a feature's own.
    Microsoft.Extensions.AI.IChatClient ChatClient { get; }

    // The tools the model can use, in words (for a planner).
    string ToolSummary { get; }
}
#endif
