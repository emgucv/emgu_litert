// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Server;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using OpenAI;

namespace Emgu.TF.Lite.Test;

// Emgu.LiteRT.LM.Server (TODO item 29): the OpenAI wire format both ways, pairing, and the server over loopback called
// through Microsoft.Extensions.AI.OpenAI as a phone calls it, with a scripted model and search model (no model or native
// library); and, with TEST_MODELS, over a real LiteRtLmChatClient and EmbeddingGemma (the Native category).
[TestFixture]
public class UnitTestChatServer
{
    // ---------- The protocol ----------

    [Test]
    public void ReadsARequestAsTheClientKeepsTheConversation()
    {
        string png = "data:image/png;base64," + Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 });
        string json = @"{
          ""model"": ""gemma-4-e4b"", ""stream"": true, ""max_completion_tokens"": 512, ""reasoning_effort"": ""none"",
          ""messages"": [
            { ""role"": ""system"", ""content"": ""You are helpful."" },
            { ""role"": ""developer"", ""content"": [ { ""type"": ""text"", ""text"": ""Be brief."" } ] },
            { ""role"": ""user"", ""content"": [ { ""type"": ""text"", ""text"": ""What is in it, and the time?"" }, { ""type"": ""image_url"", ""image_url"": { ""url"": """ + png + @""" } } ] },
            { ""role"": ""assistant"", ""content"": ""Let me check."", ""tool_calls"": [
                { ""id"": ""call_a"", ""type"": ""function"", ""function"": { ""name"": ""get_time"", ""arguments"": ""{\""zone\"":\""UTC\""}"" } },
                { ""id"": ""call_b"", ""type"": ""function"", ""function"": { ""name"": ""get_place"", ""arguments"": """" } } ] },
            { ""role"": ""tool"", ""tool_call_id"": ""call_a"", ""content"": ""12:00"" },
            { ""role"": ""tool"", ""tool_call_id"": ""call_b"", ""content"": [ { ""type"": ""text"", ""text"": ""Lyon"" } ] }
          ],
          ""tools"": [ { ""type"": ""function"", ""function"": { ""name"": ""get_time"", ""description"": ""The time"", ""parameters"": { ""type"": ""object"", ""properties"": { ""zone"": { ""type"": ""string"" } } } } },
                       { ""type"": ""function"", ""function"": { ""name"": ""get_place"" } } ],
          ""tool_choice"": ""auto""
        }";
        ChatCompletionRequest request = OpenAIChatProtocol.ParseRequest(json);
        Assert.That((request.Model, request.Stream), Is.EqualTo(("gemma-4-e4b", true)));
        Assert.That(request.Options.Instructions, Is.EqualTo("You are helpful.\n\nBe brief."));
        Assert.That(request.Options.MaxOutputTokens, Is.EqualTo(512));
        Assert.That(request.Options.Reasoning.Effort, Is.EqualTo(ReasoningEffort.None));
        Assert.That(request.Options.ToolMode, Is.EqualTo(ChatToolMode.Auto));
        Assert.That(request.Options.Tools.Select(t => t.Name), Is.EqualTo(new[] { "get_time", "get_place" }));
        Assert.That(request.Options.Tools[0], Is.InstanceOf<AIFunctionDeclaration>());
        Assert.That(((AIFunctionDeclaration)request.Options.Tools[0]).JsonSchema.GetProperty("properties").TryGetProperty("zone", out _), Is.True);

        Assert.That(request.Messages.Select(m => m.Role), Is.EqualTo(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.Tool }));
        DataContent image = request.Messages[0].Contents.OfType<DataContent>().Single();
        Assert.That((image.MediaType, image.Data.Length), Is.EqualTo(("image/png", 7)));
        // The text before the calls, with their ids; the arguments as JSON values.
        ChatMessage assistant = request.Messages[1];
        Assert.That(assistant.Contents[0], Is.InstanceOf<TextContent>());
        Assert.That(assistant.Text, Is.EqualTo("Let me check."));
        FunctionCallContent[] calls = assistant.Contents.OfType<FunctionCallContent>().ToArray();
        Assert.That(calls.Select(c => (c.CallId, c.Name)), Is.EqualTo(new[] { ("call_a", "get_time"), ("call_b", "get_place") }));
        Assert.That(((JsonElement)calls[0].Arguments["zone"]).GetString(), Is.EqualTo("UTC"));
        Assert.That(calls[1].Arguments, Is.Empty);
        // A round's two tool messages are one message.
        FunctionResultContent[] results = request.Messages[2].Contents.OfType<FunctionResultContent>().ToArray();
        Assert.That(results.Select(r => (r.CallId, r.Result as string)), Is.EqualTo(new[] { ("call_a", "12:00"), ("call_b", "Lyon") }));
    }

    [Test]
    public void ReadsAToolResultSentAsJsonAsTheFunctionReturnedIt()
    {
        // The phone's client serializes its function's result: a string comes with its quotes, which the model would see.
        ChatCompletionRequest request = OpenAIChatProtocol.ParseRequest(@"{ ""messages"": [
            { ""role"": ""user"", ""content"": ""Codes?"" },
            { ""role"": ""assistant"", ""tool_calls"": [
                { ""id"": ""a"", ""type"": ""function"", ""function"": { ""name"": ""code"", ""arguments"": ""{}"" } },
                { ""id"": ""b"", ""type"": ""function"", ""function"": { ""name"": ""weather"", ""arguments"": ""{}"" } },
                { ""id"": ""c"", ""type"": ""function"", ""function"": { ""name"": ""note"", ""arguments"": ""{}"" } } ] },
            { ""role"": ""tool"", ""tool_call_id"": ""a"", ""content"": ""\""7391\"""" },
            { ""role"": ""tool"", ""tool_call_id"": ""b"", ""content"": ""{\""temp\"":12}"" },
            { ""role"": ""tool"", ""tool_call_id"": ""c"", ""content"": ""Error: not found"" } ] }");
        FunctionResultContent[] results = request.Messages[2].Contents.OfType<FunctionResultContent>().ToArray();
        JsonElement code = (JsonElement)results[0].Result;
        Assert.That((code.ValueKind, code.GetString()), Is.EqualTo((JsonValueKind.String, "7391")));
        Assert.That(((JsonElement)results[1].Result).GetProperty("temp").GetInt32(), Is.EqualTo(12));
        Assert.That(results[2].Result, Is.EqualTo("Error: not found"));
    }

    [Test]
    public void ReadsOptionsAndAudio()
    {
        string wav = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        ChatCompletionRequest request = OpenAIChatProtocol.ParseRequest(@"{ ""messages"": [ { ""role"": ""user"", ""content"": [
            { ""type"": ""input_audio"", ""input_audio"": { ""data"": """ + wav + @""", ""format"": ""wav"" } } ] } ],
            ""max_tokens"": 100, ""reasoning_effort"": ""high"", ""tool_choice"": ""none"" }");
        Assert.That(request.Stream, Is.False);
        Assert.That(request.Messages.Single().Contents.OfType<DataContent>().Single().MediaType, Is.EqualTo("audio/wav"));
        Assert.That(request.Options.MaxOutputTokens, Is.EqualTo(100));
        Assert.That(request.Options.Reasoning.Effort, Is.EqualTo(ReasoningEffort.High));
        Assert.That(request.Options.ToolMode, Is.EqualTo(ChatToolMode.None));
        Assert.That(request.Options.Tools, Is.Null);
        Assert.That(request.Options.Instructions, Is.Null);
    }

    [Test]
    public void RefusesWhatItCantTake()
    {
        string[] bad =
        {
            "not json", "[]", @"{ ""messages"": [] }", @"{ ""messages"": [ { ""role"": ""wizard"", ""content"": ""hi"" } ] }",
            @"{ ""messages"": [ { ""role"": ""user"", ""content"": [ { ""type"": ""image_url"", ""image_url"": { ""url"": ""https://example.com/a.png"" } } ] } ] }",
            @"{ ""messages"": [ { ""role"": ""tool"", ""content"": ""x"" } ] }",
            @"{ ""messages"": [ { ""role"": ""user"", ""content"": ""hi"" } ], ""tools"": [ { ""type"": ""retrieval"" } ] }",
            @"{ ""messages"": [ { ""role"": ""assistant"", ""tool_calls"": [ { ""id"": ""c"", ""function"": { ""name"": ""f"", ""arguments"": ""{oops"" } } ] } ] }"
        };
        foreach (string json in bad)
            Assert.That(Assert.Throws<ChatProtocolException>(() => OpenAIChatProtocol.ParseRequest(json)).Status, Is.EqualTo(400), json);
    }

    [Test]
    public void WritesChunksAndWholeReplies()
    {
        int toolIndex = 0;
        string first = OpenAIChatProtocol.Chunk(new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent> { new TextReasoningContent("Hmm.") }), "chatcmpl-1", "m", 7, true, ref toolIndex);
        using (JsonDocument doc = JsonDocument.Parse(first))
        {
            JsonElement delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
            Assert.That(doc.RootElement.GetProperty("object").GetString(), Is.EqualTo("chat.completion.chunk"));
            Assert.That(delta.GetProperty("role").GetString(), Is.EqualTo("assistant"));
            Assert.That(delta.GetProperty("reasoning_content").GetString(), Is.EqualTo("Hmm."));
        }
        var call = new FunctionCallContent("call_x", "get_time", new Dictionary<string, object> { ["zone"] = "UTC" });
        string calls = OpenAIChatProtocol.Chunk(new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent> { new TextContent("Checking."), call }), "chatcmpl-1", "m", 7, false, ref toolIndex);
        Assert.That(toolIndex, Is.EqualTo(1));
        using (JsonDocument doc = JsonDocument.Parse(calls))
        {
            JsonElement delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
            Assert.That(delta.GetProperty("content").GetString(), Is.EqualTo("Checking."));
            JsonElement tool = delta.GetProperty("tool_calls")[0];
            Assert.That((tool.GetProperty("index").GetInt32(), tool.GetProperty("id").GetString(), tool.GetProperty("function").GetProperty("name").GetString()),
                Is.EqualTo((0, "call_x", "get_time")));
            Assert.That(tool.GetProperty("function").GetProperty("arguments").GetString(), Is.EqualTo("{\"zone\":\"UTC\"}"));
        }
        Assert.That(OpenAIChatProtocol.Chunk(new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>()), "chatcmpl-1", "m", 7, false, ref toolIndex), Is.Null);
        using (JsonDocument doc = JsonDocument.Parse(OpenAIChatProtocol.FinalChunk("chatcmpl-1", "m", 7, ChatFinishReason.ToolCalls)))
            Assert.That(doc.RootElement.GetProperty("choices")[0].GetProperty("finish_reason").GetString(), Is.EqualTo("tool_calls"));

        string whole = OpenAIChatProtocol.Completion(new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent> { new TextReasoningContent("Think."), new TextContent("Hello ") }),
            new ChatResponseUpdate(ChatRole.Assistant, "there.") { FinishReason = ChatFinishReason.Length }
        }, "chatcmpl-2", "m", 9);
        using (JsonDocument doc = JsonDocument.Parse(whole))
        {
            JsonElement choice = doc.RootElement.GetProperty("choices")[0];
            Assert.That(choice.GetProperty("message").GetProperty("content").GetString(), Is.EqualTo("Hello there."));
            Assert.That(choice.GetProperty("message").GetProperty("reasoning_content").GetString(), Is.EqualTo("Think."));
            Assert.That(choice.GetProperty("finish_reason").GetString(), Is.EqualTo("length"));
        }
    }

    [Test]
    public void ListsTheModelWithItsCapabilities()
    {
        var model = new ServedModel("gemma-4-e4b", "Gemma 4 E4B") { ContextLength = 8192, Vision = true, Tools = true, Thinking = true };
        using (JsonDocument doc = JsonDocument.Parse(OpenAIChatProtocol.Models(model, 1)))
        {
            JsonElement entry = doc.RootElement.GetProperty("data")[0];
            Assert.That((entry.GetProperty("id").GetString(), entry.GetProperty("owned_by").GetString(), entry.GetProperty("context_length").GetInt32()),
                Is.EqualTo(("gemma-4-e4b", "emgu-litert", 8192)));
            JsonElement capabilities = entry.GetProperty("capabilities");
            Assert.That((capabilities.GetProperty("vision").GetBoolean(), capabilities.GetProperty("audio").GetBoolean(), capabilities.GetProperty("tools").GetBoolean()),
                Is.EqualTo((true, false, true)));
        }
        using (JsonDocument doc = JsonDocument.Parse(OpenAIChatProtocol.LmStudioModels(model)))
        {
            JsonElement entry = doc.RootElement.GetProperty("data")[0];
            Assert.That((entry.GetProperty("type").GetString(), entry.GetProperty("state").GetString(), entry.GetProperty("loaded_context_length").GetInt32()),
                Is.EqualTo(("vlm", "loaded", 8192)));
        }
        using (JsonDocument doc = JsonDocument.Parse(OpenAIChatProtocol.Models(null, 1)))
            Assert.That(doc.RootElement.GetProperty("data").GetArrayLength(), Is.Zero);
    }

    // ---------- Pairing ----------

    [Test]
    public void ReadsAnEmbeddingsRequest()
    {
        EmbeddingRequest one = OpenAIChatProtocol.ParseEmbeddingRequest("{\"model\":\"m\",\"input\":\"a cat\"}");
        Assert.That(one.Inputs, Is.EqualTo(new[] { "a cat" }));
        Assert.That((one.Model, one.Base64, one.Options.Dimensions, one.Options.AdditionalProperties), Is.EqualTo(("m", false, (int?)null, (AdditionalPropertiesDictionary)null)));

        EmbeddingRequest many = OpenAIChatProtocol.ParseEmbeddingRequest(
            "{\"input\":[\"a\",\"b\"],\"dimensions\":256,\"encoding_format\":\"base64\",\"input_type\":\"search_query\"}");
        Assert.That(many.Inputs, Is.EqualTo(new[] { "a", "b" }));
        Assert.That((many.Base64, many.Options.Dimensions), Is.EqualTo((true, (int?)256)));
        Assert.That(many.Options.AdditionalProperties["input_type"], Is.EqualTo("query"));
        Assert.That(OpenAIChatProtocol.ParseEmbeddingRequest("{\"input\":\"x\",\"input_type\":\"document\"}").Options.AdditionalProperties["input_type"], Is.EqualTo("document"));

        foreach (string bad in new[] { "{}", "{\"input\":[]}", "{\"input\":[[1,2,3]]}", "{\"input\":5}", "{\"input\":\"x\",\"dimensions\":0}",
            "{\"input\":\"x\",\"encoding_format\":\"int8\"}", "{\"input\":\"x\",\"input_type\":\"classification\"}", "[1]", "not json",
            "{\"input\":[" + string.Join(",", Enumerable.Repeat("\"x\"", OpenAIChatProtocol.MaxEmbeddingInputs + 1)) + "]}" })
            Assert.That(Assert.Throws<ChatProtocolException>(() => OpenAIChatProtocol.ParseEmbeddingRequest(bad)).Status, Is.EqualTo(400), bad);
    }

    [Test]
    public void WritesEmbeddingsAsNumbersOrBase64()
    {
        var vectors = new[] { new Embedding<float>(new[] { 1.5f, -2f }), new Embedding<float>(new[] { 0.25f, 3f }) };
        using (JsonDocument doc = JsonDocument.Parse(OpenAIChatProtocol.Embeddings(vectors, "search", false)))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.That(data.GetArrayLength(), Is.EqualTo(2));
            Assert.That(data[1].GetProperty("index").GetInt32(), Is.EqualTo(1));
            Assert.That(data[1].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()), Is.EqualTo(new[] { 0.25f, 3f }));
            Assert.That(doc.RootElement.GetProperty("model").GetString(), Is.EqualTo("search"));
        }
        using (JsonDocument doc = JsonDocument.Parse(OpenAIChatProtocol.Embeddings(vectors, "search", true)))
        {
            byte[] bytes = Convert.FromBase64String(doc.RootElement.GetProperty("data")[0].GetProperty("embedding").GetString());
            Assert.That(Enumerable.Range(0, 2).Select(i => BitConverter.ToSingle(bytes, i * 4)), Is.EqualTo(new[] { 1.5f, -2f }));
        }
    }

    [Test]
    public void PairsWithACodeOnceAndKeepsOnlyTheTokensHash()
    {
        DateTime now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var store = new InMemoryServerTokenStore();
        var pairing = new ServerPairing(store, () => now);
        Assert.That(pairing.TryPair("123456", "Phone"), Is.Null, "No code shown yet.");
        string code = pairing.NewCode();
        Assert.That(code, Does.Match("^[0-9]{6}$"));
        Assert.That(pairing.CurrentCode, Is.EqualTo(code));
        string token = pairing.TryPair(" " + code + " ", "Galaxy S10");
        Assert.That(token, Is.Not.Null.And.Length.GreaterThanOrEqualTo(40));
        Assert.That(pairing.CurrentCode, Is.Null, "A code pairs one device.");
        Assert.That(pairing.TryPair(code, "Another"), Is.Null);

        PairedDevice device = store.Load().Single();
        Assert.That(device.Name, Is.EqualTo("Galaxy S10"));
        Assert.That(device.TokenHash, Does.Not.Contain(token));
        Assert.That(pairing.Validate(token)?.Name, Is.EqualTo("Galaxy S10"));
        Assert.That(pairing.Validate(token + "x"), Is.Null);
        Assert.That(pairing.Validate(null), Is.Null);
        // Read back by a new pairing (the app started again).
        Assert.That(new ServerPairing(store, () => now).Validate(token)?.Name, Is.EqualTo("Galaxy S10"));
        Assert.That(pairing.Remove(device.Id), Is.True);
        Assert.That(pairing.Validate(token), Is.Null);
        Assert.That(pairing.Remove(device.Id), Is.False);
    }

    [Test]
    public void ACodeEndsAfterFiveWrongTriesOrFiveMinutes()
    {
        DateTime now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var pairing = new ServerPairing(new InMemoryServerTokenStore(), () => now);
        string code = pairing.NewCode();
        string wrong = code == "000000" ? "111111" : "000000";
        for (int i = 0; i < ServerPairing.MaxWrongCodes; i++)
            Assert.That(pairing.TryPair(wrong, "Phone"), Is.Null);
        Assert.That(pairing.TryPair(code, "Phone"), Is.Null, "Ended by the wrong ones.");

        code = pairing.NewCode();
        now += ServerPairing.CodeLifetime;
        Assert.That(pairing.CurrentCode, Is.Null);
        Assert.That(pairing.TryPair(code, "Phone"), Is.Null, "Expired.");
        Assert.That(pairing.Devices, Is.Empty);
    }

    // ---------- The server over loopback ----------

    [Test]
    public async Task StreamsTextAndThinkingToAnOpenAIClient()
    {
        var model = new ScriptedModel();
        model.Replies.Enqueue(new[] { Update(new TextReasoningContent("The user greets.")), Update(new TextContent("Hello")), Update(new TextContent(" there!")) });
        using var host = await ServerHost.StartAsync(model);
        IChatClient client = host.Client();
        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") },
            new ChatOptions { Instructions = "Be kind.", Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium } }))
        {
            text.Append(string.Concat(update.Contents.OfType<TextContent>().Select(c => c.Text)));
            reasoning.Append(string.Concat(update.Contents.OfType<TextReasoningContent>().Select(c => c.Text)));
        }
        Assert.That(text.ToString(), Is.EqualTo("Hello there!"));
        Assert.That(reasoning.ToString(), Is.EqualTo("The user greets."));
        ScriptedModel.Request request = model.Requests.Single();
        Assert.That(request.Options.Instructions, Is.EqualTo("Be kind."));
        Assert.That(request.Options.Reasoning?.Effort, Is.Not.EqualTo(ReasoningEffort.None));
        Assert.That(request.Messages.Single().Text, Is.EqualTo("Hi"));
        Assert.That(host.Requests.Single().Device, Is.EqualTo("Test phone"));

        // Not streamed.
        model.Replies.Enqueue(new[] { Update(new TextContent("Fine.")) });
        ChatResponse whole = await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "How are you?") });
        Assert.That(whole.Text, Is.EqualTo("Fine."));
    }

    [Test]
    public async Task ARoundOfToolsComesBackAsTheModelKeepsIt()
    {
        var model = new ScriptedModel();
        var call = new FunctionCallContent("call_0123456789ab", "get_time", new Dictionary<string, object> { ["zone"] = "UTC" });
        model.Replies.Enqueue(new[] { Update(call) });
        model.Replies.Enqueue(new[] { Update(new TextContent("It is noon.")) });
        using var host = await ServerHost.StartAsync(model);
        // The phone's side: the tool runs there.
        int called = 0;
        AIFunction getTime = AIFunctionFactory.Create((string zone) => { called++; return "12:00 " + zone; }, "get_time", "The time in a zone");
        IChatClient client = new ChatClientBuilder(host.Client()).UseFunctionInvocation().Build();
        ChatResponse response = await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "What time is it?") }, new ChatOptions { Tools = new List<AITool> { getTime } });
        Assert.That(response.Text, Is.EqualTo("It is noon."));
        Assert.That(called, Is.EqualTo(1));

        Assert.That(model.Requests.Count, Is.EqualTo(2));
        Assert.That(model.Requests[0].Options.Tools.Single(), Is.InstanceOf<AIFunctionDeclaration>());
        // The second request is the first, the model's own reply (the same call id and name), and one tool message.
        IList<ChatMessage> second = model.Requests[1].Messages;
        Assert.That(second.Select(m => m.Role), Is.EqualTo(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.Tool }));
        FunctionCallContent echoed = second[1].Contents.OfType<FunctionCallContent>().Single();
        Assert.That((echoed.CallId, echoed.Name, second[1].Text), Is.EqualTo((call.CallId, call.Name, "")));
        FunctionResultContent result = second[2].Contents.OfType<FunctionResultContent>().Single();
        Assert.That(result.CallId, Is.EqualTo(call.CallId));
        Assert.That(result.Result?.ToString(), Does.Contain("12:00 UTC"));
    }

    [Test]
    public async Task NeedsATokenAndAModel()
    {
        var model = new ScriptedModel();
        using var host = await ServerHost.StartAsync(model);
        using var http = new HttpClient();
        // GET / tells a device what it found, without a token.
        string about = await http.GetStringAsync(host.Url);
        Assert.That(about, Does.Contain("\"server\":\"emgu-litert\""));
        HttpResponseMessage response = await http.GetAsync(host.Url + "v1/models");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Pair with this server first"));

        var request = new HttpRequestMessage(HttpMethod.Get, host.Url + "v1/models");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", host.Token);
        Assert.That(await (await http.SendAsync(request)).Content.ReadAsStringAsync(), Does.Contain("\"context_length\":4096"));

        // No model loaded: 503, which the OpenAI client throws.
        host.Served = null;
        Assert.ThrowsAsync<ClientResultException>(() => host.Client().GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }));
        Assert.That(host.Requests.Last().Status, Is.EqualTo(503));

        // A wrong pairing code: 401.
        HttpResponseMessage pair = await http.PostAsync(host.Url + "pair", new StringContent("{\"code\":\"999999x\",\"device\":\"Intruder\"}"));
        Assert.That(pair.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task EmbedsForAnOpenAIClient()
    {
        using var host = await ServerHost.StartAsync(new ScriptedModel());
        var search = new ScriptedEmbedder();
        host.Embeddings = new ServedEmbeddings(search, new ServedModel("embeddinggemma-2", "EmbeddingGemma 2") { Dimensions = 3 });

        // The model list has the search model after the chat model.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", host.Token);
        using (JsonDocument models = JsonDocument.Parse(await http.GetStringAsync(host.Url + "v1/models")))
        {
            JsonElement data = models.RootElement.GetProperty("data");
            Assert.That(data.EnumerateArray().Select(m => m.GetProperty("id").GetString()), Is.EqualTo(new[] { "test-model", "embeddinggemma-2" }));
            Assert.That((data[1].GetProperty("type").GetString(), data[1].GetProperty("dimensions").GetInt32()), Is.EqualTo(("embeddings", 3)));
        }
        Assert.That(await http.GetStringAsync(host.Url + "api/v0/models"), Does.Contain("\"type\":\"embeddings\""));

        // Microsoft.Extensions.AI.OpenAI's generator (the OpenAI SDK asks for base64).
        IEmbeddingGenerator<string, Embedding<float>> generator = host.Embedder("embeddinggemma-2");
        GeneratedEmbeddings<Embedding<float>> embeddings = await generator.GenerateAsync(new[] { "a cat", "the dog" }, new EmbeddingGenerationOptions { Dimensions = 3 });
        Assert.That(embeddings.Select(e => e.Vector.ToArray()), Is.EqualTo(new[] { ScriptedEmbedder.Vector("a cat", 0), ScriptedEmbedder.Vector("the dog", 1) }));
        Assert.That(search.Requests.Single().Options.Dimensions, Is.EqualTo(3));
        Assert.That(host.Requests.Last().Path, Is.EqualTo("/v1/embeddings"));
        Assert.That(host.Requests.Last().Device, Is.EqualTo("Test phone"));

        // A query, as JSON numbers: "input_type" reaches the generator.
        HttpResponseMessage query = await http.PostAsync(host.Url + "v1/embeddings", new StringContent("{\"input\":\"cats\",\"input_type\":\"query\",\"encoding_format\":\"float\"}"));
        Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (JsonDocument doc = JsonDocument.Parse(await query.Content.ReadAsStringAsync()))
            Assert.That(doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()), Is.EqualTo(ScriptedEmbedder.Vector("cats", 0)));
        Assert.That(search.Requests.Last().Options.AdditionalProperties["input_type"], Is.EqualTo("query"));

        // What the generator refuses is a bad request; no search model is 503; no token is 401.
        search.Refuse = true;
        Assert.That((await http.PostAsync(host.Url + "v1/embeddings", new StringContent("{\"input\":\"x\"}"))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        host.Embeddings = null;
        Assert.That((await http.PostAsync(host.Url + "v1/embeddings", new StringContent("{\"input\":\"x\"}"))).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await http.GetStringAsync(host.Url + "v1/models"), Does.Not.Contain("embeddings"));
        using var stranger = new HttpClient();
        Assert.That((await stranger.PostAsync(host.Url + "v1/embeddings", new StringContent("{\"input\":\"x\"}"))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task APhoneEmbedsQueriesAndDocumentsOnTheServer()
    {
        using var host = await ServerHost.StartAsync(new ScriptedModel());
        var search = new ScriptedEmbedder();
        host.Embeddings = new ServedEmbeddings(search, new ServedModel("embeddinggemma-2-text-vision-440m.litertlm"));
        string token = host.Token;
        using var phone = new ServerEmbeddingGenerator(() => host.Url + "v1", () => token, "embeddinggemma-2-text-vision-440m.litertlm");

        var query = new EmbeddingGenerationOptions { AdditionalProperties = new AdditionalPropertiesDictionary { ["input_type"] = "query" } };
        GeneratedEmbeddings<Embedding<float>> one = await phone.GenerateAsync(new[] { "cats" }, query);
        Assert.That(one.Single().Vector.ToArray(), Is.EqualTo(ScriptedEmbedder.Vector("cats", 0)));
        Assert.That(search.Requests.Last().Options.AdditionalProperties["input_type"], Is.EqualTo("query"));

        // More texts than one request takes: several requests, the vectors in order.
        string[] passages = Enumerable.Range(0, OpenAIChatProtocol.MaxEmbeddingInputs + 3).Select(i => new string('x', i + 1)).ToArray();
        var document = new EmbeddingGenerationOptions { AdditionalProperties = new AdditionalPropertiesDictionary { ["input_type"] = "document" } };
        GeneratedEmbeddings<Embedding<float>> many = await phone.GenerateAsync(passages, document);
        Assert.That(many.Count, Is.EqualTo(passages.Length));
        Assert.That(many[^1].Vector.Span[0], Is.EqualTo(passages[^1].Length));
        Assert.That(search.Requests.Skip(1).Select(r => r.Texts.Count), Is.EqualTo(new[] { OpenAIChatProtocol.MaxEmbeddingInputs, 3 }));
        Assert.That(search.Requests.Skip(1).All(r => (string)r.Options.AdditionalProperties["input_type"] == "document"));

        // The server's refusals come back with its message.
        token = "wrong";
        var refused = Assert.ThrowsAsync<InvalidOperationException>(() => phone.GenerateAsync(new[] { "x" }));
        Assert.That(refused.Message, Does.Contain("401").And.Contain("paired"));
        token = host.Token;
        host.Embeddings = null;
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>(() => phone.GenerateAsync(new[] { "x" })).Message, Does.Contain("no search model"));
    }

    [Test]
    public async Task TellsAnUnreachableServerFromOneThatFails()
    {
        // Refused: nothing listens on the port.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int closed = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using (var http = new HttpClient(ServerReachability.CreateHandler()))
        {
            var refused = Assert.CatchAsync<Exception>(() => http.GetAsync("http://127.0.0.1:" + closed + "/"));
            Assert.That(ServerReachability.IsUnreachable(refused), Is.True);
        }
        // Through the OpenAI SDK, as the phone's chat goes.
        var chat = new OpenAIClient(new ApiKeyCredential("token"), new OpenAIClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:" + closed + "/v1"),
            Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(new HttpClient(ServerReachability.CreateHandler()))
        }).GetChatClient("test-model").AsIChatClient();
        Assert.That(ServerReachability.IsUnreachable(Assert.CatchAsync<Exception>(() => chat.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }))), Is.True);
        // The phone's search client says it in words.
        using (var search = new ServerEmbeddingGenerator(() => "http://127.0.0.1:" + closed + "/v1", () => "token", "m"))
            Assert.That(Assert.ThrowsAsync<InvalidOperationException>(() => search.GenerateAsync(new[] { "x" })).Message, Does.Contain("isn't answering"));
        // Nobody answers the connection (an address that drops it): the short connect timeout.
        using (var http = new HttpClient(ServerReachability.CreateHandler(TimeSpan.FromSeconds(1))))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var silent = Assert.CatchAsync<Exception>(() => http.GetAsync("http://10.255.255.1:11435/"));
            Assert.That(ServerReachability.IsUnreachable(silent), Is.True);
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        }
        // A server that connects but doesn't answer in time isn't unreachable (it's slow), nor is an answer's error.
        var mute = new TcpListener(IPAddress.Loopback, 0);
        mute.Start();
        try
        {
            using var http = new HttpClient(ServerReachability.CreateHandler()) { Timeout = TimeSpan.FromSeconds(1) };
            var slow = Assert.CatchAsync<Exception>(() => http.GetAsync("http://127.0.0.1:" + ((IPEndPoint)mute.LocalEndpoint).Port + "/"));
            Assert.That(ServerReachability.IsUnreachable(slow), Is.False);
        }
        finally { mute.Stop(); }
        Assert.That(ServerReachability.IsUnreachable(new HttpRequestException("Response status code does not indicate success: 500")), Is.False);
    }

    [Test]
    public void SearchesADocumentWithAQueryEmbeddedElsewhere()
    {
        var index = new Emgu.LiteRT.LM.Models.DocumentIndex();
        index.AddPassage("cats", "page 1", new[] { 1f, 0f });
        index.AddPassage("dogs", "page 2", new[] { 0f, 1f });
        Emgu.LiteRT.LM.Models.DocumentSearchResult result = index.Search(new[] { 0.1f, 0.9f });
        Assert.That(result.Passages.Select(m => m.Item.Text), Is.EqualTo(new[] { "dogs", "cats" }));
    }

    [Test]
    public async Task AClientThatGoesAwayStopsTheReply()
    {
        var model = new ScriptedModel { Slow = true };
        model.Replies.Enqueue(Enumerable.Range(0, 200).Select(i => Update(new TextContent("word" + i + " "))).ToArray());
        using var host = await ServerHost.StartAsync(model);
        using var cancel = new CancellationTokenSource();
        int received = 0;
        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await foreach (ChatResponseUpdate update in host.Client().GetStreamingResponseAsync(new[] { new ChatMessage(ChatRole.User, "Talk") }, null, cancel.Token))
                if (++received == 3)
                    cancel.Cancel();
        });
        // The server notices on its next write.
        for (int i = 0; i < 100 && !model.Cancelled; i++)
            await Task.Delay(50);
        Assert.That(model.Cancelled, Is.True);
        Assert.That(model.Yielded, Is.LessThan(200));
    }

    // ---------- The server over a real model (TEST_MODELS) ----------

    // Qwen3-0.6B (~500 MB download) through the server and called directly: the same answers, and the server's second
    // request continues the LiteRT-LM conversation the first one left open (the phone sends the whole history back).
