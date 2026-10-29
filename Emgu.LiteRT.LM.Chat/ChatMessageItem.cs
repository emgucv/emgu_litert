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
        Assistant,
        /// <summary>The app: a notice in the transcript (e.g. an alert card), never given to the model (<see cref="ChatConversation.AddNotice"/>)</summary>
        Notice
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

        /// <summary>
        /// Create a step line of the app's own, not a tool call (e.g. "Looked in report.pdf: page 2"), to add to a reply's
        /// <see cref="ChatMessageItem.Steps"/>. It isn't counted as a tool call in the context window.
        /// </summary>
        /// <param name="description">The line</param>
        /// <param name="isRunning">True to show it as still running ("..."); set <see cref="IsRunning"/> false when done</param>
        public ChatToolStep(string description, bool isRunning = false)
        {
            _description = description;
            _isRunning = isRunning;
        }

        /// <summary>The call, with the tool's name and arguments; null for the app's own step lines.</summary>
        public FunctionCallContent Call { get; }

        /// <summary>The tool's name; null for the app's own step lines.</summary>
        public string Name => Call?.Name;

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

        /// <summary>True until the tool's result has arrived (or, for the app's own step, until it says so).</summary>
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (Set(ref _isRunning, value))
                    Raise(nameof(Text));
            }
        }

        /// <summary>
        /// The step line to show: the description with "..." while the tool runs, said as done once it has
        /// ("Recording the meeting" - "Recorded the meeting"), or why it failed.
        /// </summary>
        public string Text => _isRunning ? _description + "..." : _error != null ? _description + " - failed: " + _error : Done(_description);

        // What the step lines start with ("Checking the weather", "Calling search on GitHub"), said as done.
        private static readonly Dictionary<string, string> DoneWords = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Adding"] = "Added", ["Analyzing"] = "Analyzed", ["Asking"] = "Asked", ["Blurring"] = "Blurred",
            ["Calculating"] = "Calculated", ["Calling"] = "Called", ["Capturing"] = "Captured", ["Checking"] = "Checked",
            ["Converting"] = "Converted", ["Counting"] = "Counted", ["Creating"] = "Created", ["Detecting"] = "Detected",
            ["Drawing"] = "Drew", ["Fetching"] = "Fetched", ["Finding"] = "Found", ["Forgetting"] = "Forgot",
            ["Getting"] = "Got", ["Listing"] = "Listed", ["Loading"] = "Loaded", ["Logging"] = "Logged",
            ["Looking"] = "Looked", ["Making"] = "Made", ["Measuring"] = "Measured", ["Opening"] = "Opened",
            ["Playing"] = "Played", ["Reading"] = "Read", ["Recording"] = "Recorded", ["Remembering"] = "Remembered",
            ["Removing"] = "Removed", ["Running"] = "Ran", ["Saving"] = "Saved", ["Searching"] = "Searched",
            ["Sending"] = "Sent", ["Setting"] = "Set", ["Starting"] = "Started", ["Stopping"] = "Stopped",
            ["Taking"] = "Took", ["Translating"] = "Translated", ["Updating"] = "Updated", ["Using"] = "Used",
            ["Writing"] = "Wrote",
        };

        /// <summary>
        /// A step's description said as done: its first word in the past tense ("Searching the web for ..." - "Searched the
        /// web for ..."). Unchanged when it starts with another word, or ends with an outcome that isn't done
        /// (" - cancelled", " - failed").
        /// </summary>
        /// <param name="description">The step's description</param>
        public static string Done(string description)
        {
            if (string.IsNullOrEmpty(description)
                || description.EndsWith(" - cancelled", StringComparison.Ordinal) || description.EndsWith(" - failed", StringComparison.Ordinal))
                return description ?? string.Empty;
            int end = 0;
            while (end < description.Length && char.IsLetter(description[end])) end++;
            return DoneWords.TryGetValue(description.Substring(0, end), out string done) ? done + description.Substring(end) : description;
        }

        /// <summary>The tool's result, once it has arrived.</summary>
        public FunctionResultContent Result { get; internal set; }

        /// <summary>Why the tool failed, or null.</summary>
        public string Error
        {
            get => _error;
            set
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
        private string _context;
        private string _transcript;
        private string _language;
        private string _thinkingTranslation;
        private bool _showThinkingTranslation;
        private object _thinkingAccessory;
        private readonly List<DataContent> _attachments;

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
            _context = context;
            _attachments = new List<DataContent>(attachments ?? Array.Empty<DataContent>());
            Attachments = _attachments.AsReadOnly();
            Steps.CollectionChanged += (s, e) => Raise(nameof(HasSteps));
            Decorations.CollectionChanged += (s, e) => Raise(nameof(HasDecorations));
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
                {
                    RaiseIsThinking(wasThinking);
                    Raise(nameof(ThinkingDisplayText));
                }
            }
        }

        /// <summary>
        /// A user message's language (a two-letter code such as "es", or null if unknown); for a reply, the language its
        /// thinking was translated into (<see cref="ThinkingTranslation"/>). Kept with a saved chat.
        /// </summary>
        public string Language
        {
            get => _language;
            set => Set(ref _language, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }

        /// <summary>A reply's thinking translated into <see cref="Language"/> on request, or null. Kept with a saved chat; not given to the model.</summary>
        public string ThinkingTranslation
        {
            get => _thinkingTranslation;
            set
            {
                if (Set(ref _thinkingTranslation, string.IsNullOrWhiteSpace(value) ? null : value))
                    Raise(nameof(ThinkingDisplayText));
            }
        }

        /// <summary>True to show the <see cref="ThinkingTranslation"/> in place of the thinking (when there is one).</summary>
        public bool ShowThinkingTranslation
        {
            get => _showThinkingTranslation;
            set
            {
                if (Set(ref _showThinkingTranslation, value))
                    Raise(nameof(ThinkingDisplayText));
            }
        }

        /// <summary>The thinking as shown: the <see cref="ThinkingTranslation"/> when it is chosen, else null (the thinking itself).</summary>
        public string ThinkingDisplayText => _showThinkingTranslation && _thinkingTranslation != null ? _thinkingTranslation.Trim() : null;

        /// <summary>
        /// The app's own view shown with the thinking's header (a ChatView shows a View there, e.g. a feature's "Translate"
        /// row), or null. Not saved, not given to the model.
        /// </summary>
        public object ThinkingAccessory
        {
            get => _thinkingAccessory;
            set => Set(ref _thinkingAccessory, value);
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

        /// <summary>True if the reply has step lines (e.g. to hide an empty list in a template).</summary>
        public bool HasSteps => Steps.Count > 0;

        /// <summary>True if the message has decorations (e.g. to hide an empty list in a template).</summary>
        public bool HasDecorations => Decorations.Count > 0;

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

        /// <summary>
        /// Add an attachment to a message after it was sent - e.g. camera frames a tool took while the reply was written -
        /// so the model is given it with the conversation from now on. (A UI bound to <see cref="Attachments"/> isn't told.)
        /// </summary>
        /// <param name="attachment">The image or audio</param>
        public void AddAttachment(DataContent attachment)
        {
            if (attachment == null)
                return;
            _attachments.Add(attachment);
            Tokens = -1;
            Raise(nameof(HasAttachments));
        }

        /// <summary>Text given to the model before a user message's text (e.g. document excerpts), not shown in the transcript; or null.</summary>
        public string Context
        {
            get => _context;
            set
            {
                // Its size changes: counted again before it is sent.
                if (Set(ref _context, value))
                    Tokens = -1;
            }
        }

        /// <summary>
        /// The words of a voice message the model was given as audio, written down by a speech recognizer for the
        /// transcript; or null. Shown, never given to the model (it hears the audio).
        /// </summary>
        public string Transcript
        {
            get => _transcript;
            set
            {
                if (Set(ref _transcript, string.IsNullOrWhiteSpace(value) ? null : value.Trim()))
                    Raise(nameof(HasTranscript));
            }
        }

        /// <summary>True if the message has a <see cref="Transcript"/>.</summary>
        public bool HasTranscript => _transcript != null;

        /// <summary>
        /// The app's own views or objects shown with the message - for a reply above its bubble (e.g. a generated picture,
        /// a device action's card), for a notice as its content. A <c>ChatView</c> shows a View as it is (a View can only
        /// be shown in one place) and anything else as text. Not given to the model.
        /// </summary>
        public ObservableCollection<object> Decorations { get; } = new ObservableCollection<object>();

        /// <summary>
        /// The app's own files kept with a reply (a picture it drew, a document it wrote; <see cref="ChatSavedFile"/>):
        /// the chats' store saves them with the reply and reads them back when the chat is opened, for the app to show
        /// again as <see cref="Decorations"/>. Not given to the model.
        /// </summary>
        public IList<ChatSavedFile> SavedFiles { get; } = new List<ChatSavedFile>();

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
