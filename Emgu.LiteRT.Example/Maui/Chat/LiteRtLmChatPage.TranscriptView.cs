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

// The transcript: reply bubbles, step lines, cards above replies, scrolling.
namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    // ---------- Transcript ----------

    private void ClearTranscript()
    {
        ClearVisualSnapshots();
#if ANDROID
        ClearVisualClips();
        ClearVisualPhotos();
#endif
        _agentBubble = null;
        _conversation.Clear();
    }

    // A reply's bubble data, with its translate row.
    private StreamingBubble BubbleFor(ChatMessageItem item)
    {
        var row = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.Accent, IsVisible = false };
        var bubble = new StreamingBubble(item, row);
        row.OnTap(() => Translations.OnTranslateTapped(bubble));
        return bubble;
    }

    // A message the conversation adds: the user's gets the data of the send (its language, its voice message's words),
    // a reply its bubble data - which tools that show cards use (_agentBubble) - with the send's document step, and
    // updates the status, the read-aloud and the step lines while it streams.
    private void OnConversationMessagesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add || e.NewItems == null)
            return;
        foreach (ChatMessageItem item in e.NewItems)
        {
            if (item.Tag != null)
                continue; // restored, or the page's own
            if (item.Role == ChatMessageRole.User && _pendingUserInfo != null)
            {
                UserMessageInfo info = _pendingUserInfo;
                item.Tag = info;
                if (_pendingVoice != null)
                    info.TranscriptTask = TranscribeVoiceMessageAsync(info, _pendingVoice);
            }
            else if (item.Role == ChatMessageRole.Assistant)
            {
                StreamingBubble bubble = BubbleFor(item);
                _agentBubble = bubble;
                if (_pendingDocumentStep != null)
                {
                    bubble.DocumentStep = _pendingDocumentStep;
                    AddCard(bubble, StepLine(_pendingDocumentStep));
                }
                WatchReply(item, _pendingReader);
            }
        }
    }

    // While a reply streams: the status ("Generating..." until it starts, the tool being called), the read-aloud, and
    // the step lines' outcomes.
    private void WatchReply(ChatMessageItem item, ReplyReader reader)
    {
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ChatMessageItem.Text))
            {
                if (item.IsStreaming && item.Steps.All(step => !step.IsRunning))
                    SetStatus(null);
                reader?.Update(item.Text);
            }
            else if (e.PropertyName == nameof(ChatMessageItem.Thinking) && item.Text.Length == 0 && item.IsStreaming && item.Steps.All(step => !step.IsRunning))
                SetStatus(null);
        };
        item.Steps.CollectionChanged += (s, e) =>
        {
            if (e.NewItems == null)
                return;
            foreach (ChatToolStep step in e.NewItems)
            {
                if (step.Call == null)
                    continue;
                UiStallWatchdog.Mark("Tool call: " + step.Description);
                SetStatus(step.Description + "...");
                string described = step.Description;
                step.PropertyChanged += (o, p) =>
                {
                    if (p.PropertyName != nameof(ChatToolStep.IsRunning) || step.IsRunning)
                        return;
                    UiStallWatchdog.Mark("Tool result" + (step.Error != null ? ": " + step.Error : ""));
                    string resultText = step.Result?.Result?.ToString() ?? string.Empty;
                    // A fact the model tried to save again isn't a new memory: say so instead of "Remembering".
                    if (resultText.StartsWith("Already remembered", StringComparison.Ordinal))
                        step.Description = "Already remembered";
                    else if (step.Error == null && (DeviceActionsFeature.ToolNames.Contains(step.Name) || IsMcpFunction(step.Name)))
                        // A device action or a tool server's tool: what the user chose, also in the saved chat.
                        step.Description = described + (IsMcpFunction(step.Name) ? McpStepOutcome(resultText) : DeviceActionsFeature.StepOutcome(resultText));
                    SetStatus("Generating...");
                };
            }
        };
    }

    // A grey step line like the tool calls', for the page's own steps (the document's).
    private static Label StepLine(string text) => new Label
    {
        Text = "\U0001F50E " + text,
        FontFamily = Theme.BodyFont,
        FontSize = 13,
        TextColor = Theme.SecondaryText,
        Margin = new Thickness(6, 0)
    };

    // A card above a reply (one of its decorations); without a reply, a notice of its own in the transcript.
    private void AddCard(StreamingBubble bubble, View card)
    {
        if (bubble != null)
            bubble.Item.Decorations.Add(card);
        else
            _conversation.AddNotice(card);
        ScrollToEnd();
    }

    // Take a card out of the transcript again. (A notice left empty stays - it shows nothing - since removing it would
    // move the conversation's ContextStart index.)
    private void RemoveCard(View card)
    {
        foreach (ChatMessageItem item in _conversation.Messages)
            if (item.Decorations.Remove(card))
                return;
    }

    private bool _restoringTranscript;

    private void ScrollToEnd(bool animated = true)
    {
        if (!_restoringTranscript)
            _chatView.ScrollToEnd(animated);
    }
}
