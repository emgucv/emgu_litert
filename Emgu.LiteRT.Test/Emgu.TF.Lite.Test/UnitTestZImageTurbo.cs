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
                    if (psnr < 25)
                        throw new Exception("The image differs from the reference");
                }
            }
        }
    }
}
