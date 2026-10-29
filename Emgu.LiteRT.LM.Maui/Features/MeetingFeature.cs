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
/// model. The tool comes with <see cref="WrittenDocumentsFeature"/>'s group, which must be on the page too. Android,
/// iOS and Mac Catalyst (<see cref="CanRecord"/>). With <see cref="Voiceprint"/> the speakers are labelled: pieces also end
/// at pauses once 2 s long (people take turns at pauses), each written-down piece gets its voice's fingerprint, and after
/// Stop they are grouped by voice (<see cref="MeetingTranscript.LabelSpeakers"/>) - "Speaker 1", "Speaker 2"... in the
/// transcript, the notes' prompts and the document. What writes the pieces down is Settings > "Meeting transcripts"
/// (<see cref="MeetingTranscription"/>): automatically the chat's model where it takes audio on a desktop (Gemma 4: fewer
/// mistakes than the recognizers, and 5x real time on an M1) and the recognizer on a phone (Gemma 4 E2B took 0.3x real
/// time on a Pixel Fold); the model where there is no recognizer; or the recognizer while recording and the model over
/// every piece after Stop. For the model the pieces are 15-28 s (its audio clips are at most 30 s), each written down
/// in a request of its own.
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
        public MeetingTranscriber Transcriber;
        // RecognizerThenModel: each written-down piece's audio, kept for the model after Stop (temporary WAV files).
        public string PieceFolder;
        public readonly List<(TimeSpan Start, string Path)> KeptPieces = new();
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

    /// <summary>
    /// Whether a meeting can be recorded now: a microphone source on this platform (Android, iOS, Mac Catalyst) and either
    /// the speech recognizer or a chat model that takes audio.
    /// </summary>
    public bool CanRecord =>
#if ANDROID || IOS || MACCATALYST
        Transcriber != null;
#else
        false;
#endif

    /// <summary>What writes the pieces down now (the setting, what this device has, the chat's model), or null if nothing can.</summary>
    public MeetingTranscriber? Transcriber => MeetingTranscription.Choose(Choice, SpeechRecognizer.IsSupported, ModelHearsAudio, IsDesktop);

    // The user's choice in the settings (null: automatic).
    private MeetingTranscriber? Choice => MeetingTranscription.FromSetting(Host?.Settings.Get(MeetingTranscription.SettingKey, string.Empty));

    private bool ModelHearsAudio => Host?.Model?.AcceptsAudio == true;

    private static bool IsDesktop =>
#if MACCATALYST || WINDOWS
        true;
#else
        false;
