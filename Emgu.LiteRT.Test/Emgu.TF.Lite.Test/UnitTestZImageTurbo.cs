//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
#if VS_TEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
using TestFixture = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
#else
using NUnit.Framework;
#endif
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Models;

namespace Emgu.TF.Lite.Test
{
    // Emgu.LiteRT.Models' ZImageTurbo. The host math is checked against the golden fixtures of the Kotlin port it comes
    // from (https://github.com/alexeyw/android-image-generator, recorded from its verified Python reference run, fetched
    // at a pinned commit rather than copied here); the tokenizer, the timestep MLP and a whole image need the 10.6 GB
    // model, so those tests are ignored by default like the other model tests (dotnet test -p:TestModels=true).
    [TestFixture]
    public class UnitTestZImageTurbo
    {
        [TestAttribute]
        public void TestZImagePartialDownloadAccounting()
        {
            string folder = Path.Combine(Path.GetTempPath(), "emgu-zimage-progress-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "host"));
            try
            {
                using (var model = new ZImageTurbo(folder))
                {
                    string graph = Path.Combine(folder, "zc_final.tflite");
                    File.WriteAllBytes(graph + ".part", new byte[65536]);
                    File.WriteAllBytes(Path.Combine(folder, "host", "vocab.json.part"), new byte[128]);
                    if (model.DownloadedBytes != 65536 + 128 || model.IsDownloaded)
                        throw new Exception("Paused graph and host downloads must count retained bytes without becoming ready.");
                    File.WriteAllBytes(graph, new byte[32]);
                    if (model.DownloadedBytes != 65536 + 128)
                        throw new Exception("An invalid final file must not replace or add to the resumable partial's progress.");
                    File.Delete(graph + ".part");
                    if (model.DownloadedBytes != 128)
                        throw new Exception("Incomplete final files cannot be resumed and must not count as downloaded bytes.");
                }
            }
            finally { Directory.Delete(folder, true); }
        }

        [TestAttribute]
        public void TestZImageCompletedFileDoesNotDoubleCountStalePartial()
        {
            string folder = Path.Combine(Path.GetTempPath(), "emgu-zimage-complete-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                using (var model = new ZImageTurbo(folder))
                {
                    const int graphSize = 1295936; // Pinned zc_final graph.
                    string graph = Path.Combine(folder, "zc_final.tflite");
                    File.WriteAllBytes(graph, new byte[graphSize]);
                    File.WriteAllBytes(graph + ".part", new byte[65536]);
                    if (model.DownloadedBytes != graphSize)
                        throw new Exception("Completed files must count once even if a stale partial remains.");
                }
            }
            finally { Directory.Delete(folder, true); }
        }

        private const String GoldenUrl =
            "https://raw.githubusercontent.com/alexeyw/android-image-generator/bcdc30e08d3ade936f003079fee383e423306afd/";

        private static readonly SemaphoreSlim FetchLock = new SemaphoreSlim(1, 1);

        // A file of the reference repository, downloaded once into the test output folder.
        private static async Task<String> Golden(String path)
        {
            String local = Path.Combine(AppContext.BaseDirectory, "zimage-golden", path.Replace('/', '_'));
            await FetchLock.WaitAsync();
            try
            {
                if (!File.Exists(local))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(local));
                    using (HttpClient client = new HttpClient())
                        File.WriteAllBytes(local, await client.GetByteArrayAsync(GoldenUrl + path));
                }
            }
            finally
            {
                FetchLock.Release();
            }
            return local;
        }

