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

// New chats, and saving, listing and reopening chats (ChatStore).
namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        // ---------- New chat / model switching ----------

        private async void OnNewChat(object sender, EventArgs e)
        {
            UiStallWatchdog.Mark("New chat");
            if (_busy || _reloadingCpuThreads)
            {
                SetStatus("Hang on - still answering.");
                return;
            }
            StartNewChat();
            RemoveDocument();
        }

        // Clear the transcript and the model's conversation; the next message starts a new saved chat.
        private void StartNewChat()
        {
            _ = CloseCameraAsync();
            _cameraFront = false;
#if ANDROID
            UpdateCameraSwitch();
#endif
            _visualRuntime?.Clear();
            ClearSoundMonitors();
            ForgetMonitorRequests();
            ClearCameraTurn();
            _sessionVersion++;
            if (IsPrivateChat) { ClearPrivateSessionContent(); ResetAgent(); }
            _conversationId = null;
            UpdateExportChatButton();
            _rereadNext = false;
            // A new conversation for the model: the tool groups start again from what its messages need.
            _toolRouter?.Reset();
            ResetDocumentContext();
            ClearTranscript();
            UpdateMemoryLabel();
        }

        // ---------- Saved chats ----------

        // Save a completed exchange: the message (with its attachments), any tool steps, and the reply.
        private async Task SaveExchangeAsync(ChatMessageItem userItem, ChatMessageItem reply, StreamingBubble bubble = null)
        {
            if (IsPrivateChat) return; // Write nothing to SQLite or attachments.
            UserMessageInfo info = userItem.Tag as UserMessageInfo ?? new UserMessageInfo();
            try
            {
                // The message, its images and audio, the step lines and the reply (an interrupted one ends with "…") are
                // saved by Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore; the language, the chat's document and the cards are the page's.
                _conversationId ??= await _store.CreateConversationAsync(ChatHistoryStore.TitleOf(userItem), SelectedChoice.ToString());
                int id = _conversationId.Value;
                // The step lines: the document's (a card above the reply), then the tool calls.
                List<string> steps = new List<string>();
                if (bubble?.DocumentStep != null)
                    steps.Add(bubble.DocumentStep);
                steps.AddRange(reply.Steps.Select(step => step.Description));
                // Pictures the reply drew are kept with it (shown again when the chat is reopened, not given to the model).
                List<(string Kind, string MediaType, byte[] Data)> drawn;
                drawn = Images.Drawn();
                // And the documents it wrote.
                drawn.AddRange(TakeWrittenDocuments());
                ChatSavedExchange saved = await _store.SaveExchangeAsync(id, userItem, reply, drawn, steps);
                info.MessageId = saved.MessageId;
                await _store.SetLanguageAsync(saved.MessageId, info.Language);
                if (info.TranscriptTask != null)
                    _ = SaveTranscriptWhenReadyAsync(id, info);
                if (bubble != null)
                    bubble.MessageId = saved.ReplyId;
                // The document the chat uses now (or none), restored when it is opened again.
                await _store.SetConversationDocumentAsync(id, _document?.Index != null ? _document.Id : null);
                UpdateExportChatButton();
                await RefreshChatListAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Could not save the chat: " + ex.Message);
            }
        }

        private async void OnHistoryTapped()
        {
            if (!CanChangeModel)
                return;
            if (!await LeavePrivateChatAsync()) return;
            List<ConversationRecord> chats = await _store.GetConversationsAsync();
            string action = await _sheet.ShowViewAsync("Chats", BuildChatList(chats, true), "Close");
            await HandleChatListActionAsync(action, true);
        }

        private async Task HandleChatListActionAsync(string action, bool reopenSheet)
        {
            if (string.IsNullOrEmpty(action))
                return;
            int id = int.Parse(action.Substring(action.IndexOf(':') + 1));
            if (action.StartsWith("export:", StringComparison.Ordinal))
            {
                await ExportChatAsync(id);
                return;
            }
            if (action.StartsWith("delete:"))
            {
                // A deleted chat can't be restored, and the cross is next to the row that opens it: ask first.
                string title = (await _store.GetConversationsAsync()).FirstOrDefault(c => c.Id == id)?.Title;
                bool confirmed = await DisplayAlertAsync("Delete this chat?",
                    string.Format("\"{0}\" and its photos and recordings will be removed from this device.", title),
                    "Delete", "Cancel");
                if (!confirmed)
                {
                    if (reopenSheet)
                        OnHistoryTapped();
                    return;
                }
                await _store.DeleteConversationAsync(id);
                if (_conversationId == id)
                {
                    StartNewChat();
                    RemoveDocument();
                }
                // Its document, unless another chat (or the one open now) uses it.
                await _store.DeleteUnusedDocumentsAsync(_document?.Id);
                await RefreshChatListAsync();
                if (reopenSheet)
                    OnHistoryTapped();
            }
            else
            {
                await OpenConversationAsync(id);
            }
        }

        // The saved chats: title, model and date; tapping opens one, the cross deletes it.
        private View BuildChatList(List<ConversationRecord> chats, bool inSheet)
        {
            var list = new VerticalStackLayout { Spacing = 2 };
            if (chats.Count == 0)
            {
                list.Children.Add(new Label
                {
                    Text = "No saved chats yet - they appear here after the first reply.",
                    FontFamily = Theme.BodyFont,
                    FontSize = 13,
                    TextColor = Theme.SecondaryText,
                    Margin = new Thickness(10, 6)
                });
                return list;
            }
            foreach (ConversationRecord chat in chats)
            {
                int id = chat.Id;
                string modelName = Models.FirstOrDefault(m => m.Choice.ToString() == chat.Model)?.Name ?? chat.Model;
                var row = new Grid
                {
                    Padding = new Thickness(10, 7),
                    ColumnSpacing = 6,
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }
                };
                var details = new VerticalStackLayout
                {
                    Children =
                    {
                        new Label { Text = chat.Title, FontFamily = Theme.TitleFont, FontSize = 14, TextColor = Theme.PrimaryText, LineBreakMode = LineBreakMode.TailTruncation },
                        new Label { Text = modelName + " \u00B7 " + chat.Updated.ToLocalTime().ToString("g"), FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText }
                    }
                };
                row.Add(details, 0, 0);
                var export = new Button { Text = "Export", FontFamily = Theme.BodyFont, FontSize = 12,
                    Padding = new Thickness(6), MinimumHeightRequest = 44, VerticalOptions = LayoutOptions.Center,
                    BackgroundColor = Colors.Transparent, TextColor = Theme.Accent };
                SemanticProperties.SetDescription(export, "Export chat " + chat.Title + " with attachments");
                export.Clicked += (_, _) =>
                {
                    if (inSheet) _sheet.Dismiss("export:" + id);
                    else if (CanChangeModel) _ = HandleChatListActionAsync("export:" + id, false);
                };
                row.Add(export, 1, 0);
                var delete = new Label
                {
                    Text = Theme.GlyphClose,
                    FontFamily = Theme.IconFont,
                    FontSize = 18,
                    TextColor = Theme.SecondaryText,
                    Padding = new Thickness(6),
                    VerticalOptions = LayoutOptions.Center
                };
                SemanticProperties.SetDescription(delete, "Delete this chat");
                row.Add(delete, 2, 0);
                var cell = new Border
                {
                    BackgroundColor = id == _conversationId ? Theme.TileBackground : Colors.Transparent,
                    Stroke = Colors.Transparent,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Content = row
                };
                delete.OnTap(() =>
                {
                    if (inSheet)
                        _sheet.Dismiss("delete:" + id);
                    else if (CanChangeModel)
                        _ = HandleChatListActionAsync("delete:" + id, false);
                });
                details.OnTap(() =>
                {
                    if (inSheet)
                        _sheet.Dismiss("open:" + id);
                    else if (CanChangeModel)
                        _ = HandleChatListActionAsync("open:" + id, false);
                });
                list.Children.Add(cell);
            }
            return list;
        }

        private async Task RefreshChatListAsync()
        {
            if (_wide != true)
                return;
            try
            {
                List<ConversationRecord> chats = await _store.GetConversationsAsync();
                _sideChatList.Children.Clear();
                _sideChatList.Children.Add(BuildChatList(chats, false));
            }
            catch (Exception)
            {
            }
        }

        // Open a saved chat: switch to its model, show its messages, and give the model the conversation so far.
        private async Task OpenConversationAsync(int id)
        {
            if (!CanChangeModel || !await LeavePrivateChatAsync()) return;
            // Ending a private chat can await confirmation; another action may have started in the meantime.
            if (!CanChangeModel) return;
            SetBusy(true);
            _restoringTranscript = true;
            try
            {
                ConversationRecord chat = (await _store.GetConversationsAsync()).FirstOrDefault(c => c.Id == id);
                if (chat == null)
                    return;
                UiStallWatchdog.Mark("Open saved chat " + id);
                int index = ModelIndexOf(chat.Model);
                if (index >= 0 && index != _selectedModel)
                    SelectModel(index);
                StartNewChat();
                // The document belongs to the chat it was attached to (what the model was given of it is saved with it).
                RemoveDocument();
                _conversationId = id;
                UpdateExportChatButton();

                // The messages, with each reply's step lines (ChatHistoryStore reads them back, and the model sees those
                // after the last compaction and the summary of the ones before); the page's data and cards with them.
                ChatHistory history = await _store.LoadAsync<MessageRecord>(id);
                if (history == null)
                    return;
                string lastUserLanguage = null;
                foreach (ChatMessageItem item in history.Messages)
                {
                    var record = (MessageRecord)history.RecordOf(item);
                    if (item.Role == ChatMessageRole.User)
                    {
                        var info = new UserMessageInfo
                        {
                            // The language of a user message (detected now for chats saved before it was kept).
                            Language = record.Language ?? await TextLanguage.DetectAsync(item.Text),
                            Transcript = item.Transcript,
                            MessageId = record.Id
                        };
                        if (chat.Summary == null)
                            info.DividerText = "Earlier messages dropped from the model's memory";
                        lastUserLanguage = info.Language;
                        item.Tag = info;
                        continue;
                    }
                    StreamingBubble bubble = BubbleFor(item);
                    bubble.MessageId = record.Id;
                    bubble.TargetLanguage = TranslationTarget(record.Language ?? lastUserLanguage);
                    bubble.Translation = record.ThinkingTranslation;
                    bubble.ShowTranslation = !string.IsNullOrEmpty(record.ThinkingTranslation);
                    foreach (ChatSavedAttachment attachment in history.AttachmentsOf(item))
                    {
                        // A document the reply wrote, or a picture it drew: cards above it, not given to the model.
                        if (attachment.Kind == DocumentKind)
                            AddSavedDocumentCard(bubble, attachment.MediaType, await System.IO.File.ReadAllBytesAsync(attachment.Path));
                        else if (attachment.Kind == ImageFeature.GeneratedKind)
                            AddCard(bubble, Images.SavedImageView(await System.IO.File.ReadAllBytesAsync(attachment.Path)));
                    }
                    _ = CheckThinkingLanguageAsync(bubble);
                }
                SetStatus($"Opening conversation: {history.Messages.Count} messages...");
                await Task.Yield();
                history.Load(_conversation);
                int messageCount = history.Messages.Count;
                // The model is given the conversation with the next message, once its size is known (compacting it first if
                // it doesn't fit, e.g. a long chat saved before compaction existed).
                _rereadNext = messageCount > 0;
                // The document the chat used, from its saved index.
                if (!string.IsNullOrEmpty(chat.DocumentId))
                    _ = RestoreDocumentAsync(chat.DocumentId);
                UpdateMemoryLabel();
                await RefreshChatListAsync();
                SetStatus(null);
                UiStallWatchdog.Mark($"Opened saved chat: {messageCount} messages");
            }
            finally
            {
                _restoringTranscript = false;
                SetBusy(false);
                ScrollToEnd(false);
            }
        }
    }
}

#endif
