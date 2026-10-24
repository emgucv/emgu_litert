//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

/// <summary>
/// The language of a text, as a two-letter code ("es", "zh"), or null when it isn't known. A script check
/// (<see cref="ReplySpeech.DetectLanguage"/>) settles Chinese, Japanese, Korean, Russian, Arabic and the other
/// non-Latin scripts; for Latin script - Spanish, French, German, English... - which it can't tell apart, Android
/// asks ML Kit's language identification (the model, ~1 MB, is part of the app and runs on the device; undetermined
/// text is "und"). The other platforms don't tell Latin-script languages apart yet.
/// </summary>
internal static class TextLanguage
{
    public static async Task<string> DetectAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        string script = ReplySpeech.DetectLanguage(text);
        if (script != null)
            return script;
#if ANDROID
        return await DetectWithMlKitAsync(text);
#elif IOS || MACCATALYST
        await Task.CompletedTask;
        return DetectWithApple(text);
#else
        await Task.CompletedTask;
        return null;
#endif
    }

#if IOS || MACCATALYST
    // Apple's NaturalLanguage recognizer (nothing to ship): the likeliest language if it is at least 60% sure.
    private static string DetectWithApple(string text)
    {
        try
        {
            using var recognizer = new NaturalLanguage.NLLanguageRecognizer();
            recognizer.Process(text);
            var best = recognizer.GetNativeLanguageHypotheses(1);
            foreach (var pair in best)
                if (pair.Value is Foundation.NSNumber probability && probability.DoubleValue >= 0.6)
                    return LanguageText.TwoLetter(pair.Key.ToString());
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
#endif

#if ANDROID
    private static Xamarin.Google.MLKit.NL.LanguageId.ILanguageIdentifier _identifier;

    private static async Task<string> DetectWithMlKitAsync(string text)
    {
        try
        {
            _identifier ??= Xamarin.Google.MLKit.NL.LanguageId.LanguageIdentification.GetClient(
                new Xamarin.Google.MLKit.NL.LanguageId.LanguageIdentificationOptions.Builder().SetConfidenceThreshold(0.5f).Build());
            Java.Lang.Object tag = await Android.Gms.Extensions.TasksExtensions.AsAsync<Java.Lang.Object>(_identifier.IdentifyLanguage(text));
            return LanguageText.TwoLetter(tag?.ToString());
        }
        catch (Exception)
        {
            return null; // The voice falls back to the device's default.
        }
    }
#endif
}
