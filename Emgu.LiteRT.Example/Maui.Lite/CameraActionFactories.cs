// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using System.Text.Json;
using Emgu.LiteRT.LM.Agents.Events;

namespace Maui.Demo.Lite;

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
        if (string.IsNullOrWhiteSpace(ReplySpeech.CleanForSpeech(text))) throw new ArgumentException("The spoken message needs pronounceable text.");
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
#endif
