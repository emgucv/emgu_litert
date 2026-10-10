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
