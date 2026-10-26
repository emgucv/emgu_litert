// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// The base of the demo's own features for Emgu.LiteRT.LM.Maui's ChatPage: the library's ChatFeature with the demo's host
// (IDemoHost), the message being sent (Context), whether the page is shown, and the app's window coming to the front or
// leaving it. Each feature registers itself as a service, so the others find it (Host.Services.Get<CameraFeature>()).
internal abstract class DemoFeature : ChatFeature
{
    private Window _window;

    // The demo's page (null before the feature is attached).
    protected IDemoHost Demo => Host as IDemoHost;

    // The message being sent: its words and attachments, read by the tools made for it (null before the first).
    protected ChatMessageContext Context { get; private set; }

    protected string Prompt => Context?.Prompt ?? string.Empty;

    // Whether the page is shown (between OnAppearing and OnDisappearing).
    protected bool PageVisible { get; private set; }

    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        host.Page.PropertyChanged += OnPagePropertyChanged;
        WatchWindow();
    }

    public override void OnDetached()
    {
        if (Host != null)
            Host.Page.PropertyChanged -= OnPagePropertyChanged;
        if (_window != null)
        {
            _window.Resumed -= OnWindowResumed;
            _window.Stopped -= OnWindowStopped;
            _window = null;
        }
        base.OnDetached();
    }

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

    // Window.Resumed: the app returns to the foreground (on desktops also when its window is activated); Stopped: it
    // leaves it. The page's Window is set after it is pushed, so this follows the property.
    private void OnPagePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Page.Window))
            WatchWindow();
    }

    private void WatchWindow()
    {
        Window window = Host?.Page.Window;
        if (window == _window)
            return;
        if (_window != null)
        {
            _window.Resumed -= OnWindowResumed;
            _window.Stopped -= OnWindowStopped;
        }
        _window = window;
        if (_window != null)
        {
            _window.Resumed += OnWindowResumed;
            _window.Stopped += OnWindowStopped;
        }
    }

    private void OnWindowResumed(object sender, EventArgs e) => OnAppResumed();

    private void OnWindowStopped(object sender, EventArgs e) => OnAppStopped();

    // The app came to the front, or left it.
    protected virtual void OnAppResumed()
    {
    }

    protected virtual void OnAppStopped()
    {
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
