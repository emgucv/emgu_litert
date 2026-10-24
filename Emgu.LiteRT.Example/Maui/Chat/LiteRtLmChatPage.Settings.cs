//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Emgu.LiteRT.LM.Chat.Sqlite;

// The settings view (a bottom sheet, or the side panel on wide screens); its sections come from the feature files.
namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        // Settings that are set once: a bottom sheet, or the side panel on wide screens.
        private void BuildSettingsView()
        {
            _readAloudSwitch = new Switch { IsToggled = false, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _readAloudSwitch.Toggled += (s, e) =>
            {
                if (!e.Value)
                    StopSpeaking();
            };
            var readAloudRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            readAloudRow.Add(new Label
            {
                Text = "Read replies aloud",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            readAloudRow.Add(_readAloudSwitch, 1, 0);
            // The language voice messages are recognized in (Emgu.LiteRT.LM.Maui's SpeechRecognizer): the device's
            // language unless chosen.
            string voiceLanguage = string.Empty;
            try { voiceLanguage = Preferences.Default.Get(VoiceLanguagePreference, string.Empty); } catch (Exception) { }
            SpeechRecognizer.Language = voiceLanguage;
            SpeechRecognizer.ChooseLanguageHint = "Choose the language in Settings > Voice language.";
            var voiceLanguagePicker = new Picker
            {
                Title = "Voice language",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                TitleColor = Theme.SecondaryText
            };
            voiceLanguagePicker.Items.Add("Device language");
            foreach (var language in VoiceLanguages.All)
                voiceLanguagePicker.Items.Add(language.Name);
            voiceLanguagePicker.SelectedIndex = Math.Max(0, VoiceLanguages.All.ToList().FindIndex(l => string.Equals(l.Tag, voiceLanguage, StringComparison.OrdinalIgnoreCase)) + 1);
            voiceLanguagePicker.SelectedIndexChanged += (s, e) =>
            {
                string tag = voiceLanguagePicker.SelectedIndex <= 0 ? string.Empty : VoiceLanguages.All[voiceLanguagePicker.SelectedIndex - 1].Tag;
                try { Preferences.Default.Set(VoiceLanguagePreference, tag); } catch (Exception) { }
                SpeechRecognizer.Language = tag;
            };
            SemanticProperties.SetDescription(voiceLanguagePicker, "The language you speak in voice messages");
            var voiceLanguageRow = new VerticalStackLayout
            {
                Spacing = 2,
                IsVisible = SpeechRecognizer.IsSupported,
                Children =
                {
                    new Label { Text = "Voice language", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText },
                    voiceLanguagePicker,
                    new Label
                    {
                        Text = "The language of your voice messages, for the speech recognizer that writes them down - the words models that can't hear audio (Qwen, your own server) are given, and the transcript under a voice message. By default it listens in the device's language, so choose yours when it differs. The phone may need to download that language for offline use.",
                        FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText
                    }
                }
            };
            _tavilyKeyEntry = new Entry
            {
                Placeholder = "Tavily API key (required for web search)",
                IsEnabled = false,
                IsPassword = true,
                FontFamily = Theme.BodyFont,
                FontSize = 14,
                TextColor = Theme.PrimaryText,
                PlaceholderColor = Theme.SecondaryText
            };
            _tavilyKeyStatus = new Label
            {
                Text = "Loading saved API key...",
                FontFamily = Theme.BodyFont,
                FontSize = 12,
                TextColor = Theme.SecondaryText
            };
            _saveTavilyKeyButton = new Button { Text = "Save API key", IsEnabled = false };
            _saveTavilyKeyButton.Clicked += (s, e) => OnTavilyKeyChanged();
            bool shareLocation = false;
            try { shareLocation = Preferences.Default.Get(ShareLocationPreference, false); } catch (Exception) { }
            _shareLocationSwitch = new Switch { IsToggled = shareLocation, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _shareLocationSwitch.Toggled += (s, e) =>
            {
                try { Preferences.Default.Set(ShareLocationPreference, e.Value); } catch (Exception) { }
            };
            var locationRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            locationRow.Add(new Label
            {
                Text = "Share my location",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            locationRow.Add(_shareLocationSwitch, 1, 0);
            bool memoryOn = true;
            try { memoryOn = Preferences.Default.Get(MemoryPreference, true); } catch (Exception) { }
            _memorySwitch = new Switch { IsToggled = memoryOn, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _memorySwitch.Toggled += (s, e) =>
            {
                try { Preferences.Default.Set(MemoryPreference, e.Value); } catch (Exception) { }
            };
            var memoryRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            memoryRow.Add(new Label
            {
                Text = "Remember things about me",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            memoryRow.Add(_memorySwitch, 1, 0);
            _memoryCountLabel = new Label
            {
                FontFamily = Theme.TitleFont,
                FontSize = 14,
                TextColor = Theme.Accent,
                Padding = new Thickness(0, 4),
                HorizontalOptions = LayoutOptions.Start
            };
            _memoryCountLabel.OnTap(OnMemoriesTapped);
            _ = RefreshMemoryCountAsync();
            _tavilyKeyEntry.Completed += (s, e) => OnTavilyKeyChanged();
            _tavilyKeyEntry.Unfocused += (s, e) => OnTavilyKeyChanged();
            _ = LoadTavilyKeyAsync();
            // How full the model's context window is, and compacting it by hand.
            _memoryLabel = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
            _compactButton = new Label
            {
                Text = "Compact now",
                FontFamily = Theme.TitleFont,
                FontSize = 14,
                TextColor = Theme.Accent,
                Padding = new Thickness(0, 4),
                HorizontalOptions = LayoutOptions.Start
            };
            SemanticProperties.SetDescription(_compactButton, "Summarize the earlier messages now");
            _compactButton.OnTap(OnCompactTapped);
            UpdateMemoryLabel();
            try { _contextSetting = Preferences.Default.Get(ContextWindowPreference, 0); } catch (Exception) { }
            if (Array.IndexOf(ContextWindowChoices, _contextSetting) < 0)
                _contextSetting = 0;
            var contextChips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (int choice in ContextWindowChoices)
            {
                int value = choice;
                ToggleChip chip = new ToggleChip(ContextWindowName(value)) { Margin = new Thickness(0, 0, 6, 6), IsToggled = value == _contextSetting };
                chip.Toggled += (s, e) => OnContextWindowChipToggled(value, e.Value);
                _contextChips.Add(chip);
                contextChips.Children.Add(chip);
            }
            _contextNote = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
            _contextSection = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = "Context window", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    contextChips,
                    _contextNote
                }
            };
            _settingsView = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    CpuThreadSettings.CreateView(ReloadCpuModelsAsync),
                    Theme.Divider(),
                    readAloudRow,
                    voiceLanguageRow,
                    Theme.Divider(),
                    new Label { Text = "Web search", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    new Label
                    {
                        Text = "Searching the web for news and recent events requires a Tavily API key from tavily.com. With \U0001F50E Web search on, Wikipedia, reading links you provide, weather and currency conversion work without a key. Your searches leave the device.",
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    _tavilyKeyEntry,
                    _saveTavilyKeyButton,
                    _tavilyKeyStatus,
                    Theme.Divider(),
                    locationRow,
                    new Label
                    {
                        Text = "Lets Gemma 4 look up your town and approximate position, for questions such as \"what's the weather here?\". Your device asks for permission the first time. The place name comes from Apple's or Google's map service, and web searches that use it leave the device.",
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    Theme.Divider(),
#if ANDROID
                    BuildCameraSoundSection(),
                    Theme.Divider(),
#endif
                    BuildAlertSoundsSection(),
                    Theme.Divider(),
                    BuildServerSection(),
                    Theme.Divider(),
                    BuildMcpSection(),
                    Theme.Divider(),
                    BuildEmbeddingModelSection(),
                    Theme.Divider(),
                    BuildPhotoSection(),
                    Theme.Divider(),
                    BuildChatSearchSection(),
                    Theme.Divider(),
                    BuildImageSection(),
                    Theme.Divider(),
                    BuildStorageSection(),
                    Theme.Divider(),
                    BuildPrivateChatSection(),
                    Theme.Divider(),
                    new Label { Text = "Memories about you", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    memoryRow,
                    new Label
                    {
                        Text = "Gemma 4 saves lasting facts you tell it - your name, preferences, plans - and knows them in later chats. They stay on this device; ask it to forget something, or delete it here.",
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    _memoryCountLabel,
                    BuildGoalsSection(),
                    Theme.Divider(),
                    new Label { Text = "Conversation memory", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    _memoryLabel,
                    _compactButton,
                    _contextSection,
                    Theme.Divider(),
                    BuildDeveloperSection()
                }
            };
        }
    }
}

#endif
