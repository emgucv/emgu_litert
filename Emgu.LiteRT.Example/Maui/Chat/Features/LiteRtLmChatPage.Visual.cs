// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Microsoft.Extensions.AI;
using Emgu.LiteRT.LM.Agents.Audio;

namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
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
    private static readonly string VisualInstructions = VisualAgentTools.Instructions +
        " Prefer coco-ssd-mobilenet-v3 for ordinary object monitors. Use the experimental coco-yolox-nano when the user asks for YOLOX." +
        " For a hand gesture use the hand-gesture agent and pick the label that matches the user's words: thumbs up = thumb up, thumbs down = thumb down, peace sign or V sign = victory, " +
        "open hand or high five = open palm, fist = closed fist, pointing finger = pointing up, rock sign = i love you. The label is just the class, never prefixed by the agent id, and never person. Raising or waving a hand is not a gesture: that is the body-pose agent. " +
        "It sees upright hands, a gesture held for about a second." +
        " To play a recorded sound when an alert fires - a lullaby, white noise, one of the user's own sounds - select play_file with parameters {sound: its name from list_visual_actions, seconds: how long to keep repeating it (60 if the user wants it to go on without saying for how long; leave out to play it once)}. " +
        " For what a person's body is doing - waving, raising a hand or both arms, standing, sitting, lying down - use the body-pose agent, not hand-gesture and not person: the label is exactly one of " +
        "arms raised (both arms up, hands in the air), hand raised (raising a hand or arm), waving, standing, sitting, lying down. " +
        "It sees one person with the head in view, within a few metres; waving takes about two seconds to show; standing and sitting need the legs in view. It can't detect falls or tell people apart." +
        " For a facial expression - smiling, closing the eyes, winking, opening the mouth, raising the eyebrows - use the face-expression agent, not person: the label is exactly one of " +
        "smiling, eyes closed, winking, mouth open, eyebrows raised. It reads one face looking at the camera, within about two metres. It can't recognise who someone is, " +
        "and it is not for checking whether someone is tired, drowsy or paying attention: decline such requests." +
#if ANDROID
        " The app switches between the back camera and the selfie (front) camera by itself when the user's request says so: don't refuse such a request, use the camera tools as usual and say which camera is used." +
#endif
#if ANDROID
        " For record a video or clip when an object appears, select record_clip with parameters {seconds: the requested length, default 10, from 3 to 30}. " +
        "It records from the live camera with sound and shows the clip in this chat session; no automatic saving or model analysis. A request for both a picture and a clip uses take_snapshot and record_clip. " +
        "For a full resolution, high resolution or high quality photo when an object appears, select take_photo (parameters {}), not take_snapshot; it is slower and shows the full-size photo in this chat session.";
#else
        "";
