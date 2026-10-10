using Foundation;

namespace Maui.Demo.Lite;

// Named by UISceneDelegateClassName in Info.plist: adopts the UIScene life cycle, which apps built with the
// iOS 27 SDK must use or UIKit refuses to launch them.
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
