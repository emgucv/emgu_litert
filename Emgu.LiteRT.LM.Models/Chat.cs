//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// A chat with a language model: keeps the message history and sends new messages with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For models whose chat template supports it (LanguageModel.SupportsMultiTurnConversation, e.g. Gemma4E2B),
    /// the chat keeps one LiteRT-LM Conversation open across messages, so each message only processes the new
    /// tokens. For the others (e.g. Qwen3), each message is sent through a new Conversation whose initial messages
    /// are the history so far: LiteRT-LM v0.17.1 renders a conversation incrementally and refuses a second message
    /// when the model's chat template renders earlier turns differently once more turns follow (Qwen3's template
    /// drops the previous answer's empty &lt;think&gt;&lt;/think&gt; block: "The new rendered template string does
    /// not start with the previous rendered template string").
    /// </para>
    /// <para>
    /// Either way the history is kept here, so it can be inspected or cleared, and an open Conversation is rebuilt
    /// from it when the settings change, or after a send fails or is cancelled (LiteRT-LM doesn't support reusing a
    /// conversation after that). One message can be sent at a time. Dispose the chat (or the model) to release an
    /// open Conversation.
    /// </para>
    /// </remarks>
    public class Chat : IDisposable
    {
        private readonly LanguageModel _model;
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private int _busy;
        // The open conversation, and the settings it was created with, when the model supports multi-turn
        // conversations.
        private ConversationResources _openConversation;
        private String _openConversationSettings;

        internal Chat(LanguageModel model, String systemMessage)
        {
            _model = model;
            SystemMessage = systemMessage;
        }

        /// <summary>
        /// The system message (instructions for the model), or null for none
        /// </summary>
        public String SystemMessage { get; set; }

        /// <summary>
        /// The maximum number of tokens to generate per reply, or 0 for LiteRT-LM's default
        /// </summary>
        public int MaxOutputTokens { get; set; }

        /// <summary>
        /// Whether the model thinks before answering, for models that support it; null for the model's default
        /// </summary>
        public bool? EnableThinking { get; set; }

        /// <summary>
        /// True if the chat keeps one LiteRT-LM Conversation open across messages, false if it sends each message
        /// through a new Conversation seeded with the history (see the remarks of Chat)
        /// </summary>
        public bool KeepsConversationOpen
        {
            get { return _model.SupportsMultiTurnConversation; }
        }

        /// <summary>
        /// The messages sent and received so far
        /// </summary>
        public ReadOnlyCollection<ChatMessage> History
        {
            get { return _history.AsReadOnly(); }
        }

        /// <summary>
        /// Clear the message history.
        /// </summary>
        public void ClearHistory()
        {
            EnterSend();
            try
            {
                CloseConversation();
                _history.Clear();
            }
            finally
            {
                ExitSend();
            }
        }

        /// <summary>
        /// Send a message and wait for the reply. The message and the reply's answer (without thinking) are added to
        /// the history.
        /// </summary>
        /// <param name="text">The message text</param>
        /// <returns>The reply</returns>
        public ChatReply Send(String text)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            EnterSend();
            try
            {
                ChatMessage message = new ChatMessage(ChatMessage.UserRole, text);
                ConversationResources resources = AcquireConversation();
                ChatReply reply;
                try
                {
                    String json = resources.Conversation.SendMessage(message.ToJson());
                    reply = new ChatReply(ChatMessage.GetText(json), json);
                }
                catch
                {
                    ReleaseConversation(resources, false);
                    throw;
                }
                ReleaseConversation(resources, true);
                AddToHistory(message, reply);
                return reply;
            }
            finally
            {
                ExitSend();
            }
        }

        /// <summary>
        /// Send a message and stream the reply as it is generated. The message and the reply's answer (without
        /// thinking) are added to the history when the reply completes; nothing is added if it is cancelled or fails.
        /// </summary>
        /// <param name="text">The message text</param>
        /// <param name="onText">Called with each piece of generated text (including any thinking), on a LiteRT-LM
        /// background thread. May be null.</param>
        /// <param name="cancellationToken">Cancels the generation; the task is then cancelled</param>
        /// <returns>A task that completes with the reply</returns>
        public async Task<ChatReply> SendAsync(
            String text,
            Action<String> onText = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (text == null)
                throw new ArgumentNullException("text");
            EnterSend();
            try
            {
                ChatMessage message = new ChatMessage(ChatMessage.UserRole, text);
                ConversationResources resources = AcquireConversation();
                String[] chunks;
                try
                {
                    Action<String> onChunk = null;
                    if (onText != null)
                        onChunk = chunkJson => onText(ChatMessage.GetText(chunkJson));
                    chunks = await resources.Conversation.SendMessageStreamAsync(
                        message.ToJson(), onChunk, null, null, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    ReleaseConversation(resources, false);
                    throw;
                }
                ReleaseConversation(resources, true);
                StringBuilder fullText = new StringBuilder();
                foreach (String chunk in chunks)
                    fullText.Append(ChatMessage.GetText(chunk));
                ChatReply reply = new ChatReply(fullText.ToString());
                AddToHistory(message, reply);
                return reply;
            }
            finally
            {
                ExitSend();
            }
        }

        /// <summary>
        /// Release the open LiteRT-LM Conversation, if any. The history is kept; a later message opens a new
        /// Conversation from it.
        /// </summary>
        public void Dispose()
        {
            CloseConversation();
        }

        /// <summary>
        /// Release the open conversation; called by the model before it disposes its engine.
        /// </summary>
        internal void CloseConversation()
        {
            if (_openConversation != null)
            {
                _openConversation.Dispose();
                _openConversation = null;
                _openConversationSettings = null;
            }
        }

        private void EnterSend()
        {
            if (Interlocked.Exchange(ref _busy, 1) != 0)
                throw new InvalidOperationException("A message is already being sent in this chat");
        }

        private void ExitSend()
        {
            Volatile.Write(ref _busy, 0);
        }

        private void AddToHistory(ChatMessage message, ChatReply reply)
        {
            _history.Add(message);
            _history.Add(new ChatMessage(ChatMessage.AssistantRole, reply.Text));
        }

        private String GetSettingsKey()
        {
            return String.Format("{0}|{1}|{2}", SystemMessage, MaxOutputTokens, EnableThinking);
        }

        // Get the conversation to send the next message through: the open one if it's still valid, else a new one
        // seeded with the history.
        private ConversationResources AcquireConversation()
        {
            if (!KeepsConversationOpen)
                return CreateConversation();

            String settings = GetSettingsKey();
            if (_openConversation != null && _openConversationSettings != settings)
                CloseConversation();
            if (_openConversation == null)
            {
                _openConversation = CreateConversation();
                _openConversationSettings = settings;
            }
            return _openConversation;
        }

        // Done with the conversation after a send: keep an open conversation if the send succeeded, otherwise (and
        // always for a per-message conversation) dispose it.
        private void ReleaseConversation(ConversationResources resources, bool succeeded)
        {
            if (resources == _openConversation)
            {
                if (!succeeded)
                    CloseConversation();
            }
            else
            {
                resources.Dispose();
            }
        }

        private ConversationResources CreateConversation()
        {
            Engine engine = _model.Engine;
            if (engine == null)
                throw new InvalidOperationException("The model is not initialized; call Init first");

            ConversationResources resources = new ConversationResources();
            try
            {
                resources.Config = new ConversationConfig();
                if (MaxOutputTokens > 0)
                {
                    resources.SessionConfig = new SessionConfig();
                    resources.SessionConfig.MaxOutputTokens = MaxOutputTokens;
                    resources.Config.SessionConfig = resources.SessionConfig;
                }
                if (EnableThinking.HasValue)
                {
                    resources.ThinkingConfig = new ThinkingConfig();
                    resources.ThinkingConfig.EnableThinking = EnableThinking.Value;
                    resources.Config.ThinkingConfig = resources.ThinkingConfig;
                }
                if (!String.IsNullOrEmpty(SystemMessage))
                    resources.Config.SystemMessage = SystemMessage;
                if (_history.Count > 0)
                    resources.Config.MessagesJson = ChatMessage.ToJson(_history);
                resources.Conversation = engine.CreateConversation(resources.Config);
                _model.RegisterChat(this);
                return resources;
            }
            catch
            {
                resources.Dispose();
                throw;
            }
        }

        /// <summary>
        /// The native objects of one conversation, disposed together.
        /// </summary>
        private class ConversationResources : IDisposable
        {
            public SessionConfig SessionConfig;
            public ThinkingConfig ThinkingConfig;
            public ConversationConfig Config;
            public Conversation Conversation;

            public void Dispose()
            {
                if (Conversation != null)
                    Conversation.Dispose();
                if (Config != null)
                    Config.Dispose();
                if (ThinkingConfig != null)
                    ThinkingConfig.Dispose();
                if (SessionConfig != null)
                    SessionConfig.Dispose();
            }
        }
    }
}
