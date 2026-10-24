// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// Where the sound events page gets its audio: 16 kHz mono samples in -1..1, delivered as they arrive.
internal interface ISoundSource : IDisposable
{
    event Action<float[]> Samples;
    /// <returns>False if the microphone permission was denied</returns>
    Task<bool> StartAsync();
    Task StopAsync();
}

// A clip played back at its own speed, so the page can be tried (and tested) without a microphone.
internal sealed class ClipSoundSource : ISoundSource
{
    private readonly float[] _clip;
    private CancellationTokenSource _cancel;
    private Task _loop;
    public event Action<float[]> Samples;
    public event Action Finished;

    public ClipSoundSource(float[] clip16k) { _clip = clip16k; }

    public Task<bool> StartAsync()
    {
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        _loop = Task.Run(async () =>
        {
            const int chunk = YamNet.SampleRate / 10;       // 100 ms, as a microphone driver would hand them over
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int at = 0; at < _clip.Length && !token.IsCancellationRequested; at += chunk)
            {
                var part = new float[Math.Min(chunk, _clip.Length - at)];
                Array.Copy(_clip, at, part, 0, part.Length);
                Samples?.Invoke(part);
                long due = (long)((at + part.Length) * 1000.0 / YamNet.SampleRate);
                int wait = (int)(due - clock.ElapsedMilliseconds);
                if (wait > 0) { try { await Task.Delay(wait, token); } catch (OperationCanceledException) { } }
            }
            if (!token.IsCancellationRequested) Finished?.Invoke();
        });
        return Task.FromResult(true);
    }

    public async Task StopAsync()
    {
        _cancel?.Cancel();
        if (_loop != null) await _loop;
        _loop = null;
    }

    public void Dispose() { _cancel?.Cancel(); }
}

// Averages input samples down to 16 kHz as they arrive (a box filter: each output is the mean of the input it spans), so that a
// microphone running at 44.1 or 48 kHz doesn't fold its high frequencies into the band the model listens to.
internal sealed class BoxResampler
{
    private readonly double _ratio;
    private double _position, _next, _sum;

    public BoxResampler(double inputRate) { _ratio = inputRate / YamNet.SampleRate; _next = _ratio; }

    public float[] Process(float[] input, int count)
    {
        var output = new List<float>((int)(count / _ratio) + 2);
        for (int i = 0; i < count; i++)
        {
            double end = _position + 1;
            while (end >= _next)
            {
                _sum += input[i] * (_next - _position);
                output.Add((float)(_sum / _ratio));
                _sum = 0;
                _position = _next;
                _next += _ratio;
            }
            _sum += input[i] * (end - _position);
            _position = end;
        }
        // Keep the numbers small on long runs.
        double whole = Math.Floor(_position);
        _position -= whole; _next -= whole;
        return output.ToArray();
    }
}

// Cuts a stream of samples into YAMNet's windows: the last 15,600 samples every 7,800, once there are that many. A window that comes due
// while the last one is still being scored is skipped, so a slow device falls behind on windows, not on audio.
internal sealed class SoundWindower
{
    private readonly List<float> _buffer = new List<float>();
    private readonly object _lock = new object();
    private int _since;
    private bool _busy;

    /// <returns>A window to score (call <see cref="Done"/> afterwards), or null</returns>
    public float[] Add(float[] samples)
    {
        lock (_lock)
        {
            _buffer.AddRange(samples);
            if (_buffer.Count > 2 * YamNet.WindowSamples) _buffer.RemoveRange(0, _buffer.Count - 2 * YamNet.WindowSamples);
            _since += samples.Length;
            if (_since < YamNet.HopSamples || _buffer.Count < YamNet.WindowSamples || _busy) return null;
            _since = 0;
            _busy = true;
            return _buffer.GetRange(_buffer.Count - YamNet.WindowSamples, YamNet.WindowSamples).ToArray();
        }
    }

