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

        // Whether the model reads the whole conversation with the next message (a reopened chat, after compacting, a model
        // loaded again): the status says so, since that is what takes the time.
        private bool _rereadNext;

        /// <summary>
        /// True when the model will read the whole conversation with the next message (a reopened chat, a model loaded
        /// again, after compacting): the status says so while it does. A derived page sets it when it reloads a model.
        /// </summary>
        protected bool RereadNext
        {
            get => _rereadNext;
            set => _rereadNext = value;
        }

        // A new conversation (a new chat, or another model): the model's sizes, how it compacts, and saving it.
        private void OnConversationChanged()
        {
            ChatConversation conversation = _chatView.Conversation;
            if (conversation == null)
                return;
            ConfigureConversation(conversation, _model, _chatView.Model);
            conversation.BeforeStreaming = OnBeforeStreamingAsync;
            conversation.Compacted += OnConversationCompacted;
            conversation.Messages.CollectionChanged += (s, e) =>
            {
                foreach (ChatMessageItem reply in e.NewItems?.OfType<ChatMessageItem>().Where(m => m.Role == ChatMessageRole.Assistant && m.IsStreaming) ?? Enumerable.Empty<ChatMessageItem>())
                    WatchSteps(reply);
            };
            _rereadNext = false;
            _skills.Reset();
            AttachHistory(0);
            SetStatus(null);
            UpdateMemoryLabel();
        }

        /// <summary>
        /// Set a conversation up for a model: how it counts tokens, its context window, how it compacts, the room the
        /// instructions and tools take, and how tool calls are described. Called for each new conversation and when the
        /// model is reloaded; a derived page may change the settings afterwards.
        /// </summary>
        /// <param name="conversation">The conversation</param>
        /// <param name="option">The model chosen</param>
        /// <param name="model">The model's instance (not loaded yet, perhaps), or null for a remote model</param>
        protected virtual void ConfigureConversation(ChatConversation conversation, ChatModelOption option, LanguageModel model)
        {
            conversation.CountTokens = text => model != null && model.Initialized ? model.CountTokens(text) : ChatConversation.ApproximateTokens(text);
            // The window the engine loads with (NewInstance set it from the user's choice); a remote model's is the option's.
            conversation.ContextWindow = model != null && model.MaxNumTokens > 0 ? model.MaxNumTokens
                : option?.ContextWindow > 0 ? option.ContextWindow : conversation.ContextWindow;
            conversation.Compaction = option?.CanSummarize == true ? ChatCompaction.Summarize : ChatCompaction.Drop;
            // The instructions and the core tools that come with every message, roughly.
            conversation.OverheadTokens = option?.CanUseTools == true ? 900 : 50;
            conversation.DescribeToolCall = DescribeToolCall;
        }

        /// <summary>
        /// Called with the message about to be sent, after the conversation was made to fit the context window and
        /// before the request goes out (<see cref="ChatConversation.BeforeStreaming"/>): the base shows "Reading the
        /// conversation so far..." when the model re-reads it (<see cref="RereadNext"/>). A derived page may change the
        /// message's <see cref="ChatMessageItem.Context"/> here (e.g. document excerpts searched again after compacting).
        /// </summary>
        /// <param name="message">The message being sent</param>
        /// <param name="cancellationToken">Stops the send</param>
        protected virtual async Task OnBeforeStreamingAsync(ChatMessageItem message, CancellationToken cancellationToken)
        {
            await RefreshAdditionsAsync(message, cancellationToken);
            if (_rereadNext)
            {
                _rereadNext = false;
                _chatView.SetStatus("Reading the conversation so far...");
            }
        }

        private void AttachHistory(int chatId)
        {
            _binding?.Dispose();
            _binding = null;
            _chatId = chatId > 0 ? chatId : (int?)null;
            ChatConversation conversation = _chatView.Conversation;
            if (_options.History == null || conversation == null || !_options.AutoSave)
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

        /// <summary>
        /// Tell the page which saved chat the conversation is (an app that saves the chats itself, <see cref="ChatPageOptions.AutoSave"/>
        /// off): the chat list marks it, and a new chat clears it.
        /// </summary>
        /// <param name="chatId">The chat, or null for one not saved yet</param>
        protected void SetCurrentChat(int? chatId)
        {
            bool saved = chatId != null && chatId != _chatId;
            _chatId = chatId;
            RefreshChatList();
            if (!saved)
                return;
            foreach (ChatFeature feature in _features)
            {
                try { feature.OnChatSaved(chatId.Value); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Start a new chat: the transcript and the model's conversation are cleared, the next message starts a new saved
        /// chat. A derived page adds what else to reset, then calls the base. Nothing while a reply is being written.
        /// </summary>
        public virtual void StartNewChat()
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
            UpdateMemoryLabel();
        }

        /// <summary>
        /// Open a saved chat: its model is chosen (a new conversation), its messages shown, and the model is given the
        /// conversation with the next message. A derived page that saves chats itself overrides it to read them back its
        /// own way, then calls <see cref="SetCurrentChat"/> and <see cref="OnChatOpened"/>.
        /// </summary>
        /// <param name="chatId">The chat</param>
        public virtual async Task OpenChatAsync(int chatId)
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
                PrepareForChat(entry);
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
            OnChatOpened(chatId);
        }

        /// <summary>
        /// Before a saved chat's messages are shown: choose its model (a new conversation), or clear the conversation if
        /// the model is the one in use; the tool groups start again.
        /// </summary>
        /// <param name="entry">The chat</param>
        protected void PrepareForChat(ChatHistoryEntry entry)
        {
            ChatModelOption option = _options.Models.FirstOrDefault(m => m.Id == entry.Model);
            if (option != null && option != _model)
                SelectModel(option, _backend);
            else
                Conversation?.Clear();
            _skills.Reset();
        }

        /// <summary>After a saved chat's messages were shown: the model re-reads them with the next message, the features are told, the list is marked.</summary>
        /// <param name="chatId">The chat</param>
        protected void OnChatOpened(int chatId)
        {
            RereadNext = true;
            foreach (ChatFeature feature in _features)
            {
                try { feature.OnChatOpened(chatId); }
                catch (Exception) { }
            }
            RefreshChatList();
            UpdateMemoryLabel();
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
            int id = int.Parse(action.Substring(action.LastIndexOf(':') + 1), System.Globalization.CultureInfo.InvariantCulture);
            if (action.StartsWith("action:", StringComparison.Ordinal))
            {
                // One of the app's buttons on the row: "action:{index}:{id}".
                string[] parts = action.Split(':');
                ChatHistoryEntry entry = (await _options.History.ListChatsAsync()).FirstOrDefault(c => c.Id == id);
                if (parts.Length == 3 && int.TryParse(parts[1], out int index) && index >= 0 && index < _options.ChatActions.Count && entry != null)
                {
                    try { await _options.ChatActions[index].Run(entry); }
                    catch (Exception ex) { SetStatus(_options.ChatActions[index].Text + ": " + ex.Message); }
                }
                return;
            }
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
                var actions = new List<(string Text, Action Tapped)>();
                for (int i = 0; i < _options.ChatActions.Count; i++)
                {
                    string key = "action:" + i + ":" + id;
                    actions.Add((_options.ChatActions[i].Text, () =>
                    {
                        if (inSheet) _sheet.Dismiss(key);
                        else _ = HandleChatActionAsync(key, false);
                    }));
                }
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
                    }, "Delete this chat", actions));
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
                await ChangePrivateModeAsync(requested);
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
            await ChangePrivateModeAsync(isPrivate);
            SetPrivateSwitch(IsPrivate);
            return IsPrivate == isPrivate;
        }

        /// <summary>
        /// Called before the chat becomes private or stops being private, with the page marked as reloading: a derived
        /// page releases or restores what the change affects (documents, indexes...). False cancels the change.
        /// </summary>
        /// <param name="isPrivate">True when a private chat starts</param>
        protected virtual Task<bool> OnPrivateModeChangingAsync(bool isPrivate) => Task.FromResult(true);

        private Task<bool> ConfirmEndPrivateChatAsync()
        {
            if (Conversation == null || Conversation.Messages.Count == 0)
                return Task.FromResult(true);
            return DisplayAlertAsync("End private chat?", "This conversation will be discarded. It cannot be reopened.", "End private chat", "Cancel");
        }

        private async Task ChangePrivateModeAsync(bool value)
        {
            using (BeginReload())
            {
                try
                {
                    if (!await OnPrivateModeChangingAsync(value))
                        return;
                }
                catch (Exception ex)
                {
                    SetStatus("Could not change private chat: " + ex.Message);
                    return;
                }
                _options.History.IsPrivate = value;
                _skills.SetPrivate(value);
            }
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
            await ChangePrivateModeAsync(false);
            SetPrivateSwitch(IsPrivate);
            return !IsPrivate;
        }

        // ---------- Tool steps ----------

        // Tell the features when a tool call of a reply has its result (a feature may reword the step with the outcome).
        private void WatchSteps(ChatMessageItem reply)
        {
            reply.Steps.CollectionChanged += (s, e) =>
            {
                foreach (ChatToolStep step in e.NewItems?.OfType<ChatToolStep>() ?? Enumerable.Empty<ChatToolStep>())
                {
                    if (!step.IsRunning)
                    {
                        OnToolStepCompleted(step);
                        continue;
                    }
                    System.ComponentModel.PropertyChangedEventHandler handler = null;
                    handler = (sender, args) =>
                    {
                        if (args.PropertyName != nameof(ChatToolStep.IsRunning) || step.IsRunning)
                            return;
                        step.PropertyChanged -= handler;
                        OnToolStepCompleted(step);
                    };
                    step.PropertyChanged += handler;
                }
            };
        }

        private void OnToolStepCompleted(ChatToolStep step)
        {
            foreach (ChatFeature feature in _features)
            {
                try { feature.OnToolStepCompleted(step); }
                catch (Exception) { }
            }
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
