// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// The base of the demo's own features for Emgu.LiteRT.LM.Maui's ChatPage: the library's ChatFeature with the message
// being sent (Context), whether the page is shown, and the demo's settings rows. Each feature registers itself as a
// service, so the others find it (Host.Services.Get<ModelSettingsFeature>()).
internal abstract class DemoFeature : ChatFeature
{
    // The message being sent: its words and attachments, read by the tools made for it (null before the first).
    protected ChatMessageContext Context { get; private set; }

    protected string Prompt => Context?.Prompt ?? string.Empty;

    // Whether the page is shown (between OnAppearing and OnDisappearing).
    protected bool PageVisible { get; private set; }

    public override Task PrepareAsync(ChatMessageContext context)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public override void OnAppearing()
    {
        PageVisible = true;
    }

    public override void OnDisappearing()
    {
        PageVisible = false;
    }

    protected static string JoinInstructions(params string[] parts)
    {
        string joined = string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p)));
        return joined.Length > 0 ? joined : null;
    }

    // A labelled row with a switch, as the demo's settings draw them.
    protected static Grid SwitchRow(string text, Switch toggle)
    {
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(new Label { Text = text, FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center }, 0, 0);
        row.Add(toggle, 1, 0);
        return row;
    }

    protected static Label Note(string text) => new Label { Text = text, FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };

    protected static Label SectionLabel(string text) =>
        new Label { Text = text, FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) };
}
