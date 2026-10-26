//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui
{
    // The conversation: its settings for the model, new chats, saved chats (listing, opening, deleting) and private chats.
    public partial class ChatPage
    {
        private IChatHistoryBinding _binding;
        private int? _chatId;
        private VerticalStackLayout _sideChatList;
        private Switch _privateSwitch;
        private bool _changingPrivate;

        /// <summary>The saved chat being shown (null until its first exchange is saved, or without saved chats).</summary>
        public int? CurrentChatId => _chatId;

        /// <summary>True in a private chat: nothing is saved, nothing saved is read.</summary>
        public bool IsPrivate => _options.History?.IsPrivate == true;

        // A new conversation (a new chat, or another model): the model's sizes, how it compacts, and saving it.
        private void OnConversationChanged()
        {
            ChatConversation conversation = _chatView.Conversation;
            if (conversation == null)
                return;
            LanguageModel model = _chatView.Model;
            ChatModelOption option = _model;
            conversation.CountTokens = text => model != null && model.Initialized ? model.CountTokens(text) : ChatConversation.ApproximateTokens(text);
            conversation.ContextWindow = option?.ContextWindow > 0 ? option.ContextWindow : model?.MaxNumTokens ?? conversation.ContextWindow;
            conversation.Compaction = option?.CanSummarize == true ? ChatCompaction.Summarize : ChatCompaction.Drop;
            // The instructions and the core tools that come with every message, roughly.
            conversation.OverheadTokens = option?.CanUseTools == true ? 900 : 50;
            conversation.DescribeToolCall = AgentTools.Describe;
            _skills.Reset();
            AttachHistory(0);
            SetStatus(null);
        }

        private void AttachHistory(int chatId)
        {
            _binding?.Dispose();
            _binding = null;
            _chatId = chatId > 0 ? chatId : (int?)null;
            ChatConversation conversation = _chatView.Conversation;
            if (_options.History == null || conversation == null)
                return;
            IChatHistoryBinding binding = _options.History.Attach(conversation, chatId, _model?.Id);
            binding.ChatCreated += (s, e) => Dispatcher.Dispatch(() =>
            {
                if (_binding == binding)
                    _chatId = binding.ChatId;
                RefreshChatList();
            });
            _binding = binding;
        }

        /// <summary>Start a new chat: the transcript and the model's conversation are cleared, the next message starts a new saved chat.</summary>
        public void StartNewChat()
        {
            if (IsWorking)
            {
                SetStatus("Hang on - still answering.");
                return;
            }
            _chatView.StopReply();
            Conversation?.Clear();
            _skills.Reset();
            AttachHistory(0);
            SetStatus(null);
            RefreshChatList();
        }

        /// <summary>Open a saved chat: its model is chosen, its messages shown, and the model is given the conversation with the next message.</summary>
        /// <param name="chatId">The chat</param>
        public async Task OpenChatAsync(int chatId)
        {
            if (_options.History == null || IsWorking)
                return;
            if (!await LeavePrivateChatAsync())
                return;
            ChatHistoryEntry entry = (await _options.History.ListChatsAsync()).FirstOrDefault(c => c.Id == chatId);
            if (entry == null)
                return;
            SetBusy(true);
            try
            {
                ChatModelOption option = _options.Models.FirstOrDefault(m => m.Id == entry.Model);
                if (option != null && option != _model)
                    SelectModel(option, _backend);
                else
                    Conversation.Clear();
                _skills.Reset();
                SetStatus("Opening the chat...");
                if (!await _options.History.LoadChatAsync(chatId, Conversation))
                    return;
                AttachHistory(chatId);
            }
            finally
            {
                SetBusy(false);
                SetStatus(null);
            }
            RefreshChatList();
            _chatView.ScrollToEnd();
        }

        private async void OnChatsTapped()
        {
            if (_options.History == null || IsWorking)
                return;
            if (!await LeavePrivateChatAsync())
                return;
            string action = await _sheet.ShowAsync("Chats", await BuildChatListAsync(true), "Close");
            await HandleChatActionAsync(action, true);
        }

        private async Task HandleChatActionAsync(string action, bool reopenSheet)
        {
            if (string.IsNullOrEmpty(action))
                return;
            int id = int.Parse(action.Substring(action.IndexOf(':') + 1), System.Globalization.CultureInfo.InvariantCulture);
            if (action.StartsWith("delete:", StringComparison.Ordinal))
            {
                // A deleted chat can't be restored, and the cross is next to the row that opens it: ask first.
                string title = (await _options.History.ListChatsAsync()).FirstOrDefault(c => c.Id == id)?.Title;
                if (!await DisplayAlertAsync("Delete this chat?", "\"" + title + "\" and its photos and recordings will be removed from this device.", "Delete", "Cancel"))
                {
                    if (reopenSheet)
                        OnChatsTapped();
                    return;
                }
                await _options.History.DeleteChatAsync(id);
                if (_chatId == id)
                    StartNewChat();
                RefreshChatList();
                if (reopenSheet)
                    OnChatsTapped();
            }
            else
            {
                await OpenChatAsync(id);
            }
        }

        // The saved chats: title, model and date; tapping opens one, the cross deletes it.
        private async Task<View> BuildChatListAsync(bool inSheet)
        {
            var list = new VerticalStackLayout { Spacing = 2 };
            IReadOnlyList<ChatHistoryEntry> chats;
            try { chats = await _options.History.ListChatsAsync(); }
            catch (Exception) { chats = Array.Empty<ChatHistoryEntry>(); }
            if (chats.Count == 0)
            {
                list.Children.Add(Note("No saved chats yet - they appear here after the first reply."));
                return list;
            }
            foreach (ChatHistoryEntry chat in chats)
            {
                int id = chat.Id;
                string modelName = _options.Models.FirstOrDefault(m => m.Id == chat.Model)?.DisplayName ?? chat.Model;
                string detail = (string.IsNullOrEmpty(modelName) ? string.Empty : modelName + " · ") + chat.Updated.ToLocalTime().ToString("g");
                list.Children.Add(ListRow(chat.Title, detail, id == _chatId,
                    () =>
                    {
                        if (inSheet) _sheet.Dismiss("open:" + id);
                        else _ = HandleChatActionAsync("open:" + id, false);
                    },
                    () =>
                    {
                        if (inSheet) _sheet.Dismiss("delete:" + id);
                        else _ = HandleChatActionAsync("delete:" + id, false);
                    }, "Delete this chat"));
            }
            return list;
        }

        private async void RefreshChatList()
        {
            if (_sideChatList == null || _wide != true || _options.History == null)
                return;
            try
            {
                View list = await BuildChatListAsync(false);
                _sideChatList.Children.Clear();
                _sideChatList.Children.Add(list);
            }
            catch (Exception)
            {
            }
        }

        // ---------- Private chats ----------

        private View BuildPrivateChatSection()
        {
            _privateSwitch = new Switch { IsToggled = IsPrivate, OnColor = ChatResources.FindColor("ChatAccent"), VerticalOptions = LayoutOptions.Center };
            _privateSwitch.Toggled += OnPrivateToggled;
            var label = new Label { Text = "Private chat" };
            ChatResources.ApplyStyle(label, "ChatSectionTextStyle");
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(label, 0, 0);
            row.Add(_privateSwitch, 1, 0);
            SemanticProperties.SetDescription(_privateSwitch, "Start or end a private chat that is not saved");
            return new VerticalStackLayout
            {
                Spacing = 4,
                Children = { row, Note("Starts a fresh chat that is not saved. Memories and goals are unavailable. Web tools still send requests online when enabled.") }
            };
        }

        private async void OnPrivateToggled(object sender, ToggledEventArgs e)
        {
            bool requested = e.Value;
            if (_changingPrivate || requested == IsPrivate)
                return;
            if (IsWorking)
            {
                SetPrivateSwitch(IsPrivate);
                SetStatus("Wait for the reply to finish before changing Private chat.");
                return;
            }
            _changingPrivate = true;
            try
            {
                if (!requested && !await ConfirmEndPrivateChatAsync())
                    return;
                ChangePrivateMode(requested);
            }
            finally
            {
                SetPrivateSwitch(IsPrivate);
                _changingPrivate = false;
            }
        }

        private void SetPrivateSwitch(bool value)
        {
            if (_privateSwitch == null)
                return;
            _privateSwitch.Toggled -= OnPrivateToggled;
            _privateSwitch.IsToggled = value;
            _privateSwitch.Toggled += OnPrivateToggled;
        }

        /// <summary>Start a private chat (nothing saved, memories and goals off), or end it (after asking, when it has messages).</summary>
        /// <param name="isPrivate">True to start one</param>
        /// <returns>True if the chat is now as asked</returns>
        public async Task<bool> SetPrivateAsync(bool isPrivate)
        {
            if (_options.History == null)
                return false;
            if (isPrivate == IsPrivate)
                return true;
            if (IsWorking)
                return false;
            if (!isPrivate && !await ConfirmEndPrivateChatAsync())
                return false;
            ChangePrivateMode(isPrivate);
            SetPrivateSwitch(isPrivate);
            return IsPrivate == isPrivate;
        }

        private Task<bool> ConfirmEndPrivateChatAsync()
        {
            if (Conversation == null || Conversation.Messages.Count == 0)
                return Task.FromResult(true);
            return DisplayAlertAsync("End private chat?", "This conversation will be discarded. It cannot be reopened.", "End private chat", "Cancel");
        }

        private void ChangePrivateMode(bool value)
        {
            _options.History.IsPrivate = value;
            _skills.SetPrivate(value);
            StartNewChat();
            UpdatePrivateState();
            SetStatus(value ? "Private chat · not saved. Memories and goals are off." : "Private chat ended. New messages will be saved.");
        }

        private async Task<bool> LeavePrivateChatAsync()
        {
            if (!IsPrivate)
                return true;
            if (IsWorking || !await ConfirmEndPrivateChatAsync())
                return false;
            ChangePrivateMode(false);
            SetPrivateSwitch(false);
            return !IsPrivate;
        }

        private void UpdatePrivateState()
        {
            if (_titleLabel != null)
                _titleLabel.Text = IsPrivate ? _options.Title + " · Private" : _options.Title;
            if (_chatsButton != null)
                _chatsButton.IsVisible = _options.History != null;
            UpdateChips();
        }
    }
}
