// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
namespace Maui.Demo.Lite;

// Settings > "Developer mode" (off by default, Preferences litert_lm_chat_developer_mode): shows what the models see, for checking and tuning them -
// while an exercise is counted, the camera preview draws the body pose as the "Body pose" page does, and the card says which position was read.
public partial class LiteRtLmChatPage
{
    private static bool? _developerMode;

    private static bool DeveloperMode
    {
        get
        {
            if (_developerMode == null)
            {
                _developerMode = AppSettings.Chat.DeveloperMode;
            }
            return _developerMode.Value;
        }
        set
        {
            _developerMode = value;
            AppSettings.Chat.DeveloperMode = value;
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
                    Text = "Shows what the models see. While an exercise is counted, the camera preview draws the body tracking and the card shows the position read.",
                    FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText
                }
            }
        };
    }
}
