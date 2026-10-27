//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// The model thinks in English whatever language it is asked in. For a message in another language, the opened thinking
/// offers "Translate to &lt;language&gt;" (a row under its header, the reply's <see cref="ChatMessageItem.ThinkingAccessory"/>):
/// the model translates it in requests of its own (like the summaries) - on request, so unread thinking costs nothing -
/// and the translation is kept with the reply (<see cref="ChatMessageItem.ThinkingTranslation"/>, saved through
/// <see cref="IChatPageHost.SaveMessageChangesAsync"/>), with a way back to the English. A message's language is
/// detected when its reply is done (<see cref="TextLanguage"/>) and kept with it (<see cref="ChatMessageItem.Language"/>);
/// a reply whose thinking is already in that language gets no row. Opened chats get their rows again. No tools.
/// </summary>
public sealed class TranslationFeature : ChatFeature
{
    /// <inheritdoc/>
    public override string Name => "translation";

    /// <inheritdoc/>
    public override string Title => "Thinking translation";

    /// <summary>Always on (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <summary>The language to translate into: that of the message a reply answers, unless it is English or not known.</summary>
    /// <param name="language">The message's language (a two-letter code), or null</param>
    public static string Target(string language) =>
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

    /// <inheritdoc/>
    public override void OnReplyCompleted(ChatExchangeEventArgs e)
    {
        if (e?.Message != null && e.Reply != null)
            _ = OfferAsync(e.Message, e.Reply);
    }

    /// <inheritdoc/>
    public override void OnChatOpened(int chatId)
    {
        ChatConversation conversation = Host?.Conversation;
        if (conversation == null)
            return;
        ChatMessageItem message = null;
        foreach (ChatMessageItem item in conversation.Messages.ToList())
        {
            if (item.Role == ChatMessageRole.User)
                message = item;
            else if (item.Role == ChatMessageRole.Assistant && message != null)
                _ = OfferAsync(message, item);
        }
    }

    // The row for a reply's thinking, once the message's language is known (and kept) and the thinking isn't already in it.
    private async Task OfferAsync(ChatMessageItem message, ChatMessageItem reply)
    {
        try
        {
            if (string.IsNullOrEmpty(reply.Thinking))
                return;
            if (message.Language == null)
            {
                // Detected now (for a message sent without it, or saved before languages were kept), and kept.
                string words = string.IsNullOrWhiteSpace(message.Text) ? message.Transcript : message.Text;
                string detected = string.IsNullOrWhiteSpace(words) ? null : await TextLanguage.DetectAsync(words);
                if (detected != null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() => message.Language = detected);
                    await Host.SaveMessageChangesAsync(message);
                }
            }
            // A translated reply keeps its translation's language; otherwise the message's.
            string target = reply.ThinkingTranslation != null ? reply.Language : Target(message.Language);
            if (target == null)
                return;
            if (reply.ThinkingTranslation == null)
            {
                // A reply whose thinking is already in that language (some models think in it) needs no translation.
                string thinking = reply.Thinking;
                string language = await TextLanguage.DetectAsync(thinking.Length > 600 ? thinking.Substring(0, 600) : thinking);
                if (language == target)
                    return;
            }
            await MainThread.InvokeOnMainThreadAsync(() => ShowRow(reply, target));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Thinking translation: " + ex.Message);
        }
    }

    private void ShowRow(ChatMessageItem reply, string language)
    {
        if (reply.ThinkingAccessory is Label existing)
        {
            UpdateRow(existing, reply, language, false);
            return;
        }
        Label row = null;
        bool translating = false;
        row = ChatPage.Link(string.Empty, async () =>
        {
            if (translating)
                return;
            if (reply.ThinkingTranslation != null)
            {
                reply.ShowThinkingTranslation = !reply.ShowThinkingTranslation;
                UpdateRow(row, reply, language, false);
                return;
            }
            if (Host.IsBusy)
            {
                Host.SetStatus("Hang on - still answering.");
                return;
            }
            if (!Host.ModelReady)
            {
                Host.SetStatus("Load the model first.");
                return;
            }
            translating = true;
            UpdateRow(row, reply, language, true);
            Host.SetBusy(true);
            Host.SetStatus("Translating the thinking...");
            try
            {
                string translation = await TranslateTextAsync(reply.Thinking, language);
                reply.Language = language;
                reply.ThinkingTranslation = translation;
                reply.ShowThinkingTranslation = true;
                // Kept with the saved chat (nothing in a private chat).
                await Host.SaveMessageChangesAsync(reply);
                Host.SetStatus(null);
            }
            catch (Exception ex)
            {
                Host.SetStatus("Couldn't translate: " + ex.Message);
            }
            finally
            {
                translating = false;
                Host.SetBusy(false);
                UpdateRow(row, reply, language, false);
            }
        });
        row.FontSize = 12;
        UpdateRow(row, reply, language, false);
        reply.ThinkingAccessory = row;
    }

    private static void UpdateRow(Label row, ChatMessageItem reply, string language, bool translating)
    {
        string name = LanguageName(language, false);
        row.Text = translating ? "Translating to " + name + "..."
            : reply.ThinkingTranslation == null ? "Translate to " + name
            : reply.ShowThinkingTranslation ? "Translated to " + name + " · Show the English"
            : "The English original · Show " + name;
        SemanticProperties.SetDescription(row, row.Text);
    }

    // Translate a text with the model, in requests of their own - one per piece if it is long, since the window is small.
    // The conversation's client takes them as new conversations (as for a summary) and is given the chat again with the
    // next message.
    private async Task<string> TranslateTextAsync(string text, string language)
    {
        string name = LanguageName(language, true);
        Func<string, int> countTokens = Host.TokenCounter;
        int contextWindow = Host.ContextWindow;
        int pieceTokens = Math.Max(200, Math.Min(1200, (contextWindow - 500) / 3));
        List<string> pieces = await Task.Run(() => LanguageText.SplitForTranslation(text, pieceTokens, countTokens));
        string Prompt(string piece) =>
            "Translate the text below into " + name + ". It is an AI assistant's private reasoning notes. Keep code, file names, " +
            "tool or function names (words with underscores), numbers and the Markdown formatting unchanged. " +
            "Write only the translation, nothing else.\n\nText:\n" + piece;
        var result = new StringBuilder();
        foreach (string piece in pieces)
        {
            // A language can need more tokens than the English (Hindi, Thai: about three times).
            // (A server's model is asked with its own limits: AskModelOnceAsync summarizes on the server.)
            string translated = await Host.AskModelOnceAsync(Prompt(piece), Math.Max(256, Math.Min(contextWindow - pieceTokens - 200, pieceTokens * 3)));
            if (translated.Length == 0)
                throw new Exception("The translation is empty");
            result.Append(result.Length > 0 ? "\n\n" : "").Append(translated);
        }
        return result.ToString();
    }
}
