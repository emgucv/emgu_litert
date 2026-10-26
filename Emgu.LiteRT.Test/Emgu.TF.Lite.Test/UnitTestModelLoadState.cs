// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Emgu.LiteRT.LM.Chat's ModelLoadState with an app's downloader (Emgu.LiteRT.LM.Maui's BackgroundDownloads in a MAUI page):
// the downloader runs before the model loads, with the state's progress, and what the card shows afterwards. No model is
// loaded: the file is one that doesn't exist, and the downloader stops before Init could run.
[TestFixture]
public class UnitTestModelLoadState
{
    private static DownloadableFile MissingFile() =>
        new DownloadableFile("https://example.invalid/model.litertlm", Path.Combine("ModelLoadStateTests", Guid.NewGuid().ToString("N")));

    [Test]
    public async Task TheDownloaderRunsFirstWithTheStatesProgress()
    {
        using var model = new Qwen3();
        var state = new ModelLoadState(model, MissingFile()) { DisplayName = "Test model", Hint = "Keep the app open." };
        string downloaded = null;
        state.Downloader = (file, name, progress, token) =>
        {
            downloaded = name;
            Assert.That(state.Stage, Is.EqualTo(ModelLoadStage.Downloading));
            Assert.That(state.IsDownloading, Is.True);
            progress(1000, 250, 25);
            // The download fails before the model could be loaded (no engine is ever touched).
            throw new IOException("no network");
        };
        Assert.That(state.IsPaused, Is.False);
        Assert.That(await state.LoadAsync(), Is.False);
        Assert.That(downloaded, Is.EqualTo("Test model"));
        Assert.That(state.HasError, Is.True);
        Assert.That(state.Error, Is.EqualTo("no network"));
        Assert.That(state.FailedStage, Is.EqualTo(ModelLoadStage.Downloading));
        Assert.That(state.IsLoading, Is.False);
        Assert.That(state.IsReady, Is.False);
        Assert.That(state.BytesReceived, Is.EqualTo(250));
        Assert.That(state.TotalBytes, Is.EqualTo(1000));
        Assert.That(state.Progress, Is.EqualTo(0.25).Within(1e-9));
        Assert.That(state.Hint, Is.EqualTo("Keep the app open."));
    }

    [Test]
    public async Task ACancelledDownloadResetsTheStage()
    {
        using var model = new Qwen3();
        var state = new ModelLoadState(model, MissingFile());
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Downloader = async (file, name, progress, token) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, token);
        };
        Task<bool> load = state.LoadAsync();
        await started.Task;
        Assert.That(state.IsLoading, Is.True);
        state.Cancel();
        Assert.That(await load, Is.False);
        Assert.That(state.Stage, Is.Null);
        Assert.That(state.HasError, Is.False);
        Assert.That(state.FailedStage, Is.Null);
        // Dismissed is the card's "Not now"; the state keeps it until a send clears it.
        state.Dismissed = true;
        Assert.That(state.Dismissed, Is.True);
    }
}
