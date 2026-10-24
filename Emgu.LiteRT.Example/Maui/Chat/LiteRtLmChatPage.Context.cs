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

// The context window: its size setting, the estimate of how full it is, and compacting the conversation.
namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        // ---------- Context window ----------

        // The context window to load a model with, or 0 for LanguageModel's default (4096): the setting, or Automatic -
        // the model's DesktopContextTokens; on phones only for models with tools, whose instructions and tool
        // definitions need more than 4k - capped at what the model's file allows.
        private int ContextTokensFor(ModelOption option)
        {
            if (option.MaxContextTokens <= 0)
                return 0;
            int wanted = _contextSetting > 0 ? _contextSetting : AutomaticContextTokens(option);
            return wanted > 0 ? Math.Min(wanted, option.MaxContextTokens) : 0;
        }

        private static int AutomaticContextTokens(ModelOption option)
        {
#if MACCATALYST || WINDOWS
            return option.DesktopContextTokens;
#else
            return option.CanUseTools ? option.DesktopContextTokens : 0;
#endif
        }

        private static string ContextWindowName(int tokens)
        {
            // Thousands, as people say them: 4096 is "4k", 32000 "32k".
            return tokens <= 0 ? "Automatic" : (int)Math.Round(tokens / 1000.0) + "k";
        }

        // The chips act as one choice: tapping the selected one keeps it selected.
        private void OnContextWindowChipToggled(int value, bool on)
        {
            if (_updatingContextChips)
                return;
            if (!on && value == _contextSetting)
            {
                SetContextChips();
                return;
            }
            if (!on)
                return;
            _contextSetting = value;
            try { Preferences.Default.Set(ContextWindowPreference, value); } catch (Exception) { }
            SetContextChips();
            UpdateContextSection();
            UiStallWatchdog.Mark("Context window: " + ContextWindowName(value));
            // Reload the selected model with the new window; the chat continues (compacted on the next message if it no
            // longer fits).
            if (_pageStarted && SelectedOption.MaxContextTokens > 0 && (_model != null || _loadTask != null))
                _ = PrepareModelAsync(false);
        }

        private void SetContextChips()
        {
            _updatingContextChips = true;
            for (int i = 0; i < _contextChips.Count; i++)
                _contextChips[i].IsToggled = ContextWindowChoices[i] == _contextSetting;
            _updatingContextChips = false;
        }

        // Shown for the models whose window can change; the note says what Automatic means here and what a size costs.
        private void UpdateContextSection()
        {
            if (_contextSection == null)
                return;
            ModelOption option = SelectedOption;
            _contextSection.IsVisible = option.MaxContextTokens > 0;
            int automatic = AutomaticContextTokens(option);
            _contextChips[0].Text = "Automatic (" + ContextWindowName(automatic > 0 ? automatic : 4096) + ")";
            string note = "How much of the chat the model keeps in mind. A larger window summarizes the chat less often, " +
                "but the model takes longer to read it again (when a saved chat is opened, or after summarizing) and needs " +
                "more memory. Changing it reloads the model; the chat continues.";
#if ANDROID || IOS
            if (_contextSetting >= 16384)
                note += " On a phone, 16k and more can take many minutes to read a long chat again, and may run out of memory.";
#else
            if (_contextSetting >= 32000)
                note += " At 32k, reading a full chat again took about 4 minutes on an Apple Silicon Mac's CPU.";
#endif
            _contextNote.Text = note;
        }


        private int ContextWindow => SelectedChoice == ModelChoice.Server ? _serverContextTokens
            : _model != null && _model.MaxNumTokens > 0 ? _model.MaxNumTokens : 4096;

        // Counts tokens with the loaded model's tokenizer, or estimates them for a server's model. Call it off the UI
        // thread.
        private Func<string, int> TokenCounter
        {
            get
            {
                LanguageModel model = _model;
                if (SelectedChoice == ModelChoice.Server || model == null)
                    return ApproximateTokens;
                return model.CountTokens;
            }
        }

        // The conversation was compacted (before a message, or "compact now"): the excerpts given with the messages that
        // were summarized or dropped are no longer in the model's context, the tool groups start again, the transcript gets
        // its divider, and the summary is saved with the chat.
        private async void OnConversationCompacted(object sender, ChatCompactedEventArgs e)
        {
            UiStallWatchdog.Mark(string.Format("Compacted at message {0}, summarized {1}", e.ContextStart, e.Summarized));
            ResetDocumentContext();
            _toolRouter?.Reset();
            _rereadNext = true;
            if (e.ContextStart < _conversation.Messages.Count && _conversation.Messages[e.ContextStart].Tag is UserMessageInfo info)
                info.DividerText = e.Summary != null ? "Earlier messages summarized \u00B7 tap to read" : "Earlier messages dropped from the model's memory";
            // The store knows each message it saved or loaded, so it finds the last one the model no longer sees.
            if (_conversationId != null && !IsPrivateChat)
            {
                try
                {
                    await _store.SaveCompactionAsync(_conversationId.Value, _conversation);
                }
                catch (Exception ex)
                {
                    SetStatus("Could not save the chat: " + ex.Message);
                }
            }
            UpdateMemoryLabel();
        }

        // Compact now (Settings): everything but the latest exchange.
        private async void OnCompactTapped()
        {
            if (_busy || _reloadingCpuThreads)
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
                ConfigureConversation();
                if (!await _conversation.CompactAsync())
                    SetStatus("Nothing to compact yet.");
                else
                    SetStatus(null);
                ScrollToEnd();
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

        private void UpdateMemoryLabel() => _ = UpdateMemoryLabelAsync();

        private async Task UpdateMemoryLabelAsync()
        {
            if (_memoryLabel == null)
                return;
            string text = string.Format(
                "The model takes in about {0:N0} tokens. Longer chats are summarized - or, for the small models, their " +
                "earliest messages are dropped - so it keeps up; the conversation here stays complete.", ContextWindow);
            int visible = ChatMessages.Skip(Math.Min(_conversation.ContextStart, _conversation.Messages.Count)).Count();
            if (ModelReady && visible > 0 && !_busy)
            {
                try
                {
                    // With what the chat template, or the agent's instructions and tools, take.
                    ConfigureConversation();
                    ChatContextUsage usage = await _conversation.GetContextUsageAsync();
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
    }
}

#endif
