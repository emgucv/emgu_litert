//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>A saved chat, as a list shows it.</summary>
    public sealed class ChatHistoryEntry
    {
        /// <summary>Create an entry.</summary>
        /// <param name="id">The chat's id</param>
        /// <param name="title">Its title</param>
        /// <param name="model">The model it was held with (a <see cref="ChatModelOption.Id"/>), or null</param>
        /// <param name="updated">When a message was last added (UTC)</param>
        public ChatHistoryEntry(int id, string title, string model, DateTime updated)
        {
            Id = id;
            Title = title ?? string.Empty;
            Model = model;
            Updated = updated;
        }

        /// <summary>The chat's id.</summary>
        public int Id { get; }

        /// <summary>Its title.</summary>
        public string Title { get; }

        /// <summary>The model it was held with, or null.</summary>
        public string Model { get; }

        /// <summary>When a message was last added (UTC).</summary>
        public DateTime Updated { get; }
    }

    /// <summary>Saves a conversation as it goes on (<see cref="IChatHistoryStore.Attach"/>); dispose it to stop.</summary>
    public interface IChatHistoryBinding : IDisposable
    {
        /// <summary>The saved chat, or 0 until the first exchange creates it.</summary>
        int ChatId { get; }

        /// <summary>Raised (possibly on a background thread) when the first exchange created the chat.</summary>
        event EventHandler ChatCreated;
    }

    /// <summary>
    /// Where saved chats are kept, as a chat UI needs it: the list, opening one into a conversation, deleting one, and
    /// saving a conversation as it goes on. Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore implements it over SQLite.
    /// </summary>
    public interface IChatHistoryStore
    {
        /// <summary>True while the chat is private: nothing is saved and no saved chat is read.</summary>
        bool IsPrivate { get; set; }

        /// <summary>Raised when a chat was created, added to or deleted.</summary>
        event EventHandler ChatsChanged;

        /// <summary>The saved chats, the most recently updated first (none while <see cref="IsPrivate"/>).</summary>
        Task<IReadOnlyList<ChatHistoryEntry>> ListChatsAsync();

        /// <summary>
        /// Show a saved chat in a conversation and continue it. The messages are added to the conversation on the caller's
        /// synchronization context (the UI thread, when a page calls it), since views may be bound to them.
        /// </summary>
        /// <param name="chatId">The chat</param>
        /// <param name="conversation">The conversation (not sending a message)</param>
        /// <returns>True if the chat was found</returns>
        Task<bool> LoadChatAsync(int chatId, ChatConversation conversation);

        /// <summary>Delete a chat and everything saved with it.</summary>
        /// <param name="chatId">The chat</param>
        Task DeleteChatAsync(int chatId);

        /// <summary>
        /// Save a conversation from now on: each completed exchange (the first creates the chat unless it continues one).
        /// </summary>
        /// <param name="conversation">The conversation</param>
        /// <param name="chatId">The saved chat it continues, or 0 to create one with the first exchange</param>
        /// <param name="model">The model (a <see cref="ChatModelOption.Id"/>), for a chat it creates; or null</param>
        IChatHistoryBinding Attach(ChatConversation conversation, int chatId = 0, string model = null);
    }
}
