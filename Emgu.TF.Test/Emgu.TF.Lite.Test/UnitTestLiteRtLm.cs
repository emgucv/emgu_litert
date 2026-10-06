using System;
#if VS_TEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
using TestFixture = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
#else
using NUnit.Framework;
#endif
using Emgu.LiteRT.LM;
using Emgu.LiteRT.Util;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.TF.Lite.Test
{
    // Tests of the Emgu.LiteRT.LM wrapper of LiteRT-LM's C API (liblitert-lm), using Qwen3-0.6B on the CPU. They are
    // skipped where liblitert-lm isn't available (it is currently built for Apple Silicon macOS and Android only).
    [TestFixture]
    public class UnitTestLiteRtLm
    {
        // The repo's mixed int4 build: its Qwen3-0.6B.litertlm needs a newer LiteRT-LM than the pinned v0.17.1 (engine
        // creation fails loading its tokenizer, "piece must not include null character", also with litert_lm_main).
        private static readonly DownloadableFile ModelFile = new DownloadableFile(
            "https://huggingface.co/litert-community/Qwen3-0.6B/resolve/main/qwen3_0_6b_mixed_int4.litertlm",
            "LiteRT-LM",
            "7900eb4e7362d88c58782c6f9999bb7a129e03544aa98b8f338ea0cc5d8c22c1");

        // Keep the generated answers short, so the tests run quickly on the CPU.
        private const int MaxOutputTokens = 32;

        private static async Task<String> GetModelPath()
        {
            try
            {
                LiteRtLmInvoke.SetMinLogLevel(LogSeverity.Warning);
            }
            catch (DllNotFoundException e)
            {
                Skip("liblitert-lm is not available on this platform: " + e.Message);
            }

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
                        // A fresh conversation for each message: with Qwen3's chat template, LiteRT-LM v0.17.1 can't
                        // send a second message in the same conversation ("The new rendered template string does not
                        // start with the previous rendered template string" - the template drops the previous
                        // answer's empty <think></think> block when rendering the next turn).
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
    }
}
