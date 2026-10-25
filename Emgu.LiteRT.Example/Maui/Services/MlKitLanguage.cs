//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if ANDROID
using System;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

/// <summary>
/// The language of Latin-script text on Android - Spanish, French, German, English..., which Emgu.LiteRT.LM.Maui's
/// <c>TextLanguage</c> script check can't tell apart - from ML Kit's language identification (the model, ~1 MB, is part
/// of the app and runs on the device). MauiProgram sets it as <c>TextLanguage.IdentifyLatin</c>.
/// </summary>
internal static class MlKitLanguage
{
    private static Xamarin.Google.MLKit.NL.LanguageId.ILanguageIdentifier _identifier;

    /// <returns>A language tag ("es", "zh-Latn"), "und" when undetermined, or null</returns>
    public static async Task<string> IdentifyAsync(string text)
    {
        try
        {
            _identifier ??= Xamarin.Google.MLKit.NL.LanguageId.LanguageIdentification.GetClient(
                new Xamarin.Google.MLKit.NL.LanguageId.LanguageIdentificationOptions.Builder().SetConfidenceThreshold(0.5f).Build());
            Java.Lang.Object tag = await Android.Gms.Extensions.TasksExtensions.AsAsync<Java.Lang.Object>(_identifier.IdentifyLanguage(text));
            return tag?.ToString();
        }
        catch (Exception)
        {
            return null; // The voice falls back to the device's default.
        }
    }
}
#endif
