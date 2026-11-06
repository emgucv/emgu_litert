using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // A scheduled task's notification opens its saved chat (ChatLaunch): the app started by it, or brought back.
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ChatLaunch.HandleIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        ChatLaunch.HandleIntent(intent);
    }
}
