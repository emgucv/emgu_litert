// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
namespace Maui.Demo.Lite;

// Settings > "Developer mode" (off by default, Preferences litert_lm_chat_developer_mode): shows what the models see, for checking and tuning them -
// while jumping jacks are counted, the camera preview draws the body pose as the "Body pose" page does, and the card says which position was read.
public partial class LiteRtLmChatPage
{
    private const string DeveloperModePreference = "litert_lm_chat_developer_mode";
    private static bool? _developerMode;

    private static bool DeveloperMode
    {
        get
        {
            if (_developerMode == null)
            {
                try { _developerMode = Preferences.Default.Get(DeveloperModePreference, false); } catch (Exception) { _developerMode = false; }
            }
            return _developerMode.Value;
        }
        set
        {
            _developerMode = value;
            try { Preferences.Default.Set(DeveloperModePreference, value); } catch (Exception) { }
        }
    }

    private View BuildDeveloperSection()
    {
        var toggle = new Switch { IsToggled = DeveloperMode, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
        toggle.Toggled += (_, e) => DeveloperMode = e.Value;
        SemanticProperties.SetDescription(toggle, "Developer mode");
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(new Label { Text = "Developer mode", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center }, 0, 0);
        row.Add(toggle, 1, 0);
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                row,
                new Label
                {
                    Text = "Shows what the models see. While jumping jacks are counted, the camera preview draws the body tracking and the card shows the position read.",
                    FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText
                }
            }
        };
    }
}
#endif
