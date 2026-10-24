// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using Emgu.LiteRT.LM.Agents;

namespace Maui.Demo.Lite;

// The page as the host of its feature classes (IChatHost), and those features.
public partial class LiteRtLmChatPage : IChatHost
{
    private MeetingFeature _meetingFeature;

    private MeetingFeature Meetings => _meetingFeature ??= new MeetingFeature(this);

    private ExerciseFeature _exerciseFeature;

    private ExerciseFeature Exercises => _exerciseFeature ??= new ExerciseFeature(this);

    void IChatHost.AddCardToReply(View card) => AddCard(_agentBubble, card);

    void IChatHost.SetStatus(string message) => SetStatus(message);

    Task<string> IChatHost.AskModelOnceAsync(string prompt, int maxTokens) => AskModelOnceAsync(prompt, maxTokens);

    int IChatHost.ContextWindow => ContextWindow;

    Func<string, int> IChatHost.TokenCounter => TokenCounter;

    Task IChatHost.SaveDocumentAsync(GeneratedDocument document) => SaveWrittenDocumentAsync(document, CancellationToken.None);

    void IChatHost.AttachDocument(byte[] bytes, string fileName)
    {
        if (!SearchDownloadRequired)
            AttachDocument(bytes, fileName);
    }

    Task IChatHost.SyncBackgroundServiceAsync() => SyncMonitoringServiceAsync();

    LiveCamera IChatHost.Camera => _camera;

    Task<string> IChatHost.OpenCameraAsync(CancellationToken token) => OpenCameraAsync(token);

    async Task IChatHost.CloseCameraIfUnusedAsync()
    {
        if (!_visualRuntime.Snapshot().Any())
            await CloseCameraAsync();
    }

    bool IChatHost.CameraMaximized => _cameraMaximized;

    void IChatHost.SetCameraOverlay(string count, string detail)
    {
        if (_cameraOverlay == null) return;
        _cameraOverlay.IsVisible = count != null;
        if (count == null) return;
        _cameraOverlayCount.Text = count;
        _cameraOverlayDetail.Text = detail;
    }

    void IChatHost.BeginSoundMute() => BeginSoundMute();

    void IChatHost.EndSoundMute() => EndSoundMute();

    IAgentGoalStore IChatHost.Goals => GoalsOn ? _store : null;

    bool IChatHost.DeveloperMode => DeveloperMode;
}
#endif
