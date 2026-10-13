//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// The host-side math of the Z-Image-Turbo pipeline (ZImageTurbo): the sigma schedule, the timestep embedder, RoPE
    /// tables, (un)patchify, the Euler step, the seeded noise and the VAE boundary. Ported from ZImageMath.kt of
    /// https://github.com/alexeyw/android-image-generator (Apache-2.0), which ports reference/zimage_math.py there and is
    /// pinned by golden fixtures recorded from that reference; float where it computes in float32, double where it
    /// computes in float64, so the results match bit for bit or to float rounding.
    /// </summary>
    internal static class ZImageMath
    {
        // The fixed geometry of the published 256 px graphs.
        public const int TextLength = 64; // qwen_enc sequence length
        public const int CaptionLength = 32; // caption slot of z_embc / z_refc
        public const int LatentChannels = 16;
        public const int LatentSize = 32; // 256 px / the VAE's factor 8
        public const int Patch = 2;
        public const int Grid = LatentSize / Patch; // 16 x 16 patches
        public const int ImageTokens = Grid * Grid; // 256
        public const int UnifiedTokens = ImageTokens + CaptionLength; // 288: [image, caption]
        public const int PatchDim = Patch * Patch * LatentChannels; // 64
        public const int LatentLength = LatentChannels * LatentSize * LatentSize;
        public const int Dim = 3840;
        public const int TextDim = 2560;
        public const int AdalnDim = 256;
        public const int TimestepMid = 1024;
        public const int RopeHalf = 64; // head_dim 128 / 2: the width of the cos/sin tables
        public const int ImageSize = 256;

        private static readonly int[] RopeAxes = { 32, 48, 48 }; // (t, h, w)
        private const double RopeTheta = 256.0;
        private const float TimestepScale = 1000f;
        private const float ScheduleShift = 3f;
        private const float VaeScaling = 0.3611f;
        private const float VaeShift = 0.1159f;

        // ---- scheduler ----

        /// <summary>
        /// FlowMatchEuler with shift 3 and no dynamic shifting: numSteps + 1 sigmas, the last 0.
        /// </summary>
        public static float[] Sigmas(int numSteps)
        {
            float[] sigmas = new float[numSteps + 1];
            double stop = 1.0 / numSteps;
            double step = numSteps > 1 ? (stop - 1.0) / (numSteps - 1) : 0.0;
            for (int i = 0; i < numSteps; i++)
            {
                float s = (float)(i == numSteps - 1 ? stop : 1.0 + i * step); // np.linspace
                sigmas[i] = ScheduleShift * s / (1f + (ScheduleShift - 1f) * s);
            }
            return sigmas;
        }

        /// <summary>
        /// The time the transformer is given: (1000 - sigma * 1000) / 1000.
        /// </summary>
        public static float ModelTime(float sigma)
        {
            float ts = sigma * 1000f;
            return (1000f - ts) / 1000f;
        }

        // ---- the timestep embedder (a small MLP run on the host) ----

        public static float[] TimestepFrequencies(float t, int dim = AdalnDim)
        {
            int half = dim / 2;
            float[] result = new float[dim];
            for (int i = 0; i < half; i++)
            {
                float freq = (float)Math.Exp(-Math.Log(10000.0) * i / half);
                float arg = t * freq;
                result[i] = (float)Math.Cos(arg);
                result[half + i] = (float)Math.Sin(arg);
            }
            return result;
        }

        /// <summary>
        /// The weights of t_embedder.mlp.{0,2}, in PyTorch's Linear layout [out, in].
        /// </summary>
        public sealed class TimestepWeights
        {
            public float[] W1; // [1024, 256]
            public float[] B1; // [1024]
            public float[] W2; // [256, 1024]
            public float[] B2; // [256]
        }

        /// <summary>
        /// t_embedder(t * 1000): the adaln vector [256] the graphs take.
        /// </summary>
        public static float[] AdalnInput(float t, TimestepWeights w)
        {
            float[] x = TimestepFrequencies(t * TimestepScale);
            float[] h = new float[TimestepMid];
            for (int o = 0; o < TimestepMid; o++)
            {
                double acc = w.B1[o];
                int row = o * AdalnDim;
                for (int i = 0; i < AdalnDim; i++)
                    acc += (double)w.W1[row + i] * x[i];
                float v = (float)acc;
                h[o] = v / (1f + (float)Math.Exp(-v)); // SiLU
            }
            float[] result = new float[AdalnDim];
            for (int o = 0; o < AdalnDim; o++)
            {
                double acc = w.B2[o];
                int row = o * TimestepMid;
                for (int i = 0; i < TimestepMid; i++)
                    acc += (double)w.W2[row + i] * h[i];
                result[o] = (float)acc;
            }
            return result;
        }

        // ---- RoPE ----

        /// <summary>
        /// The cos and sin tables, each [positions, 64], for positions given as (t, h, w) rows.
        /// </summary>
        public static void RopeCosSin(int[][] positions, out float[] cos, out float[] sin)
        {
            int count = positions.Length;
            cos = new float[count * RopeHalf];
            sin = new float[count * RopeHalf];
            int column = 0;
            for (int axis = 0; axis < RopeAxes.Length; axis++)
            {
                int d = RopeAxes[axis];
                double[] freqs = new double[d / 2];
                for (int k = 0; k < freqs.Length; k++)
                    freqs[k] = 1.0 / Math.Pow(RopeTheta, (2.0 * k) / d);
                for (int r = 0; r < count; r++)
                {
                    double pos = positions[r][axis];
                    for (int k = 0; k < freqs.Length; k++)
                    {
                        double angle = (float)(pos * freqs[k]);
                        cos[r * RopeHalf + column + k] = (float)Math.Cos(angle);
                        sin[r * RopeHalf + column + k] = (float)Math.Sin(angle);
                    }
                }
                column += d / 2;
            }
        }

        /// <summary>
        /// The caption, padded to CaptionLength: (1..32, 0, 0) - the pad slots keep counting.
        /// </summary>
        public static int[][] CaptionPositions()
        {
            int[][] positions = new int[CaptionLength][];
            for (int i = 0; i < CaptionLength; i++)
                positions[i] = new[] { i + 1, 0, 0 };
            return positions;
        }

        /// <summary>
        /// The image patches: (CaptionLength + 1, h, w), row-major over (h, w).
        /// </summary>
        public static int[][] ImagePositions()
        {
            int[][] positions = new int[ImageTokens][];
            for (int i = 0; i < ImageTokens; i++)
                positions[i] = new[] { CaptionLength + 1, i / Grid, i % Grid };
            return positions;
        }

        // ---- (un)patchify ----

        /// <summary>
        /// [16, 32, 32] -> [256, 64]; the index in a patch vector is (ph * 2 + pw) * 16 + c.
        /// </summary>
        public static float[] Patchify(float[] latent)
        {
            float[] tokens = new float[ImageTokens * PatchDim];
            for (int ht = 0; ht < Grid; ht++)
                for (int wt = 0; wt < Grid; wt++)
                    for (int ph = 0; ph < Patch; ph++)
                        for (int pw = 0; pw < Patch; pw++)
                        {
                            int token = ht * Grid + wt;
                            int y = ht * Patch + ph;
                            int x = wt * Patch + pw;
                            for (int c = 0; c < LatentChannels; c++)
                                tokens[token * PatchDim + (ph * Patch + pw) * LatentChannels + c] = latent[(c * LatentSize + y) * LatentSize + x];
                        }
            return tokens;
        }

        /// <summary>
        /// The first ImageTokens rows of [N, 64] -> [16, 32, 32]; the inverse of Patchify.
        /// </summary>
        public static float[] Unpatchify(float[] tokens)
        {
            float[] latent = new float[LatentLength];
            for (int ht = 0; ht < Grid; ht++)
                for (int wt = 0; wt < Grid; wt++)
                    for (int ph = 0; ph < Patch; ph++)
                        for (int pw = 0; pw < Patch; pw++)
                        {
                            int token = ht * Grid + wt;
                            int y = ht * Patch + ph;
                            int x = wt * Patch + pw;
                            for (int c = 0; c < LatentChannels; c++)
                                latent[(c * LatentSize + y) * LatentSize + x] = tokens[token * PatchDim + (ph * Patch + pw) * LatentChannels + c];
                        }
            return latent;
        }

        /// <summary>
        /// An Euler step of the flow: latent += (sigmaNext - sigma) * noisePrediction, where noisePrediction = -modelOutput.
        /// </summary>
        public static void EulerStep(float[] latent, float[] modelOutput, float sigma, float sigmaNext)
        {
            float dt = sigmaNext - sigma;
            for (int i = 0; i < latent.Length; i++)
                latent[i] = latent[i] + dt * -modelOutput[i];
        }

        // ---- seeded noise, bit-identical with the Python reference ----

        public sealed class SplitMix64
        {
            private ulong _state;

            public SplitMix64(long seed)
            {
                _state = unchecked((ulong)seed);
            }

            public ulong NextULong()
            {
                unchecked
                {
                    _state += 0x9E3779B97F4A7C15UL;
                    ulong z = _state;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return z ^ (z >> 31);
                }
            }

            /// <summary>
            /// Uniform in [0, 1), 53 bits.
            /// </summary>
            public double NextDouble()
            {
                return (NextULong() >> 11) * (1.0 / (1L << 53));
            }
        }

        public static float[] GaussianNoise(long seed, int count = LatentLength)
        {
            SplitMix64 rng = new SplitMix64(seed);
            float[] noise = new float[count];
            for (int i = 0; i < count; i += 2)
            {
                double u1 = 1.0 - rng.NextDouble(); // (0, 1]
                double u2 = rng.NextDouble();
                double r = Math.Sqrt(-2.0 * Math.Log(u1));
                noise[i] = (float)(r * Math.Cos(2.0 * Math.PI * u2));
                if (i + 1 < count)
                    noise[i + 1] = (float)(r * Math.Sin(2.0 * Math.PI * u2));
            }
            return noise;
        }

        // ---- the VAE boundary ----

        public static float[] VaeInput(float[] latent)
        {
            float[] z = new float[latent.Length];
            for (int i = 0; i < latent.Length; i++)
                z[i] = latent[i] / VaeScaling + VaeShift;
            return z;
        }

        /// <summary>
        /// The VAE's output in [-1, 1], planar [3, H, W] -> interleaved RGB bytes.
        /// </summary>
        public static byte[] ToRgb(float[] chw, int size = ImageSize)
        {
            int plane = size * size;
            byte[] rgb = new byte[plane * 3];
            for (int p = 0; p < plane; p++)
            {
                rgb[p * 3] = ToByte(chw[p]);
                rgb[p * 3 + 1] = ToByte(chw[plane + p]);
                rgb[p * 3 + 2] = ToByte(chw[2 * plane + p]);
            }
            return rgb;
        }

        private static byte ToByte(float v)
        {
            float x = v / 2f + 0.5f;
            if (x < 0f)
                x = 0f;
            else if (x > 1f)
                x = 1f;
            return (byte)(int)(x * 255f + 0.5f);
        }
    }
}
