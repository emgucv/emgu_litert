//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.Models.CV.Maui;

/// <summary>Sets a MAUI app up for the live camera.</summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Register the handler <see cref="LiveCameraPreview"/> needs on iOS (<c>LiveCameraPreviewHandler</c>: the frames through a display layer,
    /// without a UIImage each). Call it in MauiProgram: <c>builder.UseMauiApp&lt;App&gt;().UseLiveCamera()</c>. Nothing to register on the other
    /// platforms, and without it the preview still works through MAUI's Image handler.
    /// </summary>
    /// <param name="builder">The app builder</param>
    /// <returns>The builder</returns>
    public static MauiAppBuilder UseLiveCamera(this MauiAppBuilder builder)
    {
#if IOS && !MACCATALYST
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<LiveCameraPreview, LiveCameraPreviewHandler>());
#endif
        return builder;
    }
}
