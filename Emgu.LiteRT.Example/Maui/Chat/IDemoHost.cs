// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// What the demo's chat page offers its own features (Chat/Features/*Feature.cs, the ChatFeature wrappers) beyond the
// library's IChatPageHost: the demo's store, its workload policy, the features that still take the old IChatHost, and
// the page's state and actions the library doesn't expose. The page implements both; a feature reaches this one through
// DemoFeature.Demo. Add members here only as a feature needs them.
internal interface IDemoHost
{
    // The saved chats, memories, goals, documents and indexes (SQLite).
    ChatStore Store { get; }

    // Waits for a cool, charged device before heavy background work, and runs one embedding at a time.
    WorkloadService Workload { get; }

    // The features over the old IChatHost (made by the page when first used); the exercise feature only once made.
    GoalsFeature Goals { get; }
    PhotoSearchFeature PhotoSearch { get; }
    PastChatsFeature PastChats { get; }
    ImageFeature Images { get; }
    MeetingFeature Meetings { get; }
    ExerciseFeature Exercises { get; }
    ExerciseFeature ExercisesIfCreated { get; }
    WrittenDocumentsFeature WrittenDocuments { get; }
    TaskFeature Tasks { get; }
    TranslationFeature Translations { get; }
    DeviceActionsFeature ActionTools { get; }

    // Whether the page is shown, and whether a voice message is being recorded (the message box's microphone).
    bool PageVisible { get; }
    bool IsRecording { get; }

    // Runs work during which the models are reloading (IChatPageHost.IsReloadingModel: nothing new starts, the settings
    // wait); leaving the page waits for it.
    Task RunReloadAsync(Func<Task> work);

    // Frees the chat model's engine (its file is being deleted); the page offers to download or load it again.
    Task ReleaseModelAsync();

    // Forget the server's connection, so the next message connects again; start a new chat; list the models again
    // (their rows changed); drop the images attached to the next message (the server's model doesn't take them).
    void ResetRemote();
    void StartNewChat();
    void RefreshModelList();
    void DropPendingImages();

    // Send a recording as a voice message, the reply read aloud (a hands-free chat); stop the reply being written.
    Task SendVoiceAsync(byte[] wav, TimeSpan length);
    void StopReply();
}
