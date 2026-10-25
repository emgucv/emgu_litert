// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// MediaPipe's one-euro filter (util/filtering/one_euro_filter.cc) for one value: a low-pass filter whose cutoff rises with the value's speed, so a
    /// still landmark is held steady and a moving one follows quickly. The first value passes through; a timestamp that doesn't increase returns the
    /// value unfiltered, as in MediaPipe.
    /// </summary>
    public sealed class OneEuroFilter
    {
        private readonly double _minCutoff, _beta, _derivateCutoff;
        private double _frequency = 30;
        private long _lastTicks = -1;
        private bool _hasValue;
        private double _lastRaw, _value, _derivative;

        /// <summary>A filter</summary>
        /// <param name="minCutoff">The cutoff frequency (Hz) of a still value</param>
        /// <param name="beta">How much the cutoff rises with speed</param>
        /// <param name="derivateCutoff">The cutoff frequency (Hz) of the speed's own filter</param>
        public OneEuroFilter(double minCutoff, double beta, double derivateCutoff)
        {
            if (!(minCutoff > 1e-6)) throw new ArgumentOutOfRangeException(nameof(minCutoff));
            if (!(derivateCutoff > 1e-6)) throw new ArgumentOutOfRangeException(nameof(derivateCutoff));
            _minCutoff = minCutoff;
            _beta = beta;
            _derivateCutoff = derivateCutoff;
        }

        /// <summary>The filtered value at this time.</summary>
        /// <param name="time">The frame's time</param>
        /// <param name="value">The new value</param>
        /// <param name="valueScale">What the speed is multiplied by (1 / the object's size, so the filter doesn't depend on how near it is)</param>
        public double Apply(TimeSpan time, double value, double valueScale = 1)
        {
            long ticks = time.Ticks;
            if (_lastTicks >= ticks) return value;
            // MediaPipe starts with its last time at -1 (not 0), so it measures the frequency from the first call: harmless, as the first value
            // passes through unfiltered and its speed is 0.
            if (_lastTicks != 0 && ticks != 0) _frequency = TimeSpan.TicksPerSecond / (double)(ticks - _lastTicks);
            _lastTicks = ticks;
            double speed = _hasValue ? (value - _lastRaw) * valueScale * _frequency : 0;
            _derivative = _hasValue ? Mix(_derivative, speed, Alpha(_derivateCutoff)) : speed;
            double cutoff = _minCutoff + _beta * Math.Abs(_derivative);
            _value = _hasValue ? Mix(_value, value, Alpha(cutoff)) : value;
            _lastRaw = value;
            _hasValue = true;
            return _value;
        }

        private static double Mix(double previous, double value, double alpha) => alpha * value + (1 - alpha) * previous;

        private double Alpha(double cutoff)
        {
            double te = 1 / _frequency, tau = 1 / (2 * Math.PI * cutoff);
            return 1 / (1 + tau / te);
        }
    }

    /// <summary>
    /// MediaPipe's LandmarksSmoothingCalculator with a one-euro filter, and its VisibilitySmoothingCalculator: every coordinate of every landmark through
    /// its own <see cref="OneEuroFilter"/>, in pixels (x and z times the image's width, y times its height) with the speed divided by the object's size,
    /// and the visibility (the fourth value, when there is one) through a low-pass filter.
    /// </summary>
    public sealed class LandmarkSmoother
    {
        private readonly double _minCutoff, _beta, _derivateCutoff;
        private readonly float? _visibilityAlpha;
        private readonly bool _scaleByObject;
        private OneEuroFilter[][] _filters;
        private float[] _visibility;

        /// <summary>A smoother</summary>
        /// <param name="minCutoff">The one-euro filters' cutoff of a still landmark (Hz)</param>
        /// <param name="beta">How much their cutoff rises with speed</param>
        /// <param name="derivateCutoff">Their speed's cutoff (Hz)</param>
        /// <param name="visibilityAlpha">The visibility's low-pass weight of a new value, or null to leave it</param>
        /// <param name="scaleByObject">Divide the speed by the object's size (MediaPipe's value scaling); false for world landmarks in metres</param>
        public LandmarkSmoother(double minCutoff, double beta, double derivateCutoff, float? visibilityAlpha = null, bool scaleByObject = true)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _derivateCutoff = derivateCutoff;
            _visibilityAlpha = visibilityAlpha;
            _scaleByObject = scaleByObject;
        }

        /// <summary>Forget the past: the next landmarks pass through.</summary>
        public void Reset()
        {
            _filters = null;
            _visibility = null;
        }

        /// <summary>
        /// The smoothed landmarks (new arrays; values after the first three are copied, the visibility smoothed). Coordinates are fractions of the
        /// image (or metres when <paramref name="imageWidth"/> and <paramref name="imageHeight"/> are 1).
        /// </summary>
        /// <param name="landmarks">The landmarks, (x, y, z, ...) each</param>
        /// <param name="time">The frame's time</param>
        /// <param name="imageWidth">The image's width in pixels</param>
        /// <param name="imageHeight">The image's height in pixels</param>
        /// <param name="objectScale">The object's size in pixels (MediaPipe: the mean of its region's width and height); ignored without value scaling</param>
        public float[][] Apply(float[][] landmarks, TimeSpan time, int imageWidth = 1, int imageHeight = 1, double objectScale = 1)
        {
            if (landmarks == null) throw new ArgumentNullException(nameof(landmarks));
            if (_filters != null && _filters.Length != landmarks.Length) Reset();
            var result = new float[landmarks.Length][];
            for (int i = 0; i < landmarks.Length; i++) result[i] = (float[])landmarks[i].Clone();
            if (_visibilityAlpha.HasValue)
            {
                _visibility = _visibility ?? new float[landmarks.Length];
                bool first = _filters == null;
                for (int i = 0; i < landmarks.Length; i++)
                    if (landmarks[i].Length > 3)
                        result[i][3] = _visibility[i] = first ? landmarks[i][3]
                            : _visibilityAlpha.Value * landmarks[i][3] + (1 - _visibilityAlpha.Value) * _visibility[i];
            }
            if (_scaleByObject && objectScale < 1e-6)
            {
                // MediaPipe returns the landmarks as they are when the object is too small to scale by.
                _filters = _filters ?? NewFilters(landmarks.Length);
                return result;
            }
            double valueScale = _scaleByObject ? 1 / objectScale : 1;
            _filters = _filters ?? NewFilters(landmarks.Length);
            double[] size = { imageWidth, imageHeight, imageWidth };
            for (int i = 0; i < landmarks.Length; i++)
                for (int k = 0; k < 3; k++)
                    result[i][k] = (float)(_filters[i][k].Apply(time, landmarks[i][k] * size[k], valueScale) / size[k]);
            return result;
        }

        private OneEuroFilter[][] NewFilters(int count)
        {
            var filters = new OneEuroFilter[count][];
            for (int i = 0; i < count; i++)
                filters[i] = new[]
                {
                    new OneEuroFilter(_minCutoff, _beta, _derivateCutoff), new OneEuroFilter(_minCutoff, _beta, _derivateCutoff),
                    new OneEuroFilter(_minCutoff, _beta, _derivateCutoff)
                };
            return filters;
        }
    }
}
