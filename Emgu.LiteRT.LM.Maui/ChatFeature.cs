//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>A chip above a <see cref="ChatPage"/>'s message box, from a feature: "Web search", "Plan"...</summary>
    public sealed class ChatChip
    {
        /// <summary>Create a chip.</summary>
        /// <param name="text">Its text</param>
        /// <param name="isOn">Whether it is on now</param>
        /// <param name="toggled">Called with the new state when it is tapped</param>
        public ChatChip(string text, Func<bool> isOn, Action<bool> toggled)
        {
            Text = text;
            IsOn = isOn ?? (() => false);
            Toggled = toggled;
        }

        /// <summary>Its text.</summary>
        public string Text { get; }

        /// <summary>Whether it is on now.</summary>
        public Func<bool> IsOn { get; }

        /// <summary>Called with the new state when it is tapped.</summary>
        public Action<bool> Toggled { get; }

        /// <summary>Whether it is shown; null for always.</summary>
        public Func<bool> IsVisible { get; set; }
    }

    /// <summary>
    /// A feature of a <see cref="ChatPage"/>: a <see cref="ChatSkill"/> (tools, tool groups and instructions for the
    /// agent) with what the page shows for it - a settings section, a chip above the message box, cards on replies - and
    /// the page's life: appearing, a new chat, another model, a reply done. It reaches the page through
    /// <see cref="Host"/> (<see cref="IChatPageHost"/>). Every member has a default: a skill with no UI overrides nothing
    /// here, and a feature with a settings switch overrides <see cref="BuildSettingsSection"/>.
    /// <code>
    /// public sealed class DiceFeature : ChatFeature
    /// {
    ///     public override string Name => "dice";
    ///     public override IEnumerable&lt;AITool&gt; Tools(ChatMessageContext context) => new[] { AIFunctionFactory.Create(Roll, "roll_die", "Rolls a die.") };
    ///     public override View BuildSettingsSection() => ChatPage.SwitchRow("Dice", "Lets the assistant roll dice.", Enabled, on => Enabled = on);
    /// }
    /// </code>
    /// The page keeps <see cref="ChatSkill.Enabled"/> in its settings (key "feature_{Name}") when
    /// <see cref="PersistEnabled"/> is on, so a feature's switch survives a restart.
    /// </summary>
    public abstract class ChatFeature : ChatSkill
    {
        /// <summary>The page's services for the feature, once attached; null before.</summary>
        protected IChatPageHost Host { get; private set; }

        /// <summary>Whether the page saves <see cref="ChatSkill.Enabled"/> in its settings (key "feature_{Name}"); true by default.</summary>
        public virtual bool PersistEnabled => true;

        /// <summary>Called when the feature joins a page (before the page is shown).</summary>
        /// <param name="host">The page's services</param>
        public virtual void OnAttached(IChatPageHost host)
        {
            Host = host;
        }

        /// <summary>Called when the feature is removed from the page.</summary>
        public virtual void OnDetached()
        {
            Host = null;
        }

        /// <summary>
        /// Called once when the page is built, to register services other features may use (e.g. a camera's visual agent
        /// registry) in <see cref="IChatPageHost.Services"/>, before any feature's UI is built.
        /// </summary>
        /// <param name="services">The registry</param>
        public virtual void RegisterServices(ChatFeatureServices services)
        {
        }

        /// <summary>Called when the page appears.</summary>
        public virtual void OnAppearing()
        {
        }

        /// <summary>Called when the page disappears.</summary>
        public virtual void OnDisappearing()
        {
        }

        /// <summary>
        /// What the feature adds to a message about to be sent: text for the model before the message (e.g. a document's
        /// excerpts), attachments (a picture of it), and a step line shown above the reply; or null for nothing. The page
        /// joins the features' additions (<see cref="ChatPage.PrepareRequestAsync"/>).
        /// </summary>
        /// <param name="request">The message as the user sent it</param>
        /// <param name="cancellationToken">Stops the send</param>
        public virtual Task<ChatMessageAddition> PrepareMessageAsync(ChatSendRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<ChatMessageAddition>(null);

        /// <summary>
        /// Called just before the request goes out, after the conversation was made to fit the context window: a feature
        /// whose addition no longer fits what the model holds (compacting dropped what it gave before) returns a new one
        /// and the page joins the context again; null keeps the addition as it is. Only called for a feature that added
        /// something to this message.
        /// </summary>
        /// <param name="message">The message being sent</param>
        /// <param name="cancellationToken">Stops the send</param>
        public virtual Task<ChatMessageAddition> RefreshMessageAsync(ChatMessageItem message, CancellationToken cancellationToken) =>
            Task.FromResult<ChatMessageAddition>(null);

        /// <summary>Called when the conversation became a saved chat, or another one (its id is <see cref="IChatPageHost.CurrentChatId"/>).</summary>
        /// <param name="chatId">The chat</param>
        public virtual void OnChatSaved(int chatId)
        {
        }

        /// <summary>Called when the app comes to the front (on desktops also when its window is activated).</summary>
        public virtual void OnAppResumed()
        {
        }

        /// <summary>Called when the app leaves the front (the window is stopped).</summary>
        public virtual void OnAppStopped()
        {
        }

        /// <summary>Called when another model was chosen (its conversation is new).</summary>
        /// <param name="model">The model</param>
        public virtual void OnModelChanged(ChatModelOption model)
        {
        }

        /// <summary>Called when an exchange is complete (the reply done or interrupted), on the UI thread.</summary>
        /// <param name="e">The message and the reply</param>
        public virtual void OnReplyCompleted(ChatExchangeEventArgs e)
        {
        }

        /// <summary>Called when a saved chat was opened (its messages are in the conversation).</summary>
        /// <param name="chatId">The chat</param>
        public virtual void OnChatOpened(int chatId)
        {
        }

        /// <summary>
        /// The card for a file this feature kept with a reply (<see cref="IChatPageHost.AddCardToReply(View, ChatSavedFile)"/>),
        /// read back with a saved chat: the page shows it above the reply again. Null for a file that isn't this
        /// feature's (the page asks each feature).
        /// </summary>
        /// <param name="file">The file, with its kind</param>
        public virtual View RestoreSavedFile(ChatSavedFile file) => null;

        /// <summary>
        /// A client to run this message through instead of the model's own (e.g. task mode's, which plans the message
        /// and runs the steps one by one), or null. Asked once per message, after <see cref="PrepareMessageAsync"/>; the
        /// first feature's client wins. The client is given the page's client (<see cref="IChatPageHost.ChatClient"/>) to
        /// talk to the model.
        /// </summary>
        /// <param name="request">The message, as prepared</param>
        public virtual IChatClient CreateMessageClient(ChatSendRequest request) => null;

        /// <summary>
        /// The step line for a tool call of this feature's ("Searching Wikipedia for ...", shown while it runs), or null
        /// for a call that isn't its; the page asks each feature, then Emgu.LiteRT.LM.Agents' <c>AgentTools.Describe</c>.
        /// </summary>
        /// <param name="call">The tool call</param>
        public virtual string DescribeToolCall(FunctionCallContent call) => null;

        /// <summary>
        /// Called when a tool call of a reply has its result (<see cref="ChatToolStep.Result"/>), on the UI thread: a
        /// feature may reword the step's text with the outcome (e.g. "Already remembered").
        /// </summary>
        /// <param name="step">The step</param>
        public virtual void OnToolStepCompleted(ChatToolStep step)
        {
        }

        /// <summary>The feature's section of the settings (a switch, a list...), or null for none.</summary>
        public virtual View BuildSettingsSection() => null;

        /// <summary>The feature's chip above the message box, or null for none.</summary>
        public virtual ChatChip BuildComposerChip() => null;

        /// <summary>
        /// The feature's chips above the message box: by default its <see cref="BuildComposerChip"/>, if any. A feature with
        /// more than one (task mode's Plan and Research) overrides this. Their <see cref="ChatChip.IsVisible"/> and on state
        /// are read again whenever the page refreshes its chips (<see cref="IChatPageHost.RefreshChips"/>).
        /// </summary>
        public virtual IEnumerable<ChatChip> BuildComposerChips()
        {
            ChatChip chip = BuildComposerChip();
            return chip != null ? new[] { chip } : Array.Empty<ChatChip>();
        }

        /// <summary>
        /// A view of the feature's own in the row above the message box, beside the chips (e.g. a document attached to
        /// the chat, with its progress), or null for none; the feature shows and hides it.
        /// </summary>
        public virtual View BuildComposerView() => null;
    }

    /// <summary>What a feature adds to a message about to be sent (<see cref="ChatFeature.PrepareMessageAsync"/>).</summary>
    public sealed class ChatMessageAddition
    {
        /// <summary>Text given to the model before the message, or null.</summary>
        public string Context { get; set; }

        /// <summary>Attachments to send with the message, or null.</summary>
        public IEnumerable<DataContent> Attachments { get; set; }

        /// <summary>A step line shown above the reply ("Looked in report.pdf: pages 2 and 5"), or null.</summary>
        public string StepLine { get; set; }
    }

    /// <summary>
    /// A <see cref="ChatFeature"/> whose tools and instructions are a pure <see cref="ChatSkill"/>'s (e.g. the built-in
    /// skills of Emgu.LiteRT.LM.Agents), with the page's UI added here: the skill's members are forwarded, and
    /// <see cref="ChatSkill.Enabled"/> is the skill's.
    /// </summary>
    /// <typeparam name="TSkill">The skill</typeparam>
    public abstract class SkillFeature<TSkill> : ChatFeature where TSkill : ChatSkill
    {
        /// <summary>Create the feature over a skill.</summary>
        /// <param name="skill">The skill</param>
        protected SkillFeature(TSkill skill)
        {
            Skill = skill ?? throw new ArgumentNullException(nameof(skill));
        }

        /// <summary>The skill.</summary>
        public TSkill Skill { get; }

        /// <inheritdoc/>
        public override string Name => Skill.Name;

        /// <inheritdoc/>
        public override string Title => Skill.Title;

        /// <inheritdoc/>
        public override bool Enabled
        {
            get => Skill.Enabled;
            set => Skill.Enabled = value;
        }

        /// <inheritdoc/>
        public override Task PrepareAsync(ChatMessageContext context) => Skill.PrepareAsync(context);

        /// <inheritdoc/>
        public override IEnumerable<AITool> Tools(ChatMessageContext context) => Skill.Tools(context);

        /// <inheritdoc/>
        public override IEnumerable<ToolGroup> ToolGroups() => Skill.ToolGroups();

        /// <inheritdoc/>
        public override Task<string> InstructionsAsync(ChatMessageContext context) => Skill.InstructionsAsync(context);

        /// <inheritdoc/>
        public override string DisabledInstructions => Skill.DisabledInstructions;

        /// <inheritdoc/>
        public override string ToolSummary => Skill.ToolSummary;

        /// <inheritdoc/>
        public override void OnNewConversation() => Skill.OnNewConversation();

        /// <inheritdoc/>
        public override void OnPrivateModeChanged(bool isPrivate) => Skill.OnPrivateModeChanged(isPrivate);
    }
}
