// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Diagnostics;
using System.Text;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Meeting notes (record_meeting, <see cref="AgentTools.RecordMeeting"/>): the tool call lasts as long as the recording. The
/// microphone's samples (<see cref="MicrophoneSoundSource"/>, through <see cref="MicrophoneGate"/>, so other listeners pause)
/// are cut into 30-50 s pieces at quiet moments (<see cref="MeetingAudioChunker"/>), and each piece is written down by the
/// platform's speech recognizer (<see cref="SpeechRecognizer"/>: on the device where it can be) while the recording goes on,
/// so the card shows the transcript as it grows. Stop (the card, or <see cref="Stop"/> from elsewhere - e.g. an app's
/// notification, with a foreground service it keeps running through <see cref="SyncBackgroundService"/>) writes down the
/// rest, then the model writes the notes in requests of its own (<see cref="MeetingNotes"/>: section summaries first when
/// the transcript is longer than the window), shown in the card; the notes and the transcript go to
/// <see cref="WrittenDocumentsFeature"/> as a Word document, the transcript to <see cref="TranscriptAttacher"/> (an app
/// attaches it to the chat as its document, so later questions get the passages they need), and the notes back to the
/// model. The tool comes with <see cref="WrittenDocumentsFeature"/>'s group, which must be on the page too. Android 13+,
/// iOS and Mac Catalyst (<see cref="Supported"/>). With <see cref="Voiceprint"/> the speakers are labelled: pieces also end
/// at pauses once 2 s long (people take turns at pauses), each written-down piece gets its voice's fingerprint, and after
/// Stop they are grouped by voice (<see cref="MeetingTranscript.LabelSpeakers"/>) - "Speaker 1", "Speaker 2"... in the
/// transcript, the notes' prompts and the document.
/// </summary>
public sealed class MeetingFeature : ChatFeature
{
    /// <summary>The instructions that go with record_meeting.</summary>
    public const string Instructions = ChatWrittenDocuments.MeetingInstructions;

    private static readonly TimeSpan MeetingLimit = TimeSpan.FromHours(3);

    private MeetingSession _meeting;

    /// <inheritdoc/>
    public override string Name => "meetings";

    /// <inheritdoc/>
    public override string Title => "Meeting notes";

    /// <summary>Always on (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <summary>The meeting being recorded: its title ("" without one), or null.</summary>
    public string ActiveTitle => _meeting?.Title;

    /// <summary>Called when a recording starts and when it ends: an app keeps a foreground service running meanwhile (Android); or null.</summary>
    public Func<Task> SyncBackgroundService { get; set; }

    /// <summary>Given the transcript (as a text file's bytes and name) when the notes are done: an app attaches it to the chat as its document; or null.</summary>
    public Action<byte[], string> TranscriptAttacher { get; set; }

    /// <summary>
    /// The fingerprint of a piece's voice (16 kHz mono samples to a length-1 vector, or null for too little speech), for
    /// labelling speakers - e.g. Emgu.LiteRT.Models' SpeakerVoiceprints, prepared on its first call; or null for no labels.
    /// Called off the UI thread, one piece at a time.
    /// </summary>
    public Func<float[], CancellationToken, Task<float[]>> Voiceprint { get; set; }

    /// <summary>The shortest piece that ends at a pause when speakers are labelled, in seconds.</summary>
    public const double TurnSeconds = 2;

    /// <summary>End the recording (the notes are then written, unless the chat is closing).</summary>
    /// <param name="reason">Why, for the log ("the user pressed Stop in the notification")</param>
    public void Stop(string reason) => _meeting?.Ended.TrySetResult(reason);

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override void OnDetached()
    {
        Stop("the chat was closed");
        base.OnDetached();
    }

    private sealed class MeetingSession
    {
        public string Title = "";
        public DateTime Started;
        public MeetingAudioChunker Chunker = new MeetingAudioChunker(16000, 30, 50);
        public Func<float[], CancellationToken, Task<float[]>> Voiceprint;
        public string VoiceprintProblem;
        public readonly MeetingTranscript Transcript = new MeetingTranscript();
        public readonly TaskCompletionSource<string> Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly Queue<MeetingAudioChunk> Pending = new();
        public readonly SemaphoreSlim PendingSignal = new(0);
        public volatile bool Paused, Stopping;
        public int Failures;
        public Label Time, Status, Preview;
        public Button Pause, Stop;
        public VerticalStackLayout Content;
    }

    /// <summary>Whether meetings can be recorded here: a speech recognizer on Android 13+, iOS or Mac Catalyst.</summary>
    public static bool Supported =>
#if ANDROID || IOS || MACCATALYST
        SpeechRecognizer.IsSupported;
#else
        false;
#endif

