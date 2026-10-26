//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------


using Emgu.LiteRT.LM.Maui;
using Microsoft.Extensions.Logging;

namespace Maui.Demo.Lite;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			// The chat controls' handlers: ChatPromptEditor's on Mac Catalyst (Enter sends), InlineVideoView's on Android.
			.UseLiteRtChat()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				fonts.AddFont("Inter-Light.ttf", "InterLight");
				fonts.AddFont("Inter-Regular.ttf", "InterRegular");
				fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
				fonts.AddFont("MaterialSymbolsOutlined.ttf", "MaterialSymbols");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

#if ANDROID
        // Latin-script languages for the reading voice and the translations: ML Kit (Emgu.LiteRT.LM.Maui uses Apple's recognizer on iOS and Mac Catalyst).
        TextLanguage.IdentifyLatin = MlKitLanguage.IdentifyAsync;
#endif

		return builder.Build();
	}
}
