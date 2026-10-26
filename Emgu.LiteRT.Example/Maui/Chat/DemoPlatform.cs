// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
namespace Maui.Demo.Lite;

// The platform this build runs on, for Maui.Demo.Lite.Core's per-platform choices (the model catalog).
internal static class DemoPlatform
{
    public static ChatPlatform Current =>
#if ANDROID
        ChatPlatform.Android;
#elif MACCATALYST
        ChatPlatform.MacCatalyst;
#elif IOS
        ChatPlatform.iOS;
#else
        ChatPlatform.Windows;
#endif
}
