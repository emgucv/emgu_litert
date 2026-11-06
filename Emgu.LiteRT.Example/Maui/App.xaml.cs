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
        // A scheduled task's answer tapped: the chat page shows (it opens the run's chat when it appears).
        Emgu.LiteRT.LM.Maui.ChatLaunch.OpenChatRequested += chatId => _ = ShowChatPageAsync();

	}

    private static async Task ShowChatPageAsync()
    {
        INavigation? navigation = Shell.Current?.Navigation;
        if (navigation == null)
            return;
        // The chat page already open, under another page (Storage...): back to it; else a new one.
        if (navigation.NavigationStack.Any(p => p is LiteRtLmChatPage))
        {
            while (navigation.NavigationStack.LastOrDefault() is not LiteRtLmChatPage)
                await navigation.PopAsync(false);
        }
        else
            await navigation.PushAsync(new LiteRtLmChatPage());
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