    /// <summary>The record_meeting tool, where supported and not in a hands-free chat (the microphone is the chat's then).</summary>
    /// <param name="handsFree">Whether the chat is hands-free</param>
    public IEnumerable<AITool> Tools(bool handsFree) =>
        Supported && !handsFree ? new[] { AgentTools.RecordMeeting(RecordMeetingAsync) } : Array.Empty<AITool>();

    private Task SyncBackgroundServiceAsync() => SyncBackgroundService?.Invoke() ?? Task.CompletedTask;

    private async Task<string> RecordMeetingAsync(string title, CancellationToken token)
    {
#if ANDROID || IOS || MACCATALYST
        if (!Supported)
            return "Writing down speech needs Android 13 or later with a speech recognizer, an iPhone, iPad or a Mac. Tell the user.";
        if (_meeting != null)
            return "A meeting is already being recorded. Tell the user.";
        var session = new MeetingSession { Title = title ?? "", Started = DateTime.Now, Voiceprint = Voiceprint };
        if (session.Voiceprint != null)
            session.Chunker = new MeetingAudioChunker(16000, 30, 50, TurnSeconds);
        await MainThread.InvokeOnMainThreadAsync(() => AddMeetingCard(session));

        var source = new MicrophoneSoundSource();
        source.Samples += samples =>
        {
            if (session.Paused || session.Stopping) return;
            IList<MeetingAudioChunk> chunks;
            lock (session.Chunker) chunks = session.Chunker.Add(samples);
            foreach (MeetingAudioChunk chunk in chunks) QueueMeetingChunk(session, chunk);
        };
        await MicrophoneGate.EnterAsync();
        bool started;
        try { started = await MainThread.InvokeOnMainThreadAsync(source.StartAsync); }
        catch (Exception ex)
        {
            MicrophoneGate.Exit();
            await MainThread.InvokeOnMainThreadAsync(() => EndMeetingCard(session, "Couldn't record: " + ex.Message));
            return "Couldn't record: " + ex.Message + ". Tell the user.";
        }
        if (!started)
        {
            MicrophoneGate.Exit();
            await MainThread.InvokeOnMainThreadAsync(() => EndMeetingCard(session, "Microphone access was denied."));
            return "Microphone access was denied, so nothing could be recorded. Tell the user they can allow it in the system settings.";
        }
        _meeting = session;
#if IOS
        // Without a background audio mode, iOS stops the microphone when the screen locks.
        MainThread.BeginInvokeOnMainThread(() => DeviceDisplay.Current.KeepScreenOn = true);
#endif
        await SyncBackgroundServiceAsync();
        Task transcribing = TranscribeMeetingAsync(session);

        string ending;
        using (token.Register(() => session.Ended.TrySetResult("the reply was stopped")))
            ending = await WatchMeetingAsync(session);

        // Stop recording and write down the rest (also when the reply was stopped: the transcript is still saved).
        session.Stopping = true;
        try { await source.StopAsync(); } catch (Exception ex) { Debug.WriteLine("Stopping the microphone failed: " + ex.Message); }
        MicrophoneGate.Exit();
        MeetingAudioChunk last;
        TimeSpan duration;
        lock (session.Chunker)
        {
            last = session.Chunker.Flush();
            duration = session.Chunker.Recorded;
        }
        if (last != null) QueueMeetingChunk(session, last);
        session.PendingSignal.Release();
        _meeting = null;
        _ = SyncBackgroundServiceAsync();
#if IOS
        MainThread.BeginInvokeOnMainThread(() => DeviceDisplay.Current.KeepScreenOn = false);
#endif
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            session.Pause.IsVisible = false;
            session.Stop.IsVisible = false;
            session.Time.Text = MeetingNotes.FormatTime(duration);
            session.Status.Text = "Writing down the last part…";
        });
        Host.SetStatus("Writing down the last part of the meeting...");
        await transcribing;

        string heading = string.IsNullOrWhiteSpace(session.Title) ? "Meeting notes " + session.Started.ToString("yyyy-MM-dd HH.mm") : session.Title;
        int words = session.Transcript.WordCount;
        int speakers = session.Voiceprint != null ? session.Transcript.LabelSpeakers() : 0;
        if (session.Transcript.IsEmpty)
        {
            await MainThread.InvokeOnMainThreadAsync(() => EndMeetingCard(session, "Nothing was heard in " + MeetingNotes.FormatTime(duration) + "."));
            return AgentTools.MeetingResultText(duration, 0, null, null,
                session.Failures > 0 ? "Speech recognition failed, so nothing was written down." : "No speech was heard.");
        }

        // The notes, by the model; if that fails or the reply was stopped, the transcript is still saved.
        string notes = null, problem = null;
        if (!token.IsCancellationRequested)
        {
            try
            {
                Host.SetStatus("Writing the meeting notes...");
                int maxPrompt = Host.ContextWindow - MeetingNotes.NotesTokens - 300;
                var progress = new Progress<string>(text => MainThread.BeginInvokeOnMainThread(() => session.Status.Text = text));
                notes = await MeetingNotes.WriteAsync(session.Transcript, session.Title, duration,
                    (prompt, maxTokens, _) => Host.AskModelOnceAsync(prompt, maxTokens), Host.TokenCounter, maxPrompt, progress, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                problem = "The notes couldn't be written (" + ex.Message + "); the transcript was saved.";
                Debug.WriteLine("Meeting notes failed: " + ex);
            }
            catch (OperationCanceledException) { }
        }

        GeneratedDocument document = DocumentWriter.Create(heading,
            MeetingNotes.DocumentMarkdown(heading, session.Started, duration, notes, session.Transcript), "word");
        // Saved with the reply, with a card (WrittenDocumentsFeature).
        await Host.Services.Get<WrittenDocumentsFeature>().SaveAsync(document, CancellationToken.None);
        string transcriptName = DocumentWriter.FileName(heading + " transcript", ".txt");
        string transcriptText = "Transcript of " + heading + ", " + session.Started.ToString("f") + "\n\n" + session.Transcript.Text();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            session.Status.Text = MeetingNotes.FormatTime(duration) + " · " + words + " words written down"
                + (speakers > 1 ? " · " + speakers + " speakers" : "")
                + (session.VoiceprintProblem != null ? "\nSpeakers weren't told apart: " + session.VoiceprintProblem : "")
                + (problem != null ? "\n" + problem : notes == null ? "\nThe transcript was saved." : "");
            session.Preview.IsVisible = false;
            if (notes != null)
            {
                var view = new ReplyView { Text = notes, FontSize = 14, Margin = new Thickness(0, 6, 0, 0) };
                ChatResources.SetThemeColor(view, ReplyView.TextColorProperty, "ChatPrimaryText");
                session.Content.Children.Add(view);
            }
            // Questions about the meeting later get the transcript's passages they need, as for an attached document.
            TranscriptAttacher?.Invoke(Encoding.UTF8.GetBytes(transcriptText), transcriptName);
            Host.SetStatus("Generating...");
        });
        return AgentTools.MeetingResultText(duration, words, notes, document.FileName, problem);
