// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents.Audio
{
    /// <summary>A named set of YAMNet (AudioSet) classes that count as one kind of sound, such as a doorbell or a dog barking.</summary>
    public sealed class SoundGroup
    {
        /// <summary>Short identifier, e.g. "doorbell"</summary>
        public string Id { get; }
        /// <summary>Name for people, e.g. "Doorbell"</summary>
        public string Name { get; }
        /// <summary>The exact YAMNet class names; the group's score is the best of them.</summary>
        public IReadOnlyList<string> Classes { get; }
        /// <summary>Other words and phrases the user may use for it.</summary>
        public IReadOnlyList<string> Words { get; }

        internal SoundGroup(string id, string name, string[] classes, params string[] words)
        {
            Id = id; Name = name; Classes = Array.AsReadOnly(classes); Words = Array.AsReadOnly(words);
        }
    }

    /// <summary>The sounds a chat agent can listen for: groups of YAMNet classes with the words people use for them.</summary>
    public static class SoundGroups
    {
        /// <summary>All groups.</summary>
        public static IReadOnlyList<SoundGroup> All { get; } = Array.AsReadOnly(new[]
        {
            new SoundGroup("doorbell", "Doorbell", new[] { "Doorbell", "Ding-dong" }, "door bell", "ding dong", "someone at the door", "visitor"),
            new SoundGroup("knock", "Knocking", new[] { "Knock" }, "knocking", "knock on the door", "someone knocking"),
            new SoundGroup("phone", "Phone ringing", new[] { "Telephone bell ringing", "Ringtone", "Telephone" }, "telephone", "ringtone", "phone ringing", "phone call", "my phone"),
            new SoundGroup("smoke_alarm", "Smoke alarm", new[] { "Smoke detector, smoke alarm", "Fire alarm" }, "smoke detector", "fire alarm", "smoke alarm"),
            new SoundGroup("alarm_clock", "Alarm clock", new[] { "Alarm clock" }, "alarm clock", "wake up alarm"),
            new SoundGroup("alarm", "Alarm", new[] { "Alarm", "Smoke detector, smoke alarm", "Fire alarm", "Car alarm", "Buzzer" }, "alarm", "alarms", "buzzer"),
            new SoundGroup("siren", "Siren", new[] { "Siren", "Civil defense siren", "Police car (siren)", "Ambulance (siren)", "Fire engine, fire truck (siren)", "Emergency vehicle" }, "sirens", "ambulance", "police car", "fire truck", "emergency vehicle"),
            new SoundGroup("dog", "Dog barking", new[] { "Dog", "Bark", "Bow-wow", "Howl", "Yip" }, "dog barking", "barking", "bark", "puppy", "my dog"),
            new SoundGroup("cat", "Cat meowing", new[] { "Cat", "Meow", "Caterwaul" }, "meow", "meowing", "kitten", "my cat"),
            new SoundGroup("baby_cry", "Baby crying", new[] { "Baby cry, infant cry", "Crying, sobbing" }, "baby", "baby crying", "baby cry", "crying", "infant", "child crying"),
            new SoundGroup("speech", "Speech", new[] { "Speech", "Conversation", "Narration, monologue" }, "talking", "voice", "voices", "someone talking", "conversation", "speaking", "people talking"),
            new SoundGroup("laughter", "Laughter", new[] { "Laughter", "Giggle", "Belly laugh", "Chuckle, chortle", "Baby laughter" }, "laugh", "laughing", "giggle"),
            new SoundGroup("shouting", "Shouting or screaming", new[] { "Screaming", "Shout", "Yell", "Children shouting" }, "scream", "screaming", "shout", "yell", "yelling", "shouting"),
            new SoundGroup("cough", "Coughing", new[] { "Cough" }, "coughing"),
            new SoundGroup("sneeze", "Sneezing", new[] { "Sneeze" }, "sneezing"),
            new SoundGroup("snoring", "Snoring", new[] { "Snoring" }, "snore", "snores"),
            new SoundGroup("clapping", "Clapping", new[] { "Clapping", "Applause" }, "clap", "applause", "applauding"),
            new SoundGroup("glass_breaking", "Glass breaking", new[] { "Glass", "Shatter" }, "glass break", "breaking glass", "window breaking", "broken glass", "shatter"),
            new SoundGroup("running_water", "Running water", new[] { "Water tap, faucet", "Sink (filling or washing)", "Bathtub (filling or washing)", "Toilet flush" }, "water running", "water", "tap", "faucet", "sink", "toilet", "bath"),
            new SoundGroup("microwave", "Microwave", new[] { "Microwave oven" }, "microwave beep", "microwave oven"),
            new SoundGroup("car_horn", "Car horn", new[] { "Vehicle horn, car horn, honking", "Toot" }, "horn", "honk", "honking", "car honking"),
            new SoundGroup("music", "Music", new[] { "Music" }, "song", "songs", "radio"),
            new SoundGroup("singing", "Singing", new[] { "Singing", "Choir", "Humming" }, "someone singing", "sing"),
            new SoundGroup("footsteps", "Footsteps", new[] { "Walk, footsteps", "Run" }, "walking", "steps", "someone walking", "someone running"),
            new SoundGroup("typing", "Typing", new[] { "Typing", "Computer keyboard" }, "keyboard", "someone typing"),
            new SoundGroup("thunder", "Thunder", new[] { "Thunder", "Thunderstorm" }, "thunderstorm"),
            new SoundGroup("rain", "Rain", new[] { "Rain", "Raindrop", "Rain on surface" }, "raining"),
            new SoundGroup("bird", "Birds", new[] { "Bird vocalization, bird call, bird song", "Chirp, tweet" }, "birds", "birdsong", "bird song", "chirping"),
            new SoundGroup("vacuum", "Vacuum cleaner", new[] { "Vacuum cleaner" }, "vacuum", "hoover"),
            new SoundGroup("whistle", "Whistling", new[] { "Whistling", "Whistle", "Steam whistle" }, "whistle", "kettle", "kettle whistle")
        });

        /// <summary>The group names for a tool description, e.g. "doorbell, knock, phone, ...".</summary>
        public static string Ids => string.Join(", ", All.Select(g => g.Id));

        private static string Normalize(string text) =>
            " " + string.Join(" ", new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) + " ";

        /// <summary>
        /// The group the user's words name: an id or name, one of its words or phrases (the longest match wins), or, when the YAMNet
        /// <paramref name="labels"/> are given, an exact class name as a group of its own. Null if nothing matches.
        /// </summary>
        public static SoundGroup Find(string text, IReadOnlyList<string> labels = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string said = Normalize(text);
            SoundGroup best = null;
            int bestLength = 0;
            foreach (var group in All)
                foreach (string phrase in new[] { group.Id.Replace('_', ' '), group.Name }.Concat(group.Words))
                {
                    string p = Normalize(phrase);
                    if (said.IndexOf(p, StringComparison.Ordinal) >= 0 && p.Length > bestLength) { best = group; bestLength = p.Length; }
                }
            if (best != null) return best;
            string label = labels?.FirstOrDefault(l => string.Equals(l, text.Trim(), StringComparison.OrdinalIgnoreCase));
            return label == null ? null : new SoundGroup(label.ToLowerInvariant(), label, new[] { label });
        }
    }

    /// <summary>Phase of a sound episode.</summary>
    public enum SoundEventPhase
    {
        /// <summary>The sound was confirmed.</summary>
        Started,
        /// <summary>The sound stopped for long enough that the monitor is armed again.</summary>
        Ended
    }

    /// <summary>A change in whether a monitored sound is present.</summary>
    public sealed class SoundEvent
    {
        /// <summary>The monitor that emitted it.</summary>
        public Guid MonitorId { get; internal set; }
        /// <summary>Shared by the Started and Ended of one episode.</summary>
        public Guid EpisodeId { get; internal set; }
        /// <summary>Started or Ended</summary>
        public SoundEventPhase Phase { get; internal set; }
        /// <summary>Monotonic time of the analyzed window.</summary>
        public TimeSpan Time { get; internal set; }
        /// <summary>The group's best score in that window.</summary>
        public double Score { get; internal set; }
        /// <summary>The group's best-scoring class in that window.</summary>
        public string Label { get; internal set; }
        /// <summary>When it was detected (local time), for display.</summary>
        public DateTimeOffset At { get; internal set; }
    }

    /// <summary>What a monitor does when its sound is heard, besides showing it in the monitor's card.</summary>
    public sealed class SoundAlert
    {
        /// <summary>Play the host's alert chime.</summary>
        public bool Chime { get; }
        /// <summary>Words to speak (at most 200 characters), or null.</summary>
        public string SpeakText { get; }
        /// <summary>Seconds of video to record with the host's camera (3 to 30), or 0 for none.</summary>
        public int ClipSeconds { get; }
        /// <summary>Take a full-resolution photo with the host's camera.</summary>
        public bool TakePhoto { get; }

        /// <summary>The shortest and longest clip.</summary>
        public const int MinimumClipSeconds = 3, MaximumClipSeconds = 30, DefaultClipSeconds = 10;

        /// <summary>Creates an alert, trimming the text. <paramref name="clipSeconds"/> is 0 for no clip, else 3 to 30.</summary>
        public SoundAlert(bool chime = false, string speakText = null, int clipSeconds = 0, bool takePhoto = false)
        {
            if (clipSeconds != 0 && (clipSeconds < MinimumClipSeconds || clipSeconds > MaximumClipSeconds))
                throw new ArgumentException("The clip must be " + MinimumClipSeconds + " to " + MaximumClipSeconds + " seconds long.");
            ClipSeconds = clipSeconds; TakePhoto = takePhoto;
            string text = string.IsNullOrWhiteSpace(speakText) ? null : speakText.Trim();
            if (text != null && text.Length > 200) throw new ArgumentException("The spoken message can have at most 200 characters.");
            Chime = chime; SpeakText = text;
        }
        /// <summary>Whether it does anything.</summary>
        public bool IsEmpty => !Chime && SpeakText == null && ClipSeconds == 0 && !TakePhoto;
        internal string Key => (Chime ? "chime" : "") + "|" + (SpeakText ?? "") + "|" + ClipSeconds + (TakePhoto ? "|photo" : "");
    }

    /// <summary>Pure temporal state machine: advance it with each window's score and its monotonic time.</summary>
    /// <remarks>
    /// A sound starts when the score has been at or above the threshold for <see cref="MinimumDuration"/> (a run continues across gaps of up to
    /// <see cref="Gap"/>, since a barking dog is not loud in every window) - at once when it is zero. It ends, and the monitor is armed again,
    /// when <see cref="Release"/> passes without it, so a doorbell rung three times in a row is one episode.
    /// </remarks>
    public sealed class SoundPresenceMonitor
    {
        /// <summary>The longest pause that still continues a run.</summary>
        public static readonly TimeSpan Gap = TimeSpan.FromSeconds(2);
        /// <summary>Id</summary>
        public Guid Id { get; } = Guid.NewGuid();
        /// <summary>Score at or above which a window counts as the sound.</summary>
        public double Threshold { get; }
        /// <summary>How long the sound must last before it counts; zero for the first window.</summary>
        public TimeSpan MinimumDuration { get; }
        /// <summary>How long without it before the episode ends.</summary>
        public TimeSpan Release { get; }
        /// <summary>Whether an episode is going on.</summary>
        public bool IsActive { get; private set; }
        private Guid _episode;
        private TimeSpan? _last, _runStart, _lastPositive;

        /// <summary>Creates a state machine.</summary>
        public SoundPresenceMonitor(double threshold, TimeSpan minimumDuration, TimeSpan? release = null)
        {
            if (double.IsNaN(threshold) || threshold <= 0 || threshold > 1) throw new ArgumentOutOfRangeException(nameof(threshold));
            if (minimumDuration < TimeSpan.Zero || minimumDuration > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(minimumDuration));
            Threshold = threshold; MinimumDuration = minimumDuration; Release = release ?? TimeSpan.FromSeconds(3);
        }

        /// <summary>Forget the run and the episode (without an Ended event).</summary>
        public void Reset() { IsActive = false; _last = _runStart = _lastPositive = null; _episode = Guid.Empty; }

        /// <summary>Processes one window; returns Started or Ended, or null. Repeated and out-of-order times are ignored.</summary>
        public SoundEvent Advance(TimeSpan time, double score, string label = null)
        {
            if (_last.HasValue && time <= _last.Value) return null;
            if (_last.HasValue && time - _last.Value > TimeSpan.FromSeconds(5)) Reset();   // listening was interrupted: don't claim anything about the gap
            _last = time;
            if (score >= Threshold)
            {
                if (!_runStart.HasValue || time - _lastPositive.Value > Gap) _runStart = time;
                _lastPositive = time;
                if (!IsActive && time - _runStart.Value >= MinimumDuration)
                {
                    IsActive = true; _episode = Guid.NewGuid();
                    return new SoundEvent { MonitorId = Id, EpisodeId = _episode, Phase = SoundEventPhase.Started, Time = time, Score = score, Label = label };
                }
                return null;
            }
            if (IsActive)
            {
                if (time - _lastPositive.Value < Release) return null;
                var ended = new SoundEvent { MonitorId = Id, EpisodeId = _episode, Phase = SoundEventPhase.Ended, Time = time, Score = score, Label = label };
                Reset(); _last = time;
                return ended;
            }
            if (_runStart.HasValue && time - _lastPositive.Value > Gap) _runStart = null;
            return null;
        }
    }

    /// <summary>An immutable view of a monitor, for the UI and the tools.</summary>
    public sealed class SoundMonitorInfo
    {
        /// <summary>Id</summary>
        public Guid Id { get; internal set; }
        /// <summary>The sound group's id or the YAMNet class name.</summary>
        public string Sound { get; internal set; }
        /// <summary>A sentence for people: what it listens for and how.</summary>
        public string Description { get; internal set; }
        /// <summary>Score threshold.</summary>
        public double Threshold { get; internal set; }
        /// <summary>Seconds the sound must last.</summary>
        public double MinimumSeconds { get; internal set; }
        /// <summary>Plays the chime.</summary>
        public bool Chime { get; internal set; }
        /// <summary>Spoken words, or null.</summary>
        public string SpeakText { get; internal set; }
        /// <summary>Seconds of video it records, or 0.</summary>
        public int ClipSeconds { get; internal set; }
        /// <summary>Whether it takes a photo.</summary>
        public bool TakePhoto { get; internal set; }
        /// <summary>Whether it is listening (not paused by the user).</summary>
        public bool Enabled { get; internal set; }
        /// <summary>How many times its sound has been heard.</summary>
        public int Heard { get; internal set; }
        /// <summary>The last time it heard it, or null.</summary>
        public DateTimeOffset? LastHeard { get; internal set; }
        /// <summary>A line about the last event or action failure, or null.</summary>
        public string LastEvent { get; internal set; }
    }

    /// <summary>
    /// Session-only sound monitors: the host feeds it each audio window's class scores (<see cref="Advance"/>), and when a monitored sound is
    /// confirmed the alert callback runs on a separate worker, first come first served, so the audio loop never waits for speech or chimes.
    /// Nothing here records or keeps audio, and no model runs per event: the host's classifier runs once per window for all monitors.
    /// </summary>
    public sealed class SoundMonitorRuntime : IDisposable
    {
        /// <summary>The most monitors at once.</summary>
        public const int MaxMonitors = 8;
        private sealed class Entry
        {
            public SoundPresenceMonitor Monitor;
            public SoundGroup Group;
            public int[] Indices;
            public SoundAlert Alert;
            public string Key;
            public bool Enabled = true;
            public int Heard;
            public DateTimeOffset? LastHeard;
            public string LastEvent;
        }

        private readonly IReadOnlyList<string> _labels;
        private readonly Func<SoundEvent, SoundMonitorInfo, CancellationToken, Task> _alert;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly object _gate = new object();
        private readonly Queue<(SoundEvent, SoundMonitorInfo)> _queue = new Queue<(SoundEvent, SoundMonitorInfo)>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private Task _worker = Task.CompletedTask;
        private bool _disposed;

        /// <summary>Raised when monitors or their status changed (from any thread).</summary>
        public event Action Changed;

        /// <param name="labels">YAMNet's 521 class names, in the model's order</param>
        /// <param name="alert">Runs for each confirmed sound whose monitor has an alert; one at a time</param>
        public SoundMonitorRuntime(IReadOnlyList<string> labels, Func<SoundEvent, SoundMonitorInfo, CancellationToken, Task> alert = null)
        {
            _labels = labels ?? throw new ArgumentNullException(nameof(labels));
            _alert = alert;
        }

        /// <summary>Whether any monitor is enabled, so the host should be listening.</summary>
        public bool WantsAudio { get { lock (_gate) return _entries.Any(e => e.Enabled); } }

        /// <summary>
        /// Creates a monitor. A monitor with the same sound, settings and alert returns the existing one. Throws <see cref="ArgumentException"/>
        /// with a message the model can act on for an unknown sound or a bad value.
        /// </summary>
        public SoundMonitorInfo Create(string sound, double threshold = 0.5, double minimumSeconds = 0, SoundAlert alert = null)
        {
            SoundGroup group = SoundGroups.Find(sound, _labels)
                ?? throw new ArgumentException("Unknown sound '" + sound + "'. Use one of: " + SoundGroups.Ids + ".");
            if (double.IsNaN(threshold) || threshold < 0.1 || threshold > 0.95) throw new ArgumentException("The threshold must be between 0.1 and 0.95 (0.5 is normal).");
            if (double.IsNaN(minimumSeconds) || minimumSeconds < 0 || minimumSeconds > 120) throw new ArgumentException("minimumSeconds must be between 0 and 120.");
            alert = alert ?? new SoundAlert();
            var indices = group.Classes.Select(c => IndexOf(c)).Where(i => i >= 0).ToArray();
            if (indices.Length == 0) throw new ArgumentException("The sound model doesn't know '" + sound + "'.");
            string key = group.Id + "|" + threshold.ToString("R", CultureInfo.InvariantCulture) + "|" + minimumSeconds.ToString("R", CultureInfo.InvariantCulture) + "|" + alert.Key;
            Entry entry;
            lock (_gate)
            {
                ThrowIfDisposed();
                entry = _entries.FirstOrDefault(e => e.Key == key);
                if (entry == null)
                {
                    if (_entries.Count >= MaxMonitors) throw new ArgumentException("There are already " + MaxMonitors + " sound monitors. Remove one first.");
                    entry = new Entry
                    {
                        Monitor = new SoundPresenceMonitor(threshold, TimeSpan.FromSeconds(minimumSeconds)),
                        Group = group, Indices = indices, Alert = alert, Key = key
                    };
                    _entries.Add(entry);
                }
            }
            Changed?.Invoke();
            return Info(entry);
        }

        private int IndexOf(string label)
        {
            for (int i = 0; i < _labels.Count; i++)
                if (_labels[i] == label) return i;
            return -1;
        }

        /// <summary>The monitors, oldest first.</summary>
        public IReadOnlyList<SoundMonitorInfo> Snapshot()
        {
            lock (_gate) return _entries.Select(Info).ToArray();
        }

        private SoundMonitorInfo Info(Entry e)
        {
            var parts = new List<string>();
            if (e.Alert.Chime) parts.Add("play a chime");
            if (e.Alert.SpeakText != null) parts.Add("say \"" + e.Alert.SpeakText + "\"");
            if (e.Alert.TakePhoto) parts.Add("take a photo");
            if (e.Alert.ClipSeconds > 0) parts.Add("record a " + e.Alert.ClipSeconds + " s video clip");
            return new SoundMonitorInfo
            {
                Id = e.Monitor.Id, Sound = e.Group.Id, Threshold = e.Monitor.Threshold, MinimumSeconds = e.Monitor.MinimumDuration.TotalSeconds,
                Chime = e.Alert.Chime, SpeakText = e.Alert.SpeakText, ClipSeconds = e.Alert.ClipSeconds, TakePhoto = e.Alert.TakePhoto, Enabled = e.Enabled, Heard = e.Heard, LastHeard = e.LastHeard, LastEvent = e.LastEvent,
                Description = "Listen for " + e.Group.Name.ToLowerInvariant() +
                    (e.Monitor.MinimumDuration > TimeSpan.Zero ? " lasting at least " + e.Monitor.MinimumDuration.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s" : "") +
                    (parts.Count > 0 ? ", then " + string.Join(" and ", parts) : ", and show it here")
            };
        }

        /// <summary>Pauses or resumes a monitor.</summary>
        public void SetEnabled(Guid id, bool enabled)
        {
            lock (_gate)
            {
                var e = Find(id);
                e.Enabled = enabled;
                if (!enabled) e.Monitor.Reset();
            }
            Changed?.Invoke();
        }

        /// <summary>Removes a monitor.</summary>
        public void Remove(Guid id)
        {
            lock (_gate) _entries.Remove(Find(id));
            Changed?.Invoke();
        }

        /// <summary>Removes all monitors (a new conversation).</summary>
        public void Clear()
        {
            lock (_gate) _entries.Clear();
            Changed?.Invoke();
        }

        private Entry Find(Guid id) => _entries.FirstOrDefault(e => e.Monitor.Id == id) ?? throw new ArgumentException("No sound monitor with that ID. Use list_sound_monitors.");

        /// <summary>One window's class scores (521 values, YAMNet's order) at a monotonic time. Fast; call it from the audio loop.</summary>
        public void Advance(TimeSpan time, float[] scores)
        {
            if (scores == null || scores.Length != _labels.Count) throw new ArgumentException("Expected " + _labels.Count + " scores.", nameof(scores));
            var events = new List<(SoundEvent, SoundMonitorInfo)>();
            bool changed = false;
            lock (_gate)
            {
                if (_disposed) return;
                foreach (var e in _entries)
                {
                    if (!e.Enabled) continue;
                    int best = e.Indices[0];
                    foreach (int i in e.Indices) if (scores[i] > scores[best]) best = i;
                    var ev = e.Monitor.Advance(time, scores[best], _labels[best]);
                    if (ev == null) continue;
                    ev.At = DateTimeOffset.Now;
                    changed = true;
                    if (ev.Phase == SoundEventPhase.Started)
                    {
                        e.Heard++; e.LastHeard = ev.At;
                        e.LastEvent = "Heard " + _labels[best].ToLowerInvariant() + " at " + ev.At.ToString("HH:mm:ss", CultureInfo.CurrentCulture) + " (" + (ev.Score * 100).ToString("0", CultureInfo.InvariantCulture) + "%)";
                        if (!e.Alert.IsEmpty && _alert != null) events.Add((ev, Info(e)));
                    }
                }
            }
            if (changed) Changed?.Invoke();
            if (events.Count == 0) return;
            lock (_gate)
            {
                foreach (var item in events) { if (_queue.Count < 8) _queue.Enqueue(item); }
                if (_worker.IsCompleted) _worker = Task.Run(RunAlertsAsync);
            }
        }

        private async Task RunAlertsAsync()
        {
            while (true)
            {
                (SoundEvent, SoundMonitorInfo) item;
                lock (_gate)
                {
                    if (_queue.Count == 0 || _disposed) return;
                    item = _queue.Dequeue();
                }
                string failure = null;
                try { await _alert(item.Item1, item.Item2, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { failure = "The alert failed: " + ex.Message; }
                if (failure != null)
                {
                    lock (_gate) { var e = _entries.FirstOrDefault(x => x.Monitor.Id == item.Item1.MonitorId); if (e != null) e.LastEvent = failure; }
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Cancels running alerts and stops accepting windows.</summary>
        public void Dispose()
        {
            lock (_gate) { if (_disposed) return; _disposed = true; _queue.Clear(); }
            _stop.Cancel();
        }

        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(SoundMonitorRuntime)); }
    }

    /// <summary>Chat tools for sound monitors, backed by the host (the microphone and the classifier are the host's).</summary>
    public static class SoundMonitorTools
    {
        /// <summary>Guidance for the agent's instructions when these tools are offered.</summary>
        public const string Instructions = "To be alerted when a sound happens (the doorbell, a dog barking, a baby crying, a smoke alarm, a knock...), " +
            "use create_sound_monitor with the sound in the user's words. It listens through the microphone while the app is open; the audio isn't recorded or sent anywhere, " +
            "and each time the sound is heard the app shows it. Set playSound only if the user wants a beep or chime, and speakText only to the exact words the user asked to hear, unchanged. " +
            "When the tool has clipSeconds and takePhoto, set them only if the user asked for a video, clip or photo when the sound is heard (\"when you hear the baby cry, take a video\"); the live camera is used. " +
            "Use list_sound_monitors and control_sound_monitor to pause, resume or remove monitors. After creating one, say in one short sentence what it listens for, and don't repeat the call.";

        /// <summary>The tools; each callback returns the text the model sees.</summary>
        /// <param name="create">sound, minimumSeconds, playSound, speakText, threshold</param>
        /// <param name="list">The monitors as text</param>
        /// <param name="control">monitorId, operation (pause, resume, remove)</param>
        public static IList<AITool> Create(Func<string, double, bool, string, double, CancellationToken, Task<string>> create,
            Func<string> list, Func<string, string, CancellationToken, Task<string>> control)
            => Create((sound, minimumSeconds, playSound, speakText, threshold, clipSeconds, takePhoto, token) => create(sound, minimumSeconds, playSound, speakText, threshold, token),
                list, control, false);

        /// <summary>The same tools, and with <paramref name="cameraActions"/> the monitor can also record a video clip or take a photo (the host's camera).</summary>
        /// <param name="create">sound, minimumSeconds, playSound, speakText, threshold, clipSeconds (0 for none), takePhoto</param>
        /// <param name="list">The monitors as text</param>
        /// <param name="control">monitorId, operation (pause, resume, remove)</param>
        /// <param name="cameraActions">Whether the host can record clips and take photos</param>
        public static IList<AITool> Create(Func<string, double, bool, string, double, int, bool, CancellationToken, Task<string>> create,
            Func<string> list, Func<string, string, CancellationToken, Task<string>> control, bool cameraActions)
        {
            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(
                    ([Description("The sound to listen for, in the user's words, e.g. doorbell, dog, baby_cry, smoke_alarm.")] string sound,
                     [Description("Seconds the sound must last before it counts. Leave out unless the user gave a duration.")] double minimumSeconds = 0,
                     [Description("Set true only if the user asked for a beep, chime or sound when it is heard. Leave out otherwise.")] bool playSound = false,
                     [Description("Leave out unless the user said to say something aloud. Then the words they said to say, verbatim: for \"say hello when...\" it is hello.")] string speakText = null,
                     [Description("Sensitivity from 0.1 to 0.95. Leave out unless the user asked for more or less.")] double threshold = 0.5,
                     CancellationToken cancellationToken = default) => create(sound, minimumSeconds, playSound, speakText, threshold <= 0 ? 0.5 : threshold, 0, false, cancellationToken),
                    "create_sound_monitor",
                    "Listens through the microphone for a kind of sound and alerts when it is heard (shown in the app, plus a chime or spoken words if asked). " +
                    "Sounds: " + SoundGroups.Ids + ". Session only; runs while the app is open. Not for recording or understanding speech."),
                AIFunctionFactory.Create(list, "list_sound_monitors", "Lists the sound monitors, their IDs, states and when they last heard their sound."),
                AIFunctionFactory.Create(
                    ([Description("Monitor ID from create_sound_monitor or list_sound_monitors.")] string monitorId,
                     [Description("One of pause, resume, remove.")] string operation,
                     CancellationToken cancellationToken) => control(monitorId, operation, cancellationToken),
                    "control_sound_monitor", "Pauses, resumes or removes a sound monitor.")
            };
            if (cameraActions)
                tools[0] = AIFunctionFactory.Create(
                    ([Description("The sound to listen for, in the user's words, e.g. doorbell, dog, baby_cry, smoke_alarm.")] string sound,
                     [Description("Seconds the sound must last before it counts. Leave out unless the user gave a duration.")] double minimumSeconds = 0,
                     [Description("Set true only if the user asked for a beep, chime or sound when it is heard. Leave out otherwise.")] bool playSound = false,
                     [Description("Leave out unless the user said to say something aloud. Then the words they said to say, verbatim: for \"say hello when...\" it is hello.")] string speakText = null,
                     [Description("Sensitivity from 0.1 to 0.95. Leave out unless the user asked for more or less.")] double threshold = 0.5,
                     [Description("Set only if the user asked for a video or clip when the sound is heard: its length in seconds, 3 to 30 (10 if they gave none). Leave out otherwise.")] int clipSeconds = 0,
                     [Description("Set true only if the user asked for a photo or picture when the sound is heard. Leave out otherwise.")] bool takePhoto = false,
                     CancellationToken cancellationToken = default) => create(sound, minimumSeconds, playSound, speakText, threshold <= 0 ? 0.5 : threshold, clipSeconds, takePhoto, cancellationToken),
                    "create_sound_monitor",
                    "Listens through the microphone for a kind of sound and alerts when it is heard (shown in the app, plus a chime, spoken words, a video clip or a photo from the live camera if asked). " +
                    "Sounds: " + SoundGroups.Ids + ". Session only; runs while the app is open. Not for recording or understanding speech.");
            return tools;
        }

        /// <summary>One monitor as a line of text for a tool result.</summary>
        public static string Describe(SoundMonitorInfo m)
        {
            var text = new StringBuilder("Sound monitor " + m.Id + ": " + (m.Enabled ? "listening" : "paused") + ". " + m.Description + ".");
            if (m.Heard > 0) text.Append(" Heard " + m.Heard + (m.Heard == 1 ? " time" : " times") + "; " + m.LastEvent + ".");
            return text.ToString();
        }
    }
}