#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; downloads Qwen3-0.6B.")]
#endif
    [Test, Category("Native")]
    public async Task ServesQwen3AsCallingItDirectly()
    {
        RequireLiteRtLm();
        using var model = new Emgu.LiteRT.LM.Models.Qwen3();
        await model.Init();
        var options = new ChatOptions { MaxOutputTokens = 64, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } };
        string[] questions = { "My name is Kenji. What is the capital of France? Answer in one short sentence.", "What is my name?" };

        using var direct = new Emgu.LiteRT.LM.Extensions.AI.LiteRtLmChatClient(model) { EnableContextManagement = true };
        string[] directReplies = await TwoTurnsAsync(direct, questions, options);

        using var served = new Emgu.LiteRT.LM.Extensions.AI.LiteRtLmChatClient(model) { EnableContextManagement = true };
        var context = new List<Emgu.LiteRT.LM.Extensions.AI.LiteRtLmContextInfo>();
        served.ContextChanged += context.Add;
        using var host = await ServerHost.StartAsync(served);
        string[] servedReplies = await TwoTurnsAsync(host.Client(), questions, options);

        Console.WriteLine("Direct: " + string.Join(" | ", directReplies));
        Console.WriteLine("Served: " + string.Join(" | ", servedReplies));
        foreach (string[] replies in new[] { directReplies, servedReplies })
        {
            Assert.That(replies[0], Does.Contain("Paris"));
            Assert.That(replies[1], Does.Contain("Kenji"));
        }
        AssertConversationKept(context);
    }

    // Gemma 4 E2B on the GPU (~2.6 GB download): a tool round through the server, the tool running on the phone's side,
    // as the direct call does it; the model's call comes back from the phone with its id and name, so the round
    // continues the open conversation instead of starting it again.
