// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
namespace Maui.Demo.Lite;

// What the camera and sound monitors show about themselves: the user's own request behind each monitor (kept in memory with the monitor, not in
// private chats), and the camera preview's label, which says what the camera is doing - watching for monitors, recording a clip, taking a photo.
public partial class LiteRtLmChatPage
{
    private readonly Dictionary<Guid, string> _monitorRequests = new();
    private readonly HashSet<Guid> _expandedRequests = new();
    private readonly List<(object Owner, string Text, DateTime? Until)> _cameraActivities = new();
    private IDispatcherTimer _cameraActivityTimer;

    // The message being sent is the request: the monitor is created by the model's tool call during it. A monitor that already existed keeps
    // the request that created it.
    private void RememberMonitorRequest(Guid id)
    {
        if (IsPrivateChat || _monitorRequests.ContainsKey(id)) return;
        string request = VisualHostCatalog.CleanRequest(_routePrompt);
        if (request != null) _monitorRequests[id] = request;
    }

    private void ForgetMonitorRequest(Guid id)
    {
        _monitorRequests.Remove(id);
        _expandedRequests.Remove(id);
    }

    private void ForgetMonitorRequests()
    {
        _monitorRequests.Clear();
        _expandedRequests.Clear();
    }

    // "You asked: “...”" under a monitor's description: two lines, all of it when tapped.
    private View MonitorRequestLabel(Guid id)
    {
        if (!_monitorRequests.TryGetValue(id, out string request)) return null;
        bool expanded = _expandedRequests.Contains(id);
        var label = new Label
        {
            Text = "You asked: “" + request + "”", FontSize = 11, FontAttributes = FontAttributes.Italic, TextColor = Theme.SecondaryText,
            MaxLines = expanded ? int.MaxValue : 2, LineBreakMode = expanded ? LineBreakMode.WordWrap : LineBreakMode.TailTruncation
        };
        SemanticProperties.SetHint(label, expanded ? "Tap to show less" : "Tap to show all of the request");
        label.OnTap(() =>
        {
            if (!_expandedRequests.Remove(id)) _expandedRequests.Add(id);
            QueueVisualMonitorUi();
            QueueSoundUi();
        });
        return label;
    }

    // An action is running on the camera (a clip, a photo): the label says so, with the seconds left for a clip. Call on the UI thread.
    private object BeginCameraActivity(string text, TimeSpan? duration = null)
    {
        var owner = new object();
        _cameraActivities.Add((owner, text, duration == null ? null : DateTime.UtcNow + duration));
        if (duration != null && _cameraActivityTimer == null)
        {
            _cameraActivityTimer = Dispatcher.CreateTimer();
            _cameraActivityTimer.Interval = TimeSpan.FromSeconds(1);
            _cameraActivityTimer.Tick += (_, _) => UpdateCameraLabel();
            _cameraActivityTimer.Start();
        }
        UpdateCameraLabel();
        return owner;
    }

    private void EndCameraActivity(object owner)
    {
        _cameraActivities.RemoveAll(a => a.Owner == owner);
        if (!_cameraActivities.Any(a => a.Until != null)) { _cameraActivityTimer?.Stop(); _cameraActivityTimer = null; }
        UpdateCameraLabel();
    }

    // The preview's label while the camera runs (the opening and stopped messages are set where they happen).
    private void UpdateCameraLabel()
    {
        if (_cameraLabel == null || _camera?.IsRunning != true) return;
        if (_cameraActivities.Count > 0)
        {
            var (_, text, until) = _cameraActivities[^1];
            int left = until == null ? 0 : (int)Math.Ceiling(Math.Max(0, (until.Value - DateTime.UtcNow).TotalSeconds));
            _cameraLabel.Text = "● " + text + (until == null ? "…" : " · " + left + " s left");
            return;
        }
        int watching = _visualRuntime.Snapshot().Count(m => m.Enabled);
        _cameraLabel.Text = watching > 0 ? "Watching · " + watching + (watching == 1 ? " monitor" : " monitors") : "Live camera";
        if (_cameraFront) _cameraLabel.Text += " · selfie";
    }
}
#endif
