//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

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

// The chat page's layout: the views the constructor builds, and switching between the narrow and wide layouts.
namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    // The header: back, the model (a chip that opens the model list), saved chats, new chat, export, settings.
    private View BuildHeader()
    {
        _modelChipLabel = new Label
        {
            FontFamily = Theme.TitleFont,
            FontSize = 16,
            TextColor = Theme.PrimaryText,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center
        };
        // Spins in the model chip while the model loads.
        _chipSpinner = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            Color = Theme.Accent,
            WidthRequest = 16,
            HeightRequest = 16,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalOptions = LayoutOptions.Center
        };
        _modelChip = new Border
        {
            BackgroundColor = Theme.CardBackground,
            Stroke = Theme.RowBorder,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            Padding = new Thickness(14, 0, 8, 0),
            HeightRequest = 40,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 2,
                Children = { _modelChipLabel, _chipSpinner, Theme.MakeIcon(Theme.GlyphExpandMore, Theme.SecondaryText, 22) }
            }
        };
        _modelChip.OnTap(OnModelChipTapped);
        SemanticProperties.SetDescription(_modelChip, "Choose the model");
        _titleLabel = new Label
        {
            Text = "LiteRT-LM Chat",
            FontFamily = Theme.TitleFont,
            FontSize = 20,
            TextColor = Theme.PrimaryText,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        _newChatButton = Theme.CircleButton(Theme.GlyphWand, () => OnNewChat(this, EventArgs.Empty));
        SemanticProperties.SetDescription(_newChatButton, "New chat");
        _settingsButton = Theme.CircleButton(Theme.GlyphSettings, OnSettingsTapped);
        SemanticProperties.SetDescription(_settingsButton, "Settings");
        _historyButton = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            BackgroundColor = Theme.CardBackground,
            Stroke = Theme.RowBorder,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            VerticalOptions = LayoutOptions.Center,
            Content = new Microsoft.Maui.Controls.Shapes.Path
            {
                Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(HistoryIconPath),
                Fill = Theme.PrimaryText,
                WidthRequest = 22,
                HeightRequest = 22,
                Aspect = Stretch.Uniform,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        _historyButton.OnTap(OnHistoryTapped);
        SemanticProperties.SetDescription(_historyButton, "Saved chats");
        _exportChatButton = BuildExportChatButton();
        _privateChatBadge = new Label { Text = "Private · Not saved", FontFamily = Theme.TitleFont, FontSize = 12,
            TextColor = Theme.Accent, IsVisible = false };
        SemanticProperties.SetDescription(_privateChatBadge, "Private chat. Messages are not saved. Memories and past-chat search are unavailable.");
        var header = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(Theme.CircleButton(Theme.GlyphChevronLeft, async () => await Navigation.PopAsync()), 0, 0);
        header.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center,
            Children = { new Grid { Children = { _modelChip, _titleLabel } }, _privateChatBadge } }, 1, 0);
        header.Add(_historyButton, 2, 0);
        header.Add(_newChatButton, 3, 0);
        header.Add(_exportChatButton, 4, 0);
        header.Add(_settingsButton, 5, 0);
        return header;
    }

    // Per-message toggles, shown above the message box for models that support them.
    private void BuildMessageToggles()
    {
        _thinkingSwitch = new ToggleChip("\u2728 Think");
        // On by default (thinking first gives better answers, at the cost of time and context), then as last set.
        _thinkingSwitch.IsToggled = AppSettings.Chat.Think;
        _thinkingSwitch.Toggled += OnThinkingToggled;
        _webSearchSwitch = new ToggleChip("\U0001F50E Web search");
        _webSearchSwitch.IsToggled = AppSettings.Chat.WebSearch;
        _webSearchSwitch.Toggled += OnWebSearchToggled;
        _taskChip = new ToggleChip("\U0001F4CB Plan");
        SemanticProperties.SetDescription(_taskChip, "Plan the next message as a task and run it step by step");
        _toggleRow = new HorizontalStackLayout { Spacing = 8, Children = { _thinkingSwitch, _webSearchSwitch, _taskChip, CreateVoiceChip() } };
        _webSearchNotice = new Label
        {
            Text = "Web search requires a Tavily API key. Add one in Settings > Web search. Without a key, Wikipedia, links you provide, weather and currency tools are still available.",
            FontFamily = Theme.BodyFont,
            FontSize = 12,
            TextColor = Theme.SecondaryText,
            IsVisible = false
        };
    }

    // The status line and the message box: the toggles, attachments, document chip, editor, talk and send buttons.
    private View BuildComposer()
    {
        _statusLabel = new Label
        {
            IsVisible = false,
            Padding = new Thickness(4, 2),
            TextColor = Theme.Accent,
            FontFamily = Theme.BodyFont,
            FontSize = 14
        };

#if MACCATALYST
        _promptEditor = new ChatPromptEditor
#else
        _promptEditor = new Editor
#endif
        {
            Placeholder = "Type your message...",
            FontFamily = Theme.BodyFont,
#if WINDOWS
            // On Windows, the native TextBox's idle-state foreground brush doesn't reliably follow
            // Editor.TextColor (a known MAUI/WinUI quirk), so a dark TextColor still rendered white
            // here. Going dark end-to-end instead - a black background always shows the (effectively
            // fixed) white foreground clearly, rather than fighting it.
            BackgroundColor = Colors.Black,
            TextColor = Colors.White,
            PlaceholderColor = Colors.LightGray,
#else
            TextColor = Theme.PrimaryText,
            PlaceholderColor = Theme.SecondaryText,
#endif
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 44,
            MaximumHeightRequest = 120
        };

#if WINDOWS
        ConfigurePromptKeyboard();
#elif MACCATALYST
        SemanticProperties.SetHint(_promptEditor, "Return sends your message. Shift+Return or Option+Return adds a new line.");
        ((ChatPromptEditor)_promptEditor).SendRequested += OnSendClicked;
#endif
        _busyIndicator = new ActivityIndicator { IsVisible = false, IsRunning = false, WidthRequest = 36, Color = Theme.Accent };
        _sendButton = new Button
        {
            Text = "Send",
            FontFamily = Theme.TitleFont,
            BackgroundColor = Theme.Accent,
            TextColor = Colors.White,
            CornerRadius = 14,
            HeightRequest = 44,
            WidthRequest = 90
        };
        _sendButton.Clicked += OnSendClicked;

        var sendArea = new Grid { VerticalOptions = LayoutOptions.End };
        sendArea.Children.Add(_sendButton);
        sendArea.Children.Add(_busyIndicator);

        _attachButton = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            BackgroundColor = Theme.TileBackground,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            VerticalOptions = LayoutOptions.End,
            Content = new Label
            {
                Text = "+",
                FontFamily = Theme.TitleFont,
                FontSize = 26,
                TextColor = Theme.Accent,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        _attachButton.OnTap(OnAttachClicked);
        SemanticProperties.SetDescription(_attachButton, "Attach a document, an image or audio");

        _micIcon = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(MicPathData),
            Fill = Theme.Accent,
            WidthRequest = 24,
            HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _stopIcon = new BoxView
        {
            Color = Colors.White,
            CornerRadius = 3,
            WidthRequest = 16,
            HeightRequest = 16,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _talkButton = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            BackgroundColor = Theme.TileBackground,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            VerticalOptions = LayoutOptions.End,
            IsVisible = Models[0].AcceptsAudio,
            Content = new Grid { Children = { _micIcon, _stopIcon } }
        };
        _talkButton.OnTap(OnTalkClicked);
        SemanticProperties.SetDescription(_talkButton, "Talk: record a voice message");

        var composerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        composerRow.Add(_attachButton, 0, 0);
        composerRow.Add(_promptEditor, 1, 0);
        composerRow.Add(_talkButton, 2, 0);
        composerRow.Add(sendArea, 3, 0);

        _attachmentStrip = new HorizontalStackLayout { Spacing = 8 };
        _attachmentScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            IsVisible = false,
            Content = _attachmentStrip
        };
        _documentBar = new HorizontalStackLayout { Spacing = 8, IsVisible = false };
        var composer = new VerticalStackLayout { Spacing = 8, Children = { _documentBar, _attachmentScroll, composerRow } };

        return Theme.Card(new VerticalStackLayout { Spacing = 8, Children = { _toggleRow, _webSearchNotice, composer } }, 10);
    }

    // The main column: header, conversation (with the load card over it), status line and message box.
    private void BuildMainColumn(View header, View composerCard)
    {
        _mainColumn = new Grid
        {
            RowSpacing = 10,
            Padding = new Thickness(16, 12, 16, 16),
            MaximumWidthRequest = 760,
            HorizontalOptions = LayoutOptions.Fill,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        _mainColumn.Add(header, 0, 0);
        BuildLoadCard();
        _mainColumn.Add(new Grid { Children = { _chatView, _loadCard } }, 0, 1);
        _mainColumn.Add(_statusLabel, 0, 2);
        _mainColumn.Add(composerCard, 0, 3);
    }

    // The card at the top of the conversation while a model downloads or loads.
    private void BuildLoadCard()
    {
        _loadTitle = new Label { FontFamily = Theme.TitleFont, FontSize = 16, TextColor = Theme.PrimaryText };
        _loadStage = new Label { FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.SecondaryText };
        _loadProgress = new ProgressBar { ProgressColor = Theme.Accent, IsVisible = false };
        _loadSpinner = new ActivityIndicator { Color = Theme.Accent, WidthRequest = 22, HeightRequest = 22, VerticalOptions = LayoutOptions.Center };
        _loadPrimaryButton = Theme.PrimaryButton("Download", null);
        _loadPrimaryButton.ImageSource = null;
        _loadPrimaryButton.HeightRequest = 40;
        _loadPrimaryButton.FontSize = 14;
        _loadPrimaryButton.Clicked += (s, e) => _loadPrimaryAction?.Invoke();
        _loadCancelButton = Theme.SecondaryButton("Cancel", null);
        _loadCancelButton.ImageSource = null;
        _loadCancelButton.HeightRequest = 40;
        _loadCancelButton.FontSize = 14;
        _loadCancelButton.Clicked += (s, e) => CancelLoad();
        var loadTitleRow = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        loadTitleRow.Add(_loadSpinner, 0, 0);
        loadTitleRow.Add(_loadTitle, 1, 0);
        var loadButtons = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        loadButtons.Add(_loadPrimaryButton, 0, 0);
        loadButtons.Add(_loadCancelButton, 1, 0);
        _loadCard = Theme.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { loadTitleRow, _loadStage, _loadProgress, loadButtons }
        }, 14);
        _loadCard.IsVisible = false;
        _loadCard.VerticalOptions = LayoutOptions.Start;
        _loadCard.Margin = new Thickness(0, 4, 0, 0);
    }

    // The side panel (wide screens): saved chats, the model list and the settings.
    private void BuildSidePanel()
    {
        _sideChatList = new VerticalStackLayout { Spacing = 2 };
        _sideModelList = new VerticalStackLayout { Spacing = 4 };
        _modelsExpanded = AppSettings.Chat.ModelsExpanded;
        _cpuModelsExpanded = AppSettings.Chat.CpuModelsExpanded;
        _gpuModelsExpanded = AppSettings.Chat.GpuModelsExpanded;
        _sideModelHeader = new Button
        {
            FontFamily = Theme.TitleFont,
            FontSize = 15,
            TextColor = Theme.PrimaryText,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            MinimumHeightRequest = 44,
            Padding = new Thickness(0, 8)
        };
        _sideModelHeader.Clicked += (_, _) =>
        {
            _modelsExpanded = !_modelsExpanded;
            AppSettings.Chat.ModelsExpanded = _modelsExpanded;
            UpdateModelSectionHeader();
        };
        _sideSettingsHost = new VerticalStackLayout();
        _sidePanel = new Border
        {
            BackgroundColor = Theme.CardBackground,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0) },
            IsVisible = false,
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = new Thickness(16, 20),
                    Spacing = 10,
                    Children =
                    {
                        Theme.SectionTitle("Chats"),
                        _sideChatList,
                        Theme.Divider(),
                        _sideModelHeader,
                        _sideModelList,
                        Theme.Divider(),
                        Theme.SectionTitle("Settings"),
                        _sideSettingsHost
                    }
                }
            }
        };
    }

    // ---------- Layout ----------

    // Wide screens (desktop, the inner screen of a foldable, tablets) show the side panel; narrow ones the header
    // chip and settings button, which open bottom sheets.
    private void UpdateLayout()
    {
        if (Width <= 0)
            return;
        bool wide = Width >= WideLayoutWidth;
        if (_wide == wide)
            return;
        _wide = wide;
        _pageGrid.ColumnDefinitions[0].Width = wide ? new GridLength(SidePanelWidth) : new GridLength(0);
        _sidePanel.IsVisible = wide;
        _modelChip.IsVisible = !wide;
        _titleLabel.IsVisible = wide;
        _settingsButton.IsVisible = !wide;
        _historyButton.IsVisible = !wide;
        if (wide)
        {
            MoveTo(_settingsView, _sideSettingsHost);
            RefreshModelList();
            _ = RefreshChatListAsync();
        }
    }

    private static void MoveTo(View view, Layout newParent)
    {
        if (view.Parent is Layout oldParent)
            oldParent.Children.Remove(view);
        newParent.Children.Add(view);
    }
}
