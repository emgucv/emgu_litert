// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// Settings > "Voice language": the language voice messages are recognized in (Emgu.LiteRT.LM.Maui's SpeechRecognizer) -
// the device's language unless chosen. Shown where the recognizer works.
internal sealed class VoiceLanguageFeature : DemoFeature
{
    public VoiceLanguageFeature()
    {
        SpeechRecognizer.Language = AppSettings.Chat.VoiceLanguage;
        SpeechRecognizer.ChooseLanguageHint = "Choose the language in Settings > Voice language.";
    }

    public override string Name => "voice_language";

    public override string Title => "Voice language";

    public override bool PersistEnabled => false;

    public override View BuildSettingsSection()
    {
        string voiceLanguage = AppSettings.Chat.VoiceLanguage;
        var picker = new Picker
        {
            Title = "Voice language",
            FontFamily = Theme.BodyFont,
            FontSize = 15,
            TextColor = Theme.PrimaryText,
            TitleColor = Theme.SecondaryText
        };
        picker.Items.Add("Device language");
        foreach (var language in VoiceLanguages.All)
            picker.Items.Add(language.Name);
        picker.SelectedIndex = Math.Max(0, VoiceLanguages.All.ToList().FindIndex(l => string.Equals(l.Tag, voiceLanguage, StringComparison.OrdinalIgnoreCase)) + 1);
        picker.SelectedIndexChanged += (s, e) =>
        {
            string tag = picker.SelectedIndex <= 0 ? string.Empty : VoiceLanguages.All[picker.SelectedIndex - 1].Tag;
            AppSettings.Chat.VoiceLanguage = tag;
            SpeechRecognizer.Language = tag;
        };
        SemanticProperties.SetDescription(picker, "The language you speak in voice messages");
        return new VerticalStackLayout
        {
            Spacing = 2,
            IsVisible = SpeechRecognizer.IsSupported,
            Children =
            {
                new Label { Text = "Voice language", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText },
                picker,
                Note("The language of your voice messages, for the speech recognizer that writes them down - the words models that can't hear audio (Qwen, your own server) are given, and the transcript under a voice message. By default it listens in the device's language, so choose yours when it differs. The phone may need to download that language for offline use.")
            }
        };
    }
}
