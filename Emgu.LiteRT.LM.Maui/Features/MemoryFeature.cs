//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Skills;
using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// Memories about the user (<see cref="MemorySkill"/>): the agent saves lasting facts the user tells it and is given
    /// them with every message. The settings have the "Remember things about me" switch (kept as "memory"; the page's
    /// MemoryOn) and the list of memories, each with a delete button. Nothing in a private chat.
    /// </summary>
    public sealed class MemoryFeature : SkillFeature<MemorySkill>
    {
        private Label _countLabel;
        private View _switchRow;

        /// <summary>Create the feature.</summary>
        /// <param name="store">Where the memories are kept (e.g. Emgu.LiteRT.LM.Chat.Sqlite's SqliteAgentMemoryStore)</param>
        public MemoryFeature(IAgentMemoryStore store) : base(new MemorySkill(store))
        {
        }

        /// <summary>The switch's state is the page's "memory" setting, not this.</summary>
        public override bool PersistEnabled => false;

        /// <inheritdoc/>
        public override View BuildSettingsSection()
        {
            _switchRow = ChatPage.SwitchRow("Remember things about me", null, Host.Settings.Get("memory", true), on => Host.Settings.Set("memory", on));
            _countLabel = ChatPage.Link("", () => _ = ShowMemoriesAsync());
            SemanticProperties.SetHint(_countLabel, "Shows the memories, to delete one");
            _ = RefreshCountAsync();
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    ChatPage.SectionTitle("Memories about you"),
                    _switchRow,
                    ChatPage.Note("The assistant saves lasting facts you tell it - your name, preferences, plans - and knows them in later chats. They stay on this device; ask it to forget something, or delete it here."),
                    _countLabel
                }
            };
        }

        /// <inheritdoc/>
        public override void OnReplyCompleted(ChatExchangeEventArgs e)
        {
            // The agent may have saved or deleted a memory.
            _ = RefreshCountAsync();
        }

        /// <inheritdoc/>
        public override void OnPrivateModeChanged(bool isPrivate)
        {
            base.OnPrivateModeChanged(isPrivate);
            if (_switchRow != null)
                _switchRow.IsEnabled = !isPrivate;
            if (_countLabel != null)
                _countLabel.IsVisible = !isPrivate;
            if (!isPrivate)
                _ = RefreshCountAsync();
        }

        private async Task RefreshCountAsync()
        {
            if (_countLabel == null || Host == null || Host.IsPrivate)
                return;
            int count = 0;
            try { count = (await Skill.Store.GetMemoriesAsync()).Count; }
            catch (Exception) { }
            _countLabel.Text = count == 0 ? "No memories yet" : count == 1 ? "1 memory - show" : count + " memories - show";
        }

        // The memories in a sheet, each with a button to delete it.
        private async Task ShowMemoriesAsync()
        {
            if (Host == null || Host.IsPrivate)
                return;
            IList<AgentMemory> memories = await Skill.Store.GetMemoriesAsync();
            if (memories.Count == 0)
                return;
            var list = new VerticalStackLayout { Spacing = 2 };
            foreach (AgentMemory memory in memories)
            {
                string id = memory.Id;
                Border row = null;
                row = ChatPage.ListRow(memory.Text, null, false, null, async () =>
                {
                    await Skill.Store.DeleteMemoryAsync(id);
                    list.Children.Remove(row);
                    await RefreshCountAsync();
                }, "Delete this memory");
                list.Children.Add(row);
            }
            await Host.ShowSheetAsync("Memories about you", list, "Close");
            await RefreshCountAsync();
        }
    }
}
