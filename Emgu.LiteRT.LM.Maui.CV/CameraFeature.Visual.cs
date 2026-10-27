// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;
using Microsoft.Extensions.AI;
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.Models.CV.Maui;
using Emgu.LiteRT.LM.Agents.CV;

namespace Emgu.LiteRT.LM.Maui.CV;

// The camera monitors ("tell me when you see a person"): the visual agents on the detectors, the monitors' runtime and
// their actions (chime, speech, snapshot, a sound file; on Android a clip and a photo), the tools, and the monitors' card.
public sealed partial class CameraFeature
{
    private VisualAgentRegistry _visualAgents;
    private VisualMonitorRuntime _visualRuntime;
    private CameraChimeAction _visualChime;
    private VisualActionRegistry _visualActions;
    private AlertAudioCoordinator _alertAudio;
    private Border _visualMonitorPanel;
    private Label _visualMonitorTitle;
    private VerticalStackLayout _visualMonitorRows;
    private int _visualUiQueued;
    private long _visualFrameId, _lastVisualFrame;
    private static readonly string VisualInstructions = VisualHostCatalog.PlannerInstructions(
#if ANDROID
        android: true);
#else
        android: false);
#endif

    // The agents and the alert audio, made with the feature: other features register with them (ExerciseFeature) and the
    // reply speaker reads through the audio.
    private void BuildVisualRegistry()
    {
        _visualAgents = new VisualAgentRegistry();
        // Emgu.LiteRT.LM.Agents.CV's agents on the detectors the app's VisualDetectors makes, with at
        // most 2 of the CPU threads Settings allows, read each time an agent is prepared. The holistic agent after the pose and face agents, which
        // keep the plain postures and expressions.
        Func<int> threads = () => Math.Min(2, CpuThreadSettings.Count);
        _visualAgents.Register(new CocoVisualAgent(_detectors.Coco(), threads, CocoDetector.CanTryGpu));
        _visualAgents.Register(new CocoVisualAgent(_detectors.YoloX(), threads, CocoDetector.CanTryGpu));
        _visualAgents.Register(new HandVisualAgent(_detectors.Hand(), threads));
        _visualAgents.Register(new PoseVisualAgent(_detectors.Pose(), numThreads: threads));
        _visualAgents.Register(new FaceVisualAgent(_detectors.Face(), threads));
        _visualAgents.Register(new HolisticVisualAgent(_detectors.Holistic(), threads));
        _visualChime = new CameraChimeAction();
        _alertAudio = new AlertAudioCoordinator((text, token) => MainThread.InvokeOnMainThreadAsync(() => AlertVoice.SpeakAsync(text, token)),
            occurrence => _visualRuntime.Snapshot().Any(m => m.Id == occurrence.MonitorId &&
                m.Actions.Any(a => a.ActionId is "play_sound" or "speak_text")));
    }

    private void BuildVisualMonitoring()
    {
        _visualActions = new VisualActionRegistry();
        _visualActions.Register(new CameraChimeFactory(_visualChime));
        _visualActions.Register(new CameraSpeechFactory(_alertAudio));
        _visualActions.Register(new CameraSnapshotFactory(TakeVisualSnapshotAsync));
        _visualActions.Register(new PlayFileFactory(_alertSounds, PlayAlertSoundAsync));
#if ANDROID
        _visualActions.Register(new CameraClipFactory(RecordVisualClipAsync));
        _visualActions.Register(new CameraPhotoFactory(TakeVisualPhotoAsync));
        // Clips and photos are for the session: those of a session that ended without cleaning up (the app was killed) go now.
        foreach (string folder in new[] { "clips", "photos" })
        {
            try { Directory.Delete(System.IO.Path.Combine(FileSystem.CacheDirectory, folder), true); } catch (Exception) { }
        }
#endif
        _visualRuntime = new VisualMonitorRuntime(_visualAgents, _visualActions, actionCoordinator: _alertAudio);
        _visualRuntime.Changed += QueueVisualMonitorUi;
        // The title stays (UpdateVisualMonitorUi rebuilds the rows after it), so it can be the handle to drag the card by.
        _visualMonitorTitle = new Label { Text = "Camera monitors · session only", FontSize = 13, TextColor = CvViews.Color("ChatPrimaryText"), MinimumHeightRequest = 32, VerticalTextAlignment = TextAlignment.Center };
        SemanticProperties.SetHint(_visualMonitorTitle, "Drag to move the camera monitors card.");
        _visualMonitorRows = new VerticalStackLayout { Spacing = 6, Children = { _visualMonitorTitle } };
        _visualMonitorPanel = CvViews.Card(new ScrollView { Content = _visualMonitorRows }, 8);
        _visualMonitorPanel.WidthRequest = 310;
        _visualMonitorPanel.MaximumHeightRequest = 220;
        _visualMonitorPanel.HorizontalOptions = LayoutOptions.Start;
        _visualMonitorPanel.VerticalOptions = LayoutOptions.End;
        _visualMonitorPanel.Margin = new Thickness(8);
        _visualMonitorPanel.IsVisible = false;
        _visualMonitorPanel.ZIndex = 6;
        Host.Overlays.AddOverlay(_visualMonitorPanel);
        AttachPanelDrag(_visualMonitorTitle, _visualMonitorPanel, MoveVisualMonitorPanel);
        _cameraPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == VisualElement.IsVisibleProperty.PropertyName && !_cameraPanel.IsVisible) PauseVisualFeed();
        };
        _visualMonitorPanel.SizeChanged += (_, _) => MoveVisualMonitorPanel(_visualMonitorPanel.TranslationX, _visualMonitorPanel.TranslationY);
    }

    // The monitor cards follow the chat area's size (the camera monitors' bottom left, the sound monitors' bottom right).
    private void UpdateMonitorPanelSizes()
    {
        if (_visualMonitorPanel != null)
        {
            _visualMonitorPanel.WidthRequest = Math.Min(310, Math.Max(180, ChatSize.Width - 16));
            _visualMonitorPanel.MaximumHeightRequest = Math.Max(90, Math.Min(220, ChatSize.Height - 16));
            MoveVisualMonitorPanel(_visualMonitorPanel.TranslationX, _visualMonitorPanel.TranslationY);
        }
        if (_soundPanel != null)
        {
            _soundPanel.WidthRequest = Math.Min(310, Math.Max(180, ChatSize.Width - 16));
            _soundPanel.MaximumHeightRequest = Math.Max(90, Math.Min(220, ChatSize.Height - 16));
            MoveSoundPanel(_soundPanel.TranslationX, _soundPanel.TranslationY);
        }
    }

    // The card sits at the bottom left, so it moves right (positive X) and up (negative Y) from there.
    private void MoveVisualMonitorPanel(double x, double y)
    {
        _visualMonitorPanel.TranslationX = Math.Clamp(x, 0, Math.Max(0, ChatSize.Width - _visualMonitorPanel.Width - 16));
        _visualMonitorPanel.TranslationY = Math.Clamp(y, -Math.Max(0, ChatSize.Height - _visualMonitorPanel.Height - 16), 0);
    }

    private Task DisposeVisualMonitoringAsync()
    {
        PauseVisualFeed();
        if (_visualRuntime == null) return Task.CompletedTask;
        _visualRuntime.Changed -= QueueVisualMonitorUi;
        return _visualRuntime.DisposeAsync();
    }

    private void PauseVisualFeed()
    {
        _visualRuntime?.SetFeedVisible(false);
        if (MainThread.IsMainThread) _visualChime?.Stop();
        else MainThread.BeginInvokeOnMainThread(() => _visualChime?.Stop());
    }

    private void QueueVisualMonitorUi()
    {
        if (Interlocked.Exchange(ref _visualUiQueued, 1) != 0) return;
        MainThread.BeginInvokeOnMainThread(() => { Interlocked.Exchange(ref _visualUiQueued, 0); UpdateVisualMonitorUi(); });
    }

    private void UpdateVisualMonitorUi()
    {
        if (_visualMonitorPanel == null) return;
        UpdateCameraLabel();
        var monitors = _visualRuntime.Snapshot();
        _visualMonitorPanel.IsVisible = monitors.Count > 0;
        while (_visualMonitorRows.Children.Count > 1) _visualMonitorRows.Children.RemoveAt(_visualMonitorRows.Children.Count - 1);
        if (monitors.Count == 0) return;
        if (_camera?.IsRunning != true || !_cameraPanel.IsVisible)
        {
            var open = MonitorButton("Open live feed");
            open.Clicked += async (_, _) =>
            {
                string result = await OpenCameraCoreAsync(CancellationToken.None, false);
                if (_camera?.IsRunning != true) Host.SetStatus(result);
                UpdateVisualMonitorUi();
            };
            _visualMonitorRows.Children.Add(open);
        }
        foreach (var monitor in monitors)
        {
            string detectorName = _visualAgents.Describe().FirstOrDefault(d => d.Id == monitor.AgentId)?.Name ?? monitor.AgentId;
            _visualMonitorRows.Children.Add(new Label { Text = monitor.Condition, FontSize = 12, TextColor = CvViews.Color("ChatPrimaryText") });
            if (MonitorRequestLabel(monitor.Id) is View request) _visualMonitorRows.Children.Add(request);
            _visualMonitorRows.Children.Add(new Label { Text = detectorName + " · " + monitor.State + " · " + monitor.Status,
                FontSize = 11, TextColor = CvViews.Color("ChatSecondaryText") });
            foreach (var action in monitor.Actions)
            {
                string detail = action.ActionId == "speak_text" ? "“" + action.Parameters.GetProperty("text").GetString() + "”"
                    : action.ActionId == "record_clip" ? action.Parameters.GetProperty("seconds").GetInt32() + " s" : null;
                var result = monitor.LastActionResults.FirstOrDefault(r => r.ActionId == action.ActionId);
                _visualMonitorRows.Children.Add(new Label { Text = action.Name + (detail == null ? "" : ": " + detail) +
                    (result == null ? "" : " · " + result.State + " · " + result.Message), FontSize = 11, TextColor = CvViews.Color("ChatSecondaryText") });
            }
            if (monitor.LastEvent != null)
                _visualMonitorRows.Children.Add(new Label { Text = monitor.LastEvent,
                    FontSize = 11, TextColor = CvViews.Color("ChatSecondaryText") });
            var buttons = new HorizontalStackLayout { Spacing = 6 };
            bool pause = monitor.Enabled && monitor.State != VisualMonitorState.Faulted;
            var toggle = MonitorButton(pause ? "Pause" : "Resume");
            var remove = MonitorButton("Remove");
            toggle.Clicked += async (_, _) =>
            {
                string result = await ControlVisualMonitorAsync(monitor.Id.ToString(), pause ? "pause" : "resume", CancellationToken.None);
                if (result.StartsWith("Could not", StringComparison.Ordinal)) Host.SetStatus(result);
            };
            remove.Clicked += async (_, _) => await ControlVisualMonitorAsync(monitor.Id.ToString(), "remove", CancellationToken.None);
            SemanticProperties.SetDescription(toggle, (pause ? "Pause " : "Resume ") + monitor.Condition);
            SemanticProperties.SetDescription(remove, "Remove monitor: " + monitor.Condition);
            buttons.Children.Add(toggle); buttons.Children.Add(remove); _visualMonitorRows.Children.Add(buttons);
        }
    }

    private static Button MonitorButton(string text) => new() { Text = text, FontSize = 12, MinimumHeightRequest = 40, Padding = new Thickness(8, 0) };

    private static bool IsImage(DataContent content) => content.MediaType != null && content.MediaType.StartsWith("image/", StringComparison.Ordinal);

    // The camera monitor tools, with analyze_visual_image over the images attached to the message.
    private IList<AITool> VisualTools(IReadOnlyList<DataContent> attachments) => VisualAgentTools.Create(
        () => JsonSerializer.Serialize(_visualAgents.Describe().ToArray(), VisualJson.Default.VisualAgentDescriptorArray),
        () => JsonSerializer.Serialize(_visualActions.Describe().ToArray(), VisualJson.Default.VisualActionDescriptorArray),
        _visualActions.Describe(), CreateVisualMonitorAsync,
        () => JsonSerializer.Serialize(_visualRuntime.Snapshot().ToArray(), VisualJson.Default.VisualMonitorInfoArray), ControlVisualMonitorAsync,
        (agentId, imageId, token) => AnalyzeVisualImageAsync(agentId, imageId, attachments, token), VisualJson.Default.Options);

    private async Task<string> CreateVisualMonitorAsync(string label, int minimumCount, double confidence, VisualActionRequest[] actions, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            // "thumbs up" and the like are the hand-gesture agent's classes ("thumb up"), "smile" the face agent's, "raises a hand" the pose agent's,
            // "raises a hand and smiles" or "covers their eyes" the holistic agent's;
            // anything else goes as it came.
            string gesture = VisualClasses.CanonicalGesture(label);
            if (gesture != null) { label = gesture; confidence = VisualClasses.GestureConfidence(confidence); }
            // A posture with an expression ("hand raised and smiling") or a hand on the face: the holistic agent's; before the other two, which would take
            // only half of a combination.
            else if (VisualClasses.HolisticForMonitor(label, Prompt) is string holistic) label = holistic;
            else if (VisualClasses.ExpressionForMonitor(label, Prompt) is string expression) label = expression;
            else if (VisualClasses.PostureForMonitor(label, Prompt) is string posture) label = posture;
            var monitor = _visualRuntime.CreateObjectMonitor(label, actions, minimumCount, confidence);
            RememberMonitorRequest(monitor.Id);
            // Repeated requests return the existing rule, including its user-paused status.
            if (monitor.Enabled && monitor.State != VisualMonitorState.Faulted)
            {
                string opened = await OpenCameraAsync(token);
                if (_camera?.IsRunning != true) return MonitorToolResult(_visualRuntime.Snapshot().First(m => m.Id == monitor.Id)) + "\n" + opened;
            }
            return MonitorToolResult(_visualRuntime.Snapshot().First(m => m.Id == monitor.Id));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return "Could not create visual monitor: " + ex.Message; }
    }

    // A creation acknowledgement needs facts, not the full UI snapshot repeated in the context.
    // Which camera is in use is a fact the model should report (it said it couldn't switch, though the app had).
    private string MonitorToolResult(VisualMonitorInfo monitor) => VisualHostCatalog.MonitorResult(monitor) +
