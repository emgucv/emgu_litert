//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Specialized;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui
{
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
    /// e.g. an Editor), "PART_Send" and "PART_Stop" (Buttons, or views that are tapped) and "PART_Attach" (unused yet). The
    /// messages use <see cref="MessageTemplateSelector"/>, and the Send and Stop buttons the styles "ChatSendButtonStyle"
    /// and "ChatStopButtonStyle" unless the template gives them one.
    /// </para>
    /// </summary>
    public class ChatView : TemplatedView
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

        private ScrollView _scroll;
        private Layout _transcript;
        private View _empty;
        private ModelLoadCard _loadCard;
        private Label _status;
        private InputView _input;
        private View _send;
        private View _stop;
        private View _attach;
        private CancellationTokenSource _replyCancel;
        private bool _atEnd = true;
        private bool _ownsClient;

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

        /// <summary>The message box's placeholder.</summary>
        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

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
            Hook(_send, "ChatSendButtonStyle", () => _ = SendAsync());
            Hook(_stop, "ChatStopButtonStyle", StopReply);
            if (_attach != null)
                _attach.IsVisible = false;
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
            LanguageModel model = Model;
            IChatClient client = ChatClient;
            // Only the model client the view created itself; an app's client is the app's.
            if (_ownsClient)
                Conversation?.Client.Dispose();
            _ownsClient = false;
            LoadState = model != null ? new ModelLoadState(model) { DisplayName = model.GetType().Name } : null;
            if (LoadState != null)
                LoadState.PropertyChanged += (s, e) =>
                {
                    // Also when the card's Download or Retry button started the load.
                    if (e.PropertyName == nameof(ModelLoadState.HasError) && s is ModelLoadState state && state.HasError)
                        ModelLoadFailed?.Invoke(this, EventArgs.Empty);
                };
            if (_loadCard != null)
                _loadCard.State = LoadState;
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
            BindableLayout.SetItemTemplateSelector(_transcript, MessageTemplateSelector);
            BindableLayout.SetItemsSource(_transcript, Conversation?.Messages);
            UpdateEmpty();
            ScrollToEnd();
        }

        private void OnMessagesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateEmpty();
            // A new message (the user's or the reply) brings the transcript to the end.
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                _atEnd = true;
                ScrollToEnd();
            }
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
            SetStatus(summarizing ? "Summarizing the earlier conversation..." : "Generating...");
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
            string text = _input?.Text?.Trim();
            if (conversation == null || conversation.IsBusy || _replyCancel != null || string.IsNullOrEmpty(text))
                return;
            _input.Text = string.Empty;
            await SendAsync(text);
        }

        /// <summary>
        /// Send a message: wait for the model to load (starting it if needed), then stream the reply into the transcript.
        /// </summary>
        /// <param name="text">The message</param>
        /// <param name="attachments">Images and audio for models that take them, or null</param>
        /// <param name="context">Text given to the model before the message (e.g. document excerpts), not shown; or null</param>
        public async Task SendAsync(string text, IEnumerable<DataContent> attachments = null, string context = null)
        {
            ChatConversation conversation = Conversation;
            if (conversation == null || conversation.IsBusy || _replyCancel != null)
                return;
            CancellationTokenSource cancel = new CancellationTokenSource();
            _replyCancel = cancel;
            UpdateBusy();
            try
            {
                if (LoadState != null && !LoadState.IsReady)
                {
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
                SetStatus("Generating...");
                ChatOptions options = Options?.Clone() ?? new ChatOptions();
                if (options.Reasoning == null)
                    options.Reasoning = new ReasoningOptions { Effort = ThinkingEnabled ? ReasoningEffort.Medium : ReasoningEffort.None };
                using (StatusUntilFirstText(conversation))
                    await conversation.SendAsync(text, attachments, options, context, cancel.Token);
                SetStatus(null);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _replyCancel = null;
                cancel.Dispose();
                UpdateBusy();
            }
        }

        // Clears "Generating..." once the reply starts streaming.
        private IDisposable StatusUntilFirstText(ChatConversation conversation)
        {
            ChatMessageItem reply = null;
            System.ComponentModel.PropertyChangedEventHandler onChanged = (s, e) =>
            {
                if (reply != null && (reply.HasAnswer || reply.Thinking.Length > 0) && _status?.Text == "Generating...")
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
        }

        private void UpdateBusy()
        {
            bool busy = _replyCancel != null;
            if (_send != null)
                _send.IsVisible = !busy;
            if (_stop != null)
                _stop.IsVisible = busy;
        }

        private void SetStatus(string text)
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
