//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Specialized;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>A message being sent by a <see cref="ChatView"/>, for its <see cref="ChatView.OptionsProvider"/>.</summary>
    public sealed class ChatSendRequest
    {
        /// <summary>Create one.</summary>
        /// <param name="text">The message</param>
        /// <param name="attachments">Its images and audio, or null</param>
        /// <param name="context">Text given to the model before the message, or null</param>
        public ChatSendRequest(string text, IEnumerable<DataContent> attachments, string context)
        {
            Text = text ?? string.Empty;
            Attachments = attachments?.ToList() ?? new List<DataContent>();
            Context = context;
        }

        /// <summary>The message.</summary>
        public string Text { get; }

        /// <summary>Its images and audio.</summary>
        public IReadOnlyList<DataContent> Attachments { get; }

        /// <summary>Text given to the model before the message, or null.</summary>
        public string Context { get; }

        /// <summary>True for a voice message the microphone recorded (its audio is among <see cref="Attachments"/>, or its words are the text).</summary>
        public bool IsVoiceMessage { get; set; }
    }

    /// <summary>
    /// A chat: the transcript of a <see cref="ChatConversation"/> - user messages, and replies with their thinking, tool
    /// steps, Markdown, code and maths, streamed as they are written - a message box with Send and Stop, and for an
    /// on-device <see cref="Model"/> the card that downloads and loads it. Give it a <see cref="Model"/>, or a
    /// <see cref="ChatClient"/> (a server, an agent, another provider), or both (a client - e.g. with tools - running that
    /// model); or a <see cref="Conversation"/> of your own.
    /// <code>
    /// Content = new ChatView { Model = new Gemma4E2B() };
    /// </code>
    /// <para>
    /// Its look is the "ChatViewTemplate" template (see <see cref="ChatStyles"/>), with the parts "PART_Scroll" (a
    /// ScrollView), "PART_Transcript" (a Layout the messages are added to), "PART_Empty" (shown while there are no
    /// messages), "PART_LoadCard" (a <see cref="ModelLoadCard"/>), "PART_Status" (a Label), "PART_Input" (an InputView,
    /// e.g. an Editor), "PART_Send", "PART_Stop", "PART_Attach" and "PART_Talk" (Buttons, or views that are tapped),
    /// "PART_Attachments" (a Layout for the attachments waiting to be sent) and "PART_Header" (a ContentView showing
    /// <see cref="Header"/>). The messages use
    /// <see cref="MessageTemplateSelector"/>, and the Send, Stop, attach and microphone buttons the styles
    /// "ChatSendButtonStyle", "ChatStopButtonStyle", "ChatAttachButtonStyle" and "ChatTalkButtonStyle" (while recording
    /// "ChatTalkStopButtonStyle") unless the template gives them one. Attachments come from
    /// <see cref="AttachmentSource"/>; with <see cref="ReadAloud"/> on, <see cref="ReplySpeaker"/> reads the replies.
    /// The microphone records voice messages (<see cref="VoiceMessages"/>).
    /// </para>
    /// </summary>
    public partial class ChatView : TemplatedView
    {
        private const string TemplateKey = "ChatViewTemplate";

        /// <summary>The on-device model to chat with; the view loads it (downloading it first, after asking), or null.</summary>
        public static readonly BindableProperty ModelProperty = BindableProperty.Create(
            nameof(Model), typeof(LanguageModel), typeof(ChatView), null, propertyChanged: (b, o, n) => ((ChatView)b).OnSourceChanged());

        /// <summary>The chat client to send messages to, or null for the <see cref="Model"/>'s own.</summary>
        public static readonly BindableProperty ChatClientProperty = BindableProperty.Create(
            nameof(ChatClient), typeof(IChatClient), typeof(ChatView), null, propertyChanged: (b, o, n) => ((ChatView)b).OnSourceChanged());

        /// <summary>The conversation shown and continued; created from <see cref="Model"/> / <see cref="ChatClient"/> unless set.</summary>
        public static readonly BindableProperty ConversationProperty = BindableProperty.Create(
            nameof(Conversation), typeof(ChatConversation), typeof(ChatView), null, propertyChanged: (b, o, n) => ((ChatView)b).OnConversationChanged((ChatConversation)o));

        /// <summary>The system instructions (<see cref="ChatConversation.Instructions"/>).</summary>
        public static readonly BindableProperty InstructionsProperty = BindableProperty.Create(
            nameof(Instructions), typeof(string), typeof(ChatView), null, propertyChanged: (b, o, n) => ((ChatView)b).ApplyInstructions());

        /// <summary>True to let the model think before it answers (for models that can).</summary>
        public static readonly BindableProperty ThinkingEnabledProperty = BindableProperty.Create(
            nameof(ThinkingEnabled), typeof(bool), typeof(ChatView), true);

        /// <summary>Options for each message (tools, the longest reply...), copied per message; or null.</summary>
        public static readonly BindableProperty OptionsProperty = BindableProperty.Create(
            nameof(Options), typeof(ChatOptions), typeof(ChatView), null);

        /// <summary>Picks each message's template; a <see cref="ChatMessageTemplateSelector"/> by default.</summary>
        public static readonly BindableProperty MessageTemplateSelectorProperty = BindableProperty.Create(
            nameof(MessageTemplateSelector), typeof(DataTemplateSelector), typeof(ChatView), null,
            propertyChanged: (b, o, n) => ((ChatView)b).BindTranscript(), defaultValueCreator: b => new ChatMessageTemplateSelector());

        /// <summary>The message box's placeholder.</summary>
        public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
            nameof(Placeholder), typeof(string), typeof(ChatView), "Message", propertyChanged: (b, o, n) => ((ChatView)b).ApplyPlaceholder());

        /// <summary>Where attachments come from when the attach button is tapped.</summary>
        public static readonly BindableProperty AttachmentSourceProperty = BindableProperty.Create(
            nameof(AttachmentSource), typeof(IChatAttachmentSource), typeof(ChatView), null, defaultValueCreator: b => new MediaChatAttachmentSource());

        /// <summary>What can be attached; null (the default) for what the <see cref="Model"/> takes (nothing without one).</summary>
        public static readonly BindableProperty AttachmentKindsProperty = BindableProperty.Create(
            nameof(AttachmentKinds), typeof(ChatAttachmentKinds?), typeof(ChatView), null, propertyChanged: (b, o, n) => ((ChatView)b).UpdateAttach());

        /// <summary>True to show the attach button when something can be attached.</summary>
        public static readonly BindableProperty ShowAttachButtonProperty = BindableProperty.Create(
            nameof(ShowAttachButton), typeof(bool), typeof(ChatView), true, propertyChanged: (b, o, n) => ((ChatView)b).UpdateAttach());

        /// <summary>True to read replies aloud as they are written.</summary>
        public static readonly BindableProperty ReadAloudProperty = BindableProperty.Create(
            nameof(ReadAloud), typeof(bool), typeof(ChatView), false, propertyChanged: (b, o, n) => { if (!(bool)n) ((ChatView)b).ReplySpeaker?.Stop(); });

        /// <summary>Reads the replies aloud when <see cref="ReadAloud"/> is on.</summary>
        public static readonly BindableProperty ReplySpeakerProperty = BindableProperty.Create(
            nameof(ReplySpeaker), typeof(IReplySpeaker), typeof(ChatView), null, defaultValueCreator: b => new TextToSpeechReplySpeaker());

        /// <summary>A view shown above the transcript, scrolling with it (e.g. an app's own model card), or null.</summary>
        public static readonly BindableProperty HeaderProperty = BindableProperty.Create(
            nameof(Header), typeof(View), typeof(ChatView), null, propertyChanged: (b, o, n) => ((ChatView)b).ShowHeader());

        /// <summary>A view shown above the message box, staying put (e.g. toggle chips), or null.</summary>
        public static readonly BindableProperty ToolbarProperty = BindableProperty.Create(
            nameof(Toolbar), typeof(View), typeof(ChatView), null, propertyChanged: (b, o, n) => ((ChatView)b).ShowToolbar());

        private ScrollView _scroll;
        private ContentView _header;
        private ContentView _toolbar;
        private Layout _transcript;
        private View _empty;
        private ModelLoadCard _loadCard;
        private Label _status;
        private InputView _input;
        private View _send;
        private View _stop;
        private View _attach;
        private Layout _attachments;
        private CancellationTokenSource _replyCancel;
        private Task _pendingSend;
        private bool _replacingModel;
        private bool _atEnd = true;
        private bool _ownsClient;
        // The transcript's view of each message, and whether a pass that creates the missing ones is scheduled.
        private readonly Dictionary<ChatMessageItem, View> _messageViews = new Dictionary<ChatMessageItem, View>();
        private bool _renderScheduled;

        /// <summary>The on-device model to chat with; the view loads it (downloading it first, after asking), or null.</summary>
        public LanguageModel Model
        {
            get => (LanguageModel)GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        /// <summary>The chat client to send messages to, or null for the <see cref="Model"/>'s own.</summary>
        public IChatClient ChatClient
        {
            get => (IChatClient)GetValue(ChatClientProperty);
            set => SetValue(ChatClientProperty, value);
        }

        /// <summary>The conversation shown and continued; created from <see cref="Model"/> / <see cref="ChatClient"/> unless set.</summary>
        public ChatConversation Conversation
        {
            get => (ChatConversation)GetValue(ConversationProperty);
            set => SetValue(ConversationProperty, value);
        }

        /// <summary>The system instructions (<see cref="ChatConversation.Instructions"/>).</summary>
        public string Instructions
        {
            get => (string)GetValue(InstructionsProperty);
            set => SetValue(InstructionsProperty, value);
        }

        /// <summary>True to let the model think before it answers (for models that can).</summary>
        public bool ThinkingEnabled
        {
            get => (bool)GetValue(ThinkingEnabledProperty);
            set => SetValue(ThinkingEnabledProperty, value);
        }

        /// <summary>Options for each message (tools, the longest reply...), copied per message; or null.</summary>
        public ChatOptions Options
        {
            get => (ChatOptions)GetValue(OptionsProperty);
            set => SetValue(OptionsProperty, value);
        }

        /// <summary>Picks each message's template; a <see cref="ChatMessageTemplateSelector"/> by default.</summary>
        public DataTemplateSelector MessageTemplateSelector
        {
            get => (DataTemplateSelector)GetValue(MessageTemplateSelectorProperty);
            set => SetValue(MessageTemplateSelectorProperty, value);
        }

        /// <summary>Where attachments come from when the attach button is tapped.</summary>
        public IChatAttachmentSource AttachmentSource
        {
            get => (IChatAttachmentSource)GetValue(AttachmentSourceProperty);
            set => SetValue(AttachmentSourceProperty, value);
        }

        /// <summary>What can be attached; null (the default) for what the <see cref="Model"/> takes (nothing without one).</summary>
        public ChatAttachmentKinds? AttachmentKinds
        {
            get => (ChatAttachmentKinds?)GetValue(AttachmentKindsProperty);
            set => SetValue(AttachmentKindsProperty, value);
        }

        /// <summary>True to show the attach button when something can be attached.</summary>
        public bool ShowAttachButton
        {
            get => (bool)GetValue(ShowAttachButtonProperty);
            set => SetValue(ShowAttachButtonProperty, value);
        }

        /// <summary>True to read replies aloud as they are written.</summary>
        public bool ReadAloud
        {
            get => (bool)GetValue(ReadAloudProperty);
            set => SetValue(ReadAloudProperty, value);
        }

        /// <summary>Reads the replies aloud when <see cref="ReadAloud"/> is on.</summary>
        public IReplySpeaker ReplySpeaker
        {
            get => (IReplySpeaker)GetValue(ReplySpeakerProperty);
            set => SetValue(ReplySpeakerProperty, value);
        }

        /// <summary>The attachments waiting to be sent with the next message (shown above the message box).</summary>
        public System.Collections.ObjectModel.ObservableCollection<DataContent> PendingAttachments { get; } = new System.Collections.ObjectModel.ObservableCollection<DataContent>();

        /// <summary>What can be attached now: <see cref="AttachmentKinds"/>, or what the model takes.</summary>
        public ChatAttachmentKinds EffectiveAttachmentKinds =>
            AttachmentKinds ?? (Model == null ? ChatAttachmentKinds.None
                : (Model.SupportsImages ? ChatAttachmentKinds.Image : ChatAttachmentKinds.None)
                  | (Model.SupportsAudio ? ChatAttachmentKinds.Audio : ChatAttachmentKinds.None));

        /// <summary>A view shown above the transcript, scrolling with it (e.g. an app's own model card), or null.</summary>
        public View Header
        {
            get => (View)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        /// <summary>A view shown above the message box, staying put (e.g. toggle chips), or null.</summary>
        public View Toolbar
        {
            get => (View)GetValue(ToolbarProperty);
            set => SetValue(ToolbarProperty, value);
        }

        /// <summary>The message box's placeholder.</summary>
        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        /// <summary>
        /// Makes each message's options (tools, instructions...) as it is sent, instead of <see cref="Options"/>: for an agent
        /// whose tools depend on the message. The reasoning effort is filled in from <see cref="ThinkingEnabled"/> when the
        /// options leave it null. Or null.
        /// </summary>
        public Func<ChatSendRequest, Task<ChatOptions>> OptionsProvider { get; set; }

        /// <summary>
        /// Called with the <see cref="LoadState"/> made for a new <see cref="Model"/>, before loading starts: set its backend,
        /// display name, download size or engine settings. Or null.
        /// </summary>
        public Action<ModelLoadState> LoadStateCreated { get; set; }

        /// <summary>
        /// Asked after each reply whether the message should be sent again into the same reply, with options made anew
        /// by <see cref="OptionsProvider"/>: for an agent whose tool group ended the request to be given from the start
        /// (Emgu.LiteRT.LM.Agents' ToolRouter with RerunAfterStub; ChatSkillSet.TakeRerun). At most twice per message. Or null.
        /// </summary>
        public Func<bool> RerunRequested { get; set; }

        /// <summary>
        /// Asked when a request threw, with what it threw and the message's options: true to send the message again into
        /// the same reply with the options as the call left them (e.g. a server rejected the reasoning option, so it
        /// goes without). Once per message; the error is thrown on when it answers false. Or null.
        /// </summary>
        public Func<Exception, ChatOptions, bool> ResendRequested { get; set; }

        /// <summary>
        /// Sends a message once the model is ready, instead of <see cref="DefaultSendAsync(ChatSendRequest, CancellationToken)"/>: for an app whose sending
        /// does more (context from documents, saving the exchange with its extras...). The view has already cleared the
        /// message box, stopped the speaker and started the Stop button, whose token it passes. Or null.
        /// </summary>
        public Func<ChatSendRequest, CancellationToken, Task> Sender { get; set; }

        /// <summary>True while a message is being sent (from Send until the reply is complete or stopped).</summary>
        public bool IsSending => _replyCancel != null;

        /// <summary>The send going on, to wait for before e.g. releasing the model; a completed task when there is none.</summary>
        public Task PendingSend => _pendingSend ?? Task.CompletedTask;

        /// <summary>
        /// Shows the conversation's summary (<see cref="ChatConversation.Summary"/>) in an alert; the default message
        /// template's "Earlier messages were summarized" divider runs it when tapped.
        /// </summary>
        public System.Windows.Input.ICommand ShowSummaryCommand => _showSummaryCommand ??= new Command(async () =>
        {
            string summary = Conversation?.Summary;
            Page page = Window?.Page;
            if (page != null)
                await page.DisplayAlertAsync("Summary of the earlier messages",
                    summary ?? "The earlier messages were dropped to make room; the model no longer sees them.", "Close");
        });

        private System.Windows.Input.ICommand _showSummaryCommand;

        /// <summary>
        /// Copies a message's text (the command's parameter, a <see cref="ChatMessageItem"/>) to the clipboard and says so in
        /// the status line; the default templates' "Copy" under a user message's bubble and under a finished reply
        /// (<see cref="ChatMessageItem.CanCopy"/>: the answer, without the thinking) run it when tapped.
        /// </summary>
        public System.Windows.Input.ICommand CopyMessageCommand => _copyMessageCommand ??= new Command<ChatMessageItem>(async message =>
        {
            if (string.IsNullOrEmpty(message?.Text))
                return;
            try
            {
                await Clipboard.Default.SetTextAsync(message.Text);
                SetStatus("Copied.");
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't copy: " + ex.Message);
            }
        });

        private System.Windows.Input.ICommand _copyMessageCommand;

        /// <summary>Loading the <see cref="Model"/>, or null without one.</summary>
        public ModelLoadState LoadState { get; private set; }

        /// <summary>Raised when an exchange is complete (the reply done or interrupted), e.g. to save it.</summary>
        public event EventHandler<ChatExchangeEventArgs> ExchangeCompleted;

        /// <summary>Raised when loading the <see cref="Model"/> failed (<see cref="ModelLoadState.Error"/> says why).</summary>
        public event EventHandler ModelLoadFailed;

        /// <inheritdoc/>
        protected override void OnParentSet()
        {
            base.OnParentSet();
            // Taken off the page: a recording in progress is discarded.
            if (Parent == null)
                _ = CancelRecordingAsync();
            ChatResources.ApplyDefaultTemplate(this, TemplateKey);
            // A model already on the device starts loading straight away; one that needs a download waits for the card's
            // Download button (or the first message).
            if (Parent != null && LoadState != null && LoadState.IsDownloaded && !LoadState.IsReady && !LoadState.IsLoading)
                _ = LoadModelAsync();
        }

        /// <inheritdoc/>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _scroll = GetTemplateChild("PART_Scroll") as ScrollView;
            _transcript = GetTemplateChild("PART_Transcript") as Layout;
            _empty = GetTemplateChild("PART_Empty") as View;
            _loadCard = GetTemplateChild("PART_LoadCard") as ModelLoadCard;
            _status = GetTemplateChild("PART_Status") as Label;
            _input = GetTemplateChild("PART_Input") as InputView;
            _send = GetTemplateChild("PART_Send") as View;
            _stop = GetTemplateChild("PART_Stop") as View;
            _attach = GetTemplateChild("PART_Attach") as View;
            _attachments = GetTemplateChild("PART_Attachments") as Layout;
            _header = GetTemplateChild("PART_Header") as ContentView;
            _toolbar = GetTemplateChild("PART_Toolbar") as ContentView;
            ApplyTalkPart(GetTemplateChild("PART_Talk") as View);
            ShowHeader();
            ShowToolbar();
            // Enter sends on a desktop keyboard (ChatPromptEditor; Shift+Enter for a new line).
            if (_input is ChatPromptEditor prompt)
                prompt.SendRequested += (s, e) => _ = SendAsync();
            Hook(_send, "ChatSendButtonStyle", () => _ = SendAsync());
            Hook(_stop, "ChatStopButtonStyle", StopReply);
            Hook(_attach, "ChatAttachButtonStyle", () => _ = AttachAsync());
            UpdateAttach();
            ShowPendingAttachments();
            if (_input is Entry entry)
                entry.Completed += (s, e) => _ = SendAsync();
            if (_scroll != null)
                _scroll.Scrolled += (s, e) => _atEnd = e.ScrollY >= _scroll.ContentSize.Height - _scroll.Height - 40;
            if (_transcript != null)
                _transcript.SizeChanged += (s, e) => { if (_atEnd) ScrollToEnd(); };
            if (_loadCard != null)
                _loadCard.State = LoadState;
            ApplyPlaceholder();
            BindTranscript();
            UpdateBusy();
            SetStatus(null);
        }

        /// <summary>
        /// Create the view; it shows nothing until it has a template (when it is added to a page).
        /// </summary>
        public ChatView()
        {
            PendingAttachments.CollectionChanged += (s, e) => ShowPendingAttachments();
        }

        private static void Hook(View view, string styleKey, Action action)
        {
            if (view == null)
                return;
            if (view.Style == null && ChatResources.Find<Style>(styleKey) is Style style && style.TargetType.IsInstanceOfType(view))
                view.Style = style;
            if (view is Button button)
                button.Clicked += (s, e) => action();
            else
            {
                TapGestureRecognizer tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) => action();
                view.GestureRecognizers.Add(tap);
            }
        }

        // ---------- Model, client and conversation ----------

        private void OnSourceChanged()
        {
            if (_replacingModel)
                return;
            LanguageModel model = Model;
            IChatClient client = ChatClient;
            // Only the model client the view created itself; an app's client is the app's.
            if (_ownsClient)
                Conversation?.Client.Dispose();
            _ownsClient = false;
            CreateLoadState(model);
            if (model == null && client == null)
            {
                Conversation = null;
                return;
            }
            // The model's own client is created with the first message, once the model is loaded.
            _ownsClient = client == null;
            Conversation = client != null ? new ChatConversation(client, model) : new ChatConversation(model);
            if (Parent != null && LoadState != null && LoadState.IsDownloaded)
                _ = LoadModelAsync();
            UpdateAttach();
        }

        private void CreateLoadState(LanguageModel model)
        {
            LoadState = model != null ? new ModelLoadState(model) { DisplayName = model.GetType().Name } : null;
            if (LoadState != null)
            {
                LoadState.PropertyChanged += (s, e) =>
                {
                    // Also when the card's Download or Retry button started the load.
                    if (e.PropertyName == nameof(ModelLoadState.HasError) && s is ModelLoadState state && state.HasError)
                        ModelLoadFailed?.Invoke(this, EventArgs.Empty);
                };
                LoadStateCreated?.Invoke(LoadState);
            }
            if (_loadCard != null)
                _loadCard.State = LoadState;
        }

        /// <summary>
        /// Replace the model and load the new one, keeping the conversation (which an app's <see cref="ChatClient"/>
        /// continues on the new model): for another backend, context window or thread count of the same model. The old
        /// model is the app's to release, once <see cref="PendingSend"/> is done.
        /// </summary>
        /// <param name="model">The new model (not loaded)</param>
        public void ReplaceModel(LanguageModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (ChatClient == null)
                throw new InvalidOperationException("ReplaceModel needs an app's ChatClient; without one, set Model (a new conversation).");
            _replacingModel = true;
            try
            {
                Model = model;
            }
            finally
            {
                _replacingModel = false;
            }
            CreateLoadState(model);
            if (Parent != null && LoadState.IsDownloaded)
                _ = LoadModelAsync();
            UpdateAttach();
        }

        private void OnConversationChanged(ChatConversation old)
        {
            if (old != null)
            {
                old.ExchangeCompleted -= OnExchangeCompleted;
                old.Summarizing -= OnSummarizing;
                old.Messages.CollectionChanged -= OnMessagesChanged;
            }
            ChatConversation conversation = Conversation;
            if (conversation != null)
            {
                conversation.ExchangeCompleted += OnExchangeCompleted;
                conversation.Summarizing += OnSummarizing;
                conversation.Messages.CollectionChanged += OnMessagesChanged;
            }
            ApplyInstructions();
            BindTranscript();
            UpdateBusy();
        }

        private void ApplyInstructions()
        {
            if (Conversation != null && Instructions != null)
                Conversation.Instructions = Instructions;
        }

        private void ApplyPlaceholder()
        {
            if (_input != null)
                _input.Placeholder = Placeholder;
        }

        private void BindTranscript()
        {
            if (_transcript == null)
                return;
            // The views are made by the view itself rather than a BindableLayout, so that a long saved chat appears a few
            // messages at a time (the latest first) instead of holding the UI thread until all of them are laid out.
            _transcript.Children.Clear();
            _messageViews.Clear();
            RenderTranscript();
            UpdateEmpty();
            ScrollToEnd();
        }

        private void OnMessagesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateEmpty();
            // Changes made together (e.g. loading a chat) are rendered together, once the caller is done.
            if (!_renderScheduled)
            {
                _renderScheduled = true;
                Dispatcher.Dispatch(RenderTranscript);
            }
            // A new message (the user's or the reply) brings the transcript to the end.
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                _atEnd = true;
                ScrollToEnd();
                // A new reply is read aloud as it is written.
                ChatMessageItem reply = e.NewItems?.OfType<ChatMessageItem>().FirstOrDefault(m => m.Role == ChatMessageRole.Assistant && m.IsStreaming);
                if (reply != null && ReadAloud)
                    ReplySpeaker?.Start(reply);
            }
        }

        /// <summary>True while some messages have no view yet (a long chat is still being shown).</summary>
        public bool IsRenderingTranscript
        {
            get { return Conversation != null && _messageViews.Count < Conversation.Messages.Count; }
        }

        // Make the transcript's children the views of the conversation's messages, in order: removes the views of removed
        // messages and creates the missing ones, the latest first, for about one frame; the rest in later passes.
        private void RenderTranscript()
        {
            _renderScheduled = false;
            if (_transcript == null)
                return;
            IList<ChatMessageItem> messages = (IList<ChatMessageItem>)Conversation?.Messages ?? Array.Empty<ChatMessageItem>();
            HashSet<ChatMessageItem> present = new HashSet<ChatMessageItem>(messages);
            foreach (ChatMessageItem gone in _messageViews.Keys.Where(m => !present.Contains(m)).ToList())
            {
                _transcript.Children.Remove(_messageViews[gone]);
                _messageViews.Remove(gone);
            }
            // How many of the messages before each one have a view, i.e. where its view goes.
            int[] before = new int[messages.Count];
            for (int i = 1; i < messages.Count; i++)
                before[i] = before[i - 1] + (_messageViews.ContainsKey(messages[i - 1]) ? 1 : 0);
            System.Diagnostics.Stopwatch slice = System.Diagnostics.Stopwatch.StartNew();
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                ChatMessageItem message = messages[i];
                if (!_messageViews.TryGetValue(message, out View view))
                {
                    if (slice.ElapsedMilliseconds >= 16)
                    {
                        // Let input and layout run, then go on.
                        _renderScheduled = true;
                        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), RenderTranscript);
                        return;
                    }
                    view = CreateMessageView(message);
                    _messageViews[message] = view;
                }
                int index = before[i];
                if (index < _transcript.Children.Count && _transcript.Children[index] == view)
                    continue;
                _transcript.Children.Remove(view);
                _transcript.Children.Insert(Math.Min(index, _transcript.Children.Count), view);
            }
        }

        private View CreateMessageView(ChatMessageItem message)
        {
            DataTemplate template = MessageTemplateSelector?.SelectTemplate(message, _transcript);
            while (template is DataTemplateSelector selector)
                template = selector.SelectTemplate(message, _transcript);
            View view = template?.CreateContent() as View ?? new Label { Text = message.Text };
            view.BindingContext = message;
            return view;
        }

        private void UpdateEmpty()
        {
            if (_empty != null)
                _empty.IsVisible = Conversation == null || Conversation.Messages.Count == 0;
        }

        private void OnExchangeCompleted(object sender, ChatExchangeEventArgs e)
        {
            ExchangeCompleted?.Invoke(this, e);
        }

        private void OnSummarizing(object sender, bool summarizing)
        {
            // Afterwards "Generating..." only while a message is being sent (not after "compact now").
            SetStatus(summarizing ? "Summarizing the earlier conversation..." : _replyCancel != null ? "Generating..." : null);
        }

        private async Task<bool> LoadModelAsync()
        {
            ModelLoadState state = LoadState;
            if (state == null)
                return true;
            return await state.LoadAsync();
        }

        // ---------- Sending ----------

        /// <summary>
        /// Send the message box's text, as if Send was tapped.
        /// </summary>
        public async Task SendAsync()
        {
            ChatConversation conversation = Conversation;
            string text = _input?.Text?.Trim() ?? string.Empty;
            if (conversation == null || conversation.IsBusy || _replyCancel != null || (text.Length == 0 && PendingAttachments.Count == 0))
                return;
            List<DataContent> attachments = PendingAttachments.ToList();
            if (_input != null)
                _input.Text = string.Empty;
            PendingAttachments.Clear();
            await SendAsync(text, attachments);
        }

        /// <summary>
        /// Send a message: wait for the model to load (starting it if needed), then stream the reply into the transcript.
        /// </summary>
        /// <param name="text">The message</param>
        /// <param name="attachments">Images and audio for models that take them, or null</param>
        /// <param name="context">Text given to the model before the message (e.g. document excerpts), not shown; or null</param>
        /// <param name="voiceMessage">True for a voice message (the microphone's; <see cref="ChatSendRequest.IsVoiceMessage"/>)</param>
        public async Task SendAsync(string text, IEnumerable<DataContent> attachments = null, string context = null, bool voiceMessage = false)
        {
            ChatConversation conversation = Conversation;
            if (conversation == null || conversation.IsBusy || _replyCancel != null)
                return;
            CancellationTokenSource cancel = new CancellationTokenSource();
            _replyCancel = cancel;
            ReplySpeaker?.Stop();
            UpdateBusy();
            try
            {
                if (LoadState != null && !LoadState.IsReady)
                {
                    LoadState.Dismissed = false;
                    SetStatus("Waiting for the model to load...");
                    // Stop gives up on the message; the download or load goes on (the card's Cancel stops that).
                    Task<bool> load = LoadModelAsync();
                    Task stopped = Task.Delay(Timeout.Infinite, cancel.Token);
                    if (await Task.WhenAny(load, stopped) == stopped)
                    {
                        SetStatus(null);
                        return;
                    }
                    if (!await load)
                    {
                        SetStatus(LoadState.HasError ? null : "The model isn't loaded.");
                        return;
                    }
                }
                ChatSendRequest request = new ChatSendRequest(text, attachments, context) { IsVoiceMessage = voiceMessage };
                Task send = (Sender ?? DefaultSendAsync)(request, cancel.Token);
                _pendingSend = send;
                await send;
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _pendingSend = null;
                _replyCancel = null;
                cancel.Dispose();
                UpdateBusy();
            }
        }

        /// <summary>
        /// What the view does to send a message once the model is ready (what <see cref="Sender"/> replaces): makes the
        /// options (<see cref="OptionsProvider"/> or <see cref="Options"/>), streams the reply into the conversation with
        /// "Generating..." until its first text, and sends the message again when <see cref="RerunRequested"/> says so. A
        /// <see cref="Sender"/> of an app's own can call it for the plain send.
        /// </summary>
        /// <param name="request">The message</param>
        /// <param name="cancellationToken">Stops the reply (the Stop button)</param>
        public Task DefaultSendAsync(ChatSendRequest request, CancellationToken cancellationToken) => DefaultSendAsync(request, cancellationToken, null);

        /// <summary>
        /// The plain send (<see cref="DefaultSendAsync(ChatSendRequest, CancellationToken)"/>) through a client of the
        /// message's own, e.g. one that plans the message and runs its steps.
        /// </summary>
        /// <param name="request">The message</param>
        /// <param name="cancellationToken">Stops the reply (the Stop button)</param>
        /// <param name="client">The client for this message, or null for the conversation's</param>
        public async Task DefaultSendAsync(ChatSendRequest request, CancellationToken cancellationToken, IChatClient client)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            ChatConversation conversation = Conversation;
            if (conversation == null)
                return;
            SetStatus("Generating...");
            ChatOptions options = await MakeOptionsAsync(request);
            ChatMessageItem reply;
            using (StatusUntilFirstText(conversation))
            {
                try
                {
                    reply = await conversation.SendAsync(request.Text, request.Attachments, options, request.Context, cancellationToken, client);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && !cancellationToken.IsCancellationRequested
                    && ResendRequested?.Invoke(ex, options) == true)
                {
                    // The request was refused for something in its options (a server that doesn't take reasoning_effort):
                    // the same message again into the same reply, with the options changed.
                    reply = conversation.Messages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant);
                    reply = reply == null
                        ? await conversation.SendAsync(request.Text, request.Attachments, options, request.Context, cancellationToken, client)
                        : await conversation.RegenerateAsync(reply, options, cancellationToken, client);
                }
            }
            // A tool group's stub ended the request: the message goes again, with the group's tools from the start.
            for (int rerun = 0; rerun < 2 && !cancellationToken.IsCancellationRequested && RerunRequested?.Invoke() == true; rerun++)
            {
                options = await MakeOptionsAsync(request);
                reply = await conversation.RegenerateAsync(reply, options, cancellationToken, client);
            }
            SetStatus(null);
        }

        private async Task<ChatOptions> MakeOptionsAsync(ChatSendRequest request)
        {
            ChatOptions options = OptionsProvider != null
                ? await OptionsProvider(request) ?? new ChatOptions()
                : Options?.Clone() ?? new ChatOptions();
            if (options.Reasoning == null)
                options.Reasoning = new ReasoningOptions { Effort = ThinkingEnabled ? ReasoningEffort.Medium : ReasoningEffort.None };
            return options;
        }

        // Clears "Generating..." once the reply starts streaming.
        private IDisposable StatusUntilFirstText(ChatConversation conversation)
        {
            ChatMessageItem reply = null;
            System.ComponentModel.PropertyChangedEventHandler onChanged = (s, e) =>
            {
                // The status of this send ("Generating...", "Reading the conversation so far...") goes once the reply starts.
                if (reply != null && (reply.HasAnswer || reply.Thinking.Length > 0) && _status?.IsVisible == true)
                    SetStatus(null);
            };
            NotifyCollectionChangedEventHandler onAdded = (s, e) =>
            {
                ChatMessageItem added = e.NewItems?.OfType<ChatMessageItem>().FirstOrDefault(m => m.Role == ChatMessageRole.Assistant);
                if (added != null)
                {
                    reply = added;
                    reply.PropertyChanged += onChanged;
                }
            };
            conversation.Messages.CollectionChanged += onAdded;
            return new Unsubscriber(() =>
            {
                conversation.Messages.CollectionChanged -= onAdded;
                if (reply != null)
                    reply.PropertyChanged -= onChanged;
            });
        }

        private sealed class Unsubscriber : IDisposable
        {
            private Action _dispose;
            public Unsubscriber(Action dispose) => _dispose = dispose;
            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }

        /// <summary>
        /// Stop the reply being written; it keeps what was written so far.
        /// </summary>
        public void StopReply()
        {
            _replyCancel?.Cancel();
            ReplySpeaker?.Stop();
        }

        // ---------- Attachments ----------

        private void ShowHeader()
        {
            if (_header != null)
            {
                _header.Content = Header;
                _header.IsVisible = Header != null;
            }
        }

        private void ShowToolbar()
        {
            if (_toolbar != null)
            {
                _toolbar.Content = Toolbar;
                _toolbar.IsVisible = Toolbar != null;
            }
        }

        /// <summary>
        /// Scroll the transcript to its end (e.g. after the app added a card to a message).
        /// </summary>
        public void ScrollToEnd(bool animated = false)
        {
            _atEnd = true;
            ScrollToEnd();
        }

        private void UpdateAttach()
        {
            if (_attach != null)
                _attach.IsVisible = ShowAttachButton && EffectiveAttachmentKinds != ChatAttachmentKinds.None;
            UpdateTalk();
        }

        private async Task AttachAsync()
        {
            ChatAttachmentKinds kinds = EffectiveAttachmentKinds;
            IChatAttachmentSource source = AttachmentSource;
            if (source == null || kinds == ChatAttachmentKinds.None)
                return;
            try
            {
                foreach (DataContent attachment in await source.PickAsync(kinds, this))
                    PendingAttachments.Add(attachment);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                SetStatus("Couldn't attach the file: " + ex.Message);
            }
        }

        private void ShowPendingAttachments()
        {
            Layout layout = _attachments;
            if (layout == null)
                return;
            layout.Clear();
            foreach (DataContent attachment in PendingAttachments)
            {
                ChatAttachmentView view = new ChatAttachmentView { Attachment = attachment, CanRemove = true };
                view.RemoveRequested += (s, e) => PendingAttachments.Remove(attachment);
                layout.Add(view);
            }
            layout.IsVisible = PendingAttachments.Count > 0;
            // The template's row holds the chips in a horizontal ScrollView, measured while they were hidden: on Mac Catalyst
            // it kept its zero height when they appeared, so an attached picture never showed. Showing and hiding the
            // ScrollView itself makes the message box measure the row again.
            if (layout.Parent is ScrollView scroll)
                scroll.IsVisible = layout.IsVisible;
        }

        private void UpdateBusy()
        {
            bool busy = _replyCancel != null;
            if (_send != null)
                _send.IsVisible = !busy;
            if (_stop != null)
                _stop.IsVisible = busy;
            UpdateTalk();
        }

        /// <summary>The status line under the transcript ("Generating...", an error); null hides it. Cleared when a reply's text starts.</summary>
        /// <param name="text">The status, or null</param>
        public void SetStatus(string text)
        {
            if (_status == null)
                return;
            _status.Text = text ?? string.Empty;
            _status.IsVisible = !string.IsNullOrEmpty(text);
        }

        private void ScrollToEnd()
        {
            ScrollView scroll = _scroll;
            if (scroll == null)
                return;
            // After layout: scroll to the end of the content, clamped (Android doesn't clamp double.MaxValue).
            Dispatcher.Dispatch(async () =>
            {
                try
                {
                    await scroll.ScrollToAsync(0, Math.Max(0, scroll.ContentSize.Height - scroll.Height), false);
                }
                catch (Exception)
                {
                }
            });
        }
    }
}
