// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Agents.Audio
{
    /// <summary>
    /// Where a listener (a sound monitor, meeting notes) gets its audio: 16 kHz mono samples in -1..1, delivered as they arrive, on a
    /// background thread. Emgu.LiteRT.LM.Maui's <c>MicrophoneSoundSource</c> is the microphone; <see cref="ClipSoundSource"/> plays a clip.
    /// </summary>
    public interface ISoundSource : IDisposable
    {
        /// <summary>Raised with each piece of audio (16 kHz mono, -1..1) as it arrives.</summary>
        event Action<float[]> Samples;

        /// <summary>Start delivering samples.</summary>
        /// <returns>False if the microphone permission was denied</returns>
        Task<bool> StartAsync();

        /// <summary>Stop delivering samples.</summary>
        Task StopAsync();
    }

    /// <summary>The sample rate of an <see cref="ISoundSource"/>'s audio.</summary>
    public static class SoundInput
    {
        /// <summary>16 kHz, as YAMNet and the speech models want.</summary>
        public const int SampleRate = 16000;
    }

    /// <summary>
    /// A clip played back at its own speed, 100 ms at a time as a microphone driver would hand them over, so a listener can be tried (and
    /// tested) without a microphone.
    /// </summary>
    public sealed class ClipSoundSource : ISoundSource
    {
        private readonly float[] _clip;
        private CancellationTokenSource _cancel;
        private Task _loop;

        /// <inheritdoc/>
        public event Action<float[]> Samples;

        /// <summary>Raised when the whole clip has been delivered (not when it was stopped).</summary>
        public event Action Finished;

        /// <param name="clip16k">The clip: 16 kHz mono samples in -1..1</param>
        public ClipSoundSource(float[] clip16k) { _clip = clip16k ?? throw new ArgumentNullException(nameof(clip16k)); }

        /// <inheritdoc/>
        public Task<bool> StartAsync()
        {
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            _loop = Task.Run(async () =>
            {
                const int chunk = SoundInput.SampleRate / 10;       // 100 ms
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int at = 0; at < _clip.Length && !token.IsCancellationRequested; at += chunk)
                {
                    var part = new float[Math.Min(chunk, _clip.Length - at)];
                    Array.Copy(_clip, at, part, 0, part.Length);
                    Samples?.Invoke(part);
                    long due = (long)((at + part.Length) * 1000.0 / SoundInput.SampleRate);
                    int wait = (int)(due - clock.ElapsedMilliseconds);
                    if (wait > 0) { try { await Task.Delay(wait, token); } catch (OperationCanceledException) { } }
                }
                if (!token.IsCancellationRequested) Finished?.Invoke();
            });
            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            _cancel?.Cancel();
            if (_loop != null) await _loop;
            _loop = null;
        }

        /// <inheritdoc/>
        public void Dispose() { _cancel?.Cancel(); }
    }

    /// <summary>
    /// Averages input samples down to 16 kHz as they arrive (a box filter: each output is the mean of the input it spans), so that a
    /// microphone running at 44.1 or 48 kHz doesn't fold its high frequencies into the band the model listens to.
    /// </summary>
    public sealed class BoxResampler
    {
        private readonly double _ratio;
        private double _position, _next, _sum;

        /// <param name="inputRate">The input's sample rate (below 16 kHz, samples are repeated)</param>
        public BoxResampler(double inputRate)
        {
            if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
            _ratio = inputRate / SoundInput.SampleRate;
            _next = _ratio;
        }

        /// <summary>The 16 kHz samples the next <paramref name="count"/> input samples complete.</summary>
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
}
