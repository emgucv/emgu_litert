//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Settings > "Voice language": the language voice messages are recognized in (<see cref="SpeechRecognizer"/>, for the
/// words a model that can't hear audio is given and the transcript under a voice message) - the device's language
/// unless chosen (<see cref="VoiceLanguages"/>; the "voice_language" setting). Shown where the recognizer works. The
/// recognizer's errors say where to choose the language.
/// </summary>
public sealed class VoiceLanguageFeature : ChatFeature
{
    /// <summary>The settings key of the chosen language's tag (empty for the device's).</summary>
    public const string SettingKey = "voice_language";

    /// <inheritdoc/>
    public override string Name => "voice_language";

    /// <inheritdoc/>
    public override string Title => "Voice language";

    /// <summary>Always on (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        SpeechRecognizer.Language = host.Settings.Get(SettingKey, string.Empty) ?? string.Empty;
        SpeechRecognizer.ChooseLanguageHint = "Choose the language in Settings > Voice language.";
    }

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        string voiceLanguage = Host.Settings.Get(SettingKey, string.Empty) ?? string.Empty;
        var picker = new Picker { Title = "Voice language", FontSize = 15 };
        ChatResources.SetThemeColor(picker, Picker.TextColorProperty, "ChatPrimaryText");
        ChatResources.SetThemeColor(picker, Picker.TitleColorProperty, "ChatSecondaryText");
        picker.Items.Add("Device language");
        foreach (var language in VoiceLanguages.All)
            picker.Items.Add(language.Name);
        picker.SelectedIndex = Math.Max(0, VoiceLanguages.All.ToList().FindIndex(l => string.Equals(l.Tag, voiceLanguage, StringComparison.OrdinalIgnoreCase)) + 1);
        picker.SelectedIndexChanged += (s, e) =>
        {
            string tag = picker.SelectedIndex <= 0 ? string.Empty : VoiceLanguages.All[picker.SelectedIndex - 1].Tag;
            Host.Settings.Set(SettingKey, tag);
            SpeechRecognizer.Language = tag;
        };
        SemanticProperties.SetDescription(picker, "The language you speak in voice messages");
        var title = new Label { Text = "Voice language" };
        ChatResources.ApplyStyle(title, "ChatSectionTextStyle");
        return new VerticalStackLayout
        {
            Spacing = 2,
            IsVisible = SpeechRecognizer.IsSupported,
            Children =
            {
                title,
                picker,
                ChatPage.Note("The language of your voice messages, for the speech recognizer that writes them down - the words models that can't hear audio (Qwen, your own server) are given, and the transcript under a voice message. By default it listens in the device's language, so choose yours when it differs. The phone may need to download that language for offline use.")
            }
        };
    }
}
