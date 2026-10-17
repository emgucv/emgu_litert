using System;
#if VS_TEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
using TestFixture = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
#else
using NUnit.Framework;
#endif
using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Emgu.LiteRT.Util;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.TF.Lite.Test
{
    // Tests of the Emgu.LiteRT.LM wrapper of LiteRT-LM's C API (liblitert-lm) and of Emgu.LiteRT.LM.Models, on the
    // CPU. They download large models on the first run (Qwen3-0.6B ~500 MB, Qwen3.5 0.8B ~1 GB / VL ~1.3 GB / 4B ~2.8 GB, Gemma 4 E2B ~2.6 GB, E4B ~3.7 GB), so like Emgu CV's
    // model tests they are ignored by default: opt in with dotnet test -p:TestModels=true (defines TEST_MODELS). They
    // are also skipped where liblitert-lm isn't available (it is currently built for Apple Silicon macOS and Android
    // only).
    [TestFixture]
    public class UnitTestLiteRtLm
    {
        // The model the Qwen3 class downloads (Qwen3-0.6B, dynamic int4).
        private static readonly DownloadableFile ModelFile = Qwen3.ModelFile;

        // Keep the generated answers short, so the tests run quickly on the CPU.
        private const int MaxOutputTokens = 32;

        // Skip the test if liblitert-lm can't be loaded on this platform.
        private static void RequireLiteRtLm()
        {
            try
            {
                LiteRtLmInvoke.SetMinLogLevel(LogSeverity.Warning);
            }
            catch (DllNotFoundException e)
            {
                Skip("liblitert-lm is not available on this platform: " + e.Message);
            }
        }

        private static async Task<String> GetModelPath()
        {
            RequireLiteRtLm();

            if (!ModelFile.IsLocalFileValid)
            {
                FileDownloadManager manager = new FileDownloadManager();
                manager.AddFile(ModelFile);
                await manager.Download();
            }
            if (!ModelFile.IsLocalFileValid)
                throw new Exception("Failed to download " + ModelFile.Url);
            return ModelFile.LocalFile;
        }

        private static void Skip(String message)
        {
#if VS_TEST
            Assert.Inconclusive(message);
#else
            Assert.Ignore(message);
#endif
        }

        private static Engine CreateEngine(String modelPath)
        {
            using (EngineSettings settings = new EngineSettings(modelPath, "cpu"))
            {
                settings.CacheDir = System.IO.Path.GetDirectoryName(modelPath);
                return new Engine(settings);
            }
        }

        private static SessionConfig CreateSessionConfig()
        {
            SessionConfig config = new SessionConfig();
            config.MaxOutputTokens = MaxOutputTokens;
            return config;
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestLiteRtLmLoadedFile()
        {
            String modelPath = await GetModelPath();
            using (LoadedFile file = new LoadedFile(modelPath))
            {
                Console.WriteLine("Thinking: {0}; function calling: {1}; speculative decoding: {2}; sampler {3} (temperature {4}, top-k {5}, top-p {6})",
                    file.SupportsThinking, file.SupportsFunctionCalling, file.HasSpeculativeDecodingSupport,
                    file.SamplerType, file.SamplerTemperature, file.SamplerTopK, file.SamplerTopP);
                if (!file.SupportsInputModality(Modality.Text))
                    throw new Exception("Qwen3 should accept text input");
            }
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestLiteRtLmTokenize()
        {
            String modelPath = await GetModelPath();
            using (Engine engine = CreateEngine(modelPath))
            {
                const String text = "Hello, LiteRT-LM!";
                int[] tokens = engine.Tokenize(text);
                String detokenized = engine.Detokenize(tokens);
                Console.WriteLine("'{0}' -> [{1}] -> '{2}'; start token: {3}; stop tokens: {4}",
                    text, String.Join(", ", tokens), detokenized, engine.StartToken,
                    String.Join(", ", engine.StopTokens.Select(t => t.ToString())));
                if (tokens.Length == 0)
                    throw new Exception("Tokenize returned no tokens");
                if (detokenized != text)
                    throw new Exception(String.Format("Detokenize returned '{0}', expected '{1}'", detokenized, text));
            }
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestLiteRtLmGenerate()
        {
            String modelPath = await GetModelPath();
            using (Engine engine = CreateEngine(modelPath))
            using (SessionConfig config = CreateSessionConfig())
            using (Session session = engine.CreateSession(config))
            {
                String answer = session.GenerateContent("What is the capital of France?");
                Console.WriteLine("Answer: {0}", answer);
                if (String.IsNullOrEmpty(answer))
                    throw new Exception("GenerateContent returned no text");
            }
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestLiteRtLmGenerateStream()
        {
            String modelPath = await GetModelPath();
            using (Engine engine = CreateEngine(modelPath))
            using (SessionConfig config = CreateSessionConfig())
            using (Session session = engine.CreateSession(config))
            {
                StringBuilder streamed = new StringBuilder();
                int chunkCount = 0;
                String answer = await session.GenerateContentStreamAsync(
                    "What is the capital of France?",
                    chunk =>
                    {
                        lock (streamed)
                        {
                            streamed.Append(chunk);
                            chunkCount++;
                        }
                    });
                Console.WriteLine("Streamed {0} chunks: {1}", chunkCount, answer);
                if (chunkCount == 0 || String.IsNullOrEmpty(answer))
                    throw new Exception("GenerateContentStreamAsync streamed no text");
                if (streamed.ToString() != answer)
                    throw new Exception("The streamed chunks don't add up to the returned text");
            }
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestLiteRtLmConversation()
        {
            String modelPath = await GetModelPath();
            using (Engine engine = CreateEngine(modelPath))
            using (SessionConfig sessionConfig = CreateSessionConfig())
            {
                foreach (bool enableThinking in new bool[] { false, true })
                {
                    using (ThinkingConfig thinkingConfig = new ThinkingConfig())
                    using (ConversationConfig config = new ConversationConfig())
                    {
                        thinkingConfig.EnableThinking = enableThinking;
                        config.SessionConfig = sessionConfig;
                        config.SystemMessage = "You are a helpful assistant. Answer in one short sentence.";
                        config.ThinkingConfig = thinkingConfig;
                        // A fresh conversation for each message (the raw API, without Chat's history handling).
                        using (Conversation conversation = engine.CreateConversation(config))
                        {
                            String reply = conversation.SendMessage(Conversation.CreateTextMessage("What is the capital of France?"));
                            Console.WriteLine("Thinking {0}, reply: {1}", enableThinking, reply);
                            if (reply == null || !reply.Contains("assistant"))
                                throw new Exception("SendMessage did not return an assistant message");
                        }
                        using (Conversation conversation = engine.CreateConversation(config))
                        {
                            String[] chunks = await conversation.SendMessageStreamAsync(
                                Conversation.CreateTextMessage("What is the capital of Germany?"));
                            Console.WriteLine("Thinking {0}, streamed {1} chunks, first: {2}; tokens so far: {3}",
                                enableThinking, chunks.Length, chunks.FirstOrDefault(), conversation.TokenCount);
                            if (chunks.Length == 0)
                                throw new Exception("SendMessageStreamAsync streamed no chunks");
                        }
                    }
                }
            }
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestLiteRtLmCancel()
        {
            String modelPath = await GetModelPath();
            using (Engine engine = CreateEngine(modelPath))
            using (SessionConfig config = new SessionConfig())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                // Set before creating the session: the session copies its configuration.
                config.MaxOutputTokens = 1024;
                using (Session session = engine.CreateSession(config))
                {
                    int chunkCount = 0;
                    try
                    {
                        // Cancel as soon as the first chunk arrives.
                        await session.GenerateContentStreamAsync(
                            "Write a long story about a robot learning to paint.",
                            chunk =>
                            {
                                Interlocked.Increment(ref chunkCount);
                                cancellation.Cancel();
                            },
                            cancellation.Token);
                        throw new Exception("The generation completed instead of being cancelled");
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("Cancelled after {0} chunk(s)", chunkCount);
                    }
                }
            }
        }

        // Emgu.LiteRT.LM.Models: Qwen3 downloads the model and Chat keeps the history across messages.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestQwen3Chat()
        {
            await GetModelPath();
            using (Qwen3 model = new Qwen3())
            {
                await model.Init();
                Chat chat = model.CreateChat("You are a helpful assistant. Answer in one short sentence.");
                chat.MaxOutputTokens = 64;

                ChatReply first = chat.Send("What is the capital of France?");
                Console.WriteLine("Reply 1: {0}", first.Text);
                // Check the answer, not just that there is one: a chat template that drops the user's text (as the
                // earlier qwen3_0_6b_mixed_int4.litertlm's does with LiteRT-LM v0.18.0) still produces a reply.
                if (!first.Text.Contains("Paris"))
                    throw new Exception("The first reply doesn't answer the question");

                // A follow-up that only makes sense with the history.
                StringBuilder streamed = new StringBuilder();
                ChatReply second = await chat.SendAsync("And of Germany?", text =>
                {
                    lock (streamed)
                        streamed.Append(text);
                });
                Console.WriteLine("Reply 2: {0} (streamed: {1})", second.Text, streamed);
                if (!second.Text.Contains("Berlin"))
                    throw new Exception("The second reply doesn't use the history");
                if (streamed.ToString() != second.FullText)
                    throw new Exception("The streamed text doesn't add up to the reply");
                if (chat.History.Count != 4)
                    throw new Exception(String.Format("The history has {0} messages, expected 4", chat.History.Count));
            }
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestQwen3Thinking()
        {
            await GetModelPath();
            using (Qwen3 model = new Qwen3())
            {
                model.EnableThinking = true;
                await model.Init();
                Chat chat = model.CreateChat();
                chat.MaxOutputTokens = 512;
                // A prompt that needs little thinking, so the answer fits in the token limit after the thinking. (If
                // the limit is reached while thinking, the reply has the thinking and an empty Text.)
                ChatReply reply = await chat.SendAsync("Reply with just the word: hello");
                Console.WriteLine("Thinking: {0}\nAnswer: {1}", reply.Thinking, reply.Text);
                if (String.IsNullOrEmpty(reply.Thinking))
                    throw new Exception("The reply has no thinking");
                if (String.IsNullOrEmpty(reply.Text))
                    throw new Exception("The reply has no answer after the thinking");
            }
        }

        // The safety guard must reject every GPU entry point before downloading or calling native code.
        [TestAttribute]
        public async Task TestEmbeddingGpuRequiresExplicitWindowsOptIn()
        {
            foreach (string component in new[] { "text", "vision", "audio" })
            {
                using (EmbeddingGemma2Multimodal model = new EmbeddingGemma2Multimodal())
                {
                    if (component == "vision") model.VisionBackend = "gpu";
                    if (component == "audio") model.AudioBackend = "gpu";
                    bool refused = false;
                    try { await model.Init(backend: component == "text" ? "gpu" : "cpu"); }
                    catch (NotSupportedException) { refused = true; }
                    if (!refused) throw new Exception("Embedding GPU guard did not reject " + component);
                }
            }
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                using (EmbeddingGemma2Multimodal model = new EmbeddingGemma2Multimodal { EnableWindowsWebGpu = true })
                {
                    bool refused = false;
                    try { await model.Init(backend: "gpu"); }
                    catch (NotSupportedException) { refused = true; }
                    if (!refused) throw new Exception("Windows GPU opt-in enabled another platform");
                }
            }
        }

        // Windows only: EmbeddingGemma 2 Text Vision 440M on the Direct3D 12 GPU (the source-built WebGPU bundle next to
        // libLiteRt, see TestLiteRtMobilenetGpu) must give the CPU's embeddings for text and an image.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestEmbeddingGemma2TextVisionGpuMatchesCpu()
        {
            RequireLiteRtLm();
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                Skip("The embedding models' GPU is only enabled on Windows");
            if (!System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "libLiteRtWebGpuAccelerator.dll")))
                Skip("No LiteRT WebGPU accelerator next to libLiteRt (run TestLiteRtMobilenetGpu with EMGU_LITERT_WEBGPU_DIR first)");

            String[] texts = { "How do I change my password?", "The Eiffel Tower is 330 metres tall.", "a red apple" };
            float[][] cpuText;
            float[] cpuImage;
            using (EmbeddingGemma2TextVision cpu = new EmbeddingGemma2TextVision())
            {
                await cpu.Init();
                cpuText = cpu.Embed(texts);
                cpuImage = cpu.EmbedImageFile("apple.jpg");
            }
            using (EmbeddingGemma2TextVision gpu = new EmbeddingGemma2TextVision { EnableWindowsWebGpu = true, VisionBackend = "gpu" })
            {
                await gpu.Init(backend: "gpu");
                float[][] gpuText = gpu.Embed(texts);
                float[] gpuImage = gpu.EmbedImageFile("apple.jpg");
                for (int i = 0; i < texts.Length; i++)
                {
                    float cosine = EmbeddingModel.CosineSimilarity(cpuText[i], gpuText[i]);
                    Console.WriteLine("Text {0}: GPU/CPU cosine {1:F5}", i, cosine);
                    if (cosine < 0.99f)
                        throw new Exception("GPU text embedding differs from the CPU's: " + texts[i]);
                }
                float imageCosine = EmbeddingModel.CosineSimilarity(cpuImage, gpuImage);
                Console.WriteLine("Image: GPU/CPU cosine {0:F5}", imageCosine);
                if (imageCosine < 0.99f)
                    throw new Exception("GPU image embedding differs from the CPU's");
                // The apple picture should match its description best, on the GPU too.
                float apple = EmbeddingModel.CosineSimilarity(gpuImage, gpuText[2]);
                if (apple <= EmbeddingModel.CosineSimilarity(gpuImage, gpuText[0]) || apple <= EmbeddingModel.CosineSimilarity(gpuImage, gpuText[1]))
                    throw new Exception("The GPU image embedding doesn't match its description best");
            }
        }

        // EmbeddingGemma 2 Text 270M through the EmbeddingEngine: sizes, normalization, batch vs single, and semantic
        // search - each question's best match should be the document that answers it (165 MB download).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestEmbeddingGemma2()
        {
            RequireLiteRtLm();
            using (EmbeddingGemma2 model = new EmbeddingGemma2())
            {
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                await model.Init();
                Console.WriteLine("Loaded in {0:F1}s", sw.Elapsed.TotalSeconds);

                float[] full = model.Embed("The quick brown fox jumps over the lazy dog.");
                float[] small = model.Embed("The quick brown fox jumps over the lazy dog.", 256);
                double norm = Math.Sqrt(full.Sum(v => (double)v * v));
                Console.WriteLine("Sizes {0} and {1}, norm {2:F4}", full.Length, small.Length, norm);
                if (full.Length != 768 || small.Length != 256 || Math.Abs(norm - 1) > 1e-3)
                    throw new Exception("Unexpected embedding size or norm");

                String[] documents =
                {
                    "To reset your password, open Settings, choose Account and tap Forgot password.",
                    "Our store is open from 9 am to 6 pm on weekdays and closed on Sundays.",
                    "The Eiffel Tower is 330 metres tall and was completed in 1889.",
                    "Photosynthesis lets plants turn sunlight, water and carbon dioxide into sugar and oxygen.",
                    "Shipping within the country takes three to five business days."
                };
                String[] queries =
                {
                    "How do I change my password?",
                    "When can I visit the shop?",
                    "How high is the tower in Paris?",
                    "How do plants make food?",
                    "How long does delivery take?"
                };
                float[][] docVectors = model.EmbedDocuments(documents);
                // The batch gives the same embeddings as one at a time.
                float[] single = model.Embed(model.DocumentPrefix + documents[0]);
                float batchVsSingle = EmbeddingModel.CosineSimilarity(single, docVectors[0]);
                Console.WriteLine("Batch vs single: {0:F5}", batchVsSingle);
                if (batchVsSingle < 0.999f)
                    throw new Exception("The batch embedding differs from the single one");

                float[][] plainDocs = model.Embed(documents);
                int correct = 0, plainCorrect = 0;
                for (int q = 0; q < queries.Length; q++)
                {
                    float[] query = model.EmbedQuery(queries[q]);
                    float[] plainQuery = model.Embed(queries[q]);
                    float[] scores = docVectors.Select(d => EmbeddingModel.CosineSimilarity(query, d)).ToArray();
                    float[] plainScores = plainDocs.Select(d => EmbeddingModel.CosineSimilarity(plainQuery, d)).ToArray();
                    int best = Array.IndexOf(scores, scores.Max());
                    int plainBest = Array.IndexOf(plainScores, plainScores.Max());
                    Console.WriteLine("\"{0}\": best {1} ({2:F3}, next {3:F3}); without prefixes {4} ({5:F3})", queries[q], best,
                        scores[best], scores.Where((s, i) => i != best).Max(), plainBest, plainScores[plainBest]);
                    if (best == q) correct++;
                    if (plainBest == q) plainCorrect++;
                }
                Console.WriteLine("Correct: {0}/{1} with prefixes, {2}/{1} without", correct, queries.Length, plainCorrect);
                if (correct != queries.Length)
                    throw new Exception("Semantic search picked the wrong document");

                sw.Restart();
                for (int i = 0; i < 10; i++)
                    model.EmbedQuery(queries[i % queries.Length]);
                Console.WriteLine("Query embedding: {0:F1} ms each", sw.Elapsed.TotalMilliseconds / 10);
            }
        }

        // A conversation rebuilt from a history with an image (a reopened chat, or after compaction), continued with
        // thinking on: LiteRT-LM v0.18.0 drops the history's images when it re-reads the conversation to remove the
        // previous thinking, and the second message failed ("Provided less images than expected in the prompt") until
        // Chat and LiteRtLmChatClient kept the thinking in the KV cache for such conversations.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestHistoryWithImageAndThinking()
        {
            RequireLiteRtLm();
            using (Gemma4E2B model = new Gemma4E2B())
            {
                await model.Init();
                byte[] apple = System.IO.File.ReadAllBytes("apple.jpg");
                byte[] tulips = System.IO.File.ReadAllBytes("tulips.jpg");

                using (Chat chat = model.CreateChat())
                {
                    chat.MaxOutputTokens = 1024;
                    chat.EnableThinking = true;
                    chat.LoadHistory(new List<Emgu.LiteRT.LM.Models.ChatMessage>
                    {
                        new Emgu.LiteRT.LM.Models.ChatMessage("user", "What fruit is this?", new[] { ChatAttachment.Image(apple) }),
                        new Emgu.LiteRT.LM.Models.ChatMessage("assistant", "An apple.")
                    });
                    ChatReply first = chat.Send("What colour was it? One word.");
                    ChatReply second = chat.Send("What flowers are these? One word.", ChatAttachment.Image(tulips));
                    Console.WriteLine("Chat: \"{0}\" (thinking {1} chars), then \"{2}\" (thinking {3} chars)",
                        first.Text, first.Thinking.Length, second.Text, second.Thinking.Length);
                    if (first.Text.IndexOf("red", StringComparison.OrdinalIgnoreCase) < 0
                        || second.Text.IndexOf("tulip", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("Wrong answers after a history with an image");
                }

                using (IChatClient client = model.AsIChatClient())
                {
                    ChatOptions options = new ChatOptions { MaxOutputTokens = 1024, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium } };
                    List<Microsoft.Extensions.AI.ChatMessage> messages = new List<Microsoft.Extensions.AI.ChatMessage>
                    {
                        new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, new List<AIContent> { new DataContent(apple, "image/jpeg"), new TextContent("What fruit is this?") }),
                        new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "An apple."),
                        new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "What colour was it? One word.")
                    };
                    ChatResponse first = await client.GetResponseAsync(messages, options);
                    messages.AddRange(first.Messages);
                    messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, new List<AIContent> { new DataContent(tulips, "image/jpeg"), new TextContent("What flowers are these? One word.") }));
                    ChatResponse second = await client.GetResponseAsync(messages, options);
                    Console.WriteLine("IChatClient: \"{0}\", then \"{1}\"", first.Text, second.Text);
                    if (first.Text.IndexOf("red", StringComparison.OrdinalIgnoreCase) < 0
                        || second.Text.IndexOf("tulip", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("Wrong answers after a history with an image (IChatClient)");
                }
            }
        }

        // Emgu.LiteRT.LM.Agents' Calculator, which the calculate tool uses.
        [TestAttribute]
        public void TestCalculator()
        {
            (String Expression, double Value)[] cases =
            {
                ("1234.5 * 678.9", 838102.05),
                ("(3 + 4) ^ 2", 49),
                ("2 ^ 3 ^ 2", 512),
                ("-2 ^ 2", -4),
                ("2 ^ -1", 0.5),
                ("-3 * -2", 6),
                ("15% * 80", 12),
                ("17 % 5", 2),
                ("12 \u00D7 3 \u00F7 4 \u2212 1", 8),
                ("2 ** 10", 1024),
                ("sqrt(16) + cbrt(-27) + abs(-2)", 3),
                ("log(1000) + log2(8) + ln(e)", 7),
                ("round(2.345, 2) + floor(1.9) + ceil(1.1)", 5.35),
                ("min(3, 1, 2) + max(3, 1, 2) + pow(2, 3) + hypot(3, 4)", 17),
                ("sin(pi / 2) + cos(0)", 2),
                ("1e3 + 2.5E-1", 1000.25)
            };
            foreach (var c in cases)
            {
                double value = Emgu.LiteRT.LM.Agents.Calculator.Evaluate(c.Expression);
                Console.WriteLine("{0} = {1}", c.Expression, Emgu.LiteRT.LM.Agents.Calculator.Format(value));
                if (Math.Abs(value - c.Value) > 1e-9 * Math.Max(1, Math.Abs(c.Value)))
                    throw new Exception(String.Format("{0} gave {1}, expected {2}", c.Expression, value, c.Value));
            }
            if (Emgu.LiteRT.LM.Agents.Calculator.Format(0.1 + 0.2) != "0.3")
                throw new Exception("Format should hide floating-point noise");
            foreach (String bad in new[] { "2 +", "(1 + 2", "foo(3)", "1 / 0 - 1 / 0", "2 $ 3" })
            {
                try
                {
                    Emgu.LiteRT.LM.Agents.Calculator.Evaluate(bad);
                    throw new Exception("No error for " + bad);
                }
                catch (FormatException e) { Console.WriteLine("{0}: {1}", bad, e.Message); }
                catch (ArithmeticException e) { Console.WriteLine("{0}: {1}", bad, e.Message); }
            }
        }

        // Emgu.LiteRT.LM.Agents' UnitConverter, which the convert_units tool uses.
        [TestAttribute]
        public void TestUnitConverter()
        {
            (double Value, String From, String To, double Expected)[] cases =
            {
                (26.2, "miles", "km", 42.164813),
                (350, "°F", "°C", 176.666667),
                (100, "degrees Celsius", "fahrenheit", 212),
                (0, "K", "C", -273.15),
                (2, "cups", "ml", 473.176473),
                (1, "imperial gallon", "litres", 4.54609),
                (5, "kg", "lbs", 11.023113),
                (12, "stone", "kg", 76.203518),
                (60, "mph", "km/h", 96.56064),
                (1, "acre", "m2", 4046.856422),
                (2, "hours", "minutes", 120),
                (1, "GiB", "MB", 1073.741824),
                (500, "kcal", "kJ", 2092),
                (32, "psi", "bar", 2.206322),
                (6, "feet", "metres", 1.8288),
                (10, "inches", "cm", 25.4)
            };
            foreach (var c in cases)
            {
                double result = Emgu.LiteRT.LM.Agents.UnitConverter.Convert(c.Value, c.From, c.To);
                Console.WriteLine("{0} {1} = {2} {3}", c.Value, c.From, result, c.To);
                if (Math.Abs(result - c.Expected) > 1e-5 * Math.Max(1, Math.Abs(c.Expected)))
                    throw new Exception(String.Format("{0} {1} to {2} gave {3}, expected {4}", c.Value, c.From, c.To, result, c.Expected));
            }
            foreach (var bad in new[] { ("kg", "km"), ("furlongs", "m"), ("m", "parsecs") })
            {
                try
                {
                    Emgu.LiteRT.LM.Agents.UnitConverter.Convert(1, bad.Item1, bad.Item2);
                    throw new Exception("No error for " + bad);
                }
                catch (ArgumentException e) { Console.WriteLine(e.Message); }
            }
        }

        // Emgu.LiteRT.LM.Agents' HtmlText, which the read_web_page tool uses: the article's text without the page's
        // navigation, scripts and footer.
        [TestAttribute]
        public void TestHtmlText()
        {
            String filler = String.Join(" ", Enumerable.Repeat("The bakery's ovens run from four in the morning.", 6));
            String html = "<html><head><title>Contoso &amp; Co news</title><style>p{color:red}</style>" +
                "<script>var menu = 'Home';</script></head><body>" +
                "<nav><ul><li><a href='/'>Home</a></li><li><a href='/shop'>Shop</a></li></ul></nav>" +
                "<header><h1>Site header</h1></header>" +
                "<article data-mw='{\"a\":\"<b>x</b>\"}'><h1>New shop opens in York</h1><table class='infobox'><tr><td>Founded</td><td>1990</td></tr></table>" +
                "<p>Contoso Bakery opened its second shop on Monday.[1]&nbsp;Queues formed early.</p>" +
                "<p>" + filler + "</p><ul><li>Bread from 1.20 pounds</li></ul></article>" +
                "<footer><p>Copyright Contoso 2026. All rights reserved by the company and its partners worldwide.</p></footer>" +
                "</body></html>";
            String text = Emgu.LiteRT.LM.Agents.HtmlText.Extract(html, out String title);
            Console.WriteLine("Title: {0}\n{1}", title, text);
            if (title != "Contoso & Co news")
                throw new Exception("Wrong title: " + title);
            if (!text.Contains("New shop opens in York") || !text.Contains("second shop on Monday. Queues formed early.")
                || !text.Contains("Bread from 1.20 pounds"))
                throw new Exception("The article text is missing");
            if (text.Contains("Shop\n") || text.Contains("var menu") || text.Contains("color:red") || text.Contains("Copyright") || text.Contains("Site header")
                || text.Contains("Founded") || text.Contains("\"a\"") || text.Contains("[1]"))
                throw new Exception("Navigation, scripts, styles, header or footer were kept");

            // Without an article element: the paragraphs, not the short link lists.
            String plain = "<html><body><div><a href='/a'>Link one</a> | <a href='/b'>Link two</a></div>" +
                "<p>" + filler + "</p><p>Short.</p><h2>Opening hours</h2><p>The shop is open from nine until six every weekday, and until four on Saturdays.</p></body></html>";
            String plainText = Emgu.LiteRT.LM.Agents.HtmlText.Extract(plain, out String noTitle);
            Console.WriteLine(plainText);
            if (noTitle != null || plainText.Contains("Link one") || !plainText.Contains("Opening hours") || !plainText.Contains("until four on Saturdays"))
                throw new Exception("Unexpected text of a page without an article element");
        }

        // Emgu.LiteRT.LM.Agents' PlaceLookup with the MAUI demo's place list (GeoNames' towns of 5,000+ people).
        [TestAttribute]
        public void TestPlaceLookup()
        {
            Emgu.LiteRT.LM.Agents.PlaceLookup places;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            using (System.IO.Stream stream = System.IO.File.OpenRead("places.tsv.gz"))
                places = Emgu.LiteRT.LM.Agents.PlaceLookup.Load(stream);
            Console.WriteLine("{0} towns loaded in {1} ms", places.Count, sw.ElapsedMilliseconds);
            (double Lat, double Lon, String Expected)[] cases =
            {
                (48.8584, 2.2945, "Paris, Île-de-France, France"),          // the Eiffel Tower
                (36.9741, -122.0308, "Santa Cruz, California, United States"),
                (35.6595, 139.7005, "Tokyo, Japan"),                         // Shibuya: a ward, named after the city
                (52.3600, 4.8852, "Amsterdam, North Holland, The Netherlands"),
                (-33.8568, 151.2153, "Sydney, New South Wales, Australia"),  // the Opera House
                (48.8049, 2.1204, "Versailles, Île-de-France, France"),     // the palace: its own town, not Paris
                (-16.5, 179.9, null)                                        // near the date line: Fiji or nothing
            };
            sw.Restart();
            foreach (var c in cases)
            {
                Emgu.LiteRT.LM.Agents.Place place = places.FindNearest(c.Lat, c.Lon);
                Console.WriteLine("{0}, {1}: {2} ({3:F1} km)", c.Lat, c.Lon, place, place?.DistanceKm ?? 0);
                if (c.Expected != null && place?.ToString() != c.Expected)
                    throw new Exception(String.Format("{0}, {1}: expected {2}, got {3}", c.Lat, c.Lon, c.Expected, place));
            }
            Console.WriteLine("{0} lookups in {1} ms", cases.Length, sw.ElapsedMilliseconds);
            if (places.FindNearest(0, -30) != null)
                throw new Exception("The middle of the Atlantic should have no town");
            if (!Emgu.LiteRT.LM.Agents.PlaceLookup.Matches("Paris, Île-de-France, France", "ile de france")
                || !Emgu.LiteRT.LM.Agents.PlaceLookup.Matches("Paris, Île-de-France, France", "FRANCE")
                || Emgu.LiteRT.LM.Agents.PlaceLookup.Matches("Paris, Île-de-France, France", "Par"))
                throw new Exception("Unexpected place matching");
        }

        // PhotoRequest.Parse turns find_photos' request (the user's words, as Gemma passes them on) into a PhotoQuery.
        [TestAttribute]
        public void TestPhotoRequest()
        {
            DateTime now = new DateTime(2026, 10, 12, 15, 0, 0);   // a Monday
            String[] places = { "Paris, Île-de-France, France", "Santa Cruz, California, United States", "Valencia, Valencia, Spain" };
            String[] cameras = { "Google Pixel 8", "Apple iPhone 15" };
            DateTime End(int y, int m, int d) => new DateTime(y, m, d).AddDays(1).AddTicks(-1);
            (String Request, String Description, String Place, DateTime? From, DateTime? To, String Kind, String Camera)[] cases =
            {
                ("beach photos from Spain last summer", "beach", "Spain", new DateTime(2026, 6, 1), End(2026, 8, 31), null, null),
                ("selfies", null, null, null, null, "selfie", null),
                ("photos from Paris", null, "Paris", null, null, null, null),
                ("screenshots from last week", null, null, new DateTime(2026, 10, 5), End(2026, 10, 11), "screenshot", null),
                ("dog in the snow", "dog snow", null, null, null, null, null),
                ("trip to Japan in 2024", null, "Japan", new DateTime(2024, 1, 1), End(2024, 12, 31), null, null),
                ("photos taken with Pixel", null, null, null, null, null, "Pixel"),
                ("sunset in California in March", "sunset", "California", new DateTime(2026, 3, 1), End(2026, 3, 31), null, null),
                ("my favourite photos from the USA", null, "United States", null, null, "favorite", null),
                ("pictures of the Eiffel Tower", "Eiffel Tower", null, null, null, null, null),
                ("photos from 3 weeks ago", null, null, new DateTime(2026, 9, 18), End(2026, 9, 24), null, null),
                ("Christmas 2025", "Christmas", null, new DateTime(2025, 1, 1), End(2025, 12, 31), null, null),
                ("my iPhone photos from last winter", null, null, new DateTime(2025, 12, 1), End(2026, 2, 28), null, "iPhone"),
                ("valencia beach in august", "beach", "Valencia", new DateTime(2026, 8, 1), End(2026, 8, 31), null, null)
            };
            foreach (var c in cases)
            {
                Emgu.LiteRT.LM.Agents.PhotoQuery q = Emgu.LiteRT.LM.Agents.PhotoRequest.Parse(c.Request, now, places, cameras);
                Console.WriteLine("\"{0}\" -> {1}", c.Request, q);
                if (q.Description != c.Description || q.Place != c.Place || q.From != c.From || q.To != c.To || q.Kind != c.Kind || q.Camera != c.Camera)
                    throw new Exception(String.Format("\"{0}\": got description {1}, place {2}, {3:yyyy-MM-dd} to {4:yyyy-MM-dd}, kind {5}, camera {6}",
                        c.Request, q.Description, q.Place, q.From, q.To, q.Kind, q.Camera));
            }
        }

        // The remember / forget tools with an in-memory store: a fact already saved in other words isn't saved again.
        [TestAttribute]
        public async Task TestAgentMemoryTools()
        {
            Emgu.LiteRT.LM.Agents.InMemoryAgentMemoryStore store = new Emgu.LiteRT.LM.Agents.InMemoryAgentMemoryStore();
            IList<AIFunction> tools = Emgu.LiteRT.LM.Agents.AgentTools.Memory(store);
            AIFunction remember = tools.First(t => t.Name == "remember");
            AIFunction forget = tools.First(t => t.Name == "forget");
            Func<AIFunction, String, String, Task<String>> call = async (f, name, value) =>
                (await f.InvokeAsync(new AIFunctionArguments { [name] = value }))?.ToString();

            String saved = await call(remember, "fact", "The user's daughter is called Mia.");
            String again = await call(remember, "fact", "My daughter's name is Mia.");
            String other = await call(remember, "fact", "The user is vegetarian.");
            Console.WriteLine("{0} | {1} | {2}", saved, again, other);
            if (!saved.StartsWith("Saved") || !again.StartsWith("Already remembered") || !other.StartsWith("Saved"))
                throw new Exception("Unexpected remember results");
            String instructions = await Emgu.LiteRT.LM.Agents.AgentTools.MemoryInstructionsAsync(store);
            Console.WriteLine(instructions);
            if (instructions == null || !instructions.Contains("Mia") || !instructions.Contains("vegetarian"))
                throw new Exception("The memories are missing from the instructions");
            String forgotten = await call(forget, "text", "vegetarian");
            Console.WriteLine(forgotten);
            if ((await store.GetMemoriesAsync()).Count != 1 || !forgotten.StartsWith("Deleted"))
                throw new Exception("forget didn't delete the memory");
            if (!(await call(forget, "text", "pizza")).StartsWith("No saved fact"))
                throw new Exception("forget should report nothing to delete");
            // The model words what to forget its own way.
            await call(remember, "fact", "The user loves hiking in the Alps.");
            String hiking = await call(forget, "text", "I love hiking in the Alps");
            Console.WriteLine(hiking);
            if (!hiking.StartsWith("Deleted") || (await store.GetMemoriesAsync()).Any(m => m.Text.Contains("hiking")))
                throw new Exception("forget didn't match the reworded fact");
        }

        // The web tools against the real services (Open-Meteo, Wikipedia): needs the network, so off by default.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestAgentWebTools()
        {
            String weather = (await Emgu.LiteRT.LM.Agents.AgentTools.Weather().InvokeAsync(
                new AIFunctionArguments { ["place"] = "Paris" }))?.ToString();
            Console.WriteLine(weather);
            if (weather == null || !weather.Contains("Paris") || !weather.Contains("\u00B0C"))
                throw new Exception("Unexpected weather result");
            String wikipedia = (await Emgu.LiteRT.LM.Agents.AgentTools.Wikipedia().InvokeAsync(
                new AIFunctionArguments { ["query"] = "Ada Lovelace" }))?.ToString();
            Console.WriteLine(wikipedia);
            if (wikipedia == null || !wikipedia.Contains("Ada Lovelace") || !wikipedia.Contains("wikipedia.org"))
                throw new Exception("Unexpected Wikipedia result");
            String currency = (await Emgu.LiteRT.LM.Agents.AgentTools.ConvertCurrency().InvokeAsync(
                new AIFunctionArguments { ["amount"] = 100.0, ["from"] = "US dollars", ["to"] = "EUR" }))?.ToString();
            Console.WriteLine(currency);
            if (currency == null || !currency.StartsWith("100 USD = ") || !currency.Contains(" EUR (European Central Bank"))
                throw new Exception("Unexpected currency result");
            String page = (await Emgu.LiteRT.LM.Agents.AgentTools.ReadWebPage().InvokeAsync(
                new AIFunctionArguments { ["url"] = "https://en.wikipedia.org/wiki/Ada_Lovelace" }))?.ToString();
            Console.WriteLine(page);
            if (page == null || !page.StartsWith("Page: Ada Lovelace") || !page.Contains("Babbage") || page.Contains("Jump to content")
                || page.Contains("{{") || page.Contains("\"wt\"") || System.Text.RegularExpressions.Regex.IsMatch(page, @"\[\d+\]"))
                throw new Exception("Unexpected web page result");
        }

        // Gemma 4 E2B using Emgu.LiteRT.LM.Agents' tools through UseFunctionInvocation(): the calculator for arithmetic
        // and the weather tool for a place.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestAgentTools()
        {
            RequireLiteRtLm();
            using (Gemma4E2B model = new Gemma4E2B())
            {
                await model.Init();
                using (IChatClient client = new ChatClientBuilder(model.AsIChatClient()).UseFunctionInvocation().Build())
                {
                    ChatOptions options = new ChatOptions
                    {
                        Tools = new List<AITool> { Emgu.LiteRT.LM.Agents.AgentTools.CurrentDateTime(), Emgu.LiteRT.LM.Agents.AgentTools.Calculate(),
                            Emgu.LiteRT.LM.Agents.AgentTools.Weather(), Emgu.LiteRT.LM.Agents.AgentTools.Wikipedia() },
                        Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
                    };
                    // A stand-in photo search, recording what Gemma asks for.
                    List<String> photoRequests = new List<String>();
                    options.Tools.Add(Emgu.LiteRT.LM.Agents.AgentTools.FindPhotos((request, cancel) =>
                    {
                        photoRequests.Add(request);
                        Emgu.LiteRT.LM.Agents.PhotoQuery query = Emgu.LiteRT.LM.Agents.PhotoRequest.Parse(request, DateTime.Now);
                        Console.WriteLine("  find_photos \"{0}\" -> {1}", request, query);
                        return Task.FromResult("Found 3 photos " + query + ": taken 2 Aug 2026 in Valencia, Spain; taken 5 Aug 2026 in Valencia, Spain; taken 9 Aug 2026 in Alicante, Spain. They are now shown to the user above your reply. You can't see them: say briefly what you found (when and where, if known), without describing them.");
                    }));
                    options.Tools.Add(Emgu.LiteRT.LM.Agents.AgentTools.ConvertUnits());
                    options.Tools.Add(Emgu.LiteRT.LM.Agents.AgentTools.ConvertCurrency());
                    options.Tools.Add(Emgu.LiteRT.LM.Agents.AgentTools.ReadWebPage());
                    foreach (var q in new[]
                    {
                        ("What is 1234.5 times 678.9?", "calculate", "838"),
                        ("What's the weather in Paris right now?", "get_weather", "\u00B0"),
                        ("How many kilometres is a marathon of 26.2 miles?", "convert_units", "42.1"),
                        ("What is 100 US dollars in euros today?", "convert_currency", "euro"),
                        ("In one sentence, what is https://www.python.org/about/ about?", "read_web_page", "Python"),
("Show me my photos of the beach.", "find_photos", "3"),
                        ("Show me my beach photos from Spain last summer.", "find_photos", "Spain")
                    })
                    {
                        ChatResponse response = await client.GetResponseAsync(q.Item1, options);
                        List<FunctionCallContent> calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
                        Console.WriteLine("{0} -> {1}: {2}", q.Item1,
                            String.Join(", ", calls.Select(Emgu.LiteRT.LM.Agents.AgentTools.Describe)), response.Text);
                        if (!calls.Any(c => c.Name == q.Item2) || response.Text.IndexOf(q.Item3, StringComparison.OrdinalIgnoreCase) < 0)
                            throw new Exception("The tool wasn't used, or its result isn't in the answer: " + q.Item1);
                    }
                    // The last question's place and dates reached the search.
                    Emgu.LiteRT.LM.Agents.PhotoQuery spain = Emgu.LiteRT.LM.Agents.PhotoRequest.Parse(photoRequests.Last(), DateTime.Now);
                    if (spain.Place == null || spain.Place.IndexOf("Spain", StringComparison.OrdinalIgnoreCase) < 0 || spain.From == null)
                        throw new Exception("find_photos didn't get the place and dates: " + photoRequests.Last());
                    {
                    }
                }
            }
        }

        // DocumentIndex.SplitText: passages within the limit, at sentence ends, overlapping, covering the whole text.
        [TestAttribute]
        public void TestDocumentSplitText()
        {
            StringBuilder text = new StringBuilder();
            for (int i = 1; i <= 40; i++)
            {
                text.AppendFormat("Sentence number {0} talks about topic {0} in some detail, e.g. its history.", i);
                text.Append(i % 7 == 0 ? "\n\n" : (i % 3 == 0 ? "\n" : " "));
            }
            text.Append("\nA heading line\nAnother paragraph starts here. It ends here.\n");
            text.Append(new String('x', 1600));
            List<String> passages = DocumentIndex.SplitText(text.ToString(), 400, 100);
            Console.WriteLine("{0} passages", passages.Count);
            foreach (String passage in passages)
                Console.WriteLine("[{0}] {1}...", passage.Length, passage.Substring(0, Math.Min(60, passage.Length)).Replace("\n", " / "));
            if (passages.Any(p => p.Length > 400))
                throw new Exception("A passage is longer than the limit");
            for (int i = 1; i <= 40; i++)
                if (!passages.Any(p => p.Contains("Sentence number " + i + " talks")))
                    throw new Exception("Sentence " + i + " is missing");
            // Sentences aren't cut (except the over-long run of x), and consecutive passages overlap.
            if (passages.Where(p => !p.StartsWith("x")).Any(p => !p.StartsWith("Sentence") && !p.StartsWith("A heading") && !p.StartsWith("Another")))
                throw new Exception("A passage starts mid-sentence");
            int overlapping = 0;
            for (int i = 1; i < passages.Count; i++)
            {
                String firstSentence = passages[i].Split(new[] { ". " }, StringSplitOptions.None)[0];
                if (passages[i - 1].Contains(firstSentence))
                    overlapping++;
            }
            Console.WriteLine("{0} of {1} passages start with the end of the one before", overlapping, passages.Count - 1);
            if (overlapping < (passages.Count - 1) / 2)
                throw new Exception("Passages don't overlap");
            if (!passages.Any(p => p.StartsWith("Another paragraph")) && !passages.Any(p => p.Contains("A heading line\nAnother paragraph")))
                throw new Exception("The heading line wasn't kept apart");
            if (DocumentIndex.SplitText("  \n ", 400, 100).Count != 0)
                throw new Exception("White space should give no passages");
        }

        // DocumentIndex: a "document" of pages and pictures, searched by questions (EmbeddingGemma 2 Text Vision 440M).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestDocumentIndex()
        {
            RequireLiteRtLm();
            using (EmbeddingGemma2TextVision model = new EmbeddingGemma2TextVision())
            {
                await model.Init();
                DocumentIndex index = new DocumentIndex();
                String[] pages =
                {
                    "Annual report 2025. Contoso Bakery opened two new shops this year, in Leeds and in York. "
                        + "Revenue grew by 12 percent to 4.2 million pounds, mostly from wholesale bread.",
                    "Staff. The bakery now employs 58 people. Every new baker spends six weeks in training "
                        + "with a senior baker before working a shift alone.",
                    "Equipment. In March we replaced the old deck oven with a stone-floor oven that bakes 300 loaves "
                        + "an hour and uses 20 percent less gas.",
                    "Outlook. Next year we plan a shop in Durham and a range of gluten-free cakes."
                };
                for (int i = 0; i < pages.Length; i++)
                    index.AddText(pages[i], "page " + (i + 1));
                index.AddImage(System.IO.File.ReadAllBytes("tulips.jpg"), "image/jpeg", "page 2");
                index.AddImage(System.IO.File.ReadAllBytes("surfers.jpg"), "image/jpeg", "page 4");
                List<DocumentIndexProgress> reports = new List<DocumentIndexProgress>();
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                await Task.Run(() => index.Build(model, new SynchronousProgress<DocumentIndexProgress>(reports.Add)));
                Console.WriteLine("Built {0} passages and {1} images in {2:F1}s", index.Passages.Count, index.Images.Count, sw.Elapsed.TotalSeconds);
                if (!index.IsBuilt || reports.Count == 0 || reports.Last().ImagesDone != 2)
                    throw new Exception("The index wasn't built");

                (String Question, String Location)[] questions =
                {
                    ("How much money did the bakery make?", "page 1"),
                    ("How long is the training for new bakers?", "page 2"),
                    ("What oven did they buy?", "page 3"),
                    ("Where will the next shop open?", "page 4")
                };
                foreach (var q in questions)
                {
                    DocumentSearchResult result = index.Search(model, q.Question);
                    DocumentMatch<DocumentPassage> best = result.Passages[0];
                    Console.WriteLine("\"{0}\": {1} ({2:F3}, next {3:F3}); best image {4} ({5:F3})", q.Question, best.Item.Location,
                        best.Score, result.Passages[1].Score, result.Images[0].Item.Location, result.Images[0].Score);
                    if (best.Item.Location != q.Location)
                        throw new Exception("The wrong passage was found for: " + q.Question);
                }
                DocumentSearchResult flowers = index.Search(model, "Is there a picture of flowers?");
                Console.WriteLine("Flowers: {0} ({1:F3}), other {2:F3}", flowers.Images[0].Item.Location, flowers.Images[0].Score, flowers.Images[1].Score);
                if (flowers.Images[0].Item.Location != "page 2")
                    throw new Exception("The wrong picture was found");

                // An index rebuilt from saved passages and embeddings (AddPassage / AddImage with embeddings) needs no
                // Build and finds the same.
                DocumentIndex restored = new DocumentIndex();
                foreach (DocumentPassage passage in index.Passages)
                    restored.AddPassage(passage.Text, passage.Location, passage.Embedding);
                foreach (DocumentImage image in index.Images)
                    restored.AddImage(image.Data, image.MediaType, image.Location, image.IsPage, image.Embedding);
                if (!restored.IsBuilt)
                    throw new Exception("The restored index should not need building");
                DocumentSearchResult again = restored.Search(model, questions[1].Question);
                if (again.Passages[0].Item.Location != questions[1].Location)
                    throw new Exception("The restored index found another passage");
            }
        }

        // Reports on the calling thread (Progress<T> posts to the thread pool).
        private sealed class SynchronousProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;
            public SynchronousProgress(Action<T> report) { _report = report; }
            public void Report(T value) { _report(value); }
        }

        // Text-to-image search with EmbeddingGemma 2 Text Vision 440M: images and texts share one embedding space.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestEmbeddingGemma2TextVision()
        {
            RequireLiteRtLm();
            using (EmbeddingGemma2TextVision model = new EmbeddingGemma2TextVision())
            {
                await CheckImageSearch(model);
            }
        }

        // EmbeddingGemma 2 740M: images as above, and audio found by the text of what is said.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestEmbeddingGemma2Multimodal()
        {
            RequireLiteRtLm();
            using (EmbeddingGemma2Multimodal model = new EmbeddingGemma2Multimodal())
            {
                await CheckImageSearch(model);

                String[] texts =
                {
                    "Have a wonderful day!",
                    "The Eiffel Tower is 330 metres tall.",
                    "Shipping takes three to five business days.",
                    "Photosynthesis turns sunlight into sugar."
                };
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                float[] audio = model.EmbedAudioFile("have_a_wonderful_day.wav");
                Console.WriteLine("Audio embedded in {0:F0} ms (first use loads the audio encoder)", sw.Elapsed.TotalMilliseconds);
                float[][] textVectors = model.EmbedDocuments(texts);
                float[] scores = textVectors.Select(t => EmbeddingModel.CosineSimilarity(audio, t)).ToArray();
                for (int i = 0; i < texts.Length; i++)
                    Console.WriteLine("Audio vs \"{0}\": {1:F3}", texts[i], scores[i]);
                if (Array.IndexOf(scores, scores.Max()) != 0)
                    throw new Exception("The spoken words didn't match their text");
            }
        }

        private static async Task CheckImageSearch(EmbeddingModel model)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            await model.Init();
            Console.WriteLine("{0} loaded in {1:F1}s", model.GetType().Name, sw.Elapsed.TotalSeconds);

            String[] imageFiles = { "apple.jpg", "grace_hopper.jpg", "tulips.jpg", "surfers.jpg" };
            String[] queries =
            {
                "a piece of fruit",
                "a portrait of a woman in a military uniform",
                "a field of flowers",
                "people with surfboards at the sea"
            };
            sw.Restart();
            float[] first = model.EmbedImageFile(imageFiles[0]);
            Console.WriteLine("First image embedded in {0:F0} ms (loads the vision encoder)", sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            float[][] images = model.EmbedImages(imageFiles.Select(f => System.IO.File.ReadAllBytes(f)).ToList());
            Console.WriteLine("{0} images embedded in {1:F0} ms", images.Length, sw.Elapsed.TotalMilliseconds);
            if (images[0].Length != 768 || Math.Abs(Math.Sqrt(images[0].Sum(v => (double)v * v)) - 1) > 1e-3)
                throw new Exception("Unexpected image embedding size or norm");
            float batchVsSingle = EmbeddingModel.CosineSimilarity(first, images[0]);
            Console.WriteLine("Batch vs single: {0:F5}", batchVsSingle);
            if (batchVsSingle < 0.999f)
                throw new Exception("The batch embedding differs from the single one");

            int correct = 0;
            for (int q = 0; q < queries.Length; q++)
            {
                float[] query = model.EmbedQuery(queries[q]);
                float[] scores = images.Select(i => EmbeddingModel.CosineSimilarity(query, i)).ToArray();
                int best = Array.IndexOf(scores, scores.Max());
                Console.WriteLine("\"{0}\": {1} ({2})", queries[q], imageFiles[best],
                    String.Join(", ", scores.Select(s => s.ToString("F3"))));
                if (best == q) correct++;
            }
            Console.WriteLine("Correct: {0}/{1}", correct, queries.Length);
            if (correct != queries.Length)
                throw new Exception("Text-to-image search picked the wrong image");

            // An image with a caption, at a shorter size.
            float[] captioned = model.EmbedImageFile("apple.jpg", "A red apple on a white background.", 256);
            if (captioned.Length != 256)
                throw new Exception("Unexpected size of the captioned image embedding");

            // Text-only embedding still works on the multimodal model.
            float[] text = model.EmbedQuery("How tall is the Eiffel Tower?");
            if (text.Length != 768)
                throw new Exception("Unexpected text embedding size");
        }

        // A chat continued from a saved history (Chat.LoadHistory), as after restarting an app.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestChatLoadHistory()
        {
            RequireLiteRtLm();
            using (Qwen3 model = new Qwen3())
            {
                await model.Init();
                List<Emgu.LiteRT.LM.Models.ChatMessage> saved;
                using (Chat chat = model.CreateChat("Answer in one short sentence."))
                {
                    chat.MaxOutputTokens = 64;
                    chat.EnableThinking = false;
                    chat.Send("My favourite colour is teal. Just say OK.");
                    saved = chat.History.ToList();
                }
                using (Chat restored = model.CreateChat("Answer in one short sentence."))
                {
                    restored.MaxOutputTokens = 64;
                    restored.EnableThinking = false;
                    restored.LoadHistory(saved);
                    ChatReply reply = restored.Send("What is my favourite colour?");
                    Console.WriteLine("Restored chat: {0}", reply.Text);
                    if (reply.Text.IndexOf("teal", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("The restored chat doesn't use the loaded history");
                    if (restored.History.Count != saved.Count + 2)
                        throw new Exception("The history should continue from the loaded messages");
                }
            }
        }

        // LanguageModel.MaxNumTokens is the context window: CountTokens measures text, and a chat whose history is
        // larger than the window fails.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestChatContextWindow()
        {
            RequireLiteRtLm();
            using (Qwen3 model = new Qwen3())
            {
                await model.Init();
                if (model.MaxNumTokens != 4096)
                    throw new Exception("The default context window should be 4096 tokens");
                int tokens = model.CountTokens("The quick brown fox jumps over the lazy dog.");
                Console.WriteLine("Tokens: {0}", tokens);
                if (tokens < 8 || tokens > 14)
                    throw new Exception("Unexpected token count " + tokens);

                // A history of about 5000 tokens.
                string filler = String.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 50));
                List<Emgu.LiteRT.LM.Models.ChatMessage> history = new List<Emgu.LiteRT.LM.Models.ChatMessage>();
                int total = 0;
                while (total < 5000)
                {
                    history.Add(new Emgu.LiteRT.LM.Models.ChatMessage("user", filler));
                    history.Add(new Emgu.LiteRT.LM.Models.ChatMessage("assistant", "OK."));
                    total += model.CountTokens(filler) + 10;
                }
                using (Chat chat = model.CreateChat())
                {
                    chat.MaxOutputTokens = 32;
                    chat.EnableThinking = false;
                    chat.LoadHistory(history);
                    // LiteRT-LM refuses it: "Input token ids are too long. Exceeding the maximum number of tokens
                    // allowed: 5136 >= 4096".
                    try
                    {
                        ChatReply reply = await chat.SendAsync("What animal jumps?");
                        throw new Exception("A history larger than the context window should fail, but got: " + reply.Text);
                    }
                    catch (LiteRtLmException e)
                    {
                        Console.WriteLine("Over the window (~{0} tokens): {1}", total, e.Message);
                        if (e.Message.IndexOf("too long", StringComparison.OrdinalIgnoreCase) < 0)
                            throw;
                    }
                }
            }
        }

        // Gemma 4 supports multi-turn LiteRT-LM conversations, so Chat keeps one conversation open (E2B: 2.6 GB download).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestGemma4E2BChat()
        {
            await TestGemma4Chat(new Gemma4E2B());
        }

        // The same with Gemma 4 E4B (3.7 GB download).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestGemma4E4BChat()
        {
            await TestGemma4Chat(new Gemma4E4B());
        }

        private static async Task TestGemma4Chat(Gemma4 gemma)
        {
            RequireLiteRtLm();
            using (Gemma4 model = gemma)
            {
                await model.Init();
                using (Chat chat = model.CreateChat("You are a helpful assistant. Answer in one short sentence."))
                {
                    chat.MaxOutputTokens = 64;
                    if (!chat.KeepsConversationOpen)
                        throw new Exception("Gemma 4 chats should keep the conversation open");

                    ChatReply first = chat.Send("What is the capital of France?");
                    ChatReply second = await chat.SendAsync("And of Germany?");
                    // Needs both earlier answers.
                    ChatReply third = chat.Send("Which of the two cities is bigger? Answer with just its name.");
                    Console.WriteLine("Replies: {0} | {1} | {2}", first.Text, second.Text, third.Text);
                    if (!second.Text.Contains("Berlin"))
                        throw new Exception("The second reply doesn't use the history");
                    if (chat.History.Count != 6)
                        throw new Exception(String.Format("The history has {0} messages, expected 6", chat.History.Count));

                    // Changing a setting reopens the conversation from the history.
                    chat.MaxOutputTokens = 32;
                    ChatReply fourth = chat.Send("And what is the capital of Italy?");
                    Console.WriteLine("After a settings change: {0}", fourth.Text);
                    if (String.IsNullOrEmpty(fourth.Text) || chat.History.Count != 8)
                        throw new Exception("Sending after a settings change failed");

                    chat.ClearHistory();
                    if (chat.History.Count != 0)
                        throw new Exception("ClearHistory didn't clear the history");
                }
            }
        }

        // Gemma 4 reports its thinking in a separate channel; ChatReply.Thinking exposes it like Qwen3's.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestGemma4E2BThinking()
        {
            await TestGemma4Thinking(new Gemma4E2B());
        }

        // The same with Gemma 4 E4B (3.7 GB download).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestGemma4E4BThinking()
        {
            await TestGemma4Thinking(new Gemma4E4B());
        }

        private static async Task TestGemma4Thinking(Gemma4 gemma)
        {
            RequireLiteRtLm();
            using (Gemma4 model = gemma)
            {
                model.EnableThinking = true;
                await model.Init();
                using (Chat chat = model.CreateChat())
                {
                    chat.MaxOutputTokens = 2048;
                    System.Text.StringBuilder streamed = new System.Text.StringBuilder();
                    ChatReply reply = await chat.SendAsync("A farmer has 17 sheep, all but 9 die, then he buys 3 times as many as remain. How many sheep now? Give the final number.",
                        text => { lock (streamed) streamed.Append(text); });
                    Console.WriteLine("Streamed: {0}\nThinking: {1}\nAnswer: {2}", streamed, reply.Thinking, reply.Text);
                    if (String.IsNullOrEmpty(reply.Thinking))
                        throw new Exception("The reply has no thinking");
                    if (!reply.Text.Contains("36"))
                        throw new Exception("The reply has no (correct) answer after the thinking");

                    chat.EnableThinking = false;
                    ChatReply plain = chat.Send("What is 5 plus 5? Answer with just the number.");
                    Console.WriteLine("Without thinking: {0} / {1}", plain.Thinking, plain.Text);
                    if (!String.IsNullOrEmpty(plain.Thinking))
                        throw new Exception("The reply has thinking although it is disabled");
                }
            }
        }

        // Gemma 4 also accepts images and audio (test inputs from the LiteRT-LM submodule).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestGemma4E2BImageAndAudio()
        {
            await TestGemma4ImageAndAudio(new Gemma4E2B());
        }

        // The same with Gemma 4 E4B (3.7 GB download).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestGemma4E4BImageAndAudio()
        {
            await TestGemma4ImageAndAudio(new Gemma4E4B());
        }

        private static async Task TestGemma4ImageAndAudio(Gemma4 gemma)
        {
            RequireLiteRtLm();
            using (Gemma4 model = gemma)
            {
                await model.Init();
                if (!model.SupportsImages || !model.SupportsAudio)
                    throw new Exception("Gemma 4 should accept images and audio");
                using (Chat chat = model.CreateChat())
                {
                    chat.MaxOutputTokens = 64;

                    ChatReply image = chat.Send("What fruit is in this image? Answer with one word.",
                        ChatAttachment.ImageFile("apple.jpg"));
                    Console.WriteLine("Image: {0}", image.Text);
                    if (image.Text.IndexOf("apple", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("The image reply doesn't mention an apple");

                    // Audio given as bytes (sent base64-encoded), in the same conversation.
                    ChatReply audio = await chat.SendAsync("Transcribe this audio.",
                        new ChatAttachment[] { ChatAttachment.Audio(System.IO.File.ReadAllBytes("audio_sample.wav")) });
                    Console.WriteLine("Audio: {0}", audio.Text);
                    if (String.IsNullOrEmpty(audio.Text))
                        throw new Exception("The audio reply is empty");

                    if (chat.History[0].Attachments.Count != 1 || chat.History[2].Attachments.Count != 1)
                        throw new Exception("The history should keep the attachments");
                }
            }
        }

#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestQwen3RejectsImages()
        {
            await GetModelPath();
            using (Qwen3 model = new Qwen3())
            {
                await model.Init();
                using (Chat chat = model.CreateChat())
                {
                    try
                    {
                        chat.Send("What is in this image?", ChatAttachment.ImageFile("apple.jpg"));
                        throw new Exception("Qwen3 should not accept images");
                    }
                    catch (NotSupportedException e)
                    {
                        Console.WriteLine("Rejected: {0}", e.Message);
                    }
                }
            }
        }

        // A tool only the test knows the answer of, so a correct reply shows the model called it.
        private static AIFunction CreateSecretCodeTool(Action onCall = null)
        {
            return AIFunctionFactory.Create(
                (string person) =>
                {
                    if (onCall != null)
                        onCall();
                    return person.IndexOf("Alice", StringComparison.OrdinalIgnoreCase) >= 0 ? "7391" : "unknown";
                },
                "get_secret_code",
                "Returns the secret code of a person.");
        }

        // Gemma 4 E2B through Microsoft.Extensions.AI's IChatClient: answers, a follow-up and streaming.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestChatClient()
        {
            RequireLiteRtLm();
            using (Gemma4E2B model = new Gemma4E2B())
            {
                await model.Init();
                using (IChatClient client = model.AsIChatClient())
                {
                    ChatOptions options = new ChatOptions { MaxOutputTokens = 64 };
                    List<Microsoft.Extensions.AI.ChatMessage> history = new List<Microsoft.Extensions.AI.ChatMessage>
                    {
                        new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, "Answer in one short sentence."),
                        new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "What is the capital of France?")
                    };
                    ChatResponse first = await client.GetResponseAsync(history, options);
                    history.AddMessages(first);
                    history.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "And of Germany?"));

                    StringBuilder streamed = new StringBuilder();
                    List<ChatResponseUpdate> updates = new List<ChatResponseUpdate>();
                    await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(history, options))
                    {
                        streamed.Append(update.Text);
                        updates.Add(update);
                    }
                    Console.WriteLine("Replies: {0} | {1} ({2} updates)", first.Text, streamed, updates.Count);
                    if (!first.Text.Contains("Paris"))
                        throw new Exception("The first reply is wrong");
                    if (!streamed.ToString().Contains("Berlin"))
                        throw new Exception("The follow-up doesn't use the history");
                    if (updates.Count < 2)
                        throw new Exception("The reply wasn't streamed");
                    if (updates[updates.Count - 1].FinishReason != ChatFinishReason.Stop)
                        throw new Exception("The last update has no Stop finish reason");
                    ChatClientMetadata metadata = client.GetService<ChatClientMetadata>();
                    if (metadata == null || metadata.DefaultModelId != "gemma-4-E2B-it.litertlm")
                        throw new Exception("Wrong metadata");
                }
            }
        }

        // Tool calling through UseFunctionInvocation(): the answer needs the tool's result.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestChatClientTools()
        {
            // Without constraints (the client's default is constrained).
            await TestChatClientTools(false);
        }

        // The same with constrained decoding (LiteRT-LM v0.18.0 or later; the client's default).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestChatClientToolsConstrained()
        {
            await TestChatClientTools(true);
        }

        private static async Task TestChatClientTools(bool constrained)
        {
            RequireLiteRtLm();
            if (constrained && !LiteRtLmInvoke.IsV018OrLater)
                throw new Exception("Constrained decoding needs LiteRT-LM v0.18.0 or later");
            using (Gemma4E2B model = new Gemma4E2B())
            {
                await model.Init();
                int calls = 0;
                LiteRtLmChatClient inner = new LiteRtLmChatClient(model) { EnableConstrainedDecoding = constrained };
                using (IChatClient client = new ChatClientBuilder(inner).UseFunctionInvocation().Build())
                {
                    ChatOptions options = new ChatOptions { Tools = new List<AITool> { CreateSecretCodeTool(() => calls++) } };
                    List<Microsoft.Extensions.AI.ChatMessage> history = new List<Microsoft.Extensions.AI.ChatMessage>
                    {
                        new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "What is the secret code of Alice?")
                    };
                    ChatResponse response = await client.GetResponseAsync(history, options);
                    Console.WriteLine("Reply: {0}", response.Text);
                    foreach (Microsoft.Extensions.AI.ChatMessage message in response.Messages)
                        Console.WriteLine("  {0}: {1}", message.Role, String.Join(", ", message.Contents.Select(c => c.GetType().Name)));
                    if (calls != 1)
                        throw new Exception(String.Format("The tool was called {0} times, expected once", calls));
                    if (!response.Text.Contains("7391"))
                        throw new Exception("The reply doesn't use the tool's result");
                    if (!response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any()
                        || !response.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any())
                        throw new Exception("The response should include the tool call and its result");

                    // A follow-up in the same conversation.
                    history.AddMessages(response);
                    history.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "Repeat the code backwards, digits only."));
                    ChatResponse second = await client.GetResponseAsync(history, options);
                    Console.WriteLine("Follow-up: {0}", second.Text);
                    if (!second.Text.Contains("1937"))
                        throw new Exception("The follow-up doesn't use the earlier tool result");
                }
            }
        }

        // An image as DataContent.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestChatClientImage()
        {
            RequireLiteRtLm();
            using (Gemma4E2B model = new Gemma4E2B())
            {
                await model.Init();
                using (IChatClient client = model.AsIChatClient())
                {
                    Microsoft.Extensions.AI.ChatMessage message = new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, new List<AIContent>
                    {
                        new DataContent(System.IO.File.ReadAllBytes("apple.jpg"), "image/jpeg"),
                        new TextContent("What fruit is in this image? Answer with one word.")
                    });
                    ChatResponse response = await client.GetResponseAsync(new[] { message }, new ChatOptions { MaxOutputTokens = 32 });
                    Console.WriteLine("Image: {0}", response.Text);
                    if (response.Text.IndexOf("apple", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("The reply doesn't mention an apple");
                }
            }
        }

        // A Microsoft Agent Framework agent (ChatClientAgent) with a tool, two turns in one session.
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestAgent()
        {
            RequireLiteRtLm();
            using (Gemma4E2B model = new Gemma4E2B())
            {
                await model.Init();
                using (IChatClient client = model.AsIChatClient())
                {
                    ChatClientAgent agent = new ChatClientAgent(
                        client,
                        "You are a helpful assistant. Use the tools when they can help. Answer briefly.",
                        "Assistant",
                        null,
                        new List<AITool> { CreateSecretCodeTool() });
                    AgentSession session = await agent.CreateSessionAsync();
                    AgentResponse first = await agent.RunAsync("What is the secret code of Alice?", session);
                    AgentResponse second = await agent.RunAsync("What is that code plus one? Just the number.", session);
                    Console.WriteLine("Agent: {0} | {1}", first.Text, second.Text);
                    if (!first.Text.Contains("7391"))
                        throw new Exception("The agent didn't use the tool");
                    if (!second.Text.Contains("7392"))
                        throw new Exception("The agent's session doesn't keep the conversation");
                }
            }
        }

        // Qwen3.5 0.8B keeps one LiteRT-LM conversation open across messages (1 GB download).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestQwen35Chat()
        {
            RequireLiteRtLm();
            using (Qwen35_0_8B model = new Qwen35_0_8B())
            {
                await model.Init();
                using (Chat chat = model.CreateChat("You are a helpful assistant. Answer in one short sentence."))
                {
                    chat.MaxOutputTokens = 64;
                    if (!chat.KeepsConversationOpen)
                        throw new Exception("Qwen3.5 chats should keep the conversation open");
                    ChatReply first = chat.Send("My name is Kenji. What is the capital of France?");
                    ChatReply second = await chat.SendAsync("What is my name?");
                    Console.WriteLine("Replies: {0} | {1}", first.Text, second.Text);
                    if (!first.Text.Contains("Paris"))
                        throw new Exception("The first reply is wrong");
                    if (!second.Text.Contains("Kenji"))
                        throw new Exception("The second reply doesn't use the history");
                }
            }
        }

        // Qwen3.5 0.8B with its vision encoder accepts images (1.3 GB download).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestQwen35VLImage()
        {
            RequireLiteRtLm();
            using (Qwen35_0_8B_VL model = new Qwen35_0_8B_VL())
            {
                await model.Init();
                if (!model.SupportsImages || model.SupportsAudio)
                    throw new Exception("Qwen3.5 0.8B VL should accept images and no audio");
                using (Chat chat = model.CreateChat())
                {
                    chat.MaxOutputTokens = 64;
                    ChatReply image = chat.Send("What fruit is in this image? Answer with one word.",
                        ChatAttachment.ImageFile("apple.jpg"));
                    ChatReply color = chat.Send("What color is it? Answer with one word.");
                    Console.WriteLine("Image: {0} | {1}", image.Text, color.Text);
                    if (image.Text.IndexOf("apple", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("The image reply doesn't mention an apple");
                    if (color.Text.IndexOf("red", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("The follow-up reply doesn't say red");
                }
            }
        }

        // Qwen3.5 4B reports its thinking in a separate channel (2.8 GB download).
#if !TEST_MODELS
#if VS_TEST
        [Ignore()]
#else
        [Ignore("Ignore from test run by default.")]
#endif
#endif
        [TestAttribute]
        public async Task TestQwen35_4BThinking()
        {
            RequireLiteRtLm();
            using (Qwen35_4B model = new Qwen35_4B())
            {
                model.EnableThinking = true;
                await model.Init();
                using (Chat chat = model.CreateChat())
                {
                    chat.MaxOutputTokens = 1024;
                    ChatReply reply = await chat.SendAsync("What is 17 times 23?");
                    Console.WriteLine("Thinking: {0}\nAnswer: {1}", reply.Thinking, reply.Text);
                    if (String.IsNullOrEmpty(reply.Thinking))
                        throw new Exception("The reply has no thinking");
                    if (!reply.Text.Contains("391"))
                        throw new Exception("The reply has no (correct) answer after the thinking");

                    chat.EnableThinking = false;
                    ChatReply plain = chat.Send("What is 5 plus 5? Answer with just the number.");
                    Console.WriteLine("Without thinking: {0} / {1}", plain.Thinking, plain.Text);
                    if (!String.IsNullOrEmpty(plain.Thinking))
                        throw new Exception("The reply has thinking although it is disabled");
                }
            }
        }
    }
}