#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; downloads Gemma 4 E2B.")]
#endif
    [Test, Category("Native")]
    public async Task ServesAToolRoundOfGemma4KeepingTheConversation()
    {
        RequireLiteRtLm();
        if (!UnitTest1.StageGpuPlugin())
            Assert.Ignore("No LiteRT GPU accelerator plugin next to libLiteRt");
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B();
        await model.Init(backend: "gpu");
        int calls = 0;
        AIFunction secretCode = AIFunctionFactory.Create(
            (string person) => { calls++; return person.IndexOf("Alice", StringComparison.OrdinalIgnoreCase) >= 0 ? "7391" : "unknown"; },
            "get_secret_code", "Returns the secret code of a person.");
        var options = new ChatOptions { Tools = new List<AITool> { secretCode }, MaxOutputTokens = 256, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } };
        var question = new[] { new ChatMessage(ChatRole.User, "What is the secret code of Alice?") };

        using var direct = new Emgu.LiteRT.LM.Extensions.AI.LiteRtLmChatClient(model) { EnableContextManagement = true };
        var directContext = new List<Emgu.LiteRT.LM.Extensions.AI.LiteRtLmContextInfo>();
        direct.ContextChanged += directContext.Add;
        ChatResponse directReply = await new ChatClientBuilder(direct).UseFunctionInvocation().Build().GetResponseAsync(question, options);

        using var served = new Emgu.LiteRT.LM.Extensions.AI.LiteRtLmChatClient(model) { EnableContextManagement = true };
        var context = new List<Emgu.LiteRT.LM.Extensions.AI.LiteRtLmContextInfo>();
        served.ContextChanged += context.Add;
        using var host = await ServerHost.StartAsync(served);
        ChatResponse servedReply = await new ChatClientBuilder(host.Client()).UseFunctionInvocation().Build().GetResponseAsync(question, options);

        Console.WriteLine("Direct: " + directReply.Text);
        Console.WriteLine("Served: " + servedReply.Text);
        Assert.That(calls, Is.EqualTo(2), "The tool ran once each way.");
        Assert.That(directReply.Text, Does.Contain("7391"));
        Assert.That(servedReply.Text, Does.Contain("7391"));
        AssertConversationKept(directContext);
        AssertConversationKept(context);
        Assert.That(context.Count(c => c.Stage == "complete" && c.ToolCalls > 0), Is.EqualTo(1));
        // The model reads the same tokens either way: the tool's result isn't quoted by its trip over the wire.
        Assert.That(context.Select(c => (c.Stage, c.InputTokens, c.CachedTokens)), Is.EqualTo(directContext.Select(c => (c.Stage, c.InputTokens, c.CachedTokens))));
    }

    // EmbeddingGemma 2 Text Vision on the CPU (~440 MB download): through the server, through the OpenAI client and with a
    // query and documents, the vectors are the model's own.
