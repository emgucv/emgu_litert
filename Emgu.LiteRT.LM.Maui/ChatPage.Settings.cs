//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    // The settings (a sheet, or the side panel on wide screens) and the chips above the message box.
    public partial class ChatPage
    {
        private VerticalStackLayout _settingsView;
        private FlexLayout _chips;
        private ToggleChip _thinkChip;
        private readonly List<(ChatChip Chip, ToggleChip View)> _featureChips = new List<(ChatChip, ToggleChip)>();

        /// <summary>Settings > Developer mode: a notice after each reply with the tools and instructions the model was given.</summary>
        public bool DeveloperMode => _options.Settings.Get("developer_mode", false);

        // The settings, built once: the page's own, then each feature's section.
        private void BuildSettingsView()
        {
            var view = new VerticalStackLayout { Spacing = 6 };
            view.Children.Add(SwitchRow("Read replies aloud", null, _chatView.ReadAloud, on =>
            {
                _chatView.ReadAloud = on;
                _options.Settings.Set("read_aloud", on);
            }));
            if (_options.History != null && _options.ShowPrivateChat)
            {
                view.Children.Add(Divider());
                view.Children.Add(BuildPrivateChatSection());
            }
            foreach (ChatFeature feature in _features)
            {
                View section;
                try { section = feature.BuildSettingsSection(); }
                catch (Exception ex) { section = Note(feature.Title + ": " + ex.Message); }
                if (section == null)
                    continue;
                view.Children.Add(Divider());
                view.Children.Add(section);
            }
            view.Children.Add(Divider());
            view.Children.Add(SwitchRow("Developer mode", "Shows, after each reply, the tools and instructions the model was given.",
                DeveloperMode, on => _options.Settings.Set("developer_mode", on)));
            if (_settingsView?.Parent is Layout parent)
            {
                int index = parent.Children.IndexOf(_settingsView);
                parent.Children.Remove(_settingsView);
                parent.Children.Insert(index, view);
            }
            _settingsView = view;
        }

        private async void OnSettingsTapped()
        {
            await _sheet.ShowAsync("Settings", _settingsView, "Done");
            // Back into the side panel, if it was there.
            if (_wide == true && _sideContent != null && _settingsView.Parent == null)
                _sideContent.Children.Add(_settingsView);
        }

        // The chips: Think (for models that can think), then the features'.
        private void BuildChips()
        {
            _featureChips.Clear();
            var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(0, 0, 0, 2) };
            _thinkChip = new ToggleChip("Think") { Margin = new Thickness(0, 0, 6, 6), IsToggled = _chatView.ThinkingEnabled };
            _thinkChip.Toggled += (s, e) =>
            {
                _chatView.ThinkingEnabled = e.Value;
                _options.Settings.Set("think", e.Value);
            };
            SemanticProperties.SetHint(_thinkChip, "Let the model think before it answers");
            chips.Children.Add(_thinkChip);
            foreach (ChatFeature feature in _features)
            {
                ChatChip chip;
                try { chip = feature.BuildComposerChip(); }
                catch (Exception) { chip = null; }
                if (chip == null)
                    continue;
                var toggle = new ToggleChip(chip.Text) { Margin = new Thickness(0, 0, 6, 6), IsToggled = chip.IsOn() };
                toggle.Toggled += (s, e) =>
                {
                    chip.Toggled?.Invoke(e.Value);
                    UpdateChips();
                };
                _featureChips.Add((chip, toggle));
                chips.Children.Add(toggle);
            }
            _chips = chips;
            _chatView.Toolbar = chips;
            UpdateChips();
        }

        // Which chips show, and their state, for the model and the features' state.
        private void UpdateChips()
        {
            if (_chips == null)
                return;
            _thinkChip.IsVisible = _options.ShowThinkToggle && _model?.CanThink == true;
            bool any = _thinkChip.IsVisible;
            foreach ((ChatChip chip, ToggleChip view) in _featureChips)
            {
                view.IsVisible = chip.IsVisible?.Invoke() ?? true;
                if (view.IsToggled != chip.IsOn())
                    view.IsToggled = chip.IsOn();
                any |= view.IsVisible;
            }
            _chatView.Toolbar = any ? _chips : null;
        }

        /// <summary>Build the settings and the chips again (e.g. after a feature's state changed what it shows).</summary>
        public void RefreshSettings()
        {
            BuildSettingsView();
            BuildChips();
        }
    }
}
