//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.CV;

/// <summary>
/// The base of this package's features: the library's <see cref="ChatFeature"/> with the message being sent
/// (<see cref="Context"/>), whether the page is shown, and the settings' building blocks in the library's styles.
/// </summary>
public abstract class CvFeature : ChatFeature
{
    /// <summary>The message being sent: its words and attachments, read by the tools made for it (null before the first).</summary>
    protected ChatMessageContext Context { get; private set; }

    /// <summary>The message being sent, or "".</summary>
    protected string Prompt => Context?.Prompt ?? string.Empty;

    /// <summary>Whether the page is shown (between OnAppearing and OnDisappearing).</summary>
    protected bool PageVisible { get; private set; }

    /// <inheritdoc/>
    public override Task PrepareAsync(ChatMessageContext context)
    {
        Context = context;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override void OnAppearing()
    {
        PageVisible = true;
    }

    /// <inheritdoc/>
    public override void OnDisappearing()
    {
        PageVisible = false;
    }

    /// <summary>The non-empty parts, each a paragraph; null if none.</summary>
    /// <param name="parts">The parts</param>
    protected static string JoinInstructions(params string[] parts)
    {
        string joined = string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p)));
        return joined.Length > 0 ? joined : null;
    }

    /// <summary>A labelled row with a switch.</summary>
    /// <param name="text">The label</param>
    /// <param name="toggle">The switch</param>
    protected static Grid SwitchRow(string text, Switch toggle)
    {
        var label = new Label { Text = text, VerticalOptions = LayoutOptions.Center };
        ChatResources.ApplyStyle(label, "ChatSectionTextStyle");
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(label, 0, 0);
        row.Add(toggle, 1, 0);
        return row;
    }

    /// <summary>A note under a setting.</summary>
    /// <param name="text">The note</param>
    protected static Label Note(string text) => ChatPage.Note(text);

    /// <summary>A setting's label on a line of its own.</summary>
    /// <param name="text">The label</param>
    protected static Label SectionLabel(string text)
    {
        var label = new Label { Text = text, Margin = new Thickness(0, 6, 0, 0) };
        ChatResources.ApplyStyle(label, "ChatSectionTextStyle");
        return label;
    }
}

/// <summary>Views and colours in the library's theme, for this package's cards and panels.</summary>
internal static class CvViews
{
    /// <summary>A theme colour by name ("ChatPrimaryText"), for the app's current theme.</summary>
    public static Color Color(string name) => ChatResources.ThemeColor(name);

    /// <summary>Run an action when a view is tapped.</summary>
    public static void OnTap(this View view, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => action();
        view.GestureRecognizers.Add(tap);
    }

    /// <summary>A rounded card on the page's surface colour.</summary>
    public static Border Card(View content, double padding = 16)
    {
        var card = new Border
        {
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            Padding = new Thickness(padding),
            Content = content
        };
        ChatResources.SetThemeColor(card, VisualElement.BackgroundColorProperty, "ChatSurface");
        return card;
    }

    /// <summary>An image source over encoded image bytes.</summary>
    public static ImageSource ToImageSource(byte[] bytes) => ImageSource.FromStream(() => new MemoryStream(bytes));
}

/// <summary>The sounds this package carries as embedded resources: the alert chime, the camera click, white noise and the lullaby.</summary>
internal static class CvAssets
{
    /// <summary>Open an embedded sound by its file name ("visual_chime.wav").</summary>
    public static Task<Stream> OpenAsync(string name)
    {
        Stream stream = typeof(CvAssets).Assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException("The sound " + name + " isn't in " + typeof(CvAssets).Assembly.GetName().Name);
        return Task.FromResult(stream);
    }
}