#else
        await Task.CompletedTask;
        return "Recording meetings isn't available on this device. Tell the user.";
#endif
    }

    private static void QueueMeetingChunk(MeetingSession session, MeetingAudioChunk chunk)
    {
        lock (session.Pending) session.Pending.Enqueue(chunk);
        session.PendingSignal.Release();
    }

    // Writes down each piece as it comes, one at a time, until the recording has stopped and every piece is done.
    private async Task TranscribeMeetingAsync(MeetingSession session)
    {
        while (true)
        {
            await session.PendingSignal.WaitAsync();
            while (true)
            {
                MeetingAudioChunk chunk;
                lock (session.Pending)
                {
                    if (session.Pending.Count == 0) break;
                    chunk = session.Pending.Dequeue();
                }
                if (chunk.IsSilent) continue;
                string text = null;
                for (int attempt = 0; attempt < 2 && text == null; attempt++)
                {
                    try { text = await SpeechRecognizer.TranscribeAsync(chunk.ToWav()); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Writing down a meeting piece failed: " + ex.Message);
                        if (attempt == 0) await Task.Delay(3000);
                        else
                        {
                            session.Failures++;
                            string message = ex.Message;
                            MainThread.BeginInvokeOnMainThread(() => session.Status.Text = "Couldn't write down " + MeetingNotes.FormatTime(chunk.Start)
                                + "-" + MeetingNotes.FormatTime(chunk.Start + chunk.Duration) + ": " + message);
                        }
                    }
                }
                session.Transcript.Add(chunk.Start, text, await VoiceprintOfAsync(session, chunk, text), chunk.Duration.TotalSeconds);
                ShowMeetingProgress(session);
            }
            bool done;
            lock (session.Pending) done = session.Stopping && session.Pending.Count == 0;
            if (done) return;
        }
    }

    // A written-down piece's voice fingerprint, when speakers are labelled; after a failure (e.g. the model couldn't be
    // downloaded) no more are asked for, and the card says why.
    private static async Task<float[]> VoiceprintOfAsync(MeetingSession session, MeetingAudioChunk chunk, string text)
    {
        if (session.Voiceprint == null || session.VoiceprintProblem != null || string.IsNullOrWhiteSpace(text))
            return null;
        try { return await Task.Run(() => session.Voiceprint(chunk.Samples, CancellationToken.None)); }
        catch (Exception ex)
        {
            Debug.WriteLine("A meeting piece's voiceprint failed: " + ex);
            session.VoiceprintProblem = ex.Message;
            return null;
        }
    }

    // Updates the card's time twice a second until the recording ends: Stop, the reply stopped, the chat closed, or the limit.
    private async Task<string> WatchMeetingAsync(MeetingSession session)
    {
        while (true)
        {
            if (await Task.WhenAny(session.Ended.Task, Task.Delay(500)) == session.Ended.Task)
                return await session.Ended.Task;
            TimeSpan recorded;
            lock (session.Chunker) recorded = session.Chunker.Recorded;
            if (recorded >= MeetingLimit)
                return "the " + (int)MeetingLimit.TotalHours + "-hour limit was reached";
            MainThread.BeginInvokeOnMainThread(() => session.Time.Text = (session.Paused ? "⏸ " : "● ") + MeetingNotes.FormatTime(recorded));
        }
    }

    private void ShowMeetingProgress(MeetingSession session)
    {
        string tail = session.Transcript.Tail(320);
        int pending;
        lock (session.Pending) pending = session.Pending.Count;
        int words = session.Transcript.WordCount;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            session.Preview.Text = tail;
            session.Preview.IsVisible = tail.Length > 0;
            session.Status.Text = words + " words written down" + (session.Stopping ? pending > 0 ? " · finishing the last " + (pending + 1) + " pieces…" : "" : "");
        });
    }

    private void AddMeetingCard(MeetingSession session)
    {
        Color accent = ChatResources.FindColor("ChatAccent");
        session.Time = new Label { Text = "● 0:00", FontSize = 28 };
        ChatResources.ApplyStyle(session.Time, "ChatListTitleStyle");
        session.Time.FontSize = 28;
        session.Status = new Label { Text = "Recording. Make sure everyone knows they are being recorded. What is said is written down as it goes." };
        ChatResources.ApplyStyle(session.Status, "ChatListDetailStyle");
        session.Preview = new Label { IsVisible = false, LineBreakMode = LineBreakMode.WordWrap };
        ChatResources.ApplyStyle(session.Preview, "ChatSectionTextStyle");
        session.Pause = new Button { Text = "Pause", FontSize = 14, BackgroundColor = Colors.Transparent, TextColor = accent,
            BorderColor = accent, BorderWidth = 1, CornerRadius = 12, Padding = new Thickness(16, 6), MinimumHeightRequest = 40 };
        session.Pause.Clicked += (_, _) =>
        {
            session.Paused = !session.Paused;
            session.Pause.Text = session.Paused ? "Resume" : "Pause";
            SemanticProperties.SetDescription(session.Pause, session.Paused ? "Resume recording" : "Pause recording");
        };
        SemanticProperties.SetDescription(session.Pause, "Pause recording");
        session.Stop = new Button { Text = "Stop and write notes", FontSize = 14, BackgroundColor = accent, TextColor = Colors.White,
            CornerRadius = 12, Padding = new Thickness(16, 6), MinimumHeightRequest = 40 };
        session.Stop.Clicked += (_, _) => session.Ended.TrySetResult("the user pressed Stop");
        var heading = new Label { Text = "\U0001F399  " + (string.IsNullOrWhiteSpace(session.Title) ? "Meeting notes" : session.Title) };
        ChatResources.ApplyStyle(heading, "ChatListTitleStyle");
        session.Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                heading,
                session.Time, session.Status, session.Preview,
                new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { session.Stop, session.Pause } }
            }
        };
        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14, 12), Margin = new Thickness(6, 2), MaximumWidthRequest = 520, HorizontalOptions = LayoutOptions.Start,
            Content = session.Content
        };
        ChatResources.SetThemeColor(card, VisualElement.BackgroundColorProperty, "ChatSurface");
        ChatResources.SetThemeColor(card, Border.StrokeProperty, "ChatRowBorder");
        Host.AddCardToReply(card);
        Host.SetStatus("Recording the meeting…");
    }

    private static void EndMeetingCard(MeetingSession session, string text)
    {
        session.Status.Text = text;
        session.Pause.IsVisible = false;
        session.Stop.IsVisible = false;
        session.Time.IsVisible = false;
    }
}