#endif

    private void BuildVisualMonitoring()
    {
        _visualAgents = new VisualAgentRegistry();
        _visualAgents.Register(new CocoVisualAgent());
        _visualAgents.Register(new CocoVisualAgent(useYoloX: true));
        _visualAgents.Register(new HandVisualAgent());
        _visualAgents.Register(new PoseVisualAgent());
        _visualAgents.Register(new FaceVisualAgent());
        _visualChime = new CameraChimeAction();
        _alertAudio = new AlertAudioCoordinator((text, token) => MainThread.InvokeOnMainThreadAsync(() => ReplySpeech.SpeakAsync(text, token)),
            occurrence => _visualRuntime.Snapshot().Any(m => m.Id == occurrence.MonitorId &&
                m.Actions.Any(a => a.ActionId is "play_sound" or "speak_text")));
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
        _visualMonitorTitle = new Label { Text = "Camera monitors · session only", FontSize = 13, FontFamily = Theme.TitleFont,
            TextColor = Theme.PrimaryText, MinimumHeightRequest = 32, VerticalTextAlignment = TextAlignment.Center };
        SemanticProperties.SetHint(_visualMonitorTitle, "Drag to move the camera monitors card.");
        _visualMonitorRows = new VerticalStackLayout { Spacing = 6, Children = { _visualMonitorTitle } };
        _visualMonitorPanel = Theme.Card(new ScrollView { Content = _visualMonitorRows }, 8);
        _visualMonitorPanel.WidthRequest = 310;
        _visualMonitorPanel.MaximumHeightRequest = 220;
        _visualMonitorPanel.HorizontalOptions = LayoutOptions.Start;
        _visualMonitorPanel.VerticalOptions = LayoutOptions.End;
        _visualMonitorPanel.Margin = new Thickness(8);
        _visualMonitorPanel.IsVisible = false;
        _visualMonitorPanel.ZIndex = 6;
        _mainColumn.Add(_visualMonitorPanel, 0, 1);
        AttachPanelDrag(_visualMonitorTitle, _visualMonitorPanel, MoveVisualMonitorPanel);
        _cameraPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == IsVisibleProperty.PropertyName && !_cameraPanel.IsVisible) PauseVisualFeed();
        };
        _chatView.SizeChanged += (_, _) =>
        {
            _visualMonitorPanel.WidthRequest = Math.Min(310, Math.Max(180, _mainColumn.Width - 16));
            _visualMonitorPanel.MaximumHeightRequest = Math.Max(90, Math.Min(220, _chatView.Height - 16));
            MoveVisualMonitorPanel(_visualMonitorPanel.TranslationX, _visualMonitorPanel.TranslationY);
        };
        _visualMonitorPanel.SizeChanged += (_, _) => MoveVisualMonitorPanel(_visualMonitorPanel.TranslationX, _visualMonitorPanel.TranslationY);
    }

    // The card sits at the bottom left, so it moves right (positive X) and up (negative Y) from there.
    private void MoveVisualMonitorPanel(double x, double y)
    {
        _visualMonitorPanel.TranslationX = Math.Clamp(x, 0, Math.Max(0, _mainColumn.Width - _visualMonitorPanel.Width - 16));
        _visualMonitorPanel.TranslationY = Math.Clamp(y, -Math.Max(0, _chatView.Height - _visualMonitorPanel.Height - 16), 0);
    }

    private Task DisposeVisualMonitoringAsync()
    {
        PauseVisualFeed();
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
                if (_camera?.IsRunning != true) SetStatus(result);
                UpdateVisualMonitorUi();
            };
            _visualMonitorRows.Children.Add(open);
        }
        foreach (var monitor in monitors)
        {
            string detectorName = _visualAgents.Describe().FirstOrDefault(d => d.Id == monitor.AgentId)?.Name ?? monitor.AgentId;
            _visualMonitorRows.Children.Add(new Label { Text = monitor.Condition, FontSize = 12, TextColor = Theme.PrimaryText });
            if (MonitorRequestLabel(monitor.Id) is View request) _visualMonitorRows.Children.Add(request);
            _visualMonitorRows.Children.Add(new Label { Text = detectorName + " · " + monitor.State + " · " + monitor.Status,
                FontSize = 11, TextColor = Theme.SecondaryText });
            foreach (var action in monitor.Actions)
            {
                string detail = action.ActionId == "speak_text" ? "“" + action.Parameters.GetProperty("text").GetString() + "”"
                    : action.ActionId == "record_clip" ? action.Parameters.GetProperty("seconds").GetInt32() + " s" : null;
                var result = monitor.LastActionResults.FirstOrDefault(r => r.ActionId == action.ActionId);
                _visualMonitorRows.Children.Add(new Label { Text = action.Name + (detail == null ? "" : ": " + detail) +
                    (result == null ? "" : " · " + result.State + " · " + result.Message), FontSize = 11, TextColor = Theme.SecondaryText });
            }
            if (monitor.LastEvent != null)
                _visualMonitorRows.Children.Add(new Label { Text = monitor.LastEvent,
                    FontSize = 11, TextColor = Theme.SecondaryText });
            var buttons = new HorizontalStackLayout { Spacing = 6 };
            bool pause = monitor.Enabled && monitor.State != VisualMonitorState.Faulted;
            var toggle = MonitorButton(pause ? "Pause" : "Resume");
            var remove = MonitorButton("Remove");
            toggle.Clicked += async (_, _) =>
            {
                string result = await ControlVisualMonitorAsync(monitor.Id.ToString(), pause ? "pause" : "resume", CancellationToken.None);
                if (result.StartsWith("Could not", StringComparison.Ordinal)) SetStatus(result);
            };
            remove.Clicked += async (_, _) => await ControlVisualMonitorAsync(monitor.Id.ToString(), "remove", CancellationToken.None);
            SemanticProperties.SetDescription(toggle, (pause ? "Pause " : "Resume ") + monitor.Condition);
            SemanticProperties.SetDescription(remove, "Remove monitor: " + monitor.Condition);
            buttons.Children.Add(toggle); buttons.Children.Add(remove); _visualMonitorRows.Children.Add(buttons);
        }
    }
    private static Button MonitorButton(string text) => new() { Text = text, FontSize = 12, MinimumHeightRequest = 40, Padding = new Thickness(8, 0) };

    private IList<AITool> VisualTools(List<PendingAttachment> attachments) => VisualAgentTools.Create(
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
            // "thumbs up" and the like are the hand-gesture agent's classes ("thumb up"), "smile" the face agent's, "raises a hand" the pose agent's;
            // anything else goes as it came.
            string gesture = VisualHostCatalog.CanonicalGesture(label);
            if (gesture != null) { label = gesture; confidence = VisualHostCatalog.GestureConfidence(confidence); }
            else if (VisualHostCatalog.ExpressionForMonitor(label, _routePrompt) is string expression) label = expression;
            else if (VisualHostCatalog.PostureForMonitor(label, _routePrompt) is string posture) label = posture;
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
    private async Task<string> AnalyzeVisualImageAsync(string agentId, string imageId, List<PendingAttachment> attachments, CancellationToken token)
    {
        try
        {
            var images = attachments.Where(a => a.Image != null).ToArray();
            if (imageId == null || !imageId.StartsWith("image-", StringComparison.Ordinal) ||
                !int.TryParse(imageId.Substring(6), out int number) || number < 1 || number > images.Length)
                return "No attached image with that reference in this message. Attach an image, then use image-1, image-2, etc.";
            _visualAgents.Get(agentId); // Reject unavailable agents before decoding.
            return await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                using var mat = new Mat();
                CvInvoke.Imdecode(images[number - 1].Image, ImreadModes.ColorBgr, mat);
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
