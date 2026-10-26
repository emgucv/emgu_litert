//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    // Settings > Conversation memory: how full the model's context window is, compacting the chat by hand, and the
    // context window to load the model with (for models whose file allows a choice).
    public partial class ChatPage
    {
        private Label _memoryLabel;
        private Label _compactButton;
        private VerticalStackLayout _contextSection;
        private Label _contextNote;
        private readonly List<ToggleChip> _contextChips = new List<ToggleChip>();
        private bool _updatingContextChips;

        /// <summary>The context window the user chose, in tokens, or 0 for Automatic (the "context_tokens" setting).</summary>
        public int ContextSetting
        {
            get
            {
                int setting = _options.Settings.Get("context_tokens", 0);
                return Array.IndexOf(ChatContextWindow.Choices, setting) >= 0 ? setting : 0;
            }
        }

        private View BuildConversationMemorySection()
        {
            _memoryLabel = Note(string.Empty);
            _compactButton = Link("Compact now", () => _ = CompactAsync());
            SemanticProperties.SetDescription(_compactButton, "Summarize the earlier messages now");
            var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            _contextChips.Clear();
            foreach (int choice in ChatContextWindow.Choices)
            {
                int value = choice;
                var chip = new ToggleChip(ChatContextWindow.Name(value)) { Margin = new Thickness(0, 0, 6, 6), IsToggled = value == ContextSetting };
                chip.Toggled += (s, e) => OnContextChipToggled(value, e.Value);
                _contextChips.Add(chip);
                chips.Children.Add(chip);
            }
            _contextNote = Note(string.Empty);
            _contextSection = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { SectionTitle("Context window"), chips, _contextNote }
            };
            UpdateContextSection();
            UpdateMemoryLabel();
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children = { SectionTitle("Conversation memory"), _memoryLabel, _compactButton, _contextSection }
            };
        }

        // The chips act as one choice: tapping the chosen one keeps it.
        private void OnContextChipToggled(int value, bool on)
        {
            if (_updatingContextChips)
                return;
            if (!on)
            {
                if (value == ContextSetting)
                    SetContextChips();
                return;
            }
            _options.Settings.Set("context_tokens", value);
            SetContextChips();
            UpdateContextSection();
            // The model loads again with the new window; the chat goes on (compacted with the next message if it no longer fits).
            if (_started && _model?.MaxContextWindow > 0)
                _ = ReloadModelAsync();
        }

        private void SetContextChips()
        {
            _updatingContextChips = true;
            int setting = ContextSetting;
            for (int i = 0; i < _contextChips.Count; i++)
                _contextChips[i].IsToggled = ChatContextWindow.Choices[i] == setting;
            _updatingContextChips = false;
        }

        // Shown for the models whose window can change; the note says what Automatic means here and what a size costs.
        private void UpdateContextSection()
        {
            if (_contextSection == null)
                return;
            ChatModelOption option = _model;
            _contextSection.IsVisible = _options.ShowContextWindow && option?.MaxContextWindow > 0;
            if (option == null)
                return;
            int automatic = ChatContextWindow.Automatic(option.ContextWindow, OnPhone, option.CanUseTools);
            _contextChips[0].Text = "Automatic (" + ChatContextWindow.Name(automatic > 0 ? automatic : 4096) + ")";
            _contextNote.Text = ChatContextWindow.Note(ContextSetting, OnPhone);
        }

        /// <summary>Update the "takes in about N tokens... this chat uses about N" line (after a reply, a reload, an opened chat).</summary>
        protected void UpdateMemoryLabel() => _ = UpdateMemoryLabelAsync();

        private async Task UpdateMemoryLabelAsync()
        {
            if (_memoryLabel == null)
                return;
            ChatConversation conversation = Conversation;
            int window = conversation?.ContextWindow ?? 4096;
            string text = string.Format(
                "The model takes in about {0:N0} tokens. Longer chats are summarized - or, for the small models, their " +
                "earliest messages are dropped - so it keeps up; the conversation here stays complete.", window);
            int visible = conversation == null ? 0
                : conversation.Messages.Skip(Math.Min(conversation.ContextStart, conversation.Messages.Count)).Count(m => m.Role != ChatMessageRole.Notice);
            if (conversation != null && ModelReady && visible > 0 && !IsWorking)
            {
                try
                {
                    ChatContextUsage usage = await conversation.GetContextUsageAsync();
                    text += string.Format(" This chat uses about {0:N0} ({1:P0}).", usage.UsedTokens, usage.Fraction);
                }
                catch (Exception)
                {
                    // Not counted (e.g. the model is reloading).
                }
            }
            _memoryLabel.Text = text;
            _compactButton.IsVisible = visible >= 4;
        }

        // Compact now: everything but the latest exchange.
        private async Task CompactAsync()
        {
            ChatConversation conversation = Conversation;
            if (conversation == null)
                return;
            if (IsWorking)
            {
                SetStatus("Hang on - still answering.");
                return;
            }
            if (!ModelReady)
            {
                SetStatus("Load the model first.");
                return;
            }
            SetBusy(true);
            try
            {
                SetStatus(await conversation.CompactAsync() ? null : "Nothing to compact yet.");
                _chatView.ScrollToEnd();
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

        // The conversation was compacted (before a message, or "compact now"): the tool groups start again, and the model
        // reads the conversation again with the next message.
        private void OnConversationCompacted(object sender, ChatCompactedEventArgs e)
        {
            _skills.Router.Reset();
            RereadNext = true;
            UpdateMemoryLabel();
        }
    }
}