    public void Done() { lock (_lock) _busy = false; }

    public void Reset() { lock (_lock) { _buffer.Clear(); _since = 0; } }
}

#if ANDROID || IOS || MACCATALYST
// The microphone: AudioRecord at 16 kHz on Android, an AVAudioEngine input tap on iOS and Mac Catalyst.
internal sealed class MicrophoneSoundSource : ISoundSource
{
    public event Action<float[]> Samples;

#if ANDROID
    private Android.Media.AudioRecord _record;
    private volatile bool _reading;
    private Task _loop;

    public async Task<bool> StartAsync()
    {
        PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted) return false;
        int rate = YamNet.SampleRate;
        int bufferSize = Math.Max(Android.Media.AudioRecord.GetMinBufferSize(rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit), rate * 2);
        _record = new Android.Media.AudioRecord(Android.Media.AudioSource.Mic, rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit, bufferSize);
        if (_record.State != Android.Media.State.Initialized)
        {
            _record.Release(); _record = null;
            throw new InvalidOperationException("Could not open the microphone.");
        }
        _reading = true;
        _record.StartRecording();
        var record = _record;
        _loop = Task.Run(() =>
        {
            var bytes = new byte[rate / 10 * 2];            // 100 ms
            while (_reading)
            {
                int read = record.Read(bytes, 0, bytes.Length);
                if (read < 0) break;
                if (read < 2) continue;
                var samples = new float[read / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8) / 32768f;
                Samples?.Invoke(samples);
            }
        });
        return true;
    }

    public async Task StopAsync()
    {
        _reading = false;
        if (_record != null) { try { _record.Stop(); } catch { } }
        if (_loop != null) await _loop;
        _record?.Release(); _record = null; _loop = null;
    }
#else
    private AVFoundation.AVAudioEngine _engine;

    public async Task<bool> StartAsync()
    {
        PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted) return false;
        AVFoundation.AVAudioSession session = AVFoundation.AVAudioSession.SharedInstance();
        Foundation.NSError error = session.SetCategory(AVFoundation.AVAudioSessionCategory.PlayAndRecord, AVFoundation.AVAudioSessionCategoryOptions.DefaultToSpeaker);
        if (error != null) throw new InvalidOperationException(error.LocalizedDescription);
        session.SetActive(true, out error);
        if (error != null) throw new InvalidOperationException(error.LocalizedDescription);
        _engine = new AVFoundation.AVAudioEngine();
        var format = _engine.InputNode.GetBusOutputFormat(0);
        if (format.SampleRate < 8000) throw new InvalidOperationException("The microphone isn't available.");
        var resampler = new BoxResampler(format.SampleRate);
        _engine.InputNode.InstallTapOnBus(0, 4096, format, (buffer, when) =>
        {
            int frames = (int)buffer.FrameLength;
            if (frames == 0 || buffer.FloatChannelData == IntPtr.Zero) return;
            var channel = new float[frames];
            System.Runtime.InteropServices.Marshal.Copy(System.Runtime.InteropServices.Marshal.ReadIntPtr(buffer.FloatChannelData), channel, 0, frames);
            float[] converted = resampler.Process(channel, frames);
            if (converted.Length > 0) Samples?.Invoke(converted);
        });
        _engine.Prepare();
        if (!_engine.StartAndReturnError(out error))
        {
            _engine.InputNode.RemoveTapOnBus(0);
            _engine.Dispose(); _engine = null;
            throw new InvalidOperationException(error?.LocalizedDescription ?? "Could not start the microphone.");
        }
        return true;
    }

    public Task StopAsync()
    {
        if (_engine != null)
        {
            _engine.InputNode.RemoveTapOnBus(0);
            _engine.Stop();
            _engine.Dispose();
            _engine = null;
        }
        return Task.CompletedTask;
    }
#endif

    public void Dispose() { StopAsync().GetAwaiter().GetResult(); }
}
#endif
