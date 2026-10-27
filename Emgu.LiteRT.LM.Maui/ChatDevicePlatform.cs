//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>The platform this build runs on, for <see cref="ChatModelCatalog.For"/>.</summary>
    public static class ChatDevicePlatform
    {
        /// <summary>The platform this build runs on.</summary>
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
}
