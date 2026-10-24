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
}
#endif