#endif

    private readonly List<(MeetingTranscriber? Value, ToggleChip Chip)> _transcriberChips = new();
    private Label _transcriberNote;
    private bool _updatingChips;

    /// <summary>Settings > "Meeting transcripts": automatic, the speech recognizer, the chat's model, or both (where meetings can be recorded).</summary>
    public override View BuildSettingsSection()
    {
#if ANDROID || IOS || MACCATALYST
        var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        _transcriberChips.Clear();
        foreach ((MeetingTranscriber? value, string text) in new (MeetingTranscriber?, string)[]
        {
            (null, "Automatic"), (MeetingTranscriber.Recognizer, "Speech recognizer"), (MeetingTranscriber.Model, "Chat model"),
            (MeetingTranscriber.RecognizerThenModel, "Recognizer, then chat model")
        })
        {
            var chip = new ToggleChip(text) { Margin = new Thickness(0, 0, 6, 6), IsToggled = value == Choice };
            MeetingTranscriber? captured = value;
            chip.Toggled += (_, e) => OnTranscriberChipToggled(captured, e.Value);
            _transcriberChips.Add((value, chip));
            chips.Children.Add(chip);
        }
        _transcriberNote = ChatPage.Note(string.Empty);
        UpdateTranscriberNote();
        return new VerticalStackLayout { Spacing = 6, Children = { ChatPage.SectionTitle("Meeting transcripts"), chips, _transcriberNote } };
#else
        return null;
#endif
    }

    /// <inheritdoc/>
    public override void OnModelChanged(ChatModelOption model) => MainThread.BeginInvokeOnMainThread(UpdateTranscriberNote);

    // The chips act as one choice: tapping the chosen one keeps it.
    private void OnTranscriberChipToggled(MeetingTranscriber? value, bool on)
    {
        if (_updatingChips) return;
        if (on) Host.Settings.Set(MeetingTranscription.SettingKey, MeetingTranscription.ToSetting(value));
        _updatingChips = true;
        foreach (var (chipValue, chip) in _transcriberChips) chip.IsToggled = chipValue == Choice;
        _updatingChips = false;
        UpdateTranscriberNote();
    }

    // What the choice means here, now: the model's name, and what is used instead when this device can't.
    private void UpdateTranscriberNote()
    {
        if (_transcriberNote == null) return;
        string model = Host?.Model?.DisplayName ?? "the chat model";
        MeetingTranscriber? used = Transcriber;
        string now = used == MeetingTranscriber.Model ? model + " writes down what is said as it records - more accurate than the speech recognizer, but slow on a phone (Gemma 4 E2B took 3 s per second of speech on a Pixel Fold) and the chat waits meanwhile."
            : used == MeetingTranscriber.RecognizerThenModel ? "The speech recognizer writes it down as it records, then " + model + " again after Stop for a better transcript (on a phone that takes about 3 s per second of speech)."
            : used == MeetingTranscriber.Recognizer ? "The device's speech recognizer writes down what is said as it records."
            : "Meetings can't be written down here: there is no speech recognizer and the chat model doesn't take audio.";
        if (used != null && Choice != null && used != Choice)
            now += ModelHearsAudio ? " (No speech recognizer here.)" : " (" + model + " doesn't take audio.)";
        _transcriberNote.Text = now;
    }

    /// <summary>The record_meeting tool, where a meeting can be recorded and not in a hands-free chat (the microphone is the chat's then).</summary>
    /// <param name="handsFree">Whether the chat is hands-free</param>
    public IEnumerable<AITool> Tools(bool handsFree) =>
        CanRecord && !handsFree ? new[] { AgentTools.RecordMeeting(RecordMeetingAsync) } : Array.Empty<AITool>();

    private Task SyncBackgroundServiceAsync() => SyncBackgroundService?.Invoke() ?? Task.CompletedTask;

    private async Task<string> RecordMeetingAsync(string title, CancellationToken token)
    {
#if ANDROID || IOS || MACCATALYST
        MeetingTranscriber? transcriber = Transcriber;
        if (transcriber == null)
            return "Writing down speech needs a speech recognizer (Android 13 or later, an iPhone, iPad or a Mac) or a chat model that hears audio (Gemma 4). Tell the user.";
        if (_meeting != null)
            return "A meeting is already being recorded. Tell the user.";
        var session = new MeetingSession { Title = title ?? "", Started = DateTime.Now, Voiceprint = Voiceprint, Transcriber = transcriber.Value };
        // The model takes clips of at most 30 s; after Stop it writes down the same pieces again.
        bool modelPieces = session.Transcriber != MeetingTranscriber.Recognizer;
        session.Chunker = new MeetingAudioChunker(16000, modelPieces ? MeetingTranscription.ModelMinSeconds : 30,
            modelPieces ? MeetingTranscription.ModelMaxSeconds : 50, session.Voiceprint != null ? TurnSeconds : 0);
        if (session.Transcriber == MeetingTranscriber.RecognizerThenModel)
        {
            session.PieceFolder = System.IO.Path.Combine(FileSystem.CacheDirectory, "meeting-pieces", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(session.PieceFolder);
        }
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
        if (session.Transcriber == MeetingTranscriber.RecognizerThenModel)
            await RewriteWithModelAsync(session, token);

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
                    try
                    {
                        byte[] wav = chunk.ToWav();
                        if (session.Transcriber == MeetingTranscriber.Model)
                            text = chunk.HasSpeech ? await TranscribeWithModelAsync(wav, CancellationToken.None) : string.Empty;
                        else
                        {
                            text = await SpeechRecognizer.TranscribeAsync(wav);
                            if (session.PieceFolder != null && chunk.HasSpeech)
                                KeepPiece(session, chunk, wav);
                        }
                    }
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
                session.Transcript.Add(chunk.Start, text, await VoiceprintOfAsync(session, chunk, text), chunk.VoicedSeconds);
                ShowMeetingProgress(session);
            }
            bool done;
            lock (session.Pending) done = session.Stopping && session.Pending.Count == 0;
            if (done) return;
        }
    }

    // A piece written down by the chat's model, in a request of its own (as the notes are written): its audio and the
    // instruction, no tools, no thinking.
    private async Task<string> TranscribeWithModelAsync(byte[] wav, CancellationToken token)
    {
        var message = new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, new List<AIContent>
        {
            new DataContent(wav, "audio/wav"),
            new TextContent(MeetingTranscription.Prompt)
        });
        ChatResponse response = await Host.ChatClient.GetResponseAsync(new[] { message },
            new ChatOptions { MaxOutputTokens = 400, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } }, token);
        return MeetingTranscription.Clean(new Emgu.LiteRT.LM.Models.ChatReply(response.Text ?? string.Empty).Text);
    }

    private static void KeepPiece(MeetingSession session, MeetingAudioChunk chunk, byte[] wav)
    {
        try
        {
            string path = System.IO.Path.Combine(session.PieceFolder, session.KeptPieces.Count.ToString("D5") + ".wav");
            File.WriteAllBytes(path, wav);
            lock (session.KeptPieces) session.KeptPieces.Add((chunk.Start, path));
        }
        catch (Exception ex) { Debug.WriteLine("Keeping a meeting piece failed: " + ex.Message); }
    }

    // After Stop, the model writes every kept piece down again (its version replaces the recognizer's), unless the reply
    // was stopped; the pieces' files are deleted either way.
    private async Task RewriteWithModelAsync(MeetingSession session, CancellationToken token)
    {
        List<(TimeSpan Start, string Path)> pieces;
        lock (session.KeptPieces) pieces = session.KeptPieces.ToList();
        try
        {
            string model = Host.Model?.DisplayName ?? "the model";
            for (int i = 0; i < pieces.Count && !token.IsCancellationRequested; i++)
            {
                string progress = "Writing the transcript down again with " + model + ": " + (i + 1) + " of " + pieces.Count + "…";
                MainThread.BeginInvokeOnMainThread(() => session.Status.Text = progress);
                Host.SetStatus(progress);
                try { session.Transcript.Replace(pieces[i].Start, await TranscribeWithModelAsync(File.ReadAllBytes(pieces[i].Path), token)); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Debug.WriteLine("Writing a meeting piece down again failed: " + ex.Message); }
            }
        }
        finally
        {
            try { Directory.Delete(session.PieceFolder, true); } catch (Exception) { }
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
            session.Status.Text = words + " words written down" + (session.Stopping ? pending > 0 ? " · finishing the last " + (pending + 1) + " pieces…" : ""
                : pending > 1 ? " · " + pending + " pieces waiting" : "");
        });
    }

    private void AddMeetingCard(MeetingSession session)
    {
        Color accent = ChatResources.FindColor("ChatAccent");
        session.Time = new Label { Text = "● 0:00", FontSize = 28 };
        ChatResources.ApplyStyle(session.Time, "ChatListTitleStyle");
        session.Time.FontSize = 28;
        string writer = session.Transcriber == MeetingTranscriber.Model ? " by " + (Host.Model?.DisplayName ?? "the model")
            : session.Transcriber == MeetingTranscriber.RecognizerThenModel ? ", and again by " + (Host.Model?.DisplayName ?? "the model") + " after Stop" : "";
        session.Status = new Label { Text = "Recording. Make sure everyone knows they are being recorded. What is said is written down as it goes" + writer + "." };
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
