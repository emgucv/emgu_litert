// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>A sound class and its score.</summary>
    public sealed class SoundScore
    {
        /// <summary>The class's index in <see cref="YamNet.Labels"/>, 0..520</summary>
        public int Index { get; internal set; }
        /// <summary>The AudioSet class name, e.g. "Doorbell"</summary>
        public string Label { get; internal set; }
        /// <summary>Probability 0..1. Classes aren't exclusive: "Dog" and "Bark" are both high for a barking dog.</summary>
        public float Score { get; internal set; }
    }

    /// <summary>
    /// YAMNet, Google's audio event classifier (521 AudioSet classes: speech, laughter, dog, doorbell, siren, music, ...), as
    /// MediaPipe's audio classifier ships it: a float32 graph taking 15,600 mono samples at 16 kHz (0.975 s, in -1..1) and returning 521
    /// sigmoid scores, with the mel spectrogram inside the graph. The label list is appended to the file as a zip. The model is not
    /// copied to Emgu's server: it is downloaded from Google's (<see cref="ModelUrl"/>) and checked against <see cref="ModelSha256"/>.
    /// Calls are serialized by the host. See the demo's YAMNET.md.
    /// </summary>
    public sealed class YamNet : IDisposable
    {
        /// <summary>Google's MediaPipe copy of YAMNet (Apache-2.0 in its metadata), float32, version 1</summary>
        public const string ModelUrl = "https://storage.googleapis.com/mediapipe-models/audio_classifier/yamnet/float32/1/yamnet.tflite";
        /// <summary>The file's SHA-256 (upper case)</summary>
        public const string ModelSha256 = "4D8B4A53282DC83EF04E3E7DBC4FBC98082E34E44ED798E16C3A0CDD4C584FAF";
        /// <summary>The file's size in bytes</summary>
        public const long ModelSize = 4126810;
        /// <summary>The sample rate the model takes</summary>
        public const int SampleRate = 16000;
        /// <summary>Samples in one window (0.975 s)</summary>
        public const int WindowSamples = 15600;
        /// <summary>The usual step between windows (0.4875 s), as in YAMNet's own streaming demo (half a window)</summary>
        public const int HopSamples = 7800;
        /// <summary>The number of classes</summary>
        public const int ClassCount = 521;

        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private bool _disposed;

        /// <summary>The 521 class names in the model's order.</summary>
        public IReadOnlyList<string> Labels { get; }

        /// <summary>Compile a local yamnet.tflite. Compilation should run off the UI thread.</summary>
        public YamNet(string modelPath, int numThreads = 2, HwAccelerators accelerators = HwAccelerators.Cpu)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                Labels = ReadLabels(modelPath);
                _environment = new Environment();
                _model = new Model(_environment, modelPath);
                var signature = _model.GetSignature(0);
                var input = signature.GetInputTensorType(0);
                var output = signature.GetOutputTensorType(0);
                if (signature.InputCount != 1 || signature.OutputCount != 1 || input.ElementType != ElementType.Float32 ||
                    !input.Dimensions.SequenceEqual(new[] { WindowSamples }) || output.ElementType != ElementType.Float32 ||
                    !output.Dimensions.SequenceEqual(new[] { 1, ClassCount }))
                    throw new ArgumentException("Expected YAMNet: float32 input [15600], output [1,521].", nameof(modelPath));
                using (var options = new Options(accelerators))
                {
                    options.AddCpuOptions(new CpuOptions { NumThreads = numThreads });
                    _compiled = new CompiledModel(_environment, _model, options);
                }
                _inputs = _compiled.CreateInputBuffers();
                _outputs = _compiled.CreateOutputBuffers();
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Whether LiteRT reports the complete graph accelerated by the requested backend.</summary>
        public bool IsFullyAccelerated
        {
            get { CheckActive(); return _compiled.IsFullyAccelerated; }
        }

        /// <summary>The class names in a yamnet.tflite: a zip appended to the flatbuffer, with yamnet_label_list.txt.</summary>
        public static IReadOnlyList<string> ReadLabels(string modelPath)
        {
            using (var archive = ZipFile.OpenRead(modelPath))
            {
                var entry = archive.GetEntry("yamnet_label_list.txt") ?? throw new InvalidDataException("yamnet_label_list.txt isn't in " + Path.GetFileName(modelPath) + ".");
                using (var reader = new StreamReader(entry.Open()))
                {
                    var labels = new List<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.Length > 0) labels.Add(line.Trim());
                    if (labels.Count != ClassCount) throw new InvalidDataException("Expected " + ClassCount + " labels, got " + labels.Count + ".");
                    return labels.AsReadOnly();
                }
            }
        }

        /// <summary>Scores of the 521 classes for 15,600 samples starting at <paramref name="offset"/> (16 kHz, mono, -1..1).</summary>
        public float[] Classify(float[] samples, int offset = 0)
        {
            CheckActive();
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (offset < 0 || offset + WindowSamples > samples.Length) throw new ArgumentOutOfRangeException(nameof(offset), "A window is " + WindowSamples + " samples.");
            float[] window = samples;
            if (offset != 0 || samples.Length != WindowSamples)
            {
                window = new float[WindowSamples];
                Array.Copy(samples, offset, window, 0, WindowSamples);
            }
            _inputs[0].Write(window);
            _compiled.Run(_inputs, _outputs);
            return _outputs[0].Read<float>();
        }

        /// <summary>The best <paramref name="count"/> classes of a result, highest first.</summary>
        public IReadOnlyList<SoundScore> Top(float[] scores, int count = 5, float minimum = 0f)
        {
            if (scores == null || scores.Length != ClassCount) throw new ArgumentException("Expected " + ClassCount + " scores.", nameof(scores));
            return Enumerable.Range(0, ClassCount)
                .Where(i => scores[i] >= minimum)
                .OrderByDescending(i => scores[i])
                .Take(count)
                .Select(i => new SoundScore { Index = i, Label = Labels[i], Score = scores[i] })
                .ToList();
        }

        /// <summary>Convert to 16 kHz by linear interpolation (the model's own front end filters; a clean recording needs no more).</summary>
        public static float[] Resample(float[] samples, int sampleRate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (sampleRate < 1) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (sampleRate == SampleRate) return samples;
            int length = (int)((long)samples.Length * SampleRate / sampleRate);
            var result = new float[length];
            for (int i = 0; i < length; i++)
            {
                double position = i * (double)sampleRate / SampleRate;
                int i0 = Math.Min((int)position, samples.Length - 1);
                int i1 = Math.Min(i0 + 1, samples.Length - 1);
                float f = (float)(position - i0);
                result[i] = samples[i0] * (1 - f) + samples[i1] * f;
            }
            return result;
        }

        /// <summary>Read a PCM (8, 16, 24 or 32 bit) or float32 WAV file as mono samples in -1..1 (channels averaged).</summary>
        public static float[] ReadWave(byte[] data, out int sampleRate)
        {
            if (data == null || data.Length < 44 || BitConverter.ToUInt32(data, 0) != 0x46464952 || BitConverter.ToUInt32(data, 8) != 0x45564157)
                throw new InvalidDataException("Not a WAV file.");
            int format = 0, channels = 0, bits = 0, dataStart = -1, dataLength = 0;
            sampleRate = 0;
            int pos = 12;
            while (pos + 8 <= data.Length)
            {
                uint id = BitConverter.ToUInt32(data, pos);
                int size = (int)Math.Min(BitConverter.ToUInt32(data, pos + 4), (uint)(data.Length - pos - 8));
                if (id == 0x20746D66 && size >= 16)        // "fmt "
                {
                    format = BitConverter.ToUInt16(data, pos + 8);
                    channels = BitConverter.ToUInt16(data, pos + 10);
                    sampleRate = (int)BitConverter.ToUInt32(data, pos + 12);
                    bits = BitConverter.ToUInt16(data, pos + 22);
                    if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(data, pos + 32);   // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == 0x61746164)                  // "data"
                {
                    dataStart = pos + 8; dataLength = size;
                    break;
                }
                pos += 8 + size + (size & 1);
            }
            if (dataStart < 0 || channels < 1 || sampleRate < 1) throw new InvalidDataException("The WAV file has no usable fmt and data chunks.");
            bool isFloat = format == 3 && bits == 32;
            if (!(format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !isFloat)
                throw new NotSupportedException("Only PCM (8, 16, 24, 32 bit) and float32 WAV files are supported.");
            int bytes = bits / 8, frames = dataLength / (bytes * channels);
            var mono = new float[frames];
            for (int n = 0; n < frames; n++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int at = dataStart + (n * channels + c) * bytes;
                    switch (bits)
                    {
                        case 8: sum += (data[at] - 128) / 128.0; break;
                        case 16: sum += BitConverter.ToInt16(data, at) / 32768.0; break;
                        case 24: sum += ((data[at] << 8 | data[at + 1] << 16 | data[at + 2] << 24) >> 8) / 8388608.0; break;
                        default: sum += isFloat ? BitConverter.ToSingle(data, at) : BitConverter.ToInt32(data, at) / 2147483648.0; break;
                    }
                }
                mono[n] = (float)(sum / channels);
            }
            return mono;
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(YamNet));
        }

        /// <summary>Release the compiled model and its buffers.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_inputs != null) foreach (var b in _inputs) b?.Dispose();
            if (_outputs != null) foreach (var b in _outputs) b?.Dispose();
            _compiled?.Dispose();
            _model?.Dispose();
            _environment?.Dispose();
        }
    }
}