#if ANDROID
        " Camera: " + (_cameraFront ? "the selfie (front) camera" : "the back camera") + ".";
#else
        "";
#endif

    private async Task<string> ControlVisualMonitorAsync(string monitorId, string operation, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParse(monitorId, out var id)) return "Could not control monitor: invalid ID. Use list_visual_monitors.";
            switch (operation?.Trim().ToLowerInvariant())
            {
                case "pause": _visualRuntime.SetEnabled(id, false); break;
                case "resume":
                    _visualRuntime.SetEnabled(id, true);
                    string opened = await OpenCameraAsync(token);
                    if (_camera?.IsRunning != true) return "Monitor retained but paused. " + opened;
                    break;
                case "remove": _visualRuntime.Remove(id); ForgetMonitorRequest(id); return "Monitor removed.";
                default: return "Could not control monitor: operation must be pause, resume or remove.";
            }
            return JsonSerializer.Serialize(_visualRuntime.Snapshot().First(m => m.Id == id), VisualJson.Default.VisualMonitorInfo);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return "Could not control visual monitor: " + ex.Message; }
    }

    private async Task<string> AnalyzeVisualImageAsync(string agentId, string imageId, IReadOnlyList<DataContent> attachments, CancellationToken token)
    {
        try
        {
            var images = attachments.Where(IsImage).ToArray();
            if (imageId == null || !imageId.StartsWith("image-", StringComparison.Ordinal) ||
                !int.TryParse(imageId.Substring(6), out int number) || number < 1 || number > images.Length)
                return "No attached image with that reference in this message. Attach an image, then use image-1, image-2, etc.";
            _visualAgents.Get(agentId); // Reject unavailable agents before decoding.
            byte[] bytes = images[number - 1].Data.ToArray();
            return await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                using var mat = new Mat();
                CvInvoke.Imdecode(bytes, ImreadModes.ColorBgr, mat);
                if (mat.IsEmpty) return "The attached image could not be decoded.";
                using var frame = CopyVisualFrame(mat, "image-" + number);
                var result = await _visualRuntime.AnalyzeImageAsync(agentId, frame, token);
                var observations = result.Observations.Where(o => o.Confidence >= 0.5).OrderByDescending(o => o.Confidence).ToArray();
                return JsonSerializer.Serialize(new VisualImageSummary { AgentId = result.AgentId, SourceId = result.SourceId,
                    FrameId = result.FrameId, CapturedAt = result.CapturedAt, Count = observations.Length,
                    Observations = observations.Take(20).ToArray(), Truncated = observations.Length > 20 }, VisualJson.Default.VisualImageSummary);
            }, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return "Could not analyze image: " + ex.Message; }
    }

    private void QueueVisualFrame(LiveCamera camera, Mat frame)
    {
        if (_camera != camera || !_visualRuntime.WantsFrames) return;
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastVisualFrame, now) < TimeSpan.FromMilliseconds(200)) return;
        _lastVisualFrame = now;
        _visualRuntime.SubmitFrame(CopyVisualFrame(frame, "camera"));
    }

    private VisualFrame CopyVisualFrame(Mat mat, string source)
    {
        int stride = checked(mat.Width * 3), length = checked(stride * mat.Height);
        byte[] pixels = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            for (int y = 0; y < mat.Height; y++) Marshal.Copy(IntPtr.Add(mat.DataPointer, y * mat.Step), pixels, y * stride, stride);
            return new VisualFrame(pixels.AsMemory(0, length), mat.Width, mat.Height, stride, source,
                Interlocked.Increment(ref _visualFrameId), DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency), () => ArrayPool<byte>.Shared.Return(pixels));
        }
        catch { ArrayPool<byte>.Shared.Return(pixels); throw; }
    }
}
