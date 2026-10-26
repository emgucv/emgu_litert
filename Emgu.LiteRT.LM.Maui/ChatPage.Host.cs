//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui.Features;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    // The page as the host of its features (IChatPageHost).
    public partial class ChatPage
    {
        /// <summary>True when the user lets the agent remember things about them (Settings), there is a memory store, and the chat isn't private.</summary>
        public bool MemoryOn => !IsPrivate && Memory != null && _options.Settings.Get("memory", true);

        /// <summary>The memories about the user (the <see cref="MemoryFeature"/>'s store), or null without one.</summary>
        public IAgentMemoryStore Memory => _features.OfType<MemoryFeature>().FirstOrDefault()?.Skill.Store;

        /// <summary>
        /// True while a reply is being written, or the page is busy with a feature's work (<see cref="SetBusy"/>). (Not
        /// <see cref="Page.IsBusy"/>, the platform's activity indicator.)
        /// </summary>
        public bool IsWorking => Conversation?.IsBusy == true || _busyDepth > 0;

        /// <summary>Mark the page busy with a feature's work (new chats and opening chats wait), or done; counted.</summary>
        /// <param name="busy">True for busy</param>
        public void SetBusy(bool busy)
        {
            _busyDepth = Math.Max(0, _busyDepth + (busy ? 1 : -1));
        }

        Page IChatPageHost.Page => this;

        ChatConversation IChatPageHost.Conversation => Conversation;

        IChatClient IChatPageHost.ChatClient => _client;

        ChatModelOption IChatPageHost.Model => _model;

        bool IChatPageHost.ModelReady => ModelReady;

        bool IChatPageHost.IsBusy => IsWorking;

        bool IChatPageHost.IsReplying => Conversation?.IsBusy == true;

        bool IChatPageHost.IsPrivate => IsPrivate;

        bool IChatPageHost.MemoryOn => MemoryOn;

        bool IChatPageHost.DeveloperMode => DeveloperMode;

        int IChatPageHost.ContextWindow => Conversation?.ContextWindow ?? 4096;

        Func<string, int> IChatPageHost.TokenCounter => Conversation?.CountTokens ?? ChatConversation.ApproximateTokens;

        Size IChatPageHost.PageSize => new Size(Width, Height);

        string IChatPageHost.SettingsLocation => _wide == true ? "the settings panel beside the chat" : "this chat's settings (the Settings button at the top)";

        IAgentMemoryStore IChatPageHost.Memory => Memory;

        IChatPageSettings IChatPageHost.Settings => _options.Settings;

        ChatSkillSet IChatPageHost.Skills => _skills;

        ChatFeatureServices IChatPageHost.Services => _services;

        void IChatPageHost.SetStatus(string message) => SetStatus(message);

        void IChatPageHost.SetBusy(bool busy) => SetBusy(busy);

        void IChatPageHost.AddCardToReply(View card)
        {
            ChatMessageItem reply = Conversation?.Messages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant);
            if (reply == null)
                return;
            reply.Decorations.Add(card);
            _chatView.ScrollToEnd();
        }

        void IChatPageHost.AddNotice(string text) => Conversation?.AddNotice(null, text);

        Task<string> IChatPageHost.AskModelOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken) =>
            AskModelOnceAsync(prompt, maxTokens, cancellationToken);

        Task IChatPageHost.SendMessageAsync(string text) => _chatView.SendAsync(text);

        async Task<bool> IChatPageHost.AskAsync(string title, string message, string accept, string cancel)
        {
            if (cancel == null)
            {
                await DisplayAlertAsync(title, message, accept);
                return true;
            }
            return await DisplayAlertAsync(title, message, accept, cancel);
        }

        Task<string> IChatPageHost.PromptAsync(string title, string message, string accept, string cancel, string initialValue, int maxLength) =>
            DisplayPromptAsync(title, message, accept, cancel, initialValue: initialValue, maxLength: maxLength);

        Task<string> IChatPageHost.ShowSheetAsync(string title, View content, string closeText) => _sheet.ShowAsync(title, content, closeText);

        void IChatPageHost.DismissSheet(string result) => _sheet.Dismiss(result);

        void IChatPageHost.RefreshSettings() => RefreshSettings();
    }
}
