using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Audio;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// SceneNarrator ("watch and listen") with a scripted model, camera and microphone: no device, no model.
[TestFixture]
public class UnitTestSceneNarrator
{
    private const int Rate = SoundInput.SampleRate;

    [Test]
    public async Task EachRoundIsOneFreshRequestWithLabelledFramesAndSound()
    {
        var sound = new FakeSound();
        var model = new ScriptedModel(_ => "See: a desk.\nHear: \"hello\"");
        int frameNumber = 0;
        var narrator = new SceneNarrator(model, _ =>
        {
            sound.Push(Tone(0.5));
            return Task.FromResult(BitConverter.GetBytes(++frameNumber));
        }, sound) { Delay = NoDelay };
        var captions = new List<SceneCaption>();
        using var stop = new CancellationTokenSource();
        narrator.Caption += c => { captions.Add(c); if (captions.Count == 2) stop.Cancel(); };

        string reason = await narrator.RunAsync(stop.Token);

        Assert.That(reason, Is.EqualTo(SceneNarrator.StoppedReason));
        Assert.That(model.Requests.Count, Is.EqualTo(2));
        foreach (var request in model.Requests)
        {
            Assert.That(request.Messages.Count, Is.EqualTo(1), "a fresh conversation each round, no history");
            Assert.That(request.Messages[0].Role, Is.EqualTo(ChatRole.User));
            List<AIContent> parts = request.Messages[0].Contents.ToList();
            Assert.That(parts.Select(Kind), Is.EqualTo(new[] { "text", "image", "image", "image", "text", "audio", "text" }));
            Assert.That(((TextContent)parts[0]).Text, Is.EqualTo("Camera pictures:"));
            Assert.That(((TextContent)parts[4]).Text, Is.EqualTo("Microphone audio:"));
            Assert.That(((TextContent)parts[6]).Text, Does.StartWith(SceneNarrationOptions.DefaultInstructions));
            Assert.That(request.Options.MaxOutputTokens, Is.EqualTo(120));
            Assert.That(request.Options.Reasoning.Effort, Is.EqualTo(ReasoningEffort.None));
        }
        // The frames are in order, the second round's taken after the first's.
        int[] FrameNumbers(int request) => model.Requests[request].Messages[0].Contents.OfType<DataContent>()
            .Where(d => d.HasTopLevelMediaType("image")).Select(d => BitConverter.ToInt32(d.Data.ToArray(), 0)).ToArray();
        int[] first = FrameNumbers(0), second = FrameNumbers(1);
        Assert.That(first[1] == first[0] + 1 && first[2] == first[1] + 1, Is.True, string.Join(",", first));
        Assert.That(second[0], Is.GreaterThan(first[2]));
        Assert.That(captions[0].Round, Is.EqualTo(1));
        Assert.That(captions[0].Frames, Is.EqualTo(3));
        Assert.That(captions[0].HeardSound, Is.True);
        Assert.That(captions[0].Text, Is.EqualTo("See: a desk.\nHear: \"hello\""));
        Assert.That(narrator.LastCaption, Is.EqualTo(captions[1].Text));
        Assert.That(sound.Subscribers, Is.EqualTo(0), "the microphone is let go");
    }

