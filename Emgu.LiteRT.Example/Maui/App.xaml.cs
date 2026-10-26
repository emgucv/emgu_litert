namespace Maui.Demo.Lite;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
        // The demo's pages are drawn in fixed light colours (Theme.cs); the chat page (Emgu.LiteRT.LM.Maui's ChatPage, which
        // follows the system theme) is kept light with them.
        UserAppTheme = AppTheme.Light;
        Emgu.CV.Platform.Maui.MauiInvoke.Init();
        Emgu.TF.Lite.Platform.Maui.MauiInvoke.Init();
        // Debug builds: log UI thread stalls (see UiStallWatchdog).
        UiStallWatchdog.Start();

	}

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
