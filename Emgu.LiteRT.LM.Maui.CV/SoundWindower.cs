// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Maui.CV;

// Cuts a stream of samples into YAMNet's windows: the last 15,600 samples every 7,800, once there are that many. A window that comes due
// while the last one is still being scored is skipped, so a slow device falls behind on windows, not on audio.
/// <summary>Cuts a stream of microphone samples into the windows a sound classifier scores, dropping what arrives while one is being scored.</summary>
public sealed class SoundWindower
{
    private readonly List<float> _buffer = new List<float>();
    private readonly object _lock = new object();
    private int _since;
    private bool _busy;

    /// <returns>A window to score (call <see cref="Done"/> afterwards), or null</returns>
    /// <summary>Add samples; returns a window to score when one is ready, else null.</summary>
    /// <param name="samples">The samples</param>
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

    /// <summary>The window handed out was scored: the next can be made.</summary>
    public void Done() { lock (_lock) _busy = false; }

    /// <summary>Forget the samples kept so far.</summary>
    public void Reset() { lock (_lock) { _buffer.Clear(); _since = 0; } }
}
