//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS

using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Chat demo backed by LiteRT-LM: downloads a .litertlm model and runs it entirely on this device
    /// through Emgu.LiteRT.LM.Models (LanguageModel, Chat). Only wired up for Windows for now -
    /// LiteRT-LM's native library is not yet bundled with this app on the other MAUI targets.
    /// </summary>
    public class LiteRtLmChatPage : ContentPage
    {
        private enum ModelChoice
        {
            Qwen3,
            Gemma4E2B
        }

        private sealed class ModelOption
        {
            public string Name;
            public string Detail;
            public ModelChoice Choice;
        }

        private static readonly ModelOption[] Models = new[]
        {
            new ModelOption { Name = "Qwen3 0.6B", Detail = "~500 MB download. Fast, and can think before answering.", Choice = ModelChoice.Qwen3 },
            new ModelOption { Name = "Gemma 4 E2B", Detail = "~2.6 GB download. Larger, keeps a conversation open across turns.", Choice = ModelChoice.Gemma4E2B },
        };

        private static readonly Color UserBubbleColor = Color.FromArgb("#512BD4");
        private static readonly Color ModelBubbleColor = Color.FromArgb("#DFD8F7");
        private static readonly Color UserTextColor = Colors.White;
        private static readonly Color ModelTextColor = Color.FromArgb("#212121");
        private static readonly Color ThinkingTextColor = Color.FromArgb("#6E6E6E");

        private readonly Picker _modelPicker;
        private readonly Label _modelDetailLabel;
        private readonly Switch _thinkingSwitch;
        private readonly Label _thinkingLabel;
        private readonly Button _newChatButton;
        private readonly VerticalStackLayout _transcript;
        private readonly Label _emptyLabel;
        private readonly ScrollView _scroll;
        private readonly Label _statusLabel;
        private readonly Editor _promptEditor;
        private readonly Button _sendButton;
        private readonly ActivityIndicator _busyIndicator;

        private LanguageModel _model;
        private Chat _chat;
        private ModelChoice? _loaded;
        private Task _generation;
        private bool _busy;

        public LiteRtLmChatPage()
        {
            Title = "LiteRT-LM Chat";
            BackgroundColor = Colors.White;

            _modelPicker = new Picker { Title = "Model", TextColor = ModelTextColor, TitleColor = Colors.Gray };
            foreach (ModelOption option in Models)
                _modelPicker.Items.Add(option.Name);
            _modelPicker.SelectedIndex = 0;
            _modelPicker.SelectedIndexChanged += OnModelPickerChanged;

            _modelDetailLabel = new Label
            {
                Text = Models[0].Detail,
                FontSize = 12,
                TextColor = Colors.Gray
            };

            _thinkingLabel = new Label { Text = "Let it think before answering", TextColor = ModelTextColor, VerticalOptions = LayoutOptions.Center };
            _thinkingSwitch = new Switch { IsToggled = false };
            _thinkingSwitch.Toggled += OnThinkingToggled;
            var thinkingRow = new HorizontalStackLayout
            {
                Spacing = 8,
                Children = { _thinkingSwitch, _thinkingLabel }
            };

            _newChatButton = new Button { Text = "New Chat", HorizontalOptions = LayoutOptions.Start };
            _newChatButton.Clicked += OnNewChat;

            var header = new VerticalStackLayout
            {
                Spacing = 6,
                Padding = new Thickness(16, 12, 16, 8),
                Children = { _modelPicker, _modelDetailLabel, thinkingRow, _newChatButton }
            };

            _emptyLabel = new Label
            {
                Text = "Ask anything - the model runs entirely on this device.",
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = Colors.Gray,
                Margin = new Thickness(24)
            };
            _transcript = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(12, 4), Children = { _emptyLabel } };
            _scroll = new ScrollView { Content = _transcript };

            _statusLabel = new Label
            {
                IsVisible = false,
                Padding = new Thickness(16, 4),
                TextColor = Color.FromArgb("#512BD4"),
                FontAttributes = FontAttributes.Italic
            };

            _promptEditor = new Editor
            {
                Placeholder = "Type your message...",
                // On Windows, the native TextBox's idle-state foreground brush doesn't reliably follow
                // Editor.TextColor (a known MAUI/WinUI quirk), so a dark TextColor still rendered white
                // here. Going dark end-to-end instead - a black background always shows the (effectively
                // fixed) white foreground clearly, rather than fighting it.
                BackgroundColor = Colors.Black,
                TextColor = Colors.White,
                PlaceholderColor = Colors.LightGray,
                AutoSize = EditorAutoSizeOption.TextChanges,
                MinimumHeightRequest = 44,
                MaximumHeightRequest = 120
            };

            _busyIndicator = new ActivityIndicator { IsVisible = false, IsRunning = false, WidthRequest = 36 };
            _sendButton = new Button { Text = "Send", WidthRequest = 90 };
            _sendButton.Clicked += OnSendClicked;

            var sendArea = new Grid();
            sendArea.Children.Add(_sendButton);
            sendArea.Children.Add(_busyIndicator);

            var composerRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 8,
                Padding = new Thickness(12)
            };
            composerRow.Add(_promptEditor, 0, 0);
            composerRow.Add(sendArea, 1, 0);

            var root = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            root.Add(header, 0, 0);
            root.Add(_scroll, 0, 1);
            root.Add(_statusLabel, 0, 2);
            root.Add(composerRow, 0, 3);

            Content = root;
        }

        private ModelChoice SelectedChoice => Models[Math.Max(_modelPicker.SelectedIndex, 0)].Choice;

        // ---------- Model selection ----------

        private void OnModelPickerChanged(object sender, EventArgs e)
        {
            _modelDetailLabel.Text = Models[Math.Max(_modelPicker.SelectedIndex, 0)].Detail;
            bool isQwen3 = SelectedChoice == ModelChoice.Qwen3;
            _thinkingSwitch.IsEnabled = isQwen3;
            if (!isQwen3)
                _thinkingSwitch.IsToggled = false;
        }

        private void OnThinkingToggled(object sender, ToggledEventArgs e)
        {
            if (_chat != null)
                _chat.EnableThinking = e.Value;
        }

        // ---------- Sending a message ----------

        private async void OnSendClicked(object sender, EventArgs e)
        {
            if (_busy)
                return;

            string prompt = _promptEditor.Text;
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            SetBusy(true);
            try
            {
                if (!await EnsureModelReadyAsync())
                    return;

                _promptEditor.Text = string.Empty;
                AddMessage(prompt.Trim(), true);
                SetStatus("Generating...");

                // Chat.Send blocks on native LiteRT-LM calls; keep it off the UI thread, and keep the
                // task around so teardown (OnNavigatedFrom / switching models) can wait for it instead
                // of freeing the chat/engine while this is still reading them.
                Chat chat = _chat;
                string text = prompt;
                Task<ChatReply> generation = Task.Run(() => chat.Send(text));
                _generation = generation;
                ChatReply reply = await generation;

                AddReply(reply);
                SetStatus(null);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task<bool> EnsureModelReadyAsync()
        {
            ModelChoice choice = SelectedChoice;

            if (_model != null && _loaded != choice)
            {
                await CleanupModelAsync();
                ClearTranscript();
            }

            if (_model != null)
                return true;

            SetStatus("Preparing the model... the first run downloads it.");

            LanguageModel model = choice == ModelChoice.Qwen3 ? (LanguageModel)new Qwen3() : new Gemma4E2B();
            model.OnDownloadProgressChanged += OnDownloadProgressChanged;
            try
            {
                await model.Init();
            }
            catch (Exception ex)
            {
                model.Dispose();
                SetStatus("Could not load the model: " + ex.Message);
                return false;
            }

            _model = model;
            _chat = model.CreateChat();
            _chat.EnableThinking = choice == ModelChoice.Qwen3 ? _thinkingSwitch.IsToggled : (bool?)null;
            _loaded = choice;
            return true;
        }

        private void OnDownloadProgressChanged(long? totalBytesToReceive, long bytesReceived, double? progressPercentage)
        {
            string message = totalBytesToReceive.HasValue
                ? string.Format("Downloading model... {0} of {1} MB ({2}%)", bytesReceived / (1024 * 1024), totalBytesToReceive.Value / (1024 * 1024), (int)(progressPercentage ?? 0))
                : string.Format("Downloading model... {0} MB", bytesReceived / (1024 * 1024));
            MainThread.BeginInvokeOnMainThread(() => SetStatus(message));
        }

        // ---------- New chat / model switching ----------

        private async void OnNewChat(object sender, EventArgs e)
        {
            if (_busy)
            {
                SetStatus("Hang on - still answering.");
                return;
            }
            if (_chat != null)
            {
                _chat.ClearHistory();
                ClearTranscript();
            }
        }

        // Wait for any in-flight generation before freeing the chat/engine: Chat.Send and the engine it
        // runs on are native objects a background Task.Run is still reading, and disposing them out from
        // under it is a hard crash, not a catchable exception.
        private async Task CleanupModelAsync()
        {
            Task pending = _generation;
            if (pending != null && !pending.IsCompleted)
            {
                try { await pending; }
                catch { /* already reported via SetStatus where it was awaited */ }
            }

            Chat chat = _chat;
            LanguageModel model = _model;
            _chat = null;
            _model = null;
            _loaded = null;
            chat?.Dispose();
            model?.Dispose();
        }

        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            if (!Navigation.NavigationStack.Contains(this) && !Navigation.ModalStack.Contains(this))
            {
                _ = CleanupModelAsync();
            }
        }

        // ---------- Transcript ----------

        private void ClearTranscript()
        {
            _transcript.Children.Clear();
            _transcript.Children.Add(_emptyLabel);
        }

        private void AddReply(ChatReply reply)
        {
            if (!string.IsNullOrWhiteSpace(reply.Thinking))
            {
                var bubble = ModelBubble(new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = reply.Thinking, FontAttributes = FontAttributes.Italic, TextColor = ThinkingTextColor, FontSize = 13 },
                        new BoxView { HeightRequest = 1, Color = Colors.LightGray },
                        new Label { Text = reply.Text, TextColor = ModelTextColor }
                    }
                });
                AddBubbleRow(bubble, false);
            }
            else
            {
                AddMessage(reply.Text, false);
            }
        }

        private void AddMessage(string text, bool isUser)
        {
            Border bubble = isUser
                ? Bubble(text, UserBubbleColor, UserTextColor)
                : ModelBubble(new Label { Text = text, TextColor = ModelTextColor });
            AddBubbleRow(bubble, isUser);
        }

        private void AddBubbleRow(Border bubble, bool isUser)
        {
            if (_transcript.Children.Contains(_emptyLabel))
                _transcript.Children.Remove(_emptyLabel);

            bubble.HorizontalOptions = isUser ? LayoutOptions.End : LayoutOptions.Start;
            bubble.MaximumWidthRequest = 420;

            _transcript.Children.Add(bubble);
            ScrollToEnd();
        }

        private static Border Bubble(string text, Color background, Color textColor)
        {
            return new Border
            {
                BackgroundColor = background,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                Padding = new Thickness(12, 9),
                Content = new Label { Text = text, TextColor = textColor }
            };
        }

        private static Border ModelBubble(View content)
        {
            return new Border
            {
                BackgroundColor = ModelBubbleColor,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                Padding = new Thickness(12, 9),
                Content = content
            };
        }

        private async void ScrollToEnd()
        {
            try
            {
                await Task.Yield();
                await _scroll.ScrollToAsync(0, double.MaxValue, true);
            }
            catch (Exception)
            {
            }
        }

        // ---------- Busy / status ----------

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _sendButton.IsVisible = !busy;
            _busyIndicator.IsVisible = busy;
            _busyIndicator.IsRunning = busy;
            _promptEditor.IsEnabled = !busy;
            _modelPicker.IsEnabled = !busy;
            _newChatButton.IsEnabled = !busy;
        }

        private void SetStatus(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _statusLabel.IsVisible = !string.IsNullOrEmpty(message);
                _statusLabel.Text = message ?? string.Empty;
            });
        }
    }
}

#endif
