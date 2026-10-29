// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Kaldi's log-mel filterbank features (as torchaudio.compliance.kaldi.fbank with its defaults): 25 ms frames every
    /// 10 ms without padding at the edges (snip edges), the frame's mean removed, pre-emphasis 0.97, a window, a
    /// 512-point power spectrum, triangular mel filters from 20 Hz to half the sample rate and the log, floored at
    /// float's epsilon. No dither. What speaker-embedding models such as WeSpeaker take (<see cref="SpeakerEmbedder"/>).
    /// </summary>
    public sealed class KaldiFbank
    {
        private readonly int _sampleRate, _frameLength, _frameShift, _fftSize, _melBins;
        private readonly double[] _window;
        private readonly double[][] _filters; // per mel bin: the weight of each FFT bin 0..fftSize/2-1
        private readonly int[] _filterStart, _filterEnd;

        /// <summary>Create the filterbank.</summary>
        /// <param name="sampleRate">The audio's sample rate</param>
        /// <param name="melBins">The number of mel bins</param>
        /// <param name="hamming">A Hamming window (true; WeSpeaker's) or Kaldi's default Povey window (false)</param>
        public KaldiFbank(int sampleRate = 16000, int melBins = 80, bool hamming = true)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (melBins < 1) throw new ArgumentOutOfRangeException(nameof(melBins));
            _sampleRate = sampleRate;
            _melBins = melBins;
            _frameLength = sampleRate * 25 / 1000;
            _frameShift = sampleRate * 10 / 1000;
            _fftSize = 1;
            while (_fftSize < _frameLength) _fftSize <<= 1;
            _window = new double[_frameLength];
            for (int n = 0; n < _frameLength; n++)
            {
                double c = Math.Cos(2 * Math.PI * n / (_frameLength - 1));
                _window[n] = hamming ? 0.54 - 0.46 * c : Math.Pow(0.5 - 0.5 * c, 0.85);
            }
            // Kaldi's mel banks: equally spaced on the mel scale from 20 Hz to Nyquist; the Nyquist bin gets no weight.
            int fftBins = _fftSize / 2;
            double binWidth = (double)sampleRate / _fftSize;
            double melLow = Mel(20), melHigh = Mel(sampleRate / 2.0);
            double delta = (melHigh - melLow) / (melBins + 1);
            _filters = new double[melBins][];
            _filterStart = new int[melBins];
            _filterEnd = new int[melBins];
            for (int m = 0; m < melBins; m++)
            {
                double left = melLow + m * delta, center = left + delta, right = center + delta;
                var weights = new double[fftBins];
                int start = -1, end = -1;
                for (int i = 0; i < fftBins; i++)
                {
                    double mel = Mel(binWidth * i);
                    if (mel > left && mel < right)
                    {
                        weights[i] = mel <= center ? (mel - left) / (center - left) : (right - mel) / (right - center);
                        if (start < 0) start = i;
                        end = i + 1;
                    }
                }
                _filters[m] = weights;
                _filterStart[m] = Math.Max(0, start);
                _filterEnd[m] = Math.Max(0, end);
            }
        }

        /// <summary>The number of frames for this many samples (0 if fewer than one frame).</summary>
        /// <param name="samples">The number of samples</param>
        public int FrameCount(int samples) => samples < _frameLength ? 0 : 1 + (samples - _frameLength) / _frameShift;

        /// <summary>The number of mel bins.</summary>
        public int MelBins => _melBins;

        /// <summary>
        /// The features of mono audio in -1..1 (scaled to 16-bit values, as Kaldi reads WAV files): frames x mel bins,
        /// row by row.
        /// </summary>
        /// <param name="audio">The samples</param>
        /// <param name="offset">The first sample</param>
        /// <param name="count">How many samples</param>
        public float[] Compute(float[] audio, int offset, int count)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            if (offset < 0 || count < 0 || offset + count > audio.Length) throw new ArgumentOutOfRangeException(nameof(count));
            int frames = FrameCount(count);
            var result = new float[frames * _melBins];
            var frame = new double[_frameLength];
            var re = new double[_fftSize];
            var im = new double[_fftSize];
            var power = new double[_fftSize / 2];
            for (int f = 0; f < frames; f++)
            {
                int first = offset + f * _frameShift;
                double mean = 0;
                for (int n = 0; n < _frameLength; n++)
                {
                    frame[n] = audio[first + n] * 32768.0;
                    mean += frame[n];
                }
                mean /= _frameLength;
                for (int n = 0; n < _frameLength; n++)
                    frame[n] -= mean;
                // Pre-emphasis, the first sample against itself (Kaldi's).
                for (int n = _frameLength - 1; n > 0; n--)
                    frame[n] -= 0.97 * frame[n - 1];
                frame[0] -= 0.97 * frame[0];
                Array.Clear(re, 0, _fftSize);
                Array.Clear(im, 0, _fftSize);
                for (int n = 0; n < _frameLength; n++)
                    re[n] = frame[n] * _window[n];
                Fft(re, im);
                for (int i = 0; i < power.Length; i++)
                    power[i] = re[i] * re[i] + im[i] * im[i];
                for (int m = 0; m < _melBins; m++)
                {
                    double energy = 0;
                    double[] weights = _filters[m];
                    for (int i = _filterStart[m]; i < _filterEnd[m]; i++)
                        energy += weights[i] * power[i];
                    result[f * _melBins + m] = (float)Math.Log(Math.Max(energy, 1.1920928955078125e-07));
                }
            }
            return result;
        }

        private static double Mel(double hz) => 1127.0 * Math.Log(1.0 + hz / 700.0);

        // In-place radix-2 FFT (the length is a power of two).
        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                double wr = Math.Cos(angle), wi = Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = nr;
                    }
                }
            }
        }
    }
}
