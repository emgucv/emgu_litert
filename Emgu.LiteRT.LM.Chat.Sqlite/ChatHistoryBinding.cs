//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Chat.Sqlite
{
    /// <summary>
    /// Saves a conversation into a <see cref="ChatHistoryStore"/> as it goes on (<see cref="ChatHistoryStore.Attach"/>):
    /// each completed exchange - the first creates the chat - a regenerated reply in place of the one before, the
    /// compaction point, and a voice message's transcript written down after the message was saved. The saves run one
    /// after another, in the background; a failed one is reported (<see cref="SaveFailed"/>), not thrown. Dispose it to
    /// stop saving.
    /// </summary>
    public sealed class ChatHistoryBinding : IDisposable
    {
        private readonly ChatHistoryStore _store;
        private readonly ChatConversation _conversation;
        private readonly string _model;
        private Task _pending = Task.CompletedTask;
        private readonly object _lock = new object();
        private bool _disposed;

        internal ChatHistoryBinding(ChatHistoryStore store, ChatConversation conversation, int chatId, string model)
        {
            _store = store;
            _conversation = conversation;
            _model = model;
            ChatId = chatId;
            conversation.ExchangeCompleted += OnExchangeCompleted;
            conversation.Compacted += OnCompacted;
        }

        /// <summary>The saved chat, or 0 until the first exchange creates it.</summary>
        public int ChatId { get; private set; }

        /// <summary>Raised (on a background thread) when the first exchange created the chat; <see cref="ChatId"/> is its id.</summary>
        public event EventHandler ChatCreated;

        /// <summary>Raised (on a background thread) when saving failed; the conversation goes on.</summary>
        public event EventHandler<Exception> SaveFailed;

        /// <summary>Wait until everything given to it so far is saved (e.g. before the app closes).</summary>
        public Task FlushAsync()
        {
            lock (_lock)
                return _pending;
        }

        /// <summary>Stop saving (what was given to it before is still saved).</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _conversation.ExchangeCompleted -= OnExchangeCompleted;
            _conversation.Compacted -= OnCompacted;
        }

        private void OnExchangeCompleted(object sender, ChatExchangeEventArgs e)
        {
            ChatMessageItem message = e.Message, reply = e.Reply;
            bool regenerated = e.Regenerated;
            Enqueue(async () =>
            {
                if (_store.IsPrivate)
                    return;
                if (ChatId == 0)
                {
                    ChatId = await _store.CreateChatAsync(ChatHistoryStore.TitleOf(message), _model).ConfigureAwait(false);
                    ChatCreated?.Invoke(this, EventArgs.Empty);
                }
                if (regenerated)
                    await _store.ReplaceReplyAsync(ChatId, message, reply).ConfigureAwait(false);
                else
                    await _store.SaveExchangeAsync(ChatId, message, reply).ConfigureAwait(false);
                // A voice message whose words are written down later (while or after the model answers).
                if (!message.HasTranscript && message.Attachments.Count > 0)
                    WatchTranscript(message);
            });
        }

        private void WatchTranscript(ChatMessageItem message)
        {
            int chatId = ChatId;
            PropertyChangedEventHandler handler = null;
            handler = (s, e) =>
            {
                if (e.PropertyName != nameof(ChatMessageItem.Transcript) || !message.HasTranscript)
                    return;
                message.PropertyChanged -= handler;
                string transcript = message.Transcript;
                if (!_disposed)
                    Enqueue(() => _store.SetTranscriptAsync(chatId, _store.GetMessageId(message), transcript));
            };
            message.PropertyChanged += handler;
            // Written down while it was being saved.
            if (message.HasTranscript)
                handler(message, new PropertyChangedEventArgs(nameof(ChatMessageItem.Transcript)));
        }

        private void OnCompacted(object sender, ChatCompactedEventArgs e)
        {
            Enqueue(async () =>
            {
                if (ChatId != 0 && !_store.IsPrivate)
                    await _store.SaveCompactionAsync(ChatId, _conversation).ConfigureAwait(false);
            });
        }

        // The saves run one after another, in the order they were asked for.
        private void Enqueue(Func<Task> save)
        {
            lock (_lock)
                _pending = RunAfterAsync(_pending, save);
        }

        private async Task RunAfterAsync(Task previous, Func<Task> save)
        {
            await previous.ConfigureAwait(false);   // never faults: each save reports its own failure
            try
            {
                await save().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SaveFailed?.Invoke(this, ex);
            }
        }
    }
}
