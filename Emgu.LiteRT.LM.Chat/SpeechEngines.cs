//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Emgu.LiteRT.LM.Chat;

/// <summary>What writes down a voice recording (Emgu.LiteRT.LM.Maui's <c>SpeechRecognizer</c>).</summary>
public enum SpeechEngine
{
    /// <summary>The platform's speech recognizer: Android's or Apple's.</summary>
    System,
    /// <summary>NVIDIA's Parakeet-TDT-0.6B-v3 on LiteRT-LM, on the device (Emgu.LiteRT.LM.Models' <c>ParakeetTdt06BV3</c>).</summary>
    Parakeet
}

/// <summary>
/// Choosing what writes down a recording: Parakeet when it was chosen (or there is no other, as on Windows) and it has the
/// language spoken, else the platform's recognizer - Parakeet has 25 European languages, so Chinese or Japanese still
/// go to the platform's. And reading a recording's samples for it.
/// </summary>
public static class SpeechEngines
{
    /// <summary>The settings key of the choice ("system" or "parakeet"; absent: the platform's where there is one).</summary>
    public const string SettingKey = "speech_engine";

    /// <summary>
    /// Parakeet-TDT-0.6B-v3's languages (ISO 639-1), from NVIDIA's model card: Bulgarian, Croatian, Czech, Danish, Dutch,
    /// English, Estonian, Finnish, French, German, Greek, Hungarian, Italian, Latvian, Lithuanian, Maltese, Polish,
    /// Portuguese, Romanian, Slovak, Slovenian, Spanish, Swedish, Russian and Ukrainian. It detects which is spoken.
    /// </summary>
    public static readonly IReadOnlyList<string> ParakeetLanguages = new[]
    {
        "bg", "hr", "cs", "da", "nl", "en", "et", "fi", "fr", "de", "el", "hu", "it", "lv", "lt", "mt", "pl", "pt", "ro",
        "sk", "sl", "es", "sv", "ru", "uk"
    };

    /// <summary>Whether Parakeet has a language (any region of it).</summary>
    /// <param name="tag">A BCP-47 tag, e.g. "pt-BR"</param>
    public static bool ParakeetCovers(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        string language = VoiceLanguages.Resolve(tag, null).Split('-')[0];
        return ParakeetLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What writes down a recording: Parakeet when it was chosen, or when there is no platform recognizer, and it is here
    /// and has the language spoken; else the platform's recognizer; null when neither can.
    /// </summary>
    /// <param name="choice">The user's choice</param>
    /// <param name="systemSupported">Whether the platform's recognizer can write down recordings here</param>
    /// <param name="parakeetAvailable">Whether Parakeet can run here (its native library is in the app)</param>
    /// <param name="voiceLanguage">The language chosen in the settings, or null / empty for the device's</param>
    /// <param name="deviceLanguage">The device's language</param>
    public static SpeechEngine? Choose(SpeechEngine choice, bool systemSupported, bool parakeetAvailable, string voiceLanguage, string deviceLanguage)
    {
        bool wantParakeet = choice == SpeechEngine.Parakeet || !systemSupported;
        if (wantParakeet && parakeetAvailable && ParakeetCovers(VoiceLanguages.Resolve(voiceLanguage, deviceLanguage)))
            return SpeechEngine.Parakeet;
        return systemSupported ? SpeechEngine.System : (SpeechEngine?)null;
    }

    /// <summary>The choice in a setting's value: Parakeet for "parakeet", else the platform's.</summary>
    /// <param name="value">The setting's value, or null</param>
    /// <param name="systemSupported">Whether the platform's recognizer works here (without one, Parakeet is the default)</param>
    public static SpeechEngine Parse(string value, bool systemSupported)
    {
        if (string.IsNullOrEmpty(value))
            return systemSupported ? SpeechEngine.System : SpeechEngine.Parakeet;
        return string.Equals(value, "parakeet", StringComparison.OrdinalIgnoreCase) ? SpeechEngine.Parakeet : SpeechEngine.System;
    }

    /// <summary>A choice's setting value.</summary>
    public static string ToSetting(SpeechEngine engine) => engine == SpeechEngine.Parakeet ? "parakeet" : "system";

    /// <summary>
    /// A 16-bit PCM WAV recording's samples, mono, in [-1, 1] (a stereo one is mixed down); its other chunks (Apple's
    /// recorder writes a "FLLR" or "LIST" one before the samples) are skipped.
    /// </summary>
    /// <param name="wav">The WAV file</param>
    /// <param name="sampleRate">Its sample rate</param>
    /// <exception cref="InvalidDataException">It isn't a 16-bit PCM WAV file, or has no samples</exception>
    public static float[] WavSamples(byte[] wav, out int sampleRate)
    {
        if (wav == null || wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException("The recording isn't a WAV file.");
        int channels = 0, bits = 0;
        sampleRate = 0;
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            if (size < 0) break;
            if (id == "fmt " && pos + 24 <= wav.Length)
            {
                // PCM, or WAVE_FORMAT_EXTENSIBLE (0xFFFE, as macOS's afconvert and Windows write) whose sub-format is PCM.
                ushort format = BitConverter.ToUInt16(wav, pos + 8);
                if (format == 0xFFFE && size >= 40 && pos + 34 <= wav.Length)
                    format = BitConverter.ToUInt16(wav, pos + 32);
                if (format != 1)
                    throw new InvalidDataException("The recording isn't PCM.");
                channels = BitConverter.ToInt16(wav, pos + 10);
                sampleRate = BitConverter.ToInt32(wav, pos + 12);
                bits = BitConverter.ToInt16(wav, pos + 22);
            }
            else if (id == "data")
            {
                if (bits != 16 || channels < 1 || sampleRate <= 0)
                    throw new InvalidDataException("The recording isn't 16-bit PCM.");
                size = Math.Min(size, wav.Length - pos - 8);
                int frames = size / (2 * channels);
                var samples = new float[frames];
                for (int i = 0; i < frames; i++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++)
                        sum += BitConverter.ToInt16(wav, pos + 8 + 2 * (i * channels + c)) / 32768f;
                    samples[i] = sum / channels;
                }
                return samples;
            }
            pos += 8 + size + (size & 1);
        }
        throw new InvalidDataException("The recording has no audio data.");
    }
}