    [Test]
    public async Task ASlowAnswerIsFollowedByTheNewestFrames()
    {
        // Round 1 sends frames 1-3; while it is answered the camera takes 4-12, and round 2 sends the newest three, 10-12, not
        // the three taken right after round 1 was sent.
        int frameNumber = 0;
        var answering = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tookTwelve = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedModel(_ => "ok")
        {
            Answering = round =>
            {
                if (round != 1) return Task.CompletedTask;
                answering.TrySetResult(true);
                return tookTwelve.Task;
            }
        };
        var narrator = new SceneNarrator(model, async token =>
        {
            if (frameNumber == 3) await answering.Task;
            if (frameNumber == 12) await Task.Delay(Timeout.Infinite, token);
            if (++frameNumber == 12) tookTwelve.TrySetResult(true);
            return new[] { (byte)frameNumber };
        }, null) { Delay = NoDelay };
        using var stop = new CancellationTokenSource();
        narrator.Caption += c => { if (c.Round == 2) stop.Cancel(); };

        await narrator.RunAsync(stop.Token);

        byte[] Frames(int request) => model.Requests[request].Messages[0].Contents.OfType<DataContent>().Select(d => d.Data.Span[0]).ToArray();
        Assert.That(Frames(0), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(Frames(1), Is.EqualTo(new byte[] { 10, 11, 12 }));
    }

    [Test]
    public async Task ACameraWithoutFramesDoesNotStopTheRounds()
    {
        var model = new ScriptedModel(_ => "See: nothing.\nHear: nothing");
        var narrator = new SceneNarrator(model, _ => Task.FromResult<byte[]>(null), null) { Delay = NoDelay };
        using var stop = new CancellationTokenSource();
        SceneCaption caption = null;
        narrator.Caption += c => { caption = c; stop.Cancel(); };

        await narrator.RunAsync(stop.Token);

        Assert.That(caption.Frames, Is.EqualTo(0));
    }

    [Test]
    public async Task ACameraErrorStops()
    {
        var model = new ScriptedModel(_ => "ok");
        var narrator = new SceneNarrator(model, _ => throw new InvalidOperationException("the camera closed"), null) { Delay = NoDelay };

        string reason = await narrator.RunAsync(CancellationToken.None);

        Assert.That(reason, Is.EqualTo("Stopped: the camera closed"));
        Assert.That(model.Requests, Is.Empty);
    }

    [Test]
    public async Task SilenceIsNotSent()
    {
        var sound = new FakeSound();
        var model = new ScriptedModel(_ => "See: a wall.\nHear: nothing");
        var narrator = new SceneNarrator(model, _ => { sound.Push(new float[Rate]); return Task.FromResult(new byte[] { 1 }); }, sound)
            { Delay = NoDelay };
        using var stop = new CancellationTokenSource();
        SceneCaption caption = null;
        narrator.Caption += c => { caption = c; stop.Cancel(); };

        await narrator.RunAsync(stop.Token);

        List<AIContent> parts = model.Requests[0].Messages[0].Contents.ToList();
        Assert.That(parts.OfType<DataContent>().Any(d => d.HasTopLevelMediaType("audio")), Is.False);
        Assert.That(parts.OfType<TextContent>().Any(t => t.Text == "Microphone audio:"), Is.False);
        Assert.That(((TextContent)parts.Last()).Text, Does.EndWith("It was silent."));
        Assert.That(caption.HeardSound, Is.False);
    }

    [Test]
    public async Task WithoutAMicrophoneOnlyFramesAreSent()
    {
        var model = new ScriptedModel(_ => "See: a cat.\nHear: nothing");
        var narrator = new SceneNarrator(model, _ => Task.FromResult(new byte[] { 1 }), null) { Delay = NoDelay };
        using var stop = new CancellationTokenSource();
        narrator.Caption += _ => stop.Cancel();

        await narrator.RunAsync(stop.Token);

        Assert.That(((TextContent)model.Requests[0].Messages[0].Contents.Last()).Text, Does.EndWith("There is no microphone."));
    }

    [Test]
    public async Task SoundWhileTheModelAnswersGoesToTheNextRoundUpToMaxAudio()
    {
        var sound = new FakeSound();
        // 20 s of sound arrive while the first round is answered; the second round sends the newest 15 s.
        var model = new ScriptedModel(round => { if (round == 1) sound.Push(Tone(20)); return "ok"; });
        var narrator = new SceneNarrator(model, _ => Task.FromResult(new byte[] { 1 }), sound) { Delay = NoDelay };
        using var stop = new CancellationTokenSource();
        narrator.Caption += c => { if (c.Round == 2) stop.Cancel(); };

        await narrator.RunAsync(stop.Token);

        Assert.That(model.Requests[0].Messages[0].Contents.OfType<DataContent>().Any(d => d.HasTopLevelMediaType("audio")), Is.False);
        DataContent wav = model.Requests[1].Messages[0].Contents.OfType<DataContent>().Single(d => d.HasTopLevelMediaType("audio"));
        Assert.That(wav.MediaType, Is.EqualTo("audio/wav"));
        Assert.That(wav.Data.Length, Is.EqualTo(44 + 15 * Rate * 2));
    }

    [Test]
    public async Task StopsAtTheTimeLimit()
    {
        var model = new ScriptedModel(_ => "ok");
        DateTimeOffset now = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
        var narrator = new SceneNarrator(model, _ => Task.FromResult(new byte[] { 1 }), null,
            new SceneNarrationOptions { TimeLimit = TimeSpan.FromMinutes(2) })
        {
            Delay = NoDelay,
            Now = () => now += TimeSpan.FromSeconds(30)
        };

        string reason = await narrator.RunAsync(CancellationToken.None);

        Assert.That(reason, Is.EqualTo("Time limit reached (2 min)"));
        Assert.That(model.Requests.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task StopsWhenTheDeviceAsksToPause()
    {
        var model = new ScriptedModel(_ => "ok");
        var narrator = new SceneNarrator(model, _ => Task.FromResult(new byte[] { 1 }), null) { Delay = NoDelay };
        narrator.PauseReason = _ => Task.FromResult(model.Requests.Count == 1 ? "device is hot; waiting for it to cool down" : null);

        string reason = await narrator.RunAsync(CancellationToken.None);

        Assert.That(reason, Is.EqualTo("Paused: device is hot; waiting for it to cool down"));
        Assert.That(model.Requests.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task StopsOnAnError()
    {
        var sound = new FakeSound();
        var model = new ScriptedModel(_ => throw new InvalidOperationException("the model failed"));
        var narrator = new SceneNarrator(model, _ => Task.FromResult(new byte[] { 1 }), sound) { Delay = NoDelay };

        string reason = await narrator.RunAsync(CancellationToken.None);

        Assert.That(reason, Is.EqualTo("Stopped: the model failed"));
        Assert.That(sound.Subscribers, Is.EqualTo(0));
    }

    [Test]
    public async Task CancellingWhileTheModelAnswersStops()
    {
        using var stop = new CancellationTokenSource();
        var model = new ScriptedModel(_ => "ok") { BeforeReply = () => stop.Cancel() };
        var narrator = new SceneNarrator(model, _ => Task.FromResult(new byte[] { 1 }), null) { Delay = NoDelay };
        int captions = 0;
        narrator.Caption += _ => captions++;

        string reason = await narrator.RunAsync(stop.Token);

        Assert.That(reason, Is.EqualTo(SceneNarrator.StoppedReason));
        Assert.That(captions, Is.EqualTo(0));
    }

    [Test]
    public async Task StoppingWhileTheModelAnswersIsAStopNotAnError()
    {
        // LiteRT-LM reports a cancelled reply as its own exception, not an OperationCanceledException.
        using var stop = new CancellationTokenSource();
        var model = new ScriptedModel(_ => "ok")
        {
            BeforeReply = () => { stop.Cancel(); throw new InvalidOperationException("CANCELLED: Task cancelled."); }
        };
        var narrator = new SceneNarrator(model, _ => Task.FromResult(new byte[] { 1 }), null) { Delay = NoDelay };

        Assert.That(await narrator.RunAsync(stop.Token), Is.EqualTo(SceneNarrator.StoppedReason));
    }

    [Test]
    public void AudioWindowBufferKeepsTheNewestSamples()
    {
        var buffer = new AudioWindowBuffer(5);
        buffer.Add(new float[] { 1, 2, 3 });
        buffer.Add(new float[] { 4, 5, 6 });
        buffer.Add(new float[] { 7 });
        Assert.That(buffer.Count, Is.LessThanOrEqualTo(7));
        Assert.That(buffer.TakeSinceLastCut(), Is.EqualTo(new float[] { 3, 4, 5, 6, 7 }));
        Assert.That(buffer.TakeSinceLastCut(), Is.Empty);
        buffer.Add(new float[] { 8, 9 });
        Assert.That(buffer.TakeSinceLastCut(), Is.EqualTo(new float[] { 8, 9 }));
    }

    private static async Task NoDelay(TimeSpan time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Task.Yield();
    }

    // A 440 Hz tone at -20 dBFS: sound, not silence.
    private static float[] Tone(double seconds)
    {
        var samples = new float[(int)(seconds * Rate)];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)(0.1 * Math.Sin(2 * Math.PI * 440 * i / Rate));
        return samples;
    }

    private static string Kind(AIContent content) =>
        content is TextContent ? "text" : content is DataContent d ? (d.HasTopLevelMediaType("image") ? "image" : "audio") : "other";

    private sealed class FakeSound : ISoundSource
    {
        private Action<float[]> _samples;
        public int Subscribers => _samples?.GetInvocationList().Length ?? 0;
        public event Action<float[]> Samples { add => _samples += value; remove => _samples -= value; }
        public void Push(float[] samples) => _samples?.Invoke(samples);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class ScriptedModel : IChatClient
    {
        private readonly Func<int, string> _reply;
        public ScriptedModel(Func<int, string> reply) { _reply = reply; }
        public List<(IList<ChatMessage> Messages, ChatOptions Options)> Requests { get; } = new();
        public Action BeforeReply { get; set; }
        public Func<int, Task> Answering { get; set; }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add((messages.ToList(), options));
            await Task.Yield();
            if (Answering != null)
                await Answering(Requests.Count);
            BeforeReply?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, _reply(Requests.Count)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;
        public void Dispose() { }
    }
}
