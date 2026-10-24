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

// The model list (bottom sheet or side panel) and choosing a model.
namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        private bool? _cpuModelsExpanded;
        private bool? _gpuModelsExpanded;

        private static bool? ReadModelGroupPreference(string key) =>
            Preferences.Default.ContainsKey(key) ? Preferences.Default.Get(key, false) : null;

        private void UpdateModelSectionHeader()
        {
            string selected = SelectedOption.Name + (_selectedGpu ? " · GPU" : " · CPU");
            _sideModelList.IsVisible = _modelsExpanded;
            _sideModelHeader.Text = _modelsExpanded ? "▼ Model" : "▶ Model · " + selected;
            SemanticProperties.SetDescription(_sideModelHeader,
                "Model, " + (_modelsExpanded ? "expanded" : "collapsed") + ", selected " + selected);
            SemanticProperties.SetHint(_sideModelHeader, _modelsExpanded ? "Collapse model section" : "Expand model section");
        }

        // The model list: collapsible backend groups, with a check mark on the current model.
        private View BuildModelList(bool inSheet)
        {
            _modelDownloadRows.RemoveAll(row => row.InSheet == inSheet);
            var list = new VerticalStackLayout { Spacing = 4 };
            foreach (bool gpu in new[] { true, false })
            {
                if (gpu && !Models.Any(HasGpu)) continue;
                string title = gpu ? "GPU Models" : "CPU Models";
                var models = new VerticalStackLayout
                {
                    Spacing = 4,
                    IsVisible = (gpu ? _gpuModelsExpanded : _cpuModelsExpanded) ?? (gpu == _selectedGpu)
                };
                var header = new Button
                {
                    FontFamily = Theme.TitleFont,
                    FontSize = 15,
                    TextColor = Theme.PrimaryText,
                    BackgroundColor = Colors.Transparent,
                    HorizontalOptions = LayoutOptions.Fill,
                    Padding = new Thickness(10, 8)
                };
                void UpdateHeader()
                {
                    header.Text = (models.IsVisible ? "▼ " : "▶ ") + title;
                    SemanticProperties.SetDescription(header, title + (models.IsVisible ? ", expanded" : ", collapsed"));
                    SemanticProperties.SetHint(header, models.IsVisible ? "Collapse model list" : "Expand model list");
                }
                UpdateHeader();
                header.Clicked += (_, _) =>
                {
                    models.IsVisible = !models.IsVisible;
                    if (gpu) _gpuModelsExpanded = models.IsVisible;
                    else _cpuModelsExpanded = models.IsVisible;
                    try { Preferences.Default.Set(gpu ? GpuModelsExpandedPreference : CpuModelsExpandedPreference, models.IsVisible); } catch (Exception) { }
                    UpdateHeader();
                    if (inSheet) RefreshModelList();
                };
                list.Children.Add(header);
                list.Children.Add(models);
                for (int i = 0; i < Models.Length; i++)
                {
                    int index = i;
                    ModelOption option = Models[i];
                    if (gpu && !HasGpu(option)) continue;
                    bool selected = index == _selectedModel && gpu == _selectedGpu;
                    var detail = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
                    var status = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.Accent };
                    var resume = new Button { Text = "Continue", FontFamily = Theme.TitleFont, FontSize = 12, HorizontalOptions = LayoutOptions.Start };
                    var statusIcon = Theme.MakePathIcon(Theme.PathCloudDownload, Theme.SecondaryText, 14);
                    var downloadRow = new ModelDownloadRow { Option = option, Detail = detail, Status = status, StatusIcon = statusIcon, Continue = resume, InSheet = inSheet };
                    _modelDownloadRows.Add(downloadRow);
                    UpdateModelDownloadRow(downloadRow);
                    SemanticProperties.SetDescription(resume, "Continue downloading " + option.Name);
                    resume.Clicked += (_, _) =>
                    {
                        if (!CanChangeModel) return;
                        if (inSheet) _sheet.Dismiss(index + (gpu ? ":gpu" : ":cpu"));
                        SelectModel(index, gpu);
                        _ = PrepareModelAsync(true);
                    };
                    var row = new Grid
                    {
                        Padding = new Thickness(10, 8),
                        ColumnSpacing = 8,
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
                    };
                    row.Add(new VerticalStackLayout
                    {
                        Children =
                        {
                            new Label { Text = option.Name + (gpu ? " · GPU" : " · CPU"), FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText },
                            new HorizontalStackLayout { Spacing = 6, Children = { statusIcon, status } },
                            detail,
                            resume
                        }
                    }, 0, 0);
                    if (selected)
                        row.Add(Theme.MakeIcon(Theme.GlyphCheck, Theme.Accent, 22), 1, 0);
                    var cell = new Border
                    {
                        BackgroundColor = selected ? Theme.TileBackground : Colors.Transparent,
                        Stroke = Colors.Transparent,
                        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                        Content = row
                    };
                    cell.OnTap(() =>
                    {
                        if (inSheet)
                            _sheet.Dismiss(index + (gpu ? ":gpu" : ":cpu"));
                        else if (CanChangeModel)
                            SelectModel(index, gpu);
                    });
                    models.Children.Add(cell);
                }
            }
            list.Children.Add(BuildServerModelRow(inSheet));
            return list;
        }

        private void RefreshModelList()
        {
            UpdateModelSectionHeader();
            if (_wide != true)
                return;
            _sideModelList.Children.Clear();
            _sideModelList.Children.Add(BuildModelList(false));
        }

        private bool CanChangeModel => !_busy && !_reloadingCpuThreads && !_recorder.IsRecording;

        private async void OnModelChipTapped()
        {
            if (!CanChangeModel)
                return;
            string choice = await _sheet.ShowViewAsync("Model", BuildModelList(true), "Cancel");
            string[] parts = choice?.Split(':');
            if (parts?.Length == 2 && int.TryParse(parts[0], out int index) && CanChangeModel)
                SelectModel(index, parts[1] == "gpu");
        }

        private async void OnSettingsTapped()
        {
            // The settings view may still be in the side panel (or a previous sheet).
            if (_settingsView.Parent is Layout parent)
                parent.Children.Remove(_settingsView);
            await _sheet.ShowViewAsync("Settings", _settingsView);
        }

        private ModelChoice SelectedChoice => SelectedOption.Choice;

        // ---------- Model selection ----------

        private ModelOption SelectedOption => OptionAt(_selectedModel);

        // Select a model (loaded on the next message) and show the controls it supports.
        private void SelectModel(int index, bool? gpu = null)
        {
            UiStallWatchdog.Mark("Select model " + OptionAt(index).Name);
            // A chat belongs to a model: another model starts a new chat.
            if (_pageStarted && index != _selectedModel)
                StartNewChat();
            _selectedModel = index;
            ModelOption option = SelectedOption;
            _selectedGpu = (gpu ?? _selectedGpu) && HasGpu(option);
            _gpuUnavailable = false;
            _modelChipLabel.Text = option.Choice == ModelChoice.Server ? ServerModelLabel : option.Name + (_selectedGpu ? " · GPU" : " · CPU");
            RefreshModelList();
            _talkButton.IsVisible = TakesVoice(option);
            _thinkingSwitch.IsVisible = option.CanThink;
            _webSearchSwitch.IsVisible = option.CanUseTools;
            _taskChip.IsVisible = option.CanUseTools && !_voiceMode;
            _voiceChip.IsVisible = VoiceModeSupported;
            _toggleRow.IsVisible = option.CanThink || option.CanUseTools || _voiceChip.IsVisible;
            UpdateWebSearchNotice();
            UpdateContextSection();
            // Drop attachments the new model can't take.
            foreach (PendingAttachment attachment in _pending.ToList())
                if (attachment.Image != null ? !option.AcceptsImages : !option.AcceptsAudio)
                    RemovePending(attachment);
            if (_pageStarted)
            {
                // Constructor UI setup must not overwrite the saved backend before OnAppearing restores it.
                // (The server has no backend: the local models keep theirs.)
                if (option.Choice != ModelChoice.Server)
                    try { Preferences.Default.Set(BackendPreference, _selectedGpu); } catch (Exception) { }
                try { Preferences.Default.Set(LastModelPreference, option.Choice.ToString()); } catch (Exception) { }
                _ = PrepareModelAsync(false);
            }
        }
    }
}
