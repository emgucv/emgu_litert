//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite;

// The model thinks in English whatever language it is asked in (TODO item 39). For a message in another language, the
// opened thinking offers "Translate to <language>": the model translates it in a conversation of its own (like the
// summaries) - on request, so unread thinking costs nothing - and the translation is kept with the reply, with a way back to
// the English. The language of a message is detected when it is sent (TextLanguage) and kept with it (MessageRecord.Language).
public partial class LiteRtLmChatPage
{
    // The language to translate into: that of the message a reply answers, unless it is English or not known.
    private static string TranslationTarget(string language) =>
        string.IsNullOrEmpty(language) || language == "en" ? null : language;

    private static string LanguageName(string code, bool english)
    {
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(code);
            return english ? culture.EnglishName : culture.NativeName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    // A reply whose thinking is already in the language of the message (some models think in it) needs no translation:
    // only after this check does the row appear.
    private async Task CheckThinkingLanguageAsync(StreamingBubble bubble)
    {
        string thinking = bubble.Reply?.Thinking;
        if (bubble.TargetLanguage == null || string.IsNullOrEmpty(thinking))
            return;
        string sample = thinking.Length > 600 ? thinking.Substring(0, 600) : thinking;
        string language = await TextLanguage.DetectAsync(sample);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (language == bubble.TargetLanguage && bubble.Translation == null)
                bubble.TargetLanguage = null;
            bubble.TranslateReady = true;
            ShowThinking(bubble);
        });
    }

    // The line above an opened thinking: translate it, or switch between the translation and the English.
    private void ShowTranslateRow(StreamingBubble bubble, bool expanded)
    {
        bool offer = expanded && bubble.TranslateReady && bubble.TargetLanguage != null && !string.IsNullOrEmpty(bubble.Reply?.Thinking);
        bubble.TranslateRow.IsVisible = offer;
        if (bubble.TargetLanguage == null)
            return;
        string name = LanguageName(bubble.TargetLanguage, false);
        bubble.TranslateRow.Text = bubble.Translating ? "Translating to " + name + "..."
            : bubble.Translation == null ? "Translate to " + name
            : bubble.ShowTranslation ? "Translated to " + name + " · Show the English"
            : "The English original · Show " + name;
        SemanticProperties.SetDescription(bubble.TranslateRow, bubble.TranslateRow.Text);
    }

    private async void OnTranslateTapped(StreamingBubble bubble)
    {
        if (bubble.Translating || bubble.TargetLanguage == null)
            return;
        if (bubble.Translation != null)
        {
            bubble.ShowTranslation = !bubble.ShowTranslation;
            ShowThinking(bubble);
            return;
        }
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
        string thinking = bubble.Reply?.Thinking;
        string language = bubble.TargetLanguage;
        bubble.Translating = true;
        ShowThinking(bubble);
        SetBusy(true);
        SetStatus("Translating the thinking...");
        try
        {
            string translation = await TranslateTextAsync(thinking, language);
            bubble.Translation = translation;
            bubble.ShowTranslation = true;
            if (!IsPrivateChat)
                await _store.SetThinkingTranslationAsync(bubble.MessageId, language, translation);
            SetStatus(null);
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't translate: " + ex.Message);
        }
        finally
        {
            bubble.Translating = false;
            SetBusy(false);
            ShowThinking(bubble);
        }
    }

    // Translate a text with the model, in conversations of their own - one per piece if it is long, since the window is
    // small. The chat's and the agent's own conversations are released first (as for a summary) and given the conversation
    // again with the next message.
    private async Task<string> TranslateTextAsync(string text, string language)
    {
        string name = LanguageName(language, true);
        Func<string, int> countTokens = TokenCounter;
        int pieceTokens = Math.Max(200, Math.Min(1200, (ContextWindow - 500) / 3));
        List<string> pieces = await Task.Run(() => LanguageText.SplitForTranslation(text, pieceTokens, countTokens));
        string Prompt(string piece) =>
            "Translate the text below into " + name + ". It is an AI assistant's private reasoning notes. Keep code, file names, " +
            "tool or function names (words with underscores), numbers and the Markdown formatting unchanged. " +
            "Write only the translation, nothing else.\n\nText:\n" + piece;
        var result = new StringBuilder();
        if (SelectedChoice == ModelChoice.Server)
        {
            foreach (string piece in pieces)
                result.Append(result.Length > 0 ? "\n\n" : "").Append(await SummarizeOnServerAsync(Prompt(piece)));
            return result.ToString();
        }
        LanguageModel model = _model;
        ResetAgent();
        _chat?.LoadHistory(Array.Empty<Emgu.LiteRT.LM.Models.ChatMessage>());
        _needsPrime = true;
        foreach (string piece in pieces)
        {
            Chat translator = model.CreateChat();
            try
            {
                // A language can need more tokens than the English (Hindi, Thai: about three times).
                translator.MaxOutputTokens = Math.Max(256, Math.Min(ContextWindow - pieceTokens - 200, pieceTokens * 3));
                translator.EnableThinking = false;
                ChatReply reply = await translator.SendAsync(Prompt(piece));
                string translated = reply.Text.Trim();
                if (translated.Length == 0)
                    throw new Exception("The translation is empty");
                result.Append(result.Length > 0 ? "\n\n" : "").Append(translated);
            }
            finally
            {
                await Task.Run(() => translator.Dispose());
            }
        }
        return result.ToString();
    }
}

#endif
