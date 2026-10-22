// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;

namespace Maui.Demo.Lite;

// The play_file alert action (TODO item 53): "play the lullaby when you see the baby stand up". The sound is named in the user's words, matched against the
// library when the rule is made (so a wrong name is refused with the names that exist, for the model to act on) and found again when it fires.
internal sealed class PlayFileFactory(AlertSoundLibrary library, Func<AlertSound, int, CancellationToken, Task> play) : IVisualActionFactory
{
    public VisualActionDescriptor Descriptor => VisualHostCatalog.PlayFile(library.Names());

    public VisualActionBinding Bind(JsonElement parameters)
    {
        VisualActionParameters.AllowOnly(parameters, "sound", "seconds");
        string name = VisualActionParameters.RequiredText(parameters, "sound", 60);
        int seconds = 0;
        if (parameters.TryGetProperty("seconds", out JsonElement value))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out seconds) || seconds < 1 || seconds > 300)
                throw new ArgumentException("seconds must be a whole number from 1 to 300.");
        }
        AlertSound sound = library.Find(name, out string problem) ?? throw new ArgumentException(problem);
        string json = seconds > 0 ? JsonSerializer.Serialize(new { sound = sound.Name, seconds }) : JsonSerializer.Serialize(new { sound = sound.Name });
        return new VisualActionBinding(new PlayFileAction(library, play, sound.Name, seconds), VisualActionParameters.Parse(json));
    }
}

internal sealed class PlayFileAction(AlertSoundLibrary library, Func<AlertSound, int, CancellationToken, Task> play, string name, int seconds) : IVisualAction
{
    public string Id => "play_file";

    public async Task ExecuteAsync(VisualEvent occurrence, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        AlertSound sound = library.Find(name, out string problem) ?? throw new InvalidOperationException(problem);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds > 0 ? seconds + 20 : 600));
        try { await play(sound, seconds, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new TimeoutException("The sound took longer than expected."); }
    }
}
