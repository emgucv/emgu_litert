// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;

namespace Maui.Demo.Lite;

// The page's own data for a reply, in its ChatMessageItem.Tag (the demo's reply template binds to it): the translate
// row under the thinking's header and the thinking shown (the translation, when chosen); and the page's handle for
// the cards that features put above the reply (AddCard).
internal sealed class StreamingBubble : System.ComponentModel.INotifyPropertyChanged
{
    public StreamingBubble(ChatMessageItem item, Label translateRow)
    {
        Item = item;
        TranslateRow = translateRow;
        item.Tag = this;
    }

    public ChatMessageItem Item { get; }

    // The row that offers the translation of the thinking (TranslationFeature.cs), the ThinkingView's accessory.
    public Label TranslateRow { get; }

    public string Thinking => Item.Thinking;

    // Translating the thinking (TranslationFeature.cs): the language of the message it answers when
    // that isn't English (null: no translation offered), the translation, whether it is shown, whether one is
    // being made, and the reply's id in the chat store.
    public string TargetLanguage;
    public string Translation;
    public bool ShowTranslation;
    public bool Translating;
    // The thinking's own language has been checked (it isn't the language it would be translated into).
    public bool TranslateReady;
    public int MessageId;
    // The document's step line ("Looked in report.pdf..."), a card above the reply.
    public string DocumentStep;
    // The memories the reply drew on (LiteRtLmChatPage.ShowMemoriesUsedAsync), saved as a step line.
    public string MemoryStep;

    // What the thinking shows when opened: the translation, when chosen; null for the thinking itself.
    public string ThinkingDisplayText => ShowTranslation && Translation != null ? Translation.Trim() : null;

    public void Refresh() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ThinkingDisplayText)));

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}
