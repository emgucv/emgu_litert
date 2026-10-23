//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// Who wrote a message.
    /// </summary>
    public enum ChatMessageRole
    {
        /// <summary>The user</summary>
        User,
        /// <summary>The model</summary>
        Assistant
    }

    /// <summary>
    /// Where a message is.
    /// </summary>
    public enum ChatMessageState
    {
        /// <summary>The reply is still being written</summary>
        Streaming,
        /// <summary>Complete</summary>
        Done,
        /// <summary>The reply was cancelled while being written; it keeps the text written so far</summary>
        Interrupted,
        /// <summary>The reply failed (see <see cref="ChatMessageItem.Error"/>); the exchange isn't given to the model again</summary>
        Failed
    }

    /// <summary>
    /// Base for the observable types of a conversation.
    /// </summary>
    public abstract class ChatObservable : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Set a field and raise <see cref="PropertyChanged"/> if it changed.</summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        /// <summary>Raise <see cref="PropertyChanged"/> for a property.</summary>
        protected void Raise(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// A tool call the model made while writing a reply, for a step line ("Searching Wikipedia for ...").
    /// </summary>
    public sealed class ChatToolStep : ChatObservable
    {
        private string _description;
        private bool _isRunning = true;
        private string _error;

        internal ChatToolStep(FunctionCallContent call, string description)
        {
            Call = call;
            _description = description;
        }

        /// <summary>The call, with the tool's name and arguments.</summary>
        public FunctionCallContent Call { get; }

        /// <summary>The tool's name.</summary>
        public string Name => Call.Name;

        /// <summary>What the step does, in words (from <see cref="ChatConversation.DescribeToolCall"/>); an app may change it, e.g. once it has the result.</summary>
        public string Description
        {
            get => _description;
            set
            {
                if (Set(ref _description, value))
                    Raise(nameof(Text));
            }
        }

        /// <summary>True until the tool's result has arrived.</summary>
        public bool IsRunning
        {
            get => _isRunning;
            internal set
            {
                if (Set(ref _isRunning, value))
                    Raise(nameof(Text));
            }
        }

        /// <summary>The step line to show: the description, with "..." while the tool runs, or why it failed.</summary>
        public string Text => _description + (_isRunning ? "..." : _error != null ? " - failed: " + _error : string.Empty);

        /// <summary>The tool's result, once it has arrived.</summary>
        public FunctionResultContent Result { get; internal set; }

        /// <summary>Why the tool failed, or null.</summary>
        public string Error
        {
            get => _error;
            internal set
            {
                if (Set(ref _error, value))
                    Raise(nameof(Text));
            }
        }
    }

    /// <summary>
    /// A message of a <see cref="ChatConversation"/>: a user message with its attachments, or a reply, which changes as it
    /// streams (text, thinking, tool steps, state). Its properties raise <see cref="INotifyPropertyChanged.PropertyChanged"/>
    /// on the thread that called <see cref="ChatConversation.SendAsync"/>, so a UI can bind to them.
    /// </summary>
    public sealed class ChatMessageItem : ChatObservable
    {
        private string _text;
        private string _thinking = string.Empty;
        private TimeSpan? _thinkingDuration;
        private ChatMessageState _state;
        private string _error;
        private object _tag;
        private bool _startsContext;

        /// <summary>
        /// Create a message, e.g. to restore a saved conversation with <see cref="ChatConversation.Load"/>.
        /// </summary>
        /// <param name="role">Who wrote it</param>
        /// <param name="text">Its text (for a reply, the answer without the thinking)</param>
        /// <param name="attachments">A user message's images and audio, or null</param>
        /// <param name="context">Text given to the model before a user message's text (e.g. document excerpts), or null</param>
        public ChatMessageItem(ChatMessageRole role, string text, IEnumerable<DataContent> attachments = null, string context = null)
        {
            Role = role;
            _text = text ?? string.Empty;
            Context = context;
            Attachments = new List<DataContent>(attachments ?? Array.Empty<DataContent>()).AsReadOnly();
            _state = ChatMessageState.Done;
        }

        /// <summary>Who wrote it.</summary>
        public ChatMessageRole Role { get; }

        /// <summary>The message's text; for a reply, the answer so far (without the thinking).</summary>
        public string Text
        {
            get => _text;
            internal set
            {
                bool wasThinking = IsThinking;
                bool hadAnswer = HasAnswer;
                if (Set(ref _text, value ?? string.Empty))
                {
                    RaiseIsThinking(wasThinking);
                    if (HasAnswer != hadAnswer)
                    {
                        Raise(nameof(HasAnswer));
                        Raise(nameof(HasText));
                        Raise(nameof(IsFinishedWithoutAnswer));
                    }
                }
            }
        }

        /// <summary>A reply's thinking so far; empty if the model didn't think.</summary>
        public string Thinking
        {
            get => _thinking;
            set
            {
                bool wasThinking = IsThinking;
                if (Set(ref _thinking, value ?? string.Empty))
                    RaiseIsThinking(wasThinking);
            }
        }

        /// <summary>How long the model thought, from its first thinking to its first answer text; null if unknown (e.g. a restored reply).</summary>
        public TimeSpan? ThinkingDuration
        {
            get => _thinkingDuration;
            set => Set(ref _thinkingDuration, value);
        }

        /// <summary>True while the model is thinking: the reply streams, has thinking and no answer yet.</summary>
        public bool IsThinking => _state == ChatMessageState.Streaming && _thinking.Length > 0 && _text.Length == 0;

        /// <summary>True if the reply has answer text (not only thinking).</summary>
        public bool HasAnswer => _text.Length > 0;

        /// <summary>True if the message has text (a user message may have only attachments).</summary>
        public bool HasText => _text.Length > 0;

        /// <summary>True if the message has attachments.</summary>
        public bool HasAttachments => Attachments.Count > 0;

        /// <summary>True while the reply is being written.</summary>
        public bool IsStreaming => _state == ChatMessageState.Streaming;

        /// <summary>True if the reply was cancelled while being written.</summary>
        public bool IsInterrupted => _state == ChatMessageState.Interrupted;

        /// <summary>True if the reply failed (see <see cref="Error"/>).</summary>
        public bool IsFailed => _state == ChatMessageState.Failed;

        /// <summary>True if the reply ended without answer text, e.g. cut off while thinking (a UI then shows its thinking).</summary>
        public bool IsFinishedWithoutAnswer => _state != ChatMessageState.Streaming && _text.Length == 0;

        /// <summary>True for the first message the model still sees after the conversation was compacted (a UI may show a
        /// divider above it: the messages before are summarized or dropped).</summary>
        public bool StartsContext
        {
            get => _startsContext;
            internal set => Set(ref _startsContext, value);
        }

        private void RaiseIsThinking(bool wasThinking)
        {
            if (IsThinking != wasThinking)
                Raise(nameof(IsThinking));
        }

        /// <summary>The tool calls made while writing the reply, in order.</summary>
        public ObservableCollection<ChatToolStep> Steps { get; } = new ObservableCollection<ChatToolStep>();

        /// <summary>A user message's images and audio.</summary>
        public IReadOnlyList<DataContent> Attachments { get; }

        /// <summary>Text given to the model before a user message's text (e.g. document excerpts), not shown in the transcript; or null.</summary>
        public string Context { get; }

        /// <summary>What the model is given: <see cref="Context"/> followed by <see cref="Text"/>.</summary>
        public string ModelText => string.IsNullOrEmpty(Context) ? Text : Context + Text;

        /// <summary>Where the message is: streaming, done, interrupted or failed.</summary>
        public ChatMessageState State
        {
            get => _state;
            internal set
            {
                bool wasThinking = IsThinking;
                if (Set(ref _state, value))
                {
                    RaiseIsThinking(wasThinking);
                    Raise(nameof(IsStreaming));
                    Raise(nameof(IsInterrupted));
                    Raise(nameof(IsFailed));
                    Raise(nameof(IsFinishedWithoutAnswer));
                }
            }
        }

        /// <summary>Why the reply failed, or null.</summary>
        public string Error
        {
            get => _error;
            internal set => Set(ref _error, value);
        }

        /// <summary>The app's own data for the message, e.g. its id in the app's store.</summary>
        public object Tag
        {
            get => _tag;
            set => Set(ref _tag, value);
        }

        // Its estimated size in the model's context, or -1 until counted, and what the model's own conversation holds on
        // top until it is rebuilt (the reply's thinking, tool calls and their results).
        internal int Tokens = -1;
        internal int ExtraTokens;

        // A reply's messages as the client returned them (tool calls and results included), sent back as they were so a
        // client that keeps its conversation open (LiteRtLmChatClient) recognizes them; null after compaction or when
        // restored, when the reply is given as text.
        internal IList<ChatMessage> ModelMessages;

        // A failed exchange (user message and reply) isn't given to the model again.
        internal bool Excluded;
    }
}
