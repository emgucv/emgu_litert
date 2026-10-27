// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Text.Json;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Agents.CV;

namespace Emgu.LiteRT.LM.Maui.CV;

internal sealed class CameraChimeFactory(CameraChimeAction chime) : IVisualActionFactory
{
    public VisualActionDescriptor Descriptor => VisualHostCatalog.Chime;
    public VisualActionBinding Bind(JsonElement parameters)
    {
        VisualActionParameters.AllowOnly(parameters);
        // Event groups are FIFO and allow each type once, so this host player is never reused concurrently.
        return new VisualActionBinding(chime, VisualActionParameters.Empty);
    }
}

internal sealed class CameraSpeechFactory(AlertAudioCoordinator audio) : IVisualActionFactory
{
    public VisualActionDescriptor Descriptor => VisualHostCatalog.Speech;
    public VisualActionBinding Bind(JsonElement parameters)
    {
        VisualActionParameters.AllowOnly(parameters, "text");
        string text = VisualActionParameters.RequiredText(parameters, "text", 200);
        if (string.IsNullOrWhiteSpace(ReplySpeechText.CleanForSpeech(text))) throw new ArgumentException("The spoken message needs pronounceable text.");
        return new VisualActionBinding(new CameraSpeechAction(audio, text), VisualActionParameters.Text(text));
    }
}

internal sealed class CameraSpeechAction(AlertAudioCoordinator audio, string text) : IVisualAction
{
    public string Id => "speak_text";
    public async Task ExecuteAsync(VisualEvent occurrence, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await audio.SpeakAlertAsync(text, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new TimeoutException("Spoken alert exceeded 30 seconds."); }
    }
}

#if ANDROID
internal sealed class CameraClipFactory(Func<VisualEvent, int, CancellationToken, Task> record) : IVisualActionFactory
{
    public VisualActionDescriptor Descriptor => VisualHostCatalog.Clip;
    public VisualActionBinding Bind(JsonElement parameters)
    {
        int seconds = VisualHostCatalog.ClipSeconds(parameters, out JsonElement normalized);
        return new VisualActionBinding(new CameraClipAction(record, seconds), normalized);
    }
}

internal sealed class CameraClipAction(Func<VisualEvent, int, CancellationToken, Task> record, int seconds) : IVisualAction
{
    public string Id => "record_clip";
    public async Task ExecuteAsync(VisualEvent occurrence, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds + 20));
        try { await record(occurrence, seconds, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new TimeoutException("The clip took more than " + (seconds + 20) + " seconds."); }
    }
}
#endif

#if ANDROID
internal sealed class CameraPhotoFactory(Func<VisualEvent, CancellationToken, Task> take) : IVisualActionFactory
{
    public VisualActionDescriptor Descriptor => VisualHostCatalog.Photo;
    public VisualActionBinding Bind(JsonElement parameters)
    {
        VisualActionParameters.AllowOnly(parameters);
        return new VisualActionBinding(new CameraPhotoAction(take), VisualActionParameters.Empty);
    }
}

internal sealed class CameraPhotoAction(Func<VisualEvent, CancellationToken, Task> take) : IVisualAction
{
    public string Id => "take_photo";
    public async Task ExecuteAsync(VisualEvent occurrence, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        // A clip asked for with it may hold the camera first; the photo itself takes a few seconds.
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try { await take(occurrence, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new TimeoutException("The photo took more than 45 seconds."); }
    }
}
#endif
