// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using System.Diagnostics;
using Emgu.CV;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.Models;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

// Counting jumping jacks (count_exercise): the model is far too slow to judge each movement, so the tool call lasts as long as the
// exercise and the app counts - the camera's frames go through the pose model (the lite one, as the body-pose agent's, but its own
// instance, on as many frames as it keeps up with) and JumpingJackCounter. A card above the reply shows the count, the time and Done. It
// ends at the target, the time asked for, Done, a 10 s pause after the first jumping jack, 30 s without any, or 5 minutes; the count is
// then logged toward a goal that names the exercise ("30 jumping jacks a day") and returned to the model, which says how it went.
public partial class LiteRtLmChatPage
{
    private const string ExerciseInstructions =
        "To count jumping jacks, call count_exercise: it opens the camera itself and counts until the user is done. " +
        "The count is logged to a matching goal by the app.";

    private PoseDetector _exercisePose;
    private ExerciseSession _exercise;

    private sealed class ExerciseSession
    {
        public readonly JumpingJackCounter Counter = new();
        public readonly TaskCompletionSource<string> Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LiveCamera Camera;
        public int Busy;                       // a frame is being read
        public long LastRepTicks;              // Stopwatch time of the last jumping jack, 0 before the first
        public bool PersonSeen;
        public Label Count, Status;
    }

    private IEnumerable<Microsoft.Extensions.AI.AITool> ExerciseTools() =>
        LiveCamera.Supported && !_voiceMode ? new[] { AgentTools.CountExercise(CountExerciseAsync) } : Array.Empty<Microsoft.Extensions.AI.AITool>();

