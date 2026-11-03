//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Chat;

/// <summary>What writes down a voice recording (Emgu.LiteRT.LM.Maui's <c>SpeechRecognizer</c>).</summary>
public enum SpeechEngine
{
    /// <summary>The platform's speech recognizer: Android's or Apple's.</summary>
    System,
    /// <summary>NVIDIA's Parakeet-TDT-0.6B-v3 on LiteRT-LM, on the device (Emgu.LiteRT.LM.Models' <c>ParakeetTdt06BV3</c>).</summary>
    Parakeet,
    /// <summary>Alibaba Qwen's Qwen3-ASR-0.6B on LiteRT-LM, on the device (Emgu.LiteRT.LM.Models' <c>Qwen3Asr06B</c>).</summary>
    Qwen3Asr
}

/// <summary>
/// Choosing what writes down a recording: the on-device model chosen (Parakeet or Qwen3-ASR) when it has the language
/// spoken, else the other on-device model when it is downloaded and has it (Parakeet chosen, Japanese spoken: Qwen3-ASR),
/// else the platform's recognizer - or, where there is none (Windows), whichever model has the language. Parakeet has 25
/// European languages, Qwen3-ASR 30 including Chinese, Japanese and Korean. And reading a recording's samples for it.
/// </summary>
public static class SpeechEngines
{
    /// <summary>The settings key of the choice ("system", "parakeet" or "qwen3_asr"; absent: the platform's where there is one).</summary>
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

    /// <summary>Qwen3-ASR-0.6B's languages (ISO 639-1, "yue" for Cantonese, "fil" for Filipino), from Qwen's model card. It detects which is spoken.</summary>
    public static IReadOnlyList<string> Qwen3AsrLanguages => Qwen3Asr06B.Languages;

    /// <summary>Whether Qwen3-ASR has a language (any region of it).</summary>
    /// <param name="tag">A BCP-47 tag, e.g. "zh-CN"</param>
    public static bool Qwen3AsrCovers(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        string language = VoiceLanguages.Resolve(tag, null).Split('-')[0];
        return Qwen3AsrLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The on-device models, in the order they are tried.</summary>
    public static readonly IReadOnlyList<SpeechEngine> Models = new[] { SpeechEngine.Parakeet, SpeechEngine.Qwen3Asr };

    /// <summary>Whether an engine has a language: the platform's always says it does (it tells when it doesn't).</summary>
    /// <param name="engine">The engine</param>
    /// <param name="tag">A BCP-47 tag</param>
    public static bool Covers(SpeechEngine engine, string tag)
    {
        switch (engine)
        {
            case SpeechEngine.Parakeet: return ParakeetCovers(tag);
            case SpeechEngine.Qwen3Asr: return Qwen3AsrCovers(tag);
            default: return true;
        }
    }

    /// <summary>
    /// What writes down a recording: the on-device model chosen when it has the language spoken; else the other one if it
    /// is downloaded and has it; else the platform's recognizer; where there is no platform recognizer, whichever model has
    /// the language (downloaded when it is used); null when nothing can.
    /// </summary>
    /// <param name="choice">The user's choice</param>
    /// <param name="systemSupported">Whether the platform's recognizer can write down recordings here</param>
    /// <param name="modelsAvailable">Whether the on-device models can run here (their native library is in the app)</param>
    /// <param name="isDownloaded">Whether a model is downloaded, or null for none</param>
    /// <param name="voiceLanguage">The language chosen in the settings, or null / empty for the device's</param>
    /// <param name="deviceLanguage">The device's language</param>
    public static SpeechEngine? Choose(SpeechEngine choice, bool systemSupported, bool modelsAvailable, Func<SpeechEngine, bool> isDownloaded,
        string voiceLanguage, string deviceLanguage)
    {
        string language = VoiceLanguages.Resolve(voiceLanguage, deviceLanguage);
        if (modelsAvailable && (choice != SpeechEngine.System || !systemSupported))
        {
            if (choice != SpeechEngine.System && Covers(choice, language))
                return choice;
            foreach (SpeechEngine model in Models)
                if (model != choice && Covers(model, language) && isDownloaded != null && isDownloaded(model))
                    return model;
            if (!systemSupported)
                foreach (SpeechEngine model in Models)
                    if (Covers(model, language))
                        return model;
        }
        return systemSupported ? SpeechEngine.System : (SpeechEngine?)null;
    }

    /// <summary>The choice in a setting's value: Parakeet for "parakeet", Qwen3-ASR for "qwen3_asr", else the platform's.</summary>
    /// <param name="value">The setting's value, or null</param>
    /// <param name="systemSupported">Whether the platform's recognizer works here (without one, Parakeet is the default)</param>
    public static SpeechEngine Parse(string value, bool systemSupported)
    {
        if (string.IsNullOrEmpty(value))
            return systemSupported ? SpeechEngine.System : SpeechEngine.Parakeet;
        if (string.Equals(value, "parakeet", StringComparison.OrdinalIgnoreCase))
            return SpeechEngine.Parakeet;
        if (string.Equals(value, "qwen3_asr", StringComparison.OrdinalIgnoreCase))
            return SpeechEngine.Qwen3Asr;
        return SpeechEngine.System;
    }

    /// <summary>A choice's setting value.</summary>
    public static string ToSetting(SpeechEngine engine) =>
        engine == SpeechEngine.Parakeet ? "parakeet" : engine == SpeechEngine.Qwen3Asr ? "qwen3_asr" : "system";

    /// <summary>The peak <see cref="Boost"/> raises a quiet recording to (-6 dBFS).</summary>
    public const float BoostPeak = 0.5f;

    /// <summary>The most <see cref="Boost"/> amplifies (40 dB), so a recording of the room alone isn't blown up to full scale.</summary>
    public const float MaxBoost = 100f;

    /// <summary>
    /// The samples raised to <see cref="BoostPeak"/> when they are quieter, at most <see cref="MaxBoost"/> times, for the
    /// speech models on the device: a Galaxy S10 far from the speakers recorded a meeting's speech peaking at -43 dBFS, and
    /// Qwen3-ASR (on a Mac, 2026-11-03) got 46% of its Japanese characters wrong as recorded, 21% boosted 100 times
    /// (Chinese 5% and English 3% either way). Recordings already loud enough come back as they are (the same array).
    /// </summary>
    /// <param name="samples">Mono samples in [-1, 1]</param>
    public static float[] Boost(float[] samples)
    {
        if (samples == null || samples.Length == 0) return samples;
        float peak = 0;
        foreach (float s in samples)
            peak = Math.Max(peak, Math.Abs(s));
        if (peak <= 0) return samples;
        float gain = Math.Min(MaxBoost, BoostPeak / peak);
        if (gain <= 1.5f) return samples;
        var boosted = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            boosted[i] = samples[i] * gain;
        return boosted;
    }

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
