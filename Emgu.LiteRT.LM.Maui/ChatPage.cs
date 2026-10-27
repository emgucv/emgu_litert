//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui.Features;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A whole chat page for an app: a header with the model, saved chats, a new chat and the settings; the chat
    /// (<see cref="ChatView"/>: the transcript, the message box, the card that downloads and loads the model); chips
    /// above the message box (Think, and the features'); a settings sheet - a side panel on wide screens - with the
    /// built-in settings and each feature's section; and an agent whose tools and instructions come from the
    /// features (<see cref="ChatFeature"/>, composed by a <see cref="ChatSkillSet"/> for each message).
    /// <code>
    /// var store = new ChatHistoryStore(Path.Combine(FileSystem.AppDataDirectory, "MyApp", "chats"));
    /// var options = new ChatPageOptions { Title = "Assistant", History = store };
    /// options.Models.Add(new ChatModelOption("Gemma4E2B", "Gemma 4 E2B", () => new Gemma4E2B()) { CanUseTools = true, AcceptsImages = true, AcceptsAudio = true, CanSummarize = true });
    /// foreach (ChatFeature feature in ChatPage.DefaultFeatures(new SqliteAgentMemoryStore(store), new SqliteAgentGoalStore(store)))
    ///     options.Features.Add(feature);
    /// options.Features.Add(new DiceFeature());    // your own
    /// MainPage = new NavigationPage(new ChatPage(options));
    /// </code>
    /// Its look is the "ChatPageTemplate" template and the styles the parts use (see <see cref="ChatStyles"/>), and the
    /// colours and fonts of <see cref="ChatTheme"/>: an app changes any of them by defining a resource with the same key.
    /// Register the handlers it needs with <c>builder.UseLiteRtChat()</c>. Its settings (the Think chip, the model, the
    /// features' switches) are kept by <see cref="ChatPageOptions.Settings"/>.
    /// </summary>
    public partial class ChatPage : ContentPage, IChatPageHost, IChatOverlays
    {
        private const string TemplateKey = "ChatPageTemplate";
        // The side panel (chats, models, settings) instead of sheets from this width on.
        private const double WideLayoutWidth = 820;

        private readonly ChatPageOptions _options;
        private readonly ChatSkillSet _skills;
        private readonly ChatFeatureServices _services = new ChatFeatureServices();
        private readonly List<ChatFeature> _features;
        private readonly ChatPageClient _client;
        private Label _titleLabel;
        private Button _backButton;
        private Grid _overlay;
        private Grid _layers;
        private Button _modelButton;
        private Button _chatsButton;
        private Button _newChatButton;
        private Button _settingsButton;
        private ChatView _chatView;
        private Label _statusLabel;
        private View _sidePanel;
        private Layout _sideContent;
        private BottomSheet _sheet;
        private bool? _wide;
        private int _busyDepth;
        private bool _started;

        /// <summary>
        /// Create the page.
        /// </summary>
        /// <param name="options">What it is made of (at least one model)</param>
        public ChatPage(ChatPageOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Models.Count == 0)
                throw new ArgumentException("At least one model is needed", nameof(options));
            Title = options.Title;
            ChatResources.SetThemeColor(this, BackgroundColorProperty, "ChatPageBackground");
            // A group's stub adds its tools to the request being answered; a rerun group ends the request instead.
            _skills = new ChatSkillSet(() => FunctionInvokingChatClient.CurrentContext?.Options, () =>
            {
                FunctionInvocationContext context = FunctionInvokingChatClient.CurrentContext;
                if (context != null)
                    context.Terminate = true;
            });
            _features = options.Features.ToList();
            foreach (ChatFeature feature in _features)
                if (feature.PersistEnabled && options.Settings.Contains("feature_" + feature.Name))
                    feature.Enabled = options.Settings.Get("feature_" + feature.Name, feature.Enabled);
            foreach (ChatFeature feature in _features)
                feature.RegisterServices(_services);
            foreach (ChatFeature feature in _features)
                feature.OnAttached(this);
            _skills.AddRange(_features);
            _client = new ChatPageClient(CreateInnerClient);
            // The page's Window is set after it is pushed: the features hear the app come to the front or leave it.
            PropertyChanged += (s, e) => { if (e.PropertyName == nameof(Window)) WatchWindow(); };
            if (ControlTemplate == null)
                ControlTemplate = ChatResources.Find<ControlTemplate>(TemplateKey);
        }

        private Window _watchedWindow;

        private void WatchWindow()
        {
            Window window = Window;
            if (window == _watchedWindow)
                return;
            if (_watchedWindow != null)
            {
                _watchedWindow.Resumed -= OnWindowResumed;
                _watchedWindow.Stopped -= OnWindowStopped;
            }
            _watchedWindow = window;
            if (window != null)
            {
                window.Resumed += OnWindowResumed;
                window.Stopped += OnWindowStopped;
            }
        }

        private void OnWindowResumed(object sender, EventArgs e)
        {
            foreach (ChatFeature feature in _features)
                feature.OnAppResumed();
        }

        private void OnWindowStopped(object sender, EventArgs e)
        {
            foreach (ChatFeature feature in _features)
                feature.OnAppStopped();
        }

        /// <summary>
        /// The built-in features: the core tools (date and time, calculator, units), memories about the user (with a
        /// store), the web (weather, Wikipedia, currency, reading pages; web search with a Tavily key), the user's
        /// location (off until they allow it), the clipboard (only for a message about it) and goals (with a store).
        /// </summary>
        /// <param name="memory">Where memories are kept (e.g. Emgu.LiteRT.LM.Chat.Sqlite's SqliteAgentMemoryStore), or null for no memories</param>
        /// <param name="goals">Where goals are kept (SqliteAgentGoalStore), or null for no goals</param>
        public static IList<ChatFeature> DefaultFeatures(IAgentMemoryStore memory = null, IAgentGoalStore goals = null)
        {
            var features = new List<ChatFeature> { new CoreToolsFeature() };
            if (memory != null)
                features.Add(new MemoryFeature(memory));
            features.Add(new WebFeature());
            features.Add(new LocationFeature());
            features.Add(new ClipboardFeature());
            if (goals != null)
                features.Add(new GoalsFeature(goals));
            return features;
        }

        /// <summary>The page's options.</summary>
        public ChatPageOptions Options => _options;

        /// <summary>The chat view (the transcript and the message box).</summary>
        public ChatView Chat => _chatView;

        /// <summary>The features, in order.</summary>
        public IReadOnlyList<ChatFeature> Features => _features;

        /// <summary>The skills (the features), composed into each message's tools and instructions.</summary>
        public ChatSkillSet Skills => _skills;

        /// <summary>The conversation shown (a new one for each new chat or model), or null before the page is built.</summary>
        public ChatConversation Conversation => _chatView?.Conversation;

        /// <summary>Raised after an exchange is complete (the reply done or interrupted).</summary>
        public event EventHandler<ChatExchangeEventArgs> ExchangeCompleted;

        /// <inheritdoc/>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _titleLabel = GetTemplateChild("PART_Title") as Label;
            _backButton = GetTemplateChild("PART_Back") as Button;
            _overlay = GetTemplateChild("PART_Overlay") as Grid;
            _layers = GetTemplateChild("PART_Layers") as Grid;
            _modelButton = GetTemplateChild("PART_Model") as Button;
            _chatsButton = GetTemplateChild("PART_Chats") as Button;
            _newChatButton = GetTemplateChild("PART_NewChat") as Button;
            _settingsButton = GetTemplateChild("PART_Settings") as Button;
            _chatView = GetTemplateChild("PART_Chat") as ChatView;
            _statusLabel = GetTemplateChild("PART_Status") as Label;
            _sidePanel = GetTemplateChild("PART_SidePanel") as View;
            _sideContent = GetTemplateChild("PART_SideContent") as Layout;
            _sheet = GetTemplateChild("PART_Sheet") as BottomSheet;
            if (_chatView == null)
                throw new InvalidOperationException("ChatPageTemplate needs a ChatView named PART_Chat.");
            if (_titleLabel != null)
                _titleLabel.Text = _options.Title;
            Hook(_modelButton, "ChatModelButtonStyle", OnModelButtonTapped);
            Hook(_chatsButton, "ChatHeaderButtonStyle", OnChatsTapped);
            Hook(_newChatButton, "ChatHeaderButtonStyle", () => StartNewChat());
            Hook(_settingsButton, "ChatHeaderButtonStyle", OnSettingsTapped);
            Hook(_backButton, "ChatHeaderButtonStyle", () => _ = Navigation.PopAsync());
            if (_backButton != null)
                _backButton.IsVisible = _options.ShowBackButton;
            SemanticProperties.SetDescription(_backButton, "Back");
            _chatView.SizeChanged += (s, e) => ChatSizeChanged?.Invoke(this, EventArgs.Empty);
            FlushOverlays();
            if (_modelButton != null)
                _modelButton.IsVisible = _options.ShowModelPicker;
            if (_chatsButton != null)
                _chatsButton.IsVisible = _options.History != null;
            SemanticProperties.SetDescription(_modelButton, "Choose the model");
            SemanticProperties.SetDescription(_chatsButton, "Saved chats");
            SemanticProperties.SetDescription(_newChatButton, "New chat");
            SemanticProperties.SetDescription(_settingsButton, "Settings");

            _chatView.ChatClient = _client;
            _chatView.LoadStateCreated = ConfigureLoadState;
            _chatView.OptionsProvider = ComposeOptionsAsync;
            _chatView.RerunRequested = () => _skills.TakeRerun();
            _chatView.ResendRequested = ShouldResend;
            _chatView.Sender = SendCoreAsync;
            _chatView.ThinkingEnabled = _options.Settings.Get("think", true);
            _chatView.ReadAloud = _options.Settings.Get("read_aloud", false);
            if (_options.AttachmentSource != null)
                _chatView.AttachmentSource = _options.AttachmentSource;
            if (_options.ReplySpeaker != null)
                _chatView.ReplySpeaker = _options.ReplySpeaker;
            _chatView.ExchangeCompleted += OnExchangeCompleted;
            _chatView.ModelLoadFailed += OnModelLoadFailed;
            _chatView.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ChatView.Conversation))
                    OnConversationChanged();
            };
            BuildSettingsView();
            BuildChips();
            UpdatePrivateState();
            OnPageBuilt();
        }

        /// <summary>
        /// Called once the page's views exist (at the end of applying the template, which happens in the constructor -
        /// before a derived class's constructor body), for a derived page to add its own. The model is chosen later,
        /// when the page first appears.
        /// </summary>
        protected virtual void OnPageBuilt()
        {
        }

        private static void Hook(Button button, string styleKey, Action action)
        {
            if (button == null)
                return;
            ChatResources.ApplyStyle(button, styleKey);
            button.Clicked += (s, e) => action();
        }

        /// <inheritdoc/>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            // The model chosen last time (loading starts if it is on the device): on the first appearance, so a derived
            // page's constructor has run.
            if (!_started)
            {
                _started = true;
                SelectInitialModel();
            }
            foreach (ChatFeature feature in _features)
                feature.OnAppearing();
        }

        /// <summary>
        /// True in a hands-free chat (a voice conversation): replies are spoken, the model doesn't think first, and skills
        /// leave out tools whose results need the screen (<see cref="ChatMessageContext.HandsFree"/>).
        /// </summary>
        public bool HandsFree { get; private set; }

        /// <summary>Start or end a hands-free chat (<see cref="HandsFree"/>); the chips follow.</summary>
        /// <param name="handsFree">True to start one</param>
        public void SetHandsFree(bool handsFree)
        {
            if (HandsFree == handsFree)
                return;
            HandsFree = handsFree;
            UpdateChips();
        }

        /// <inheritdoc/>
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            foreach (ChatFeature feature in _features)
                feature.OnDisappearing();
        }

        /// <inheritdoc/>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width <= 0)
                return;
            bool wide = width >= WideLayoutWidth;
            if (_wide == wide)
                return;
            _wide = wide;
            ApplyLayout(wide);
        }

        // Wide: the chats, the models and the settings sit in the side panel; narrow: they open in sheets.
        private void ApplyLayout(bool wide)
        {
            if (_sidePanel != null)
                _sidePanel.IsVisible = wide;
            if (_titleLabel != null)
                _titleLabel.IsVisible = wide && !string.IsNullOrEmpty(_options.Title);
            if (_sideContent == null)
                return;
            _sideContent.Children.Clear();
            if (!wide)
                return;
            if (_options.History != null)
            {
                _sideContent.Children.Add(SectionTitle("Chats"));
                _sideContent.Children.Add(_sideChatList = new VerticalStackLayout());
                RefreshChatList();
                _sideContent.Children.Add(Divider());
            }
            if (_options.ShowModelPicker && _options.Models.Count > 1)
            {
                _sideContent.Children.Add(SectionTitle("Model"));
                _sideContent.Children.Add(_sideModelList = new VerticalStackLayout());
                RefreshModelList();
                _sideContent.Children.Add(Divider());
            }
            _sideContent.Children.Add(SectionTitle("Settings"));
            if (_settingsView.Parent is Layout parent)
                parent.Children.Remove(_settingsView);
            _sideContent.Children.Add(_settingsView);
        }

        // Developer mode's notice: one line ("Developer: 18 tools and the instructions · Show") that opens to the tool list
        // and the instructions - folded, as the instructions alone fill a phone's screen after every reply.
        private static View DeveloperNotice(IReadOnlyList<string> tools, string instructions)
        {
            var body = new Label
            {
                Text = "Tools: " + (tools.Count == 0 ? "none" : string.Join(", ", tools)) + "\n\nInstructions:\n" + (instructions ?? "none"),
                IsVisible = false
            };
            ChatResources.ApplyStyle(body, "ChatDecorationTextStyle");
            string summary = "Developer: " + (tools.Count == 0 ? "no tools" : tools.Count == 1 ? "1 tool" : tools.Count + " tools") + " and the instructions";
            Label header = null;
            header = Link(summary + " · Show", () =>
            {
                body.IsVisible = !body.IsVisible;
                header.Text = summary + (body.IsVisible ? " · Hide" : " · Show");
                SemanticProperties.SetDescription(header, header.Text);
            });
            header.FontSize = 13;
            SemanticProperties.SetDescription(header, header.Text);
            return new VerticalStackLayout { Spacing = 4, Children = { header, body } };
        }

        private void OnExchangeCompleted(object sender, ChatExchangeEventArgs e)
        {
            // Settings > Developer mode: what the model was given with the message.
            if (DeveloperMode && _lastComposition != null)
                Conversation?.AddNotice(DeveloperNotice(_lastComposition.Tools.Select(t => t.Name).ToList(), _lastComposition.Instructions));
            // Saved by the history binding (AutoSave): the page and the features learn the chat's id.
            if (_binding != null && _binding.ChatId > 0 && _chatId != _binding.ChatId)
                SetCurrentChat(_binding.ChatId);
            foreach (ChatFeature feature in _features)
            {
                try { feature.OnReplyCompleted(e); }
                catch (Exception) { }
            }
            UpdateMemoryLabel();
            ExchangeCompleted?.Invoke(this, e);
        }

        /// <summary>The status line under the conversation; null hides it.</summary>
        /// <param name="message">The message, or null</param>
        public void SetStatus(string message)
        {
            if (_statusLabel == null)
                return;
            _statusLabel.Text = message ?? string.Empty;
            _statusLabel.IsVisible = !string.IsNullOrEmpty(message);
        }

        // ---------- Building blocks for settings sections (for features too) ----------

        /// <summary>A settings section's heading (the "ChatSectionTitleStyle" style).</summary>
        /// <param name="text">The heading</param>
        public static Label SectionTitle(string text)
        {
            var label = new Label { Text = text };
            ChatResources.ApplyStyle(label, "ChatSectionTitleStyle");
            return label;
        }

        /// <summary>A note under a setting (the "ChatSectionNoteStyle" style).</summary>
        /// <param name="text">The note</param>
        public static Label Note(string text)
        {
            var label = new Label { Text = text };
            ChatResources.ApplyStyle(label, "ChatSectionNoteStyle");
            return label;
        }

        /// <summary>A tappable line, e.g. "3 memories - show" (the "ChatLinkStyle" style).</summary>
        /// <param name="text">The text</param>
        /// <param name="tapped">What it does</param>
        public static Label Link(string text, Action tapped)
        {
            var label = new Label { Text = text };
            ChatResources.ApplyStyle(label, "ChatLinkStyle");
            if (tapped != null)
            {
                TapGestureRecognizer tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) => tapped();
                label.GestureRecognizers.Add(tap);
            }
            return label;
        }

        /// <summary>A line between settings sections (the "ChatDividerStyle" style).</summary>
        public static BoxView Divider()
        {
            var divider = new BoxView();
            ChatResources.ApplyStyle(divider, "ChatDividerStyle");
            return divider;
        }

        /// <summary>A setting's row: its text and a switch, with a note under them.</summary>
        /// <param name="text">The setting</param>
        /// <param name="note">A note under it, or null</param>
        /// <param name="isOn">Whether it is on now</param>
        /// <param name="toggled">Called with the new state</param>
        /// <returns>The row; its switch is the Grid's second child</returns>
        public static View SwitchRow(string text, string note, bool isOn, Action<bool> toggled)
        {
            var toggle = new Switch { IsToggled = isOn, OnColor = ChatResources.FindColor("ChatAccent"), VerticalOptions = LayoutOptions.Center };
            toggle.Toggled += (s, e) => toggled(e.Value);
            var label = new Label { Text = text };
            ChatResources.ApplyStyle(label, "ChatSectionTextStyle");
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(label, 0, 0);
            row.Add(toggle, 1, 0);
            SemanticProperties.SetDescription(toggle, text);
            if (note == null)
                return row;
            return new VerticalStackLayout { Spacing = 4, Children = { row, Note(note) } };
        }

        /// <summary>A list row (a saved chat, a model, a memory): a title, a detail line and, optionally, action links and a delete cross.</summary>
        internal static Border ListRow(string title, string detail, bool selected, Action tapped, Action deleted, string deleteDescription,
            IEnumerable<(string Text, Action Tapped)> actions = null)
        {
            var titleLabel = new Label { Text = title };
            ChatResources.ApplyStyle(titleLabel, "ChatListTitleStyle");
            var details = new VerticalStackLayout { Children = { titleLabel } };
            if (!string.IsNullOrEmpty(detail))
            {
                var detailLabel = new Label { Text = detail };
                ChatResources.ApplyStyle(detailLabel, "ChatListDetailStyle");
                details.Children.Add(detailLabel);
            }
            var row = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
            row.Add(details, 0, 0);
            if (actions != null && actions.Any())
            {
                var links = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
                foreach ((string text, Action action) in actions)
                    links.Children.Add(Link(text, action));
                row.Add(links, 1, 0);
            }
            if (deleted != null)
            {
                var cross = new Label { Text = "✕", FontSize = 16, Padding = new Thickness(8, 4), VerticalOptions = LayoutOptions.Center };
                ChatResources.SetThemeColor(cross, Label.TextColorProperty, "ChatSecondaryText");
                SemanticProperties.SetDescription(cross, deleteDescription);
                TapGestureRecognizer deleteTap = new TapGestureRecognizer();
                deleteTap.Tapped += (s, e) => deleted();
                cross.GestureRecognizers.Add(deleteTap);
                row.Add(cross, 2, 0);
            }
            var cell = new Border { Content = row, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) } };
            ChatResources.ApplyStyle(cell, "ChatListRowStyle");
            if (selected)
                ChatResources.SetThemeColor(cell, BackgroundColorProperty, "ChatSelectedRow");
            if (tapped != null)
            {
                TapGestureRecognizer tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) => tapped();
                details.GestureRecognizers.Add(tap);
            }
            return cell;
        }
    }
}