    private async Task<string> CountExerciseAsync(string exercise, int target, int seconds, CancellationToken token)
    {
        string name = AgentTools.CountableExercise(exercise);
        if (name == null)
            return "Only jumping jacks can be counted. Tell the user.";
        if (_exercise != null)
            return "Jumping jacks are already being counted.";
        bool wasOpen = _camera?.IsRunning == true;
        string opened = await OpenCameraAsync(token);
        if (_camera?.IsRunning != true)
            return opened + " Nothing could be counted without the camera. Tell the user.";

        var session = new ExerciseSession();
        Button done = null;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            session.Count = new Label { Text = "0", FontFamily = Theme.TitleFont, FontSize = 44, TextColor = Theme.PrimaryText, HorizontalOptions = LayoutOptions.Start };
            session.Status = new Label { Text = "Getting the body pose model ready…", FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.SecondaryText };
            done = new Button { Text = "Done", FontFamily = Theme.TitleFont, FontSize = 14, BackgroundColor = Theme.Accent, TextColor = Colors.White,
                CornerRadius = 12, Padding = new Thickness(16, 6), MinimumHeightRequest = 40, HorizontalOptions = LayoutOptions.Start };
            done.Clicked += (_, _) => session.Ended.TrySetResult("the user pressed Done");
            SemanticProperties.SetDescription(done, "Stop counting jumping jacks");
            var title = "\U0001F3C3  Jumping jacks" + (target > 0 ? " · target " + target : "");
            AddCard(_agentBubble, new Border
            {
                BackgroundColor = Theme.CardBackground, Stroke = Theme.RowBorder, StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                Padding = new Thickness(14, 12), Margin = new Thickness(6, 2), MaximumWidthRequest = 420, HorizontalOptions = LayoutOptions.Start,
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children = { new Label { Text = title, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText }, session.Count, session.Status, done }
                }
            });
            SetStatus("Counting jumping jacks…");
        });

        var watch = Stopwatch.StartNew();
        string ending;
        try
        {
            _exercisePose ??= DetectorFactory.Pose();
            if (!_exercisePose.Imported)
                await _exercisePose.PrepareAsync(PoseModelFiles.Lite, false, Math.Min(2, CpuThreadSettings.Count),
                    message => MainThread.BeginInvokeOnMainThread(() => session.Status.Text = message), token);
            session.Camera = _camera;
            watch.Restart();
            _exercise = session;
            ending = await WatchExerciseAsync(session, watch, target, seconds, token);
        }
        catch (OperationCanceledException)
        {
            ending = "the reply was stopped";
        }
        catch (Exception ex)
        {
            _exercise = null;
            await MainThread.InvokeOnMainThreadAsync(() => { session.Status.Text = "Couldn't count: " + ex.Message; done.IsVisible = false; });
            return "Couldn't count jumping jacks: " + ex.Message + ". Tell the user.";
        }
        _exercise = null;
        while (Volatile.Read(ref session.Busy) != 0)        // the last frame's reading
            await Task.Delay(20);

        var result = new ExerciseResult(name, session.Counter.Count, watch.Elapsed, ending);
        string logged = null;
        try { logged = await AgentTools.LogExerciseAsync(GoalsOn ? _store : null, result, DateTime.Now); }
        catch (Exception ex) { logged = null; Debug.WriteLine("Logging the exercise failed: " + ex.Message); }
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            done.IsVisible = false;
            session.Count.Text = result.Count.ToString();
            session.Status.Text = (result.Count == 1 ? "1 jumping jack" : result.Count + " jumping jacks") + " in " + FormatExerciseTime(watch.Elapsed)
                + (logged != null ? "\n✓ " + logged.Substring(logged.IndexOf("Now: ", StringComparison.Ordinal) + 5) : "");
            session.Status.TextColor = logged != null ? Theme.Success : Theme.SecondaryText;
            SetStatus("Generating...");
        });
        if (!wasOpen && !_visualRuntime.Snapshot().Any())
            await CloseCameraAsync();
        return AgentTools.ExerciseResultText(result, logged, GoalsOn);
    }

    // Updates the card's time four times a second and decides when the session ends; the counting itself happens in QueueExerciseFrame.
    private async Task<string> WatchExerciseAsync(ExerciseSession session, Stopwatch watch, int target, int seconds, CancellationToken token)
    {
        using var stop = token.Register(() => session.Ended.TrySetCanceled());
        while (true)
        {
            Task tick = Task.Delay(250);
            if (await Task.WhenAny(session.Ended.Task, tick) == session.Ended.Task)
                return await session.Ended.Task;
            TimeSpan elapsed = watch.Elapsed;
            int count = session.Counter.Count;
            long last = Volatile.Read(ref session.LastRepTicks);
            if (target > 0 && count >= target) return "the target of " + target + " was reached";
            if (seconds > 0 && elapsed.TotalSeconds >= seconds) return "the " + seconds + " s were up";
            if (count > 0 && Stopwatch.GetElapsedTime(last).TotalSeconds >= 10) return "the user stopped (no jumping jack for 10 s)";
            if (count == 0 && elapsed.TotalSeconds >= 30) return session.PersonSeen ? "no jumping jack was seen in 30 s" : "nobody was seen in full view in 30 s";
            if (elapsed.TotalMinutes >= 5) return "the 5-minute limit was reached";
            if (_camera != session.Camera || _camera?.IsRunning != true) return "the camera was closed";
            string time = FormatExerciseTime(elapsed) + (seconds > 0 ? " of " + FormatExerciseTime(TimeSpan.FromSeconds(seconds)) : "");
            string hint = !session.PersonSeen ? "Stand back so the camera sees you from head to toe" : count == 0 ? "Go!" : "Keep going";
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                session.Count.Text = count.ToString();
                session.Status.Text = time + " · " + hint;
            });
        }
    }

    private static string FormatExerciseTime(TimeSpan time) => ((int)time.TotalMinutes) + ":" + time.Seconds.ToString("00");

    // Each camera frame while counting, unless one is still being read: the pose model and the counter, off the camera's thread.
    private void QueueExerciseFrame(LiveCamera camera, Mat frame)
    {
        ExerciseSession session = _exercise;
        PoseDetector pose = _exercisePose;
        if (session == null || session.Camera != camera || pose?.Imported != true || Interlocked.Exchange(ref session.Busy, 1) != 0) return;
        Mat copy = frame.Clone();
        _ = Task.Run(() =>
        {
            try
            {
                var poses = pose.Detect(copy);
                float[][] landmarks = poses.Count > 0 ? poses[0].Landmarks : null;
                if (landmarks != null && JumpingJackCounter.Read(landmarks, copy.Width, copy.Height) != JumpingJackCounter.Position.Unknown)
                    session.PersonSeen = true;
                if (session.Counter.Update(landmarks, copy.Width, copy.Height))
                {
                    Volatile.Write(ref session.LastRepTicks, Stopwatch.GetTimestamp());
                    int count = session.Counter.Count;
                    MainThread.BeginInvokeOnMainThread(() => session.Count.Text = count.ToString());
                }
            }
            catch (Exception ex) { Debug.WriteLine("Exercise frame failed: " + ex.Message); }
            finally
            {
                copy.Dispose();
                Volatile.Write(ref session.Busy, 0);
            }
        });
    }

    private void DisposeExerciseCounting()
    {
        _exercise?.Ended.TrySetResult("the chat was closed");
        _exercise = null;
        var pose = _exercisePose;
        _exercisePose = null;
        if (pose != null) _ = Task.Run(async () => { await Task.Delay(500); pose.Dispose(); });
    }
}
#endif
