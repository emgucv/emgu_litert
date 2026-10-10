//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Chat demo backed by LiteRT-LM: downloads a .litertlm model and runs it entirely on this device
    /// through Emgu.LiteRT.LM.Models (LanguageModel, Chat). Wired up for Windows, iOS and Android -
    /// LiteRT-LM's native library is not yet bundled with this app on Mac Catalyst.
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
            new ModelOption { Name = "Gemma 4 E2B", Detail = "~2.6 GB download. Larger, can think before answering, keeps a conversation open across turns.", Choice = ModelChoice.Gemma4E2B },
        };

        private static readonly Color UserBubbleColor = Theme.Accent;
        private static readonly Color ModelBubbleColor = Theme.TileBackground;
        private static readonly Color UserTextColor = Colors.White;
        private static readonly Color ModelTextColor = Theme.PrimaryText;
        private static readonly Color ThinkingTextColor = Theme.SecondaryText;

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
            Shell.SetNavBarIsVisible(this, false);
            BackgroundColor = Theme.PageBackground;

            _modelPicker = new Picker { Title = "Model", FontFamily = Theme.TitleFont, TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
            foreach (ModelOption option in Models)
                _modelPicker.Items.Add(option.Name);
            _modelPicker.SelectedIndex = 0;
            _modelPicker.SelectedIndexChanged += OnModelPickerChanged;

            _modelDetailLabel = new Label
            {
                Text = Models[0].Detail,
                FontFamily = Theme.BodyFont,
                FontSize = 13,
                TextColor = Theme.SecondaryText
            };

            _thinkingLabel = new Label
            {
                Text = "Let it think before answering",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            };
            _thinkingSwitch = new Switch { IsToggled = false, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _thinkingSwitch.Toggled += OnThinkingToggled;
            var thinkingRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            thinkingRow.Add(_thinkingLabel, 0, 0);
            thinkingRow.Add(_thinkingSwitch, 1, 0);

            _newChatButton = Theme.SecondaryButton("New Chat", Theme.GlyphWand);
            _newChatButton.HeightRequest = 44;
            _newChatButton.FontSize = 15;
            _newChatButton.Clicked += OnNewChat;

            var optionsCard = Theme.Card(new VerticalStackLayout
            {
                Spacing = 6,
                Children = { _modelPicker, _modelDetailLabel, Theme.Divider(), thinkingRow, _newChatButton }
            }, 14);

            _emptyLabel = new Label
            {
                Text = "Ask anything - the model runs entirely on this device.",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = Theme.SecondaryText,
                Margin = new Thickness(24)
            };
            _transcript = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(4), Children = { _emptyLabel } };
            _scroll = new ScrollView { Content = _transcript };

            _statusLabel = new Label
            {
                IsVisible = false,
                Padding = new Thickness(4, 2),
                TextColor = Theme.Accent,
                FontFamily = Theme.BodyFont,
                FontSize = 14
            };

            _promptEditor = new Editor
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

            var composerRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 8
            };
            composerRow.Add(_promptEditor, 0, 0);
            composerRow.Add(sendArea, 1, 0);

            var root = new Grid
            {
                RowSpacing = 12,
                Padding = new Thickness(16, 12, 16, 16),
                MaximumWidthRequest = 760,
                HorizontalOptions = LayoutOptions.Fill,
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            root.Add(Theme.PageHeader(this, Theme.GlyphText, "LiteRT-LM Chat", "On-device language model", true, false), 0, 0);
            root.Add(optionsCard, 0, 1);
            root.Add(_scroll, 0, 2);
            root.Add(_statusLabel, 0, 3);
            root.Add(Theme.Card(composerRow, 10), 0, 4);

            Content = root;
        }

        private ModelChoice SelectedChoice => Models[Math.Max(_modelPicker.SelectedIndex, 0)].Choice;

        // ---------- Model selection ----------

        private void OnModelPickerChanged(object sender, EventArgs e)
        {
            _modelDetailLabel.Text = Models[Math.Max(_modelPicker.SelectedIndex, 0)].Detail;
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
                AddUserMessage(prompt.Trim());
                SetStatus("Generating...");

                // Stream the reply into its own bubble as it's generated, rather than waiting for the
                // full answer. onChunk fires on a LiteRT-LM background thread with each incremental piece
                // of text (including any <think>...</think> block), so re-splitting the text accumulated
                // so far through ChatReply on every chunk gives the same thinking/answer split a finished
                // reply would show, just filled in live.
                StreamingBubble bubble = AddStreamingBubble();
                StringBuilder streamed = new StringBuilder();
                bool firstChunk = true;
                Action<string> onChunk = chunk =>
                {
                    streamed.Append(chunk);
                    ChatReply partial = new ChatReply(streamed.ToString());
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (firstChunk)
                        {
                            firstChunk = false;
                            SetStatus(null);
                        }
                        UpdateStreamingBubble(bubble, partial);
                    });
                };

                // Kept so teardown (OnNavigatedFrom / switching models) can wait for it instead of
                // freeing the chat/engine while LiteRT-LM is still generating into it.
                Task<ChatReply> generation = _chat.SendAsync(prompt, onChunk);
                _generation = generation;
                ChatReply reply = await generation;

                UpdateStreamingBubble(bubble, reply);
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
            _chat.EnableThinking = _thinkingSwitch.IsToggled;
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

        // The views of a model bubble that is still streaming, so each chunk can update it in place
        // instead of adding a new bubble per chunk.
        private sealed class StreamingBubble
        {
            public Label ThinkingLabel;
            public BoxView Separator;
            public Label TextLabel;
        }

        private StreamingBubble AddStreamingBubble()
        {
            var thinkingLabel = new Label { FontFamily = Theme.BodyFont, TextColor = ThinkingTextColor, FontSize = 13, IsVisible = false };
            var separator = new BoxView { HeightRequest = 1, Color = Theme.Chevron, IsVisible = false };
            var textLabel = new Label { TextColor = ModelTextColor, FontFamily = Theme.BodyFont, FontSize = 15 };

            Border bubble = ModelBubble(new VerticalStackLayout
            {
                Spacing = 4,
                Children = { thinkingLabel, separator, textLabel }
            });
            AddBubbleRow(bubble, false);

            return new StreamingBubble { ThinkingLabel = thinkingLabel, Separator = separator, TextLabel = textLabel };
        }

        // Called on the UI thread with the thinking/answer split of everything streamed so far (or, once,
        // with the finished reply).
        private void UpdateStreamingBubble(StreamingBubble bubble, ChatReply partial)
        {
            bool hasThinking = !string.IsNullOrEmpty(partial.Thinking);
            bubble.ThinkingLabel.IsVisible = hasThinking;
            bubble.Separator.IsVisible = hasThinking;
            bubble.ThinkingLabel.Text = partial.Thinking;
            bubble.TextLabel.Text = partial.Text;
            ScrollToEnd();
        }

        private void AddUserMessage(string text)
        {
            AddBubbleRow(Bubble(text, UserBubbleColor, UserTextColor), true);
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
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
                Padding = new Thickness(12, 9),
                Content = new Label { Text = text, TextColor = textColor, FontFamily = Theme.BodyFont, FontSize = 15 }
            };
        }

        private static Border ModelBubble(View content)
        {
            return new Border
            {
                BackgroundColor = ModelBubbleColor,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
                Padding = new Thickness(12, 9),
                Content = content
            };
        }

        private async void ScrollToEnd()
        {
            try
            {
                await Task.Yield();
                // Scroll to the actual bottom: Android doesn't clamp the offset, so scrolling to
                // double.MaxValue would scroll the whole transcript out of view.
                double bottom = Math.Max(0, _transcript.Height - _scroll.Height);
                await _scroll.ScrollToAsync(0, bottom, true);
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
