// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using System.Diagnostics;
using System.Text;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

// Meeting notes (record_meeting): the tool call lasts as long as the recording. The microphone's samples (MicrophoneSoundSource, through
// MicrophoneGate, so the sound monitors pause) are cut into 30-50 s pieces at quiet moments (MeetingAudioChunker), and each piece is
// written down by the platform's speech recognizer (SpeechRecognizer: on the device where it can be) while the recording goes on, so the
// card shows the transcript as it grows. Stop (the card, or the Android notification - a foreground service keeps recording with the
// screen off) writes down the rest, then the model writes the notes in requests of its own (MeetingNotes: section summaries first when
// the transcript is longer than the window), shown in the card; the notes and the transcript are saved as a Word document with the reply,
// the transcript is attached to the chat as its document - so later questions get the passages they need - and the notes go back to the
// model. Gemma 4 could listen to the audio itself, but at ~25 tokens a second an hour is ~90k tokens, far over its window.
internal sealed class MeetingFeature
{
    public const string Instructions =
        "To record a meeting, lecture or conversation and take notes of it, call record_meeting: it records until the user presses Stop, " +
        "then returns the notes.";

    private static readonly TimeSpan MeetingLimit = TimeSpan.FromHours(3);

    private readonly IChatHost _host;
    private MeetingSession _meeting;

    public MeetingFeature(IChatHost host) { _host = host; }

    // The meeting being recorded: its title ("" without one), or null.
    public string ActiveTitle => _meeting?.Title;

    // Ends the recording (the notes are then written, unless the chat is closing).
    public void Stop(string reason) => _meeting?.Ended.TrySetResult(reason);

    private sealed class MeetingSession
    {
        public string Title = "";
        public DateTime Started;
        public readonly MeetingAudioChunker Chunker = new MeetingAudioChunker(16000, 30, 50);
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

    public static bool Supported =>
#if ANDROID || IOS || MACCATALYST
        SpeechRecognizer.IsSupported;
#else
        false;
#endif

    public IEnumerable<Microsoft.Extensions.AI.AITool> Tools(bool voiceMode) =>
        Supported && !voiceMode ? new[] { AgentTools.RecordMeeting(RecordMeetingAsync) } : Array.Empty<Microsoft.Extensions.AI.AITool>();

    private async Task<string> RecordMeetingAsync(string title, CancellationToken token)
    {
#if ANDROID || IOS || MACCATALYST
        if (!Supported)
            return "Writing down speech needs Android 13 or later with a speech recognizer, an iPhone, iPad or a Mac. Tell the user.";
        if (_meeting != null)
            return "A meeting is already being recorded. Tell the user.";
        var session = new MeetingSession { Title = title ?? "", Started = DateTime.Now };
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
        UiStallWatchdog.Mark("record_meeting: started");
#if IOS
        // Without a background audio mode, iOS stops the microphone when the screen locks.
        MainThread.BeginInvokeOnMainThread(() => DeviceDisplay.Current.KeepScreenOn = true);
#endif
        await _host.SyncBackgroundServiceAsync();
        Task transcribing = TranscribeMeetingAsync(session);

        string ending;
        using (token.Register(() => session.Ended.TrySetResult("the reply was stopped")))
            ending = await WatchMeetingAsync(session);
        UiStallWatchdog.Mark("record_meeting: ended, " + ending);

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
        _ = _host.SyncBackgroundServiceAsync();
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
        _host.SetStatus("Writing down the last part of the meeting...");
        await transcribing;

        string heading = string.IsNullOrWhiteSpace(session.Title) ? "Meeting notes " + session.Started.ToString("yyyy-MM-dd HH.mm") : session.Title;
        int words = session.Transcript.WordCount;
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
                _host.SetStatus("Writing the meeting notes...");
                int maxPrompt = _host.ContextWindow - MeetingNotes.NotesTokens - 300;
                var progress = new Progress<string>(text => MainThread.BeginInvokeOnMainThread(() => session.Status.Text = text));
                notes = await MeetingNotes.WriteAsync(session.Transcript, session.Title, duration,
                    (prompt, maxTokens, _) => _host.AskModelOnceAsync(prompt, maxTokens), _host.TokenCounter, maxPrompt, progress, token);
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
        await _host.SaveDocumentAsync(document);
        string transcriptName = DocumentWriter.FileName(heading + " transcript", ".txt");
        string transcriptText = "Transcript of " + heading + ", " + session.Started.ToString("f") + "\n\n" + session.Transcript.Text();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            session.Status.Text = MeetingNotes.FormatTime(duration) + " · " + words + " words written down"
                + (problem != null ? "\n" + problem : notes == null ? "\nThe transcript was saved." : "");
            session.Preview.IsVisible = false;
            if (notes != null)
                session.Content.Children.Add(new ReplyView { Text = notes, FontSize = 14, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) });
            // Questions about the meeting later get the transcript's passages they need, as for an attached document.
            _host.AttachDocument(Encoding.UTF8.GetBytes(transcriptText), transcriptName);
            _host.SetStatus("Generating...");
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
                session.Transcript.Add(chunk.Start, text);
                ShowMeetingProgress(session);
            }
            bool done;
            lock (session.Pending) done = session.Stopping && session.Pending.Count == 0;
            if (done) return;
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
        session.Time = new Label { Text = "● 0:00", FontFamily = Theme.TitleFont, FontSize = 28, TextColor = Theme.PrimaryText };
        session.Status = new Label
        {
            Text = "Recording. Make sure everyone knows they are being recorded. What is said is written down as it goes.",
            FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.SecondaryText
        };
        session.Preview = new Label { FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.PrimaryText, IsVisible = false, LineBreakMode = LineBreakMode.WordWrap };
        session.Pause = new Button { Text = "Pause", FontFamily = Theme.TitleFont, FontSize = 14, BackgroundColor = Colors.Transparent, TextColor = Theme.Accent,
            BorderColor = Theme.Accent, BorderWidth = 1, CornerRadius = 12, Padding = new Thickness(16, 6), MinimumHeightRequest = 40 };
        session.Pause.Clicked += (_, _) =>
        {
            session.Paused = !session.Paused;
            session.Pause.Text = session.Paused ? "Resume" : "Pause";
            SemanticProperties.SetDescription(session.Pause, session.Paused ? "Resume recording" : "Pause recording");
        };
        SemanticProperties.SetDescription(session.Pause, "Pause recording");
        session.Stop = new Button { Text = "Stop and write notes", FontFamily = Theme.TitleFont, FontSize = 14, BackgroundColor = Theme.Accent, TextColor = Colors.White,
            CornerRadius = 12, Padding = new Thickness(16, 6), MinimumHeightRequest = 40 };
        session.Stop.Clicked += (_, _) => session.Ended.TrySetResult("the user pressed Stop");
        string heading = "\U0001F399  " + (string.IsNullOrWhiteSpace(session.Title) ? "Meeting notes" : session.Title);
        session.Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = heading, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText },
                session.Time, session.Status, session.Preview,
                new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { session.Stop, session.Pause } }
            }
        };
        _host.AddCardToReply(new Border
        {
            BackgroundColor = Theme.CardBackground, Stroke = Theme.RowBorder, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14, 12), Margin = new Thickness(6, 2), MaximumWidthRequest = 520, HorizontalOptions = LayoutOptions.Start,
            Content = session.Content
        });
        _host.SetStatus("Recording the meeting…");
    }

    private static void EndMeetingCard(MeetingSession session, string text)
    {
        session.Status.Text = text;
        session.Pause.IsVisible = false;
        session.Stop.IsVisible = false;
        session.Time.IsVisible = false;
    }
}
#endif
