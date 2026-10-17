//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Progress of a ZImageTurbo download
    /// </summary>
    public class ZImageDownloadProgress
    {
        /// <summary>The file being downloaded</summary>
        public String File { get; internal set; }

        /// <summary>The bytes of all the files on the device so far</summary>
        public long BytesDone { get; internal set; }

        /// <summary>The bytes of all the files</summary>
        public long TotalBytes { get; internal set; }
    }

    /// <summary>
    /// Progress of a ZImageTurbo generation
    /// </summary>
    public class ZImageProgress
    {
        /// <summary>What is being done, e.g. "Encoding the prompt", "Step 3 of 8", "Decoding the image"</summary>
        public String Stage { get; internal set; }

        /// <summary>The step being run (1-based), or 0 outside the steps</summary>
        public int Step { get; internal set; }

        /// <summary>How much of the generation is done, 0 to 1</summary>
        public double Fraction { get; internal set; }
    }

    /// <summary>
    /// An image ZImageTurbo generated
    /// </summary>
    public class ZImageResult
    {
        /// <summary>The pixels, 8-bit RGB, row by row from the top</summary>
        public byte[] Rgb { get; internal set; }

        /// <summary>The width in pixels</summary>
        public int Width { get; internal set; }

        /// <summary>The height in pixels</summary>
        public int Height { get; internal set; }

        /// <summary>The prompt</summary>
        public String Prompt { get; internal set; }

        /// <summary>The seed of the initial noise</summary>
        public long Seed { get; internal set; }

        /// <summary>The number of steps</summary>
        public int Steps { get; internal set; }

        /// <summary>How long the generation took</summary>
        public TimeSpan Elapsed { get; internal set; }
    }

    /// <summary>
    /// Text-to-image with Z-Image-Turbo (Alibaba Tongyi-MAI, a 6B single-stream diffusion transformer, Apache-2.0) on the
    /// device's CPU, through LiteRT's CompiledModel API: the 13 int8 graphs of litert-community/Z-Image-Turbo-LiteRT
    /// (9.8 GB) plus 0.78 GB of host tensors read from the original checkpoint (Tongyi-MAI/Z-Image-Turbo; only the needed
    /// byte ranges are downloaded). Makes 256x256 images from prompts of up to MaxPromptTokens tokens (about 23 words).
    /// <para>
    /// The graphs run one at a time on the CPU (XNNPACK), each compiled with a weight cache in the model folder: the first
    /// generation writes the caches (about 9 GB more), after which a graph loads in milliseconds as a memory map and stays
    /// file-backed. The GPU is no faster for this model and needs fp32 and several GB per graph. Turbo is distilled for
    /// guidance 0, so each step is one transformer pass. The host loop (tokenizer, schedule, timestep MLP, RoPE, Euler
    /// steps) is ported from https://github.com/alexeyw/android-image-generator (Apache-2.0).
    /// </para>
    /// </summary>
    public class ZImageTurbo : IDisposable
    {
        /// <summary>
        /// The default folder, under the local application data folder
        /// </summary>
        public const String DefaultLocalSubfolder = "Z-Image-Turbo";

        /// <summary>
        /// The width and height of the images
        /// </summary>
        public const int ImageSize = ZImageMath.ImageSize;

        private const String LiteRtRevision = "09ea3ae2ef44d04d0ad1591de36d1c13504e1521";
        private const String UpstreamRevision = "f332072aa78be7aecdf3ee76d5c247082da564a6";
        private const String LiteRtUrl = "https://huggingface.co/litert-community/Z-Image-Turbo-LiteRT/resolve/" + LiteRtRevision + "/";
        private const String UpstreamUrl = "https://huggingface.co/Tongyi-MAI/Z-Image-Turbo/resolve/" + UpstreamRevision + "/";
        private const String TextEncoder = "qwen_enc";
        private const String EmbedFile = "embed_tokens.bf16";
        private const int Vocabulary = 151936;

        // The graphs and their sizes at LiteRtRevision, smallest first.
        private static readonly KeyValuePair<String, long>[] Graphs =
        {
            new KeyValuePair<String, long>("z_embx", 308272L),
            new KeyValuePair<String, long>("zc_final", 1295936L),
            new KeyValuePair<String, long>("z_embc", 9906096L),
            new KeyValuePair<String, long>("zvae", 50139872L),
            new KeyValuePair<String, long>("z_refc", 355025040L),
            new KeyValuePair<String, long>("z_refx", 363386160L),
            new KeyValuePair<String, long>("zc_main0", 908480960L),
            new KeyValuePair<String, long>("zc_main1", 908480960L),
            new KeyValuePair<String, long>("zc_main2", 908480960L),
            new KeyValuePair<String, long>("zc_main3", 908480960L),
            new KeyValuePair<String, long>("zc_main4", 908480960L),
            new KeyValuePair<String, long>("zc_main5", 908480960L),
            new KeyValuePair<String, long>(TextEncoder, 3547652208L),
        };

        // Host tensors of the transformer's first checkpoint shard (float32 there): name -> file.
        private static readonly KeyValuePair<String, String>[] TransformerTensors =
        {
            new KeyValuePair<String, String>("t_embedder.mlp.0.weight", "t_emb_w1.f32"),
            new KeyValuePair<String, String>("t_embedder.mlp.0.bias", "t_emb_b1.f32"),
            new KeyValuePair<String, String>("t_embedder.mlp.2.weight", "t_emb_w2.f32"),
            new KeyValuePair<String, String>("t_embedder.mlp.2.bias", "t_emb_b2.f32"),
            new KeyValuePair<String, String>("cap_pad_token", "cap_pad_token.f32"),
        };

        // The files in the host folder and their sizes.
        private static readonly KeyValuePair<String, long>[] HostFiles =
        {
            new KeyValuePair<String, long>("vocab.json", 2776833L),
            new KeyValuePair<String, long>("merges.txt", 1671853L),
            new KeyValuePair<String, long>("t_emb_w1.f32", 1048576L),
            new KeyValuePair<String, long>("t_emb_b1.f32", 4096L),
            new KeyValuePair<String, long>("t_emb_w2.f32", 1048576L),
            new KeyValuePair<String, long>("t_emb_b2.f32", 1024L),
            new KeyValuePair<String, long>("cap_pad_token.f32", 15360L),
            new KeyValuePair<String, long>(EmbedFile, 777912320L),
        };

        private readonly String _folder;
        private Environment _environment;
        private QwenTokenizer _tokenizer;
        private ZImageMath.TimestepWeights _timestep;
        private float[] _captionPadToken;
        private float[] _imageCos, _imageSin, _captionCos, _captionSin, _unifiedCos, _unifiedSin;
        private String _cachedPrompt;
        private float[] _cachedCaption;
        private readonly SemaphoreSlim _busy = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Create the model. Call Init to download and load it.
        /// </summary>
        /// <param name="folder">The folder for the model files and the weight caches (about 21 GB in all), or null for
        /// DefaultLocalSubfolder under the local application data folder</param>
        public ZImageTurbo(String folder = null)
        {
            _folder = folder ?? Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), DefaultLocalSubfolder);
            NumThreads = Math.Max(1, Math.Min(6, System.Environment.ProcessorCount - 2));
        }

        /// <summary>
        /// The folder with the graphs, the host tensors (host/) and the weight caches (cache/)
        /// </summary>
        public String Folder
        {
            get { return _folder; }
        }

        /// <summary>
        /// The number of CPU threads the graphs run with. The default, cores minus two and at most six, was the fastest on
        /// a Galaxy S25 Ultra (6 threads: 29 s per image, 4 or 8: about 36 s).
        /// </summary>
        public int NumThreads { get; set; }

        /// <summary>
        /// The number of bytes Download fetches: 9.8 GB of graphs and 0.78 GB of host tensors. The weight caches the first
        /// generation writes take about 9 GB more.
        /// </summary>
        public static long DownloadSize
        {
            get { return Graphs.Sum(g => g.Value) + HostFiles.Sum(f => f.Value); }
        }

        /// <summary>
        /// The bytes of completed model files and resumable partial downloads already in the folder
        /// </summary>
        public long DownloadedBytes
        {
            get
            {
                return Graphs.Sum(g => DownloadedFileBytes(GraphFile(g.Key), g.Value))
                    + HostFiles.Sum(f => DownloadedFileBytes(HostFile(f.Key), f.Value));
            }
        }

        /// <summary>
        /// True if every model file is in the folder (checked by size)
        /// </summary>
        public bool IsDownloaded
        {
            get
            {
                return Graphs.All(g => FileLength(GraphFile(g.Key)) == g.Value)
                    && HostFiles.All(f => FileLength(HostFile(f.Key)) == f.Value);
            }
        }

        /// <summary>
        /// True once Init has loaded the tokenizer and host tensors
        /// </summary>
        public bool Initialized
        {
            get { return _environment != null; }
        }

        /// <summary>
        /// The most tokens a prompt can take: the graphs take a 32-token caption, the chat template included.
        /// </summary>
        public static int MaxPromptTokens
        {
            get { return ZImageMath.CaptionLength - QwenTokenizer.TemplateTokens; }
        }

        private String GraphFile(String name)
        {
            return Path.Combine(_folder, name + ".tflite");
        }

        private String HostFile(String name)
        {
            return Path.Combine(_folder, "host", name);
        }

        // Only a complete final file is usable. RangeDownloader resumes from .part, never an incomplete final.
        private static long DownloadedFileBytes(String path, long expected)
        {
            return FileLength(path) == expected ? expected : Math.Min(expected, FileLength(path + ".part"));
        }

        private static long FileLength(String path)
        {
            FileInfo info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }

        /// <summary>
        /// Download the model files that are missing; an interrupted download resumes where it stopped.
        /// </summary>
        /// <param name="progress">Reports the bytes done, or null</param>
        /// <param name="cancellationToken">Cancels the download (a partial file is kept to resume)</param>
        /// <returns>Async task</returns>
        public Task Download(IProgress<ZImageDownloadProgress> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => DownloadAsync(progress, cancellationToken), cancellationToken);
        }

        private async Task DownloadAsync(IProgress<ZImageDownloadProgress> progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.Combine(_folder, "host"));
            long total = DownloadSize;
            long done = DownloadedBytes;
            using (RangeDownloader downloader = new RangeDownloader())
            {
                Action<String, long, long> report = (file, fileDone, fileHad) =>
                {
                    if (progress != null)
                        progress.Report(new ZImageDownloadProgress { File = file, BytesDone = done + fileDone - fileHad, TotalBytes = total });
                };

                foreach (String name in new[] { "vocab.json", "merges.txt" })
                {
                    long size = HostFiles.First(f => f.Key == name).Value;
                    String file = HostFile(name);
                    if (FileLength(file) == size)
                        continue;
                    long had = DownloadedFileBytes(file, size);
                    await downloader.Download(UpstreamUrl + "tokenizer/" + name, 0, size, file, d => report(name, d, had), cancellationToken).ConfigureAwait(false);
                    done += size - had;
                }

                // The timestep embedder and the caption pad token: float32 tensors in the transformer's first shard.
                if (TransformerTensors.Any(t => FileLength(HostFile(t.Value)) != HostFiles.First(f => f.Key == t.Value).Value))
                {
                    String url = UpstreamUrl + "transformer/diffusion_pytorch_model-00001-of-00003.safetensors";
                    SafetensorsHeader header = await downloader.ReadSafetensorsHeader(url, cancellationToken).ConfigureAwait(false);
                    foreach (KeyValuePair<String, String> tensor in TransformerTensors)
                    {
                        SafetensorsHeader.Tensor info = header.Get(tensor.Key, "F32");
                        long size = HostFiles.First(f => f.Key == tensor.Value).Value;
                        if (info.End - info.Start != size)
                            throw new IOException(String.Format("{0} is {1} bytes, expected {2}", tensor.Key, info.End - info.Start, size));
                        String file = HostFile(tensor.Value);
                        if (FileLength(file) == size)
                            continue;
                        long had = DownloadedFileBytes(file, size);
                        await downloader.Download(url, info.Start, info.End, file, d => report(tensor.Value, d, had), cancellationToken).ConfigureAwait(false);
                        done += size - had;
                    }
                }

                // Qwen3's token embedding table (bf16), from the text encoder's first shard.
                long embedSize = HostFiles.First(f => f.Key == EmbedFile).Value;
                if (FileLength(HostFile(EmbedFile)) != embedSize)
                {
                    String url = UpstreamUrl + "text_encoder/model-00001-of-00003.safetensors";
                    SafetensorsHeader header = await downloader.ReadSafetensorsHeader(url, cancellationToken).ConfigureAwait(false);
                    SafetensorsHeader.Tensor info = header.Get("model.embed_tokens.weight", "BF16");
                    if (info.End - info.Start != embedSize)
                        throw new IOException(String.Format("embed_tokens is {0} bytes, expected {1}", info.End - info.Start, embedSize));
                    long had = DownloadedFileBytes(HostFile(EmbedFile), embedSize);
                    await downloader.Download(url, info.Start, info.End, HostFile(EmbedFile), d => report(EmbedFile, d, had), cancellationToken).ConfigureAwait(false);
                    done += embedSize - had;
                }

                foreach (KeyValuePair<String, long> graph in Graphs)
                {
                    String file = GraphFile(graph.Key);
                    if (FileLength(file) == graph.Value)
                        continue;
                    long had = DownloadedFileBytes(file, graph.Value);
                    String name = graph.Key + ".tflite";
                    await downloader.Download(LiteRtUrl + name, 0, graph.Value, file, d => report(name, d, had), cancellationToken).ConfigureAwait(false);
                    done += graph.Value - had;
                }
            }
        }

        /// <summary>
        /// Download the model files if needed, and load the tokenizer and the host tensors. The graphs are loaded when
        /// they run.
        /// </summary>
        /// <param name="progress">Reports the download, or null</param>
        /// <param name="cancellationToken">Cancels the download</param>
        /// <returns>Async task</returns>
        public async Task Init(IProgress<ZImageDownloadProgress> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (Initialized)
                return;
            if (!IsDownloaded)
                await Download(progress, cancellationToken).ConfigureAwait(false);
            await Task.Run(() =>
            {
                QwenTokenizer tokenizer = new QwenTokenizer(HostFile("vocab.json"), HostFile("merges.txt"));
                ZImageMath.TimestepWeights timestep = new ZImageMath.TimestepWeights
                {
                    W1 = ReadFloats(HostFile("t_emb_w1.f32"), ZImageMath.TimestepMid * ZImageMath.AdalnDim),
                    B1 = ReadFloats(HostFile("t_emb_b1.f32"), ZImageMath.TimestepMid),
                    W2 = ReadFloats(HostFile("t_emb_w2.f32"), ZImageMath.AdalnDim * ZImageMath.TimestepMid),
                    B2 = ReadFloats(HostFile("t_emb_b2.f32"), ZImageMath.AdalnDim),
                };
                float[] captionPadToken = ReadFloats(HostFile("cap_pad_token.f32"), ZImageMath.Dim);
                ZImageMath.RopeCosSin(ZImageMath.ImagePositions(), out _imageCos, out _imageSin);
                ZImageMath.RopeCosSin(ZImageMath.CaptionPositions(), out _captionCos, out _captionSin);
                _unifiedCos = _imageCos.Concat(_captionCos).ToArray();
                _unifiedSin = _imageSin.Concat(_captionSin).ToArray();
                Directory.CreateDirectory(Path.Combine(_folder, "cache"));
                _tokenizer = tokenizer;
                _timestep = timestep;
                _captionPadToken = captionPadToken;
                _environment = new Environment();
            }, cancellationToken).ConfigureAwait(false);
        }

        private void CheckInitialized()
        {
            if (!Initialized)
                throw new InvalidOperationException("The model is not initialized: call Init first");
        }

        /// <summary>
        /// The number of tokens a prompt takes (without the chat template); at most MaxPromptTokens can be generated.
        /// </summary>
        /// <param name="prompt">The prompt</param>
        /// <returns>The number of tokens</returns>
        public int CountPromptTokens(String prompt)
        {
            CheckInitialized();
            return _tokenizer.EncodePrompt(QwenTokenizer.CleanPrompt(prompt)).Length - QwenTokenizer.TemplateTokens;
        }

        /// <summary>
        /// Generate an image. Runs on a background thread; one generation at a time.
        /// </summary>
        /// <param name="prompt">What to draw, at most MaxPromptTokens tokens (about 23 words). Invisible characters are
        /// removed first.</param>
        /// <param name="seed">The seed of the initial noise: the same prompt, seed and steps give the same image</param>
        /// <param name="steps">The number of steps; Turbo is made for 8</param>
        /// <param name="progress">Reports the stage, or null</param>
        /// <param name="cancellationToken">Cancels the generation between graphs</param>
        /// <returns>The image</returns>
        public async Task<ZImageResult> Generate(String prompt, long seed, int steps = 8,
            IProgress<ZImageProgress> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            CheckInitialized();
            if (steps < 1)
                throw new ArgumentOutOfRangeException("steps");
            prompt = QwenTokenizer.CleanPrompt(prompt);
            await _busy.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => GenerateCore(prompt, seed, steps, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _busy.Release();
            }
        }

        private ZImageResult GenerateCore(String prompt, long seed, int steps, IProgress<ZImageProgress> progress,
            CancellationToken cancellationToken)
        {
            Stopwatch watch = Stopwatch.StartNew();
            bool needsText = prompt != _cachedPrompt;
            double units = (needsText ? 1 : 0) + steps + 1;
            double done = 0;
            Action<String, int> report = (stage, step) =>
            {
                if (progress != null)
                    progress.Report(new ZImageProgress { Stage = stage, Step = step, Fraction = done / units });
            };

            float[] caption;
            if (needsText)
            {
                report("Encoding the prompt", 0);
                caption = EncodePrompt(prompt, cancellationToken);
                _cachedPrompt = prompt;
                _cachedCaption = caption;
                done += 1;
            }
            else
            {
                caption = _cachedCaption;
            }

            float[] latent = ZImageMath.GaussianNoise(seed);
            float[] sigmas = ZImageMath.Sigmas(steps);
            for (int i = 0; i < steps; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                report(String.Format("Step {0} of {1}", i + 1, steps), i + 1);
                float[] output = Transformer(latent, caption, ZImageMath.ModelTime(sigmas[i]), cancellationToken);
                ZImageMath.EulerStep(latent, output, sigmas[i], sigmas[i + 1]);
                done += 1;
            }

            cancellationToken.ThrowIfCancellationRequested();
            report("Decoding the image", 0);
            float[] chw = RunGraph("zvae", ZImageMath.VaeInput(latent));
            done = units;
            report("Done", 0);
            return new ZImageResult
            {
                Rgb = ZImageMath.ToRgb(chw),
                Width = ImageSize,
                Height = ImageSize,
                Prompt = prompt,
                Seed = seed,
                Steps = steps,
                Elapsed = watch.Elapsed
            };
        }

        // The prompt -> the refined caption tokens [32, 3840], independent of the time step.
        internal float[] EncodePrompt(String prompt, CancellationToken cancellationToken)
        {
            int[] ids = _tokenizer.EncodePrompt(prompt);
            int n = ids.Length;
            if (n > ZImageMath.CaptionLength)
                throw new ArgumentException(String.Format(
                    "The prompt takes {0} tokens; at most {1} fit.", n - QwenTokenizer.TemplateTokens, MaxPromptTokens), "prompt");
            int[] padded = new int[ZImageMath.TextLength];
            for (int i = 0; i < padded.Length; i++)
                padded[i] = i < n ? ids[i] : QwenTokenizer.PadId;
            // The text encoder is causal and has no mask input: the padding can't reach the prompt's rows.
            float[] hidden = RunGraph(TextEncoder, EmbedTokens(padded));
            cancellationToken.ThrowIfCancellationRequested();

            // The caption, padded to 32 by repeating its last row (diffusers' _pad_with_ids).
            int d = ZImageMath.TextDim;
            float[] caption = new float[ZImageMath.CaptionLength * d];
            for (int r = 0; r < ZImageMath.CaptionLength; r++)
                Array.Copy(hidden, Math.Min(r, n - 1) * d, caption, r * d, d);
            float[] embedded = RunGraph("z_embc", caption);
            // torch.where(pad_mask, cap_pad_token, feats): the pad slots get the learned token.
            for (int r = n; r < ZImageMath.CaptionLength; r++)
                Array.Copy(_captionPadToken, 0, embedded, r * ZImageMath.Dim, ZImageMath.Dim);
            return RunGraph("z_refc", embedded, _captionCos, _captionSin);
        }

        // One transformer pass -> the model's output in the latent's layout [16, 32, 32].
        internal float[] Transformer(float[] latent, float[] caption, float t, CancellationToken cancellationToken)
        {
            float[] adaln = ZImageMath.AdalnInput(t, _timestep);
            float[] x = RunGraph("z_embx", ZImageMath.Patchify(latent));
            x = RunGraph("z_refx", x, _imageCos, _imageSin, adaln);
            float[] unified = new float[ZImageMath.UnifiedTokens * ZImageMath.Dim]; // [image, caption]
            Array.Copy(x, unified, x.Length);
            Array.Copy(caption, 0, unified, x.Length, caption.Length);
            for (int k = 0; k < 6; k++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                unified = RunGraph("zc_main" + k, unified, _unifiedCos, _unifiedSin, adaln);
            }
            return ZImageMath.Unpatchify(RunGraph("zc_final", unified, adaln)); // the first 256 rows are the image
        }

        // Qwen3's bf16 embedding rows of the ids -> float32 [ids, 2560], read from the 778 MB table on disk.
        private float[] EmbedTokens(int[] ids)
        {
            int d = ZImageMath.TextDim;
            float[] result = new float[ids.Length * d];
            int[] bits = new int[d];
            byte[] row = new byte[d * 2];
            using (FileStream stream = new FileStream(HostFile(EmbedFile), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] < 0 || ids[i] >= Vocabulary)
                        throw new ArgumentOutOfRangeException("ids");
                    stream.Position = (long)ids[i] * d * 2;
                    ReadExactly(stream, row);
                    for (int k = 0; k < d; k++)
                        bits[k] = (row[2 * k] | (row[2 * k + 1] << 8)) << 16; // bf16 -> float32 bits
                    Buffer.BlockCopy(bits, 0, result, i * d * 4, d * 4);
                }
            }
            return result;
        }

        // Compile a graph on the CPU with its weight cache, run it once and release it. With the cache, compiling is a
        // memory map (milliseconds), so streaming the graphs keeps memory low at no real cost.
        internal float[] RunGraph(String name, params float[][] inputs)
        {
            CpuOptions cpu = new CpuOptions
            {
                NumThreads = NumThreads,
                WeightCachePath = Path.Combine(_folder, "cache", name + ".xnnpack_cache")
            };
            using (Model model = new Model(_environment, GraphFile(name)))
            using (Options options = new Options(HwAccelerators.Cpu))
            {
                options.AddCpuOptions(cpu);
                using (CompiledModel compiled = new CompiledModel(_environment, model, options))
                {
                    TensorBuffer[] inputBuffers = compiled.CreateInputBuffers();
                    TensorBuffer[] outputBuffers = compiled.CreateOutputBuffers();
                    try
                    {
                        if (inputs.Length != inputBuffers.Length)
                            throw new ArgumentException(String.Format("{0} takes {1} inputs, not {2}", name, inputBuffers.Length, inputs.Length));
                        for (int i = 0; i < inputs.Length; i++)
                            inputBuffers[i].Write(inputs[i]);
                        compiled.Run(inputBuffers, outputBuffers);
                        return outputBuffers[0].Read<float>();
                    }
                    finally
                    {
                        foreach (TensorBuffer buffer in inputBuffers.Concat(outputBuffers))
                            buffer.Dispose();
                    }
                }
            }
        }

        private static float[] ReadFloats(String file, int count)
        {
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Length != count * 4)
                throw new IOException(String.Format("{0} is {1} bytes, expected {2}", file, bytes.Length, count * 4));
            float[] values = new float[count];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }
        }

        /// <summary>
        /// Delete the weight caches (about 9 GB); the next generation writes them again.
        /// </summary>
        public void DeleteWeightCache()
        {
            String cache = Path.Combine(_folder, "cache");
            if (Directory.Exists(cache))
            {
                foreach (String file in Directory.GetFiles(cache, "*.xnnpack_cache"))
                    File.Delete(file);
            }
        }

        /// <summary>Forget the last prompt and its text embedding. Call only when generation is idle.</summary>
        public void ClearPromptCache()
        {
            _cachedCaption = null;
            _cachedPrompt = null;
        }

        /// <summary>
        /// Release the LiteRT environment
        /// </summary>
        public void Dispose()
        {
            if (_environment != null)
            {
                _environment.Dispose();
                _environment = null;
            }
            ClearPromptCache();
        }
    }
}
