// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Extensions.AI;
using Gemma4E2B = Emgu.LiteRT.LM.Models.Gemma4E2B;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public class UnitTestCameraTools
{
    private static List<ChatMessage> Expand(params ChatMessage[] messages) =>
        (List<ChatMessage>)typeof(LiteRtLmChatClient).GetMethod("ExpandToolMedia", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { messages });

    [Test]
    public async Task CameraToolPreservesImageBytesAndCancellation()
    {
        var jpeg = new byte[] { 1, 2, 3 };
        CancellationToken observed = default;
        using var cancel = new CancellationTokenSource();
        var tool = AgentTools.CaptureCameraFrame(token =>
        {
            observed = token;
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IEnumerable<AIContent>>(new AIContent[] { new TextContent("Fresh frame"), new DataContent(jpeg, "image/jpeg") });
        });
        var result = await tool.InvokeAsync(new AIFunctionArguments(), cancel.Token);
        Assert.That(observed, Is.EqualTo(cancel.Token));
        Assert.That(result, Is.InstanceOf<IEnumerable<AIContent>>());
        Assert.That(((IEnumerable<AIContent>)result).OfType<DataContent>().Single().Data.ToArray(), Is.EqualTo(jpeg));
        cancel.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await tool.InvokeAsync(new AIFunctionArguments(), cancel.Token));
    }

    [Test]
    public void ToolImageBecomesNativeMediaTurnWithoutMutatingSessionHistory()
    {
        var image = new DataContent(new byte[] { 4, 5, 6 }, "image/jpeg");
        var result = new FunctionResultContent("capture-1", new AIContent[] { new TextContent("Fresh frame"), image });
        var original = new ChatMessage(ChatRole.Tool, new List<AIContent> { result });
        foreach (var expanded in new[] { Expand(original), Expand(original) })
        {
            Assert.That(expanded.Count, Is.EqualTo(2));
            Assert.That(expanded[0].Role, Is.EqualTo(ChatRole.Tool));
            var acknowledgement = expanded[0].Contents.OfType<FunctionResultContent>().Single();
            Assert.That(acknowledgement.CallId, Is.EqualTo("capture-1"));
            Assert.That(acknowledgement.Result, Is.TypeOf<string>());
            Assert.That(expanded[1].Role, Is.EqualTo(ChatRole.User));
            Assert.That(expanded[1].Contents.OfType<DataContent>().Single(), Is.SameAs(image));
        }
        Assert.That(original.Contents.Single(), Is.SameAs(result));
        Assert.That(result.Result, Is.InstanceOf<AIContent[]>());
    }

    [Test]
    public void FailedCaptureDoesNotInjectAnImageAndOtherToolsStayIntact()
    {
        var failure = new FunctionResultContent("failed", new AIContent[] { new TextContent("Permission denied; no image captured.") });
        var ordinary = new FunctionResultContent("calculate", "42");
        var exception = new FunctionResultContent("error", new DataContent(new byte[] { 1 }, "image/jpeg")) { Exception = new InvalidOperationException("closed") };
        var expanded = Expand(new ChatMessage(ChatRole.Tool, new List<AIContent> { failure, ordinary, exception }));
        Assert.That(expanded.Count, Is.EqualTo(1));
        Assert.That(expanded[0].Contents.OfType<FunctionResultContent>().First().Result, Is.EqualTo("Permission denied; no image captured."));
        Assert.That(expanded[0].Contents[1], Is.SameAs(ordinary));
        Assert.That(expanded[0].Contents[2], Is.SameAs(exception));
    }

#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; requires cached Gemma 4 E2B.")]
#endif
    [TestCase(false), TestCase(true), Category("AgentRegression")]
    public async Task GemmaDescribesImageReturnedByCameraTool(bool manageContext)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var model = new Gemma4E2B { AudioBackend = null };
        await model.Init(configureSettings: settings => { settings.NumThreads = 4; settings.MaxNumTokens = 4096; }, cancellationToken: cancel.Token);
        byte[] image = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "apple.jpg"));
        int captures = 0;
        var tool = AgentTools.CaptureCameraFrame(token =>
        {
            captures++;
            return Task.FromResult<IEnumerable<AIContent>>(new AIContent[] {
                new TextContent("Fresh camera image. Describe what you actually see in it."), new DataContent(image, "image/jpeg") });
        });
        var native = new LiteRtLmChatClient(model) { EnableContextManagement = manageContext };
        using var client = new ChatClientBuilder(native).UseFunctionInvocation().Build();
        var options = new ChatOptions { Tools = new List<AITool> { tool }, MaxOutputTokens = 160,
                Instructions = "Call capture_camera_frame for camera questions. Then examine its image and give a brief answer. Do not capture again for the same question.",
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } };
        var history = new List<ChatMessage> { new(ChatRole.User, "What do you see in the camera? Name the main object and its color.") };
        var response = await client.GetResponseAsync(history, options, cancel.Token);
        TestContext.WriteLine(response.Text);
        Assert.That(captures, Is.EqualTo(1));
        Assert.That(response.Text, Does.Contain("apple").IgnoreCase);
        Assert.That(response.Text, Does.Contain("red").IgnoreCase);
        var openField = typeof(LiteRtLmChatClient).GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic);
        object conversation = openField.GetValue(native);
        Assert.That(conversation, Is.Not.Null);
        history.AddRange(response.Messages);
        history.Add(new ChatMessage(ChatRole.User, "Capture a new frame from the camera. What do you see now?"));
        var second = await client.GetResponseAsync(history, options, cancel.Token);
        TestContext.WriteLine(second.Text);
        Assert.That(captures, Is.EqualTo(2));
        Assert.That(second.Text, Does.Contain("apple").IgnoreCase);
        Assert.That(openField.GetValue(native), Is.SameAs(conversation), "Camera captures should reuse the conversation cache.");
    }
}
