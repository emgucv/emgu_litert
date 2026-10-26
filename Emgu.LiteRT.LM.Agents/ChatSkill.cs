//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// What a <see cref="ChatSkill"/> is told about the message being sent, to decide what it offers for it.
    /// </summary>
    public sealed class ChatMessageContext
    {
        /// <summary>The user's message.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>The images and audio sent with it, or null.</summary>
        public IReadOnlyList<DataContent> Attachments { get; set; }

        /// <summary>True in a private chat: nothing is saved, and nothing saved before is read.</summary>
        public bool IsPrivate { get; set; }

        /// <summary>True when the user lets the agent remember things about them (and the chat isn't private).</summary>
        public bool MemoryOn { get; set; }

        /// <summary>True when the model can call tools; without that, skills are not asked for any.</summary>
        public bool ModelCanUseTools { get; set; } = true;

        /// <summary>True when the model takes images.</summary>
        public bool ModelAcceptsImages { get; set; }

        /// <summary>
        /// True in a hands-free chat: the reply is spoken and nothing on the screen is looked at, so a skill whose tools
        /// show cards or need a tap (confirmations, counting on the camera) leaves them out.
        /// </summary>
        public bool HandsFree { get; set; }
    }

    /// <summary>
    /// A skill of a chat agent: the tools it adds for a message, the instructions that go with them, and what it does when a
    /// conversation starts over. Skills are pure - no UI, any <see cref="IChatClient"/> - and a <see cref="ChatSkillSet"/>
    /// composes them into each message's <see cref="ChatOptions"/>. Every member has a default, so a skill overrides only
    /// what it uses: the simplest is a <see cref="Name"/> and <see cref="Tools"/>.
    /// <code>
    /// public sealed class DiceSkill : ChatSkill
    /// {
    ///     public override string Name => "dice";
    ///     public override IEnumerable&lt;AITool&gt; Tools(ChatMessageContext context) =>
    ///         new[] { AIFunctionFactory.Create((int sides) => new Random().Next(1, sides + 1), "roll_die", "Rolls a die with the given number of sides.") };
    /// }
    /// </code>
    /// Tools that are only sometimes needed go in <see cref="ToolGroups"/>: the set's <see cref="ToolRouter"/> adds a group
    /// when a message's words call for it (or the model asks for it through a stub), which keeps every message small.
    /// </summary>
    public abstract class ChatSkill
    {
        /// <summary>A short name, e.g. "web" (a settings key, a group name).</summary>
        public abstract string Name { get; }

        /// <summary>A title for people, e.g. "Web search"; the <see cref="Name"/> by default.</summary>
        public virtual string Title => Name;

        /// <summary>
        /// Whether the skill is in use: off, it adds no tools or instructions (its <see cref="DisabledInstructions"/> go instead).
        /// </summary>
        public virtual bool Enabled { get; set; } = true;

        /// <summary>
        /// Called before a message's tools are put together (before the groups are routed): load what the skill needs for
        /// it, e.g. the user's goals.
        /// </summary>
        /// <param name="context">The message</param>
        public virtual Task PrepareAsync(ChatMessageContext context) => Task.CompletedTask;

        /// <summary>The tools given with every message (while the skill is enabled); none by default.</summary>
        /// <param name="context">The message</param>
        public virtual IEnumerable<AITool> Tools(ChatMessageContext context) => Array.Empty<AITool>();

        /// <summary>
        /// The tool groups the skill offers on demand (see <see cref="ToolRouter"/>); none by default. Asked once, when the
        /// skill joins a <see cref="ChatSkillSet"/>; a disabled skill's groups are left out.
        /// </summary>
        public virtual IEnumerable<ToolGroup> ToolGroups() => Array.Empty<ToolGroup>();

        /// <summary>The instructions for the agent while the skill is enabled, or null.</summary>
        /// <param name="context">The message</param>
        public virtual Task<string> InstructionsAsync(ChatMessageContext context) => Task.FromResult<string>(null);

        /// <summary>
        /// The instructions for the agent while the skill is disabled (e.g. how to tell the user to turn it on), or null.
        /// </summary>
        public virtual string DisabledInstructions => null;

        /// <summary>What the tools do, in a few words for a planner ("weather, Wikipedia"), or null.</summary>
        public virtual string ToolSummary => null;

        /// <summary>Called when a conversation starts over (a new chat).</summary>
        public virtual void OnNewConversation()
        {
        }

        /// <summary>Called when the chat becomes private, or stops being private.</summary>
        /// <param name="isPrivate">True for private</param>
        public virtual void OnPrivateModeChanged(bool isPrivate)
        {
        }
    }
}
