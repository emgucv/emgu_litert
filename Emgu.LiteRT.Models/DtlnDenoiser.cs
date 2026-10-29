// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// DTLN (the dual-signal transformation LSTM network, Westhausen and Meyer, Interspeech 2020, MIT, trained on the Deep
    /// Noise Suppression Challenge's data) removing background noise as audio arrives: 32 ms blocks every 8 ms through its two
    /// stateful TFLite models (breizhn/DTLN's pretrained model_1 and model_2, 1.5 MB and 2.5 MB) - a mask on the block's
    /// spectrum, then a learned transform of the masked block - and overlap-add. The output lags the input by
    /// <see cref="Latency"/> samples (24 ms); <see cref="Denoise"/> takes that lag out. CPU, a thread: about 7x real time on
    /// an M1 in this class (its 512-point FFTs included); on the Mac it raised noisy synthetic speech from 15.0 to 21.1 dB
    /// SI-SNR.
    /// </summary>
    public sealed class DtlnDenoiser : SpeechDenoiser
    {
        /// <summary>The samples in one block (32 ms).</summary>
        public const int BlockSamples = 512;
        /// <summary>The samples between blocks (8 ms): the output comes in steps this long.</summary>
        public const int ShiftSamples = 128;
        /// <summary>How many samples the output lags the input in <see cref="Process"/>.</summary>
        public const int Latency = BlockSamples - ShiftSamples;

        private const int Bins = BlockSamples / 2 + 1, StateValues = 512;
        private const string Revision = "1de1f15a8b5b7e1c44905618ff2ef70ca8277fbc";

        private readonly Environment _environment;
        private readonly Model _maskModel, _blockModel;
        private readonly CompiledModel _mask, _block;
        private readonly TensorBuffer[] _maskIn, _maskOut, _blockIn, _blockOut;
        private readonly int _magnitudeIn, _maskStateIn, _maskOutput, _maskStateOut, _blockInput, _blockStateIn, _blockOutput, _blockStateOut;
        private readonly Fft _fft = new Fft(BlockSamples);
        private float[] _maskState = new float[StateValues], _blockState = new float[StateValues];
        private readonly double[] _input = new double[BlockSamples], _output = new double[BlockSamples];
        private readonly double[] _re = new double[BlockSamples], _im = new double[BlockSamples];
        private readonly float[] _magnitude = new float[Bins], _estimate = new float[BlockSamples];
        private readonly List<float> _pending = new List<float>();
        private bool _disposed;

        /// <summary>The first model (the spectral mask), at a fixed revision of breizhn/DTLN.</summary>
        public static DownloadableFile MaskModelFile => new DownloadableFile(
            "https://raw.githubusercontent.com/breizhn/DTLN/" + Revision + "/pretrained_model/model_1.tflite",
            "DTLN",
            "91281a38e80fe9fd330e28eda7e16fe4e483ee5199a3e687a099939013c25de0");

        /// <summary>The second model (the learned transform), at a fixed revision of breizhn/DTLN.</summary>
        public static DownloadableFile BlockModelFile => new DownloadableFile(
            "https://raw.githubusercontent.com/breizhn/DTLN/" + Revision + "/pretrained_model/model_2.tflite",
            "DTLN",
            "7ae37ec802862d8a65b5cdabfbcbbe22caaf7cd39e79adf574d15837d1520830");

        /// <summary>Download the two models if needed.</summary>
        /// <param name="cacheDirectory">Where to (default: a folder in the temp path)</param>
        /// <param name="progress">Status lines, or null</param>
        /// <param name="token">Cancels it</param>
        /// <returns>The two files' paths</returns>
        public static async Task<(string MaskModel, string BlockModel)> DownloadAsync(string cacheDirectory = null, Action<string> progress = null,
            CancellationToken token = default)
        {
            string[] paths = await DownloadAsync(new[] { (MaskModelFile, "dtln_model_1.tflite"), (BlockModelFile, "dtln_model_2.tflite") },
                cacheDirectory, "noise removal models", progress, token).ConfigureAwait(false);
            return (paths[0], paths[1]);
        }

        /// <summary>Compile the two models on the CPU.</summary>
        /// <param name="maskModelPath">model_1.tflite (<see cref="DownloadAsync"/>)</param>
        /// <param name="blockModelPath">model_2.tflite</param>
        /// <param name="numThreads">CPU threads (one is plenty: each step is small)</param>
        public DtlnDenoiser(string maskModelPath, string blockModelPath, int numThreads = 1)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                _environment = new Environment();
                _maskModel = new Model(_environment, maskModelPath);
                _blockModel = new Model(_environment, blockModelPath);
                // Each model: a 3-D signal ([1,1,257] magnitudes / [1,1,512] block) and a 4-D LSTM state ([1,2,128,2]).
                (_magnitudeIn, _maskStateIn) = Find(_maskModel.GetSignature(0), true, Bins);
                (_maskOutput, _maskStateOut) = Find(_maskModel.GetSignature(0), false, Bins);
                (_blockInput, _blockStateIn) = Find(_blockModel.GetSignature(0), true, BlockSamples);
                (_blockOutput, _blockStateOut) = Find(_blockModel.GetSignature(0), false, BlockSamples);
                _mask = Compile(_maskModel, numThreads);
                _block = Compile(_blockModel, numThreads);
                _maskIn = _mask.CreateInputBuffers();
                _maskOut = _mask.CreateOutputBuffers();
                _blockIn = _block.CreateInputBuffers();
                _blockOut = _block.CreateOutputBuffers();
            }
            catch { Dispose(); throw; }
        }

        private CompiledModel Compile(Model model, int numThreads)
        {
            using (var options = new Options(HwAccelerators.Cpu))
            {
                options.AddCpuOptions(new CpuOptions { NumThreads = numThreads });
                return new CompiledModel(_environment, model, options);
            }
        }

        // The index of the signal tensor (3-D, `values` values) and of the state (4-D, 512 values).
        private static (int Signal, int State) Find(Signature signature, bool inputs, int values)
        {
            int count = inputs ? signature.InputCount : signature.OutputCount, signal = -1, state = -1;
            for (int i = 0; i < count; i++)
            {
                int[] dims = (inputs ? signature.GetInputTensorType(i) : signature.GetOutputTensorType(i)).Dimensions;
                int size = 1;
                foreach (int d in dims) size *= d;
                if (dims.Length == 4 && size == StateValues) state = i;
                else if (dims.Length == 3 && size == values) signal = i;
            }
            if (count != 2 || signal < 0 || state < 0)
                throw new ArgumentException("Expected DTLN's models: a [1,1," + values + "] signal and a [1,2,128,2] state.");
            return (signal, state);
        }

        /// <summary>Start again: the models' state and the buffers cleared (a new recording).</summary>
        public void Reset()
        {
            CheckActive();
            _maskState = new float[StateValues];
            _blockState = new float[StateValues];
            Array.Clear(_input, 0, _input.Length);
            Array.Clear(_output, 0, _output.Length);
            _pending.Clear();
        }

        /// <summary>
        /// Clean audio as it arrives: the samples are added to what is waiting, and every full step of
        /// <see cref="ShiftSamples"/> gives as many cleaned samples, <see cref="Latency"/> samples behind the input.
        /// </summary>
        /// <param name="samples">The next 16 kHz mono samples, any number</param>
        /// <returns>The cleaned samples for the steps completed (a multiple of <see cref="ShiftSamples"/>, maybe none)</returns>
        public float[] Process(float[] samples)
        {
            CheckActive();
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            _pending.AddRange(samples);
            int steps = _pending.Count / ShiftSamples;
            var result = new float[steps * ShiftSamples];
            for (int s = 0; s < steps; s++)
                Step(s * ShiftSamples, result);
            _pending.RemoveRange(0, steps * ShiftSamples);
            return result;
        }

        // One block: the newest 128 samples in, the magnitudes masked (phase kept), the learned transform, overlap-add.
        private void Step(int from, float[] result)
        {
            Array.Copy(_input, ShiftSamples, _input, 0, BlockSamples - ShiftSamples);
            for (int n = 0; n < ShiftSamples; n++) _input[BlockSamples - ShiftSamples + n] = _pending[from + n];
            Array.Copy(_input, _re, BlockSamples);
            Array.Clear(_im, 0, BlockSamples);
            _fft.Transform(_re, _im, false);
            for (int k = 0; k < Bins; k++) _magnitude[k] = (float)Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            _maskIn[_magnitudeIn].Write(_magnitude);
            _maskIn[_maskStateIn].Write(_maskState);
            _mask.Run(_maskIn, _maskOut);
            float[] mask = _maskOut[_maskOutput].Read<float>();
            _maskState = _maskOut[_maskStateOut].Read<float>();
            // The masked spectrum, mirrored for a real signal, back to time.
            for (int k = 0; k < Bins; k++) { _re[k] *= mask[k]; _im[k] *= mask[k]; }
            for (int k = 1; k < Bins - 1; k++) { _re[BlockSamples - k] = _re[k]; _im[BlockSamples - k] = -_im[k]; }
            _fft.Transform(_re, _im, true);
            for (int n = 0; n < BlockSamples; n++) _estimate[n] = (float)_re[n];
            _blockIn[_blockInput].Write(_estimate);
            _blockIn[_blockStateIn].Write(_blockState);
            _block.Run(_blockIn, _blockOut);
            float[] block = _blockOut[_blockOutput].Read<float>();
            _blockState = _blockOut[_blockStateOut].Read<float>();
            Array.Copy(_output, ShiftSamples, _output, 0, BlockSamples - ShiftSamples);
            Array.Clear(_output, BlockSamples - ShiftSamples, ShiftSamples);
            for (int n = 0; n < BlockSamples; n++) _output[n] += block[n];
            for (int n = 0; n < ShiftSamples; n++) result[from + n] = (float)_output[n];
        }

        /// <summary>A whole recording without its background noise, aligned with the input (from a fresh state).</summary>
        /// <param name="samples">16 kHz mono samples, -1..1</param>
        public override float[] Denoise(float[] samples)
        {
            CheckActive();
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            Reset();
            var padded = new float[samples.Length + Latency + ShiftSamples];
            Array.Copy(samples, padded, samples.Length);
            float[] output = Process(padded);
            var result = new float[samples.Length];
            Array.Copy(output, Latency, result, 0, Math.Min(samples.Length, output.Length - Latency));
            Reset();
            return result;
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DtlnDenoiser));
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var buffers in new[] { _maskIn, _maskOut, _blockIn, _blockOut })
                if (buffers != null) foreach (var b in buffers) b?.Dispose();
            _mask?.Dispose();
            _block?.Dispose();
            _maskModel?.Dispose();
            _blockModel?.Dispose();
            _environment?.Dispose();
        }

        // An in-place radix-2 complex FFT of a fixed power-of-two length; the inverse divides by the length.
        private sealed class Fft
        {
            private readonly int _n;
            private readonly int[] _reversed;
            private readonly double[] _cos, _sin;

            public Fft(int n)
            {
                _n = n;
                _reversed = new int[n];
                int bits = 0;
                while ((1 << bits) < n) bits++;
                for (int i = 0; i < n; i++)
                {
                    int r = 0;
                    for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                    _reversed[i] = r;
                }
                _cos = new double[n / 2];
                _sin = new double[n / 2];
                for (int i = 0; i < n / 2; i++)
                {
                    _cos[i] = Math.Cos(2 * Math.PI * i / n);
                    _sin[i] = Math.Sin(2 * Math.PI * i / n);
                }
            }

            public void Transform(double[] re, double[] im, bool inverse)
            {
                for (int i = 0; i < _n; i++)
                {
                    int j = _reversed[i];
                    if (j > i)
                    {
                        double t = re[i]; re[i] = re[j]; re[j] = t;
                        t = im[i]; im[i] = im[j]; im[j] = t;
                    }
                }
                double sign = inverse ? 1 : -1;
                for (int size = 2; size <= _n; size <<= 1)
                {
                    int half = size / 2, step = _n / size;
                    for (int start = 0; start < _n; start += size)
                        for (int k = 0; k < half; k++)
                        {
                            double wr = _cos[k * step], wi = sign * _sin[k * step];
                            int a = start + k, b = a + half;
                            double tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                            re[b] = re[a] - tr; im[b] = im[a] - ti;
                            re[a] += tr; im[a] += ti;
                        }
                }
                if (inverse)
                    for (int i = 0; i < _n; i++) { re[i] /= _n; im[i] /= _n; }
            }
        }
    }
}
