//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>Sets a MAUI app up for the chat controls.</summary>
    public static class MauiAppBuilderExtensions
    {
        /// <summary>
        /// Register the handlers the chat controls need: <see cref="ChatPromptEditor"/>'s on Mac Catalyst (Enter sends) and
        /// <c>InlineVideoView</c>'s on Android. Call it in MauiProgram: <c>builder.UseMauiApp&lt;App&gt;().UseLiteRtChat()</c>.
        /// </summary>
        /// <param name="builder">The app builder</param>
        /// <returns>The builder</returns>
        public static MauiAppBuilder UseLiteRtChat(this MauiAppBuilder builder)
        {
#if MACCATALYST
            builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<ChatPromptEditor, ChatPromptEditorHandler>());
#elif ANDROID
            builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<InlineVideoView, InlineVideoHandler>());
#endif
            return builder;
        }
    }
}
