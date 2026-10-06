using System;
#if VS_TEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
using TestFixture = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
#else
using NUnit.Framework;
#endif
using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.TF.Lite.Test
{
    // Tests of the Emgu.LiteRT.LM wrapper of LiteRT-LM's C API (liblitert-lm) and of Emgu.LiteRT.LM.Models, on the
    // CPU. They download large models on the first run (Qwen3-0.6B ~500 MB, Gemma 4 E2B ~2.6 GB), so like Emgu CV's
    // model tests they are ignored by default: opt in with dotnet test -p:TestModels=true (defines TEST_MODELS). They
    // are also skipped where liblitert-lm isn't available (it is currently built for Apple Silicon macOS and Android
    // only).
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
                if (String.IsNullOrEmpty(first.Text))
                    throw new Exception("The first reply has no text");

                // A follow-up that only makes sense with the history.
                StringBuilder streamed = new StringBuilder();
                ChatReply second = await chat.SendAsync("And of Germany?", text =>
                {
                    lock (streamed)
                        streamed.Append(text);
                });
                Console.WriteLine("Reply 2: {0} (streamed: {1})", second.Text, streamed);
                if (String.IsNullOrEmpty(second.Text))
                    throw new Exception("The second reply has no text");
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

        // Gemma 4 E2B supports multi-turn LiteRT-LM conversations, so Chat keeps one conversation open (2.6 GB download).
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
            RequireLiteRtLm();
            using (Gemma4E2B model = new Gemma4E2B())
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

        // Gemma 4 E2B also accepts images and audio (test inputs from the LiteRT-LM submodule).
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
            RequireLiteRtLm();
            using (Gemma4E2B model = new Gemma4E2B())
            {
                await model.Init();
                if (!model.SupportsImages || !model.SupportsAudio)
                    throw new Exception("Gemma 4 E2B should accept images and audio");
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
    }
}