        private static async Task<JsonElement> GoldenJson(String name)
        {
            String file = await Golden("app/src/test/resources/golden/" + name);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file)))
                return doc.RootElement.Clone();
        }

        private static async Task<float[]> GoldenFloats(String name)
        {
            byte[] bytes = File.ReadAllBytes(await Golden("app/src/test/resources/golden/" + name + ".f32"));
            float[] values = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }

        private static float[] Floats(JsonElement array)
        {
            return array.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
        }

        private static int[] Ints(JsonElement array)
        {
            return array.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        }

        private static void AssertClose(float[] expected, float[] actual, float atol, float rtol = 0f, String what = "")
        {
            if (expected.Length != actual.Length)
                throw new Exception(String.Format("{0}: {1} values, expected {2}", what, actual.Length, expected.Length));
            float worst = 0f;
            int at = -1;
            for (int i = 0; i < expected.Length; i++)
            {
                float error = Math.Abs(expected[i] - actual[i]) - rtol * Math.Abs(expected[i]);
                if (error > worst)
                {
                    worst = error;
                    at = i;
                }
            }
            if (worst > atol)
                throw new Exception(String.Format("{0}: [{1}] expected {2} got {3}", what, at, expected[at], actual[at]));
        }

        [TestAttribute]
        public async Task TestZImageMath()
        {
            JsonElement math = await GoldenJson("math.json");

            JsonElement sigmas = math.GetProperty("sigmas");
            foreach (int n in new[] { 4, 8, 9 })
                AssertClose(Floats(sigmas.GetProperty(n.ToString(CultureInfo.InvariantCulture))), ZImageMath.Sigmas(n), 0f, what: "sigmas " + n);
            float[] s8 = ZImageMath.Sigmas(8);
            AssertClose(Floats(math.GetProperty("model_t")), Enumerable.Range(0, 8).Select(i => ZImageMath.ModelTime(s8[i])).ToArray(), 0f, what: "model t");
            AssertClose(Floats(math.GetProperty("timestep_freq_t500")), ZImageMath.TimestepFrequencies(500f), 2e-6f, what: "timestep frequencies");

            float[] cos, sin;
            ZImageMath.RopeCosSin(ZImageMath.CaptionPositions(), out cos, out sin);
            AssertClose(Floats(math.GetProperty("rope_cap_cos")), cos, 1e-6f, what: "caption cos");
            AssertClose(Floats(math.GetProperty("rope_cap_sin")), sin, 1e-6f, what: "caption sin");
            ZImageMath.RopeCosSin(ZImageMath.ImagePositions(), out cos, out sin);
            int[] rows = Ints(math.GetProperty("rope_img_rows"));
            AssertClose(Floats(math.GetProperty("rope_img_cos")),
                rows.SelectMany(r => cos.Skip(r * ZImageMath.RopeHalf).Take(ZImageMath.RopeHalf)).ToArray(), 1e-6f, what: "image cos");
            AssertClose(Floats(math.GetProperty("rope_img_sin")),
                rows.SelectMany(r => sin.Skip(r * ZImageMath.RopeHalf).Take(ZImageMath.RopeHalf)).ToArray(), 1e-6f, what: "image sin");

            ZImageMath.SplitMix64 rng = new ZImageMath.SplitMix64(42);
            foreach (JsonElement expected in math.GetProperty("splitmix_seed42").EnumerateArray())
            {
                String got = rng.NextULong().ToString(CultureInfo.InvariantCulture);
                if (got != expected.GetString())
                    throw new Exception("SplitMix64: expected " + expected.GetString() + " got " + got);
            }
            AssertClose(Floats(math.GetProperty("noise_seed42_head")), ZImageMath.GaussianNoise(42, 64), 1e-6f, what: "noise head");
            double sum = ZImageMath.GaussianNoise(7).Sum(v => (double)v);
            if (Math.Abs(sum - math.GetProperty("noise_seed7_sum").GetDouble()) > 1e-3)
                throw new Exception("Noise sum " + sum);
            float[] noise = await GoldenFloats("noise");
            AssertClose(noise, ZImageMath.GaussianNoise(42), 1e-6f, what: "run noise");

            float[] latent = ZImageMath.GaussianNoise(3);
            AssertClose(latent, ZImageMath.Unpatchify(ZImageMath.Patchify(latent)), 0f, what: "patchify round trip");

            // The recorded run: its noise and step 0's graph output give its latent after step 0.
            latent = (float[])noise.Clone();
            ZImageMath.EulerStep(latent, ZImageMath.Unpatchify(await GoldenFloats("step0_final")), s8[0], s8[1]);
            AssertClose(await GoldenFloats("step0_latent"), latent, 1e-6f, what: "latent after step 0");

            float[] z = ZImageMath.VaeInput(new[] { 0f, 0.3611f });
            AssertClose(new[] { 0.1159f, 1.1159f }, z, 1e-6f, what: "VAE input");
            byte[] rgb = ZImageMath.ToRgb(new[] { -1f, 0f, 1f }, 1);
            if (rgb[0] != 0 || rgb[1] != 128 || rgb[2] != 255)
                throw new Exception("Pixels " + String.Join(",", rgb));
        }

        // The 10.6 GB model, downloaded on first use into the default folder.
        private static async Task<ZImageTurbo> InitModel()
        {
            ZImageTurbo model = new ZImageTurbo();
            int lastPercent = -1;
            await model.Init(new Progress<ZImageDownloadProgress>(p =>
            {
                int percent = (int)(100 * p.BytesDone / p.TotalBytes);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    Console.WriteLine("Downloading {0}: {1}%", p.File, percent);
                }
            }));
            return model;
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestZImageTokenizerAndTimestep()
        {
            using (ZImageTurbo model = await InitModel())
            {
                QwenTokenizer tokenizer = new QwenTokenizer(
                    Path.Combine(model.Folder, "host", "vocab.json"), Path.Combine(model.Folder, "host", "merges.txt"));
                JsonElement math = await GoldenJson("math.json");
                foreach (JsonElement c in math.GetProperty("tokenizer").EnumerateArray())
                {
                    String prompt = c.GetProperty("prompt").GetString();
                    int[] expected = Ints(c.GetProperty("ids"));
                    int[] got = tokenizer.EncodePrompt(prompt);
                    if (!expected.SequenceEqual(got))
                        throw new Exception(String.Format("Prompt \"{0}\": expected {1} got {2}", prompt, String.Join(",", expected), String.Join(",", got)));
                }
                JsonElement run = await GoldenJson("run.json");
                if (!Ints(run.GetProperty("token_ids")).SequenceEqual(tokenizer.EncodePrompt(run.GetProperty("prompt").GetString())))
                    throw new Exception("The recorded run's tokens differ");
                if (tokenizer.EncodePrompt("").Length != QwenTokenizer.TemplateTokens)
                    throw new Exception("Template tokens");
                // A '#' merge: the Kotlin port dropped every merge starting with '#'.
                Console.WriteLine("\"## Title #1\" -> {0}", String.Join(",", tokenizer.Encode("## Title #1")));

                ZImageMath.TimestepWeights weights = new ZImageMath.TimestepWeights
                {
                    W1 = ReadFloats(Path.Combine(model.Folder, "host", "t_emb_w1.f32")),
                    B1 = ReadFloats(Path.Combine(model.Folder, "host", "t_emb_b1.f32")),
                    W2 = ReadFloats(Path.Combine(model.Folder, "host", "t_emb_w2.f32")),
                    B2 = ReadFloats(Path.Combine(model.Folder, "host", "t_emb_b2.f32")),
                };
                JsonElement adaln = math.GetProperty("adaln");
                foreach (String t in new[] { "0.0", "0.3", "0.7" })
                    AssertClose(Floats(adaln.GetProperty(t)), ZImageMath.AdalnInput(float.Parse(t, CultureInfo.InvariantCulture), weights),
                        2e-4f, 1e-4f, "adaln t=" + t);
            }
        }

        private static float[] ReadFloats(String file)
        {
            byte[] bytes = File.ReadAllBytes(file);
            float[] values = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestZImageTurbo()
        {
            using (ZImageTurbo model = await InitModel())
            {
                JsonElement run = await GoldenJson("run.json");
                String prompt = run.GetProperty("prompt").GetString();
                long seed = run.GetProperty("seed").GetInt64();
                int steps = run.GetProperty("steps").GetInt32();
                Console.WriteLine("\"{0}\": {1} tokens (at most {2})", prompt, model.CountPromptTokens(prompt), ZImageTurbo.MaxPromptTokens);

                // Step 0 of the recorded run through our graphs: the transformer's output (CPU kernels differ a little
                // between the reference's machine and this one).
                float[] caption = model.EncodePrompt(prompt, CancellationToken.None);
                float[] step0 = model.Transformer(ZImageMath.GaussianNoise(seed), caption,
                    ZImageMath.ModelTime(ZImageMath.Sigmas(steps)[0]), CancellationToken.None);
                float[] expected = ZImageMath.Unpatchify(await GoldenFloats("step0_final"));
                double dot = 0, ee = 0, ss = 0;
                for (int i = 0; i < expected.Length; i++)
                {
                    dot += (double)expected[i] * step0[i];
                    ee += (double)expected[i] * expected[i];
                    ss += (double)step0[i] * step0[i];
                }
                double correlation = dot / Math.Sqrt(ee * ss);
                Console.WriteLine("Step 0 output vs the reference: cosine {0:F5}", correlation);
                if (correlation < 0.99)
                    throw new Exception("Step 0 differs from the reference");

                ZImageResult result = null;
                for (int run2 = 0; run2 < 2; run2++)
                {
                    result = await model.Generate(prompt, seed, steps, new Progress<ZImageProgress>(p => Console.WriteLine("  {0}", p.Stage)));
                    Console.WriteLine("Generated in {0:F1} s", result.Elapsed.TotalSeconds);
                }

                // Against the reference's image of the same prompt and seed.
                String referencePng = await Golden("docs/reference_apple_seed42.png");
                using (Emgu.CV.Mat reference = Emgu.CV.CvInvoke.Imread(referencePng, Emgu.CV.CvEnum.ImreadModes.ColorBgr))
                using (Emgu.CV.Mat image = new Emgu.CV.Mat(result.Height, result.Width, Emgu.CV.CvEnum.DepthType.Cv8U, 3))
                using (Emgu.CV.Mat bgr = new Emgu.CV.Mat())
                {
                    image.SetTo(result.Rgb);
                    Emgu.CV.CvInvoke.CvtColor(image, bgr, Emgu.CV.CvEnum.ColorConversion.Rgb2Bgr);
                    Emgu.CV.CvInvoke.Imwrite(Path.Combine(AppContext.BaseDirectory, "zimage_apple_seed42.png"), bgr);
                    double psnr = Emgu.CV.CvInvoke.PSNR(reference, bgr);
                    Console.WriteLine("PSNR vs the reference image: {0:F1} dB", psnr);
                    // 35.1 dB on an M1. On Windows x64 step 0 still matches (cosine 0.995) but the CPU kernels differ a
                    // little, which grows over the 8 steps: same apple and table, different details (17.9 dB).
                    double minimumPsnr = OperatingSystem.IsWindows() ? 15 : 25;
                    if (psnr < minimumPsnr)
                        throw new Exception("The image differs from the reference");
                }
            }
        }

        // Which transformer graphs go to the GPU for a video memory budget (the file sizes plus 200 MB for the first one).
        [TestAttribute]
        public void TestZImageGpuChoosesTheGraphsThatFit()
        {
            Assert.That(ZImageTurbo.ChooseGpuGraphs(0), Is.Empty);
            // 1 GB: the small graphs; a transformer block (0.9 GB) doesn't fit beside them.
            Assert.That(ZImageTurbo.ChooseGpuGraphs(1000000000L), Is.EqualTo(new[] { "z_embx", "z_refx", "zc_final" }));
            // 5.1 GB: four blocks; the fifth and sixth don't, the small final layer after them still does.
            Assert.That(ZImageTurbo.ChooseGpuGraphs(5100000000L),
                Is.EqualTo(new[] { "z_embx", "z_refx", "zc_main0", "zc_main1", "zc_main2", "zc_main3", "zc_final" }));
            // Everything: 5.8 GB of graphs and 0.2 GB for the first (6,015,876,128 bytes).
            string[] all = { "z_embx", "z_refx", "zc_main0", "zc_main1", "zc_main2", "zc_main3", "zc_main4", "zc_main5", "zc_final" };
            Assert.That(ZImageTurbo.ChooseGpuGraphs(6015876128L), Is.EqualTo(all));
            Assert.That(ZImageTurbo.ChooseGpuGraphs(6015876127L), Is.EqualTo(all.Take(8)));
            Assert.That(ZImageTurbo.ChooseGpuGraphs(long.MaxValue), Is.EqualTo(all));
        }

        // Backend is "cpu" or "gpu", and can't change once the model is loaded.
        [TestAttribute]
        public void TestZImageBackendIsCheckedBeforeInit()
        {
            using (var model = new ZImageTurbo(Path.Combine(Path.GetTempPath(), "emgu-zimage-backend-" + Guid.NewGuid().ToString("N"))))
            {
                Assert.That(model.Backend, Is.EqualTo("cpu"));
                model.Backend = "GPU";
                Assert.That(model.Backend, Is.EqualTo("gpu"));
                Assert.Throws<ArgumentException>(() => model.Backend = "npu");
                Assert.That(model.GpuGraphsResident, Is.EqualTo(0));
                Assert.That(model.GpuGraphsTotal, Is.EqualTo(9));
                Assert.That(model.GpuFailure, Is.Null);
            }
        }

        private const string GpuPrompt = "a red fox sitting in fresh snow at sunrise";

        private static double Psnr(byte[] a, byte[] b)
        {
            double squared = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double d = a[i] - b[i];
                squared += d * d;
            }
            return 10 * Math.Log10(255.0 * 255.0 / Math.Max(squared / a.Length, 1e-9));
        }

        private static async Task<byte[]> CpuPicture()
        {
            using (var cpu = new ZImageTurbo())
            {
                await cpu.Init();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                ZImageResult result = await cpu.Generate(GpuPrompt, 42, 8, null);
                Console.WriteLine("CPU picture in {0:F1} s", watch.Elapsed.TotalSeconds);
                return result.Rgb;
            }
        }

        // Z-Image-Turbo on the GPU draws the CPU's picture (PSNR 28 dB on an RTX 3070), in seconds once the graphs are compiled.
        // Skipped without a LiteRT GPU accelerator plugin next to libLiteRt (on Windows: EMGU_LITERT_WEBGPU_DIR, the bundle
        // bazel_build_windows_gpu.ps1 makes). The whole model needs about 5.5 GB of video memory.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestZImageTurboGpu()
        {
            if (!UnitTest1.StageGpuPlugin())
                Assert.Ignore("No LiteRT GPU accelerator plugin next to libLiteRt (on Windows: set EMGU_LITERT_WEBGPU_DIR)");
            byte[] cpuPicture = await CpuPicture();
            using (var gpu = new ZImageTurbo { Backend = "gpu" })
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await gpu.Init();
                Console.WriteLine("Loaded on the GPU in {0:F1} s: {1} of {2} graphs on the GPU{3}", watch.Elapsed.TotalSeconds,
                    gpu.GpuGraphsResident, gpu.GpuGraphsTotal, gpu.GpuFailure == null ? string.Empty : " (" + gpu.GpuFailure + ")");
                watch.Restart();
                ZImageResult first = await gpu.Generate(GpuPrompt, 42, 8, null);
                Console.WriteLine("First GPU picture in {0:F1} s", watch.Elapsed.TotalSeconds);
                watch.Restart();
                ZImageResult second = await gpu.Generate(GpuPrompt, 42, 8, null);
                Console.WriteLine("Second GPU picture in {0:F1} s", watch.Elapsed.TotalSeconds);
                Assert.That(second.Rgb, Is.EqualTo(first.Rgb), "the same seed gave another picture on the GPU");
                double psnr = Psnr(cpuPicture, first.Rgb);
                Console.WriteLine("GPU against CPU: PSNR {0:F1} dB", psnr);
                Assert.That(psnr, Is.GreaterThan(25), "the GPU's picture is not the CPU's");
                // On an idle 8 GB card everything fits; a card with less, or other programs using it, leaves some on the CPU.
                if (gpu.GpuGraphsResident == gpu.GpuGraphsTotal)
                    Assert.That(gpu.GpuFailure, Is.Null);
            }
        }

        // A video memory budget that fits only part of the model: the rest runs on the CPU, and the picture is the same;
        // a budget of nothing is the CPU's picture exactly.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestZImageTurboGpuWithLessVideoMemory()
        {
            if (!UnitTest1.StageGpuPlugin())
                Assert.Ignore("No LiteRT GPU accelerator plugin next to libLiteRt (on Windows: set EMGU_LITERT_WEBGPU_DIR)");
            byte[] cpuPicture = await CpuPicture();
            using (var partial = new ZImageTurbo { Backend = "gpu", GpuMemoryBudget = 2000000000L })
            {
                await partial.Init();
                Console.WriteLine("Budget 2 GB: {0} of {1} graphs on the GPU ({2})", partial.GpuGraphsResident, partial.GpuGraphsTotal, partial.GpuFailure);
                Assert.That(partial.GpuGraphsResident, Is.GreaterThan(0).And.LessThan(partial.GpuGraphsTotal));
                Assert.That(partial.GpuFailure, Is.Not.Null);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                ZImageResult picture = await partial.Generate(GpuPrompt, 42, 8, null);
                Console.WriteLine("Partial picture in {0:F1} s, PSNR {1:F1} dB", watch.Elapsed.TotalSeconds, Psnr(cpuPicture, picture.Rgb));
                // Part of the graphs round differently (int8 activations on the GPU): 24.7 dB with 4 of 9 on the GPU.
                Assert.That(Psnr(cpuPicture, picture.Rgb), Is.GreaterThan(20));
            }
            using (var none = new ZImageTurbo { Backend = "gpu", GpuMemoryBudget = 0 })
            {
                await none.Init();
                Assert.That(none.GpuGraphsResident, Is.EqualTo(0));
                ZImageResult picture = await none.Generate(GpuPrompt, 42, 8, null);
                Assert.That(picture.Rgb, Is.EqualTo(cpuPicture), "with no graph on the GPU the picture is the CPU's");
            }
        }
    }
}