#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; downloads EmbeddingGemma 2.")]
#endif
    [Test, Category("Native")]
    public async Task ServesEmbeddingGemmaAsCallingItDirectly()
    {
        RequireLiteRtLm();
        using var model = new Emgu.LiteRT.LM.Models.EmbeddingGemma2TextVision();
        await model.Init();
        string[] texts = { "How do I change my password?", "The Eiffel Tower is 330 metres tall." };
        using var host = await ServerHost.StartAsync(new ScriptedModel());
        int entered = 0;
        var served = new Emgu.LiteRT.LM.Extensions.AI.LiteRtLmEmbeddingGenerator(_ => Task.FromResult<Emgu.LiteRT.LM.Models.EmbeddingModel>(model),
            "embeddinggemma-2", _ => { entered++; return Task.FromResult<IDisposable>(null); });
        host.Embeddings = new ServedEmbeddings(served, new ServedModel("embeddinggemma-2"));

        float[][] plain = (await host.Embedder("embeddinggemma-2").GenerateAsync(texts)).Select(e => e.Vector.ToArray()).ToArray();
        AssertSame(plain, model.Embed(texts));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", host.Token);
        float[][] Read(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("data").EnumerateArray().Select(d => d.GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray()).ToArray();
        }
        string body(string type) => "{\"input\":" + JsonSerializer.Serialize(texts) + ",\"input_type\":\"" + type + "\"}";
        float[][] documents = Read(await (await http.PostAsync(host.Url + "v1/embeddings", new StringContent(body("document")))).Content.ReadAsStringAsync());
        AssertSame(documents, model.EmbedDocuments(texts));
        float[][] queries = Read(await (await http.PostAsync(host.Url + "v1/embeddings", new StringContent(body("query")))).Content.ReadAsStringAsync());
        AssertSame(queries, texts.Select(t => model.EmbedQuery(t)).ToArray());
        Assert.That(entered, Is.EqualTo(3));
        // The phone's generator: the same vectors, with the query and document prefixes.
        using var phone = new ServerEmbeddingGenerator(() => host.Url + "v1", () => host.Token, "embeddinggemma-2");
        var asDocuments = new EmbeddingGenerationOptions { AdditionalProperties = new AdditionalPropertiesDictionary { ["input_type"] = "document" } };
        AssertSame((await phone.GenerateAsync(texts, asDocuments)).Select(e => e.Vector.ToArray()).ToArray(), documents);
        // The query finds its answer among the documents.
        float[] question = (await host.Embedder("embeddinggemma-2").GenerateAsync(new[] { "how tall is the tower in Paris" },
            new EmbeddingGenerationOptions { AdditionalProperties = new AdditionalPropertiesDictionary { ["input_type"] = "query" } })).Single().Vector.ToArray();
        Console.WriteLine("Vectors of {0}; the tower question matches {1:F3} / {2:F3}", plain[0].Length,
            Emgu.LiteRT.LM.Models.EmbeddingModel.CosineSimilarity(question, documents[1]), Emgu.LiteRT.LM.Models.EmbeddingModel.CosineSimilarity(question, documents[0]));
    }

    private static void AssertSame(float[][] served, float[][] direct)
    {
        Assert.That(served.Length, Is.EqualTo(direct.Length));
        for (int i = 0; i < served.Length; i++)
            Assert.That(served[i], Is.EqualTo(direct[i]).Within(1e-5f));
    }

    private static async Task<string[]> TwoTurnsAsync(IChatClient client, string[] questions, ChatOptions options)
    {
        var history = new List<ChatMessage>();
        var replies = new List<string>();
        foreach (string question in questions)
        {
            history.Add(new ChatMessage(ChatRole.User, question));
            ChatResponse response = await client.GetResponseAsync(history, options);
            history.AddRange(response.Messages);
            replies.Add(response.Text);
        }
        return replies.ToArray();
    }

    // Each request after the first starts from the tokens the previous one left in the conversation: the conversation was
    // continued, not created again from the history (a new one starts with none cached).
    private static void AssertConversationKept(List<Emgu.LiteRT.LM.Extensions.AI.LiteRtLmContextInfo> context)
    {
        foreach (var info in context)
            Console.WriteLine("  " + info);
        var prepared = context.Where(c => c.Stage == "prepare").ToList();
        var completed = context.Where(c => c.Stage == "complete").ToList();
        Assert.That(context.Any(c => c.Stage == "compact" || c.Stage == "failed"), Is.False);
        Assert.That(prepared.Count, Is.EqualTo(2));
        Assert.That(completed.Count, Is.EqualTo(2));
        Assert.That(completed[0].CachedTokens, Is.GreaterThan(0));
        Assert.That(prepared[1].CachedTokens, Is.EqualTo(completed[0].CachedTokens), "The second request reopened the conversation.");
    }

    private static void RequireLiteRtLm()
    {
        try
        {
            Emgu.LiteRT.LM.LiteRtLmInvoke.SetMinLogLevel(Emgu.LiteRT.LM.LogSeverity.Warning);
        }
        catch (DllNotFoundException e)
        {
            Assert.Ignore("liblitert-lm is not available on this platform: " + e.Message);
        }
    }

    private static ChatResponseUpdate Update(AIContent content) => new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent> { content });

    // A model that answers from a script and records what it was asked.
    private sealed class ScriptedModel : IChatClient
    {
        public sealed class Request
        {
            public IList<ChatMessage> Messages;
            public ChatOptions Options;
        }

        public Queue<ChatResponseUpdate[]> Replies { get; } = new Queue<ChatResponseUpdate[]>();
        public List<Request> Requests { get; } = new List<Request>();
        public bool Slow { get; set; }
        public volatile bool Cancelled;
        public int Yielded;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default) =>
            GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(new Request { Messages = messages.ToList(), Options = options });
            ChatResponseUpdate[] reply = Replies.Dequeue();
            bool calls = reply.Any(u => u.Contents.OfType<FunctionCallContent>().Any());
            using (cancellationToken.Register(() => Cancelled = true))
            {
                foreach (ChatResponseUpdate update in reply)
                {
                    if (Slow)
                        await Task.Delay(20, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    Yielded++;
                    yield return update;
                }
            }
            yield return new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>()) { FinishReason = calls ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop };
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // A search model that gives each text a made-up vector and records what it was asked.
    private sealed class ScriptedEmbedder : IEmbeddingGenerator<string, Embedding<float>>
    {
        public sealed class Request
        {
            public IList<string> Texts;
            public EmbeddingGenerationOptions Options;
        }

        public List<Request> Requests { get; } = new List<Request>();
        public bool Refuse { get; set; }

        public static float[] Vector(string text, int index) => new[] { text.Length, index, 0.5f };

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
        {
            if (Refuse)
                throw new ArgumentException("The input type is \"query\" or \"document\".");
            List<string> texts = values.ToList();
            Requests.Add(new Request { Texts = texts, Options = options });
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(texts.Select((t, i) => new Embedding<float>(Vector(t, i)))));
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // A ChatServer on a free loopback port, with a phone paired.
    private sealed class ServerHost : IDisposable
    {
        private readonly ChatServer _server;

        private ServerHost(ChatServer server, string url)
        {
            _server = server;
            Url = url;
            server.RequestCompleted += (s, e) => { lock (Requests) Requests.Add(e); };
        }

        public string Url { get; }
        public string Token { get; private set; }
        public ServedChat Served { get; set; }
        public ServedEmbeddings Embeddings { get; set; }
        public List<ServerRequestInfo> Requests { get; } = new List<ServerRequestInfo>();

        public static async Task<ServerHost> StartAsync(IChatClient model)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var pairing = new ServerPairing(new InMemoryServerTokenStore());
            ServerHost host = null;
            var server = new ChatServer(pairing, () => host.Served, () => host.Embeddings) { Name = "Test Mac" };
            host = new ServerHost(server, "http://localhost:" + port + "/");
            host.Served = new ServedChat(model, new ServedModel("test-model", "Test model") { ContextLength = 4096, Tools = true, Thinking = true });
            server.Start(port);
            using var http = new HttpClient();
            HttpResponseMessage pair = await http.PostAsync(host.Url + "pair", new StringContent("{\"code\":\"" + pairing.NewCode() + "\",\"device\":\"Test phone\"}"));
            using (JsonDocument doc = JsonDocument.Parse(await pair.Content.ReadAsStringAsync()))
            {
                Assert.That(doc.RootElement.GetProperty("server").GetString(), Is.EqualTo("Test Mac"));
                host.Token = doc.RootElement.GetProperty("token").GetString();
            }
            lock (host.Requests) host.Requests.Clear();
            return host;
        }

        // The OpenAI client a phone uses (Emgu.LiteRT.LM.Maui.OpenAI's ServerFeature).
        public IChatClient Client() =>
            new OpenAIClient(new ApiKeyCredential(Token), new OpenAIClientOptions { Endpoint = new Uri(Url + "v1") }).GetChatClient("test-model").AsIChatClient();

        public IEmbeddingGenerator<string, Embedding<float>> Embedder(string model) =>
            new OpenAIClient(new ApiKeyCredential(Token), new OpenAIClientOptions { Endpoint = new Uri(Url + "v1") }).GetEmbeddingClient(model).AsIEmbeddingGenerator();

        public void Dispose() => _server.Dispose();
    }
}
