//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Generic;
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

        /// <summary>
        /// True on a device that allows an app little memory: an iPhone or iPad with less than 5 GB (an iPhone 12's 4 GB
        /// allow ~2.2 GB, with the memory entitlements). Android phones weren't measured.
        /// </summary>
        public static bool LowMemory =>
#if IOS && !MACCATALYST
            Foundation.NSProcessInfo.ProcessInfo.PhysicalMemory < 5_000_000_000UL;
#else
            false;
#endif

        /// <summary>
        /// The catalog's models (<see cref="ChatModelCatalog.For"/>) this app can run: none when LiteRT-LM isn't in the app
        /// (<see cref="LiteRtLmInvoke.IsAvailable"/>; e.g. an iOS build from an Intel Mac, where LiteRT-LM can't be built), so
        /// the app offers only its remote models. On a <see cref="LowMemory"/> device, Gemma 4 prefills in small pieces.
        /// </summary>
        public static IList<ChatModelOption> AvailableModels() =>
            LiteRtLmInvoke.IsAvailable ? ChatModelCatalog.For(Current, LowMemory) : new List<ChatModelOption>();
    }
}
