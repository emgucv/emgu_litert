//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>What a <see cref="ChatSkillSet"/> composed for a message: the tools and the instructions.</summary>
    public sealed class ChatSkillComposition
    {
        /// <summary>The tools for the message (empty when the model can't use tools).</summary>
        public IList<AITool> Tools { get; set; } = new List<AITool>();

        /// <summary>The instructions for the message, or null.</summary>
        public string Instructions { get; set; }

        /// <summary>The names of the tool groups the message's words added (see <see cref="ToolRouter.Route"/>).</summary>
        public IList<string> GroupsAdded { get; set; } = new List<string>();
    }

    /// <summary>
    /// The skills of a chat agent, composed into each message's tools and instructions: the enabled skills' tools and
    /// instructions, and their tool groups through one <see cref="ToolRouter"/> (a group comes when a message's words need
    /// it, or the model calls its stub, and stays until <see cref="Reset"/>).
    /// <code>
    /// var skills = new ChatSkillSet(() => FunctionInvokingChatClient.CurrentContext?.Options,
    ///     () => FunctionInvokingChatClient.CurrentContext.Terminate = true);
    /// skills.Add(new CoreToolsSkill());
    /// skills.Add(new WebSkill());
    /// ...
    /// ChatSkillComposition composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = text }, ChatSkillSet.DefaultInstructions);
    /// var options = new ChatOptions { Tools = composed.Tools, Instructions = composed.Instructions };
    /// </code>
    /// </summary>
    public sealed class ChatSkillSet
    {
        /// <summary>
        /// Instructions for a general assistant with tools, to give <see cref="ComposeAsync"/> (the MAUI demo's).
        /// </summary>
        public const string DefaultInstructions = "You are a helpful assistant running on the user's device. You can look things up with your tools: use them " +
            "for facts you are not sure about, recent events and anything that depends on today's date or, when you have the " +
            "location tool, on where the user is. Use the calculate tool for any arithmetic, and the convert tools for units and money. When the user gives a link, read it with read_web_page. When the user tells you " +
            "something lasting about themselves, save it with the remember tool. Base your answer on what the tools " +
            "return, mention where it came from, and keep it short.";

        /// <summary>Instructions for a private chat: nothing is saved, nothing saved is read.</summary>
        public const string PrivateInstructions = "This is a private chat. Do not claim to remember or save anything for future chats. " +
            "You cannot access saved memories or past conversations. Use only this conversation and the available tools.";

        private readonly List<ChatSkill> _skills = new List<ChatSkill>();
        private readonly Func<ChatOptions> _currentOptions;
        private readonly Action _endRequest;
        private ToolRouter _router;
        private List<string> _activeGroups;

        /// <summary>
        /// Create a set.
        /// </summary>
        /// <param name="currentOptions">The options of the request a tool is being called in, e.g.
        /// () => FunctionInvokingChatClient.CurrentContext?.Options, so a group's stub can add its tools to the same
        /// request (see <see cref="ToolRouter"/>); or null</param>
        /// <param name="endRequest">Ends the request, for groups with RerunAfterStub (e.g. () =>
        /// FunctionInvokingChatClient.CurrentContext.Terminate = true); or null</param>
        public ChatSkillSet(Func<ChatOptions> currentOptions = null, Action endRequest = null)
        {
            _currentOptions = currentOptions;
            _endRequest = endRequest;
        }

        /// <summary>The skills, in the order they were added.</summary>
        public IReadOnlyList<ChatSkill> Skills => _skills;

        /// <summary>Raised when a stub tool adds its group, with the group's name.</summary>
        public event Action<string> GroupRequested;

        /// <summary>Add a skill (a skill with the same name replaces the one before).</summary>
        /// <param name="skill">The skill</param>
        public void Add(ChatSkill skill)
        {
            if (skill == null)
                throw new ArgumentNullException(nameof(skill));
            _skills.RemoveAll(s => s.Name == skill.Name);
            _skills.Add(skill);
            InvalidateRouter();
        }

        /// <summary>Add skills.</summary>
        /// <param name="skills">The skills</param>
        public void AddRange(IEnumerable<ChatSkill> skills)
        {
            foreach (ChatSkill skill in skills ?? Enumerable.Empty<ChatSkill>())
                Add(skill);
        }

        /// <summary>Remove a skill.</summary>
        /// <param name="skill">The skill</param>
        /// <returns>True if it was in the set</returns>
        public bool Remove(ChatSkill skill)
        {
            bool removed = _skills.Remove(skill);
            if (removed)
                InvalidateRouter();
            return removed;
        }

        /// <summary>The skill with a name, or null.</summary>
        /// <param name="name">The name</param>
        public ChatSkill Find(string name) => _skills.FirstOrDefault(s => s.Name == name);

        /// <summary>The skill of a type, or null.</summary>
        /// <typeparam name="T">The type</typeparam>
        public T Find<T>() where T : ChatSkill => _skills.OfType<T>().FirstOrDefault();

        /// <summary>The router of the skills' tool groups (made again when the skills change; the groups in use are kept).</summary>
        public ToolRouter Router
        {
            get
            {
                if (_router == null)
                {
                    var groups = new List<ToolGroup>();
                    foreach (ChatSkill skill in _skills)
                        foreach (ToolGroup group in skill.ToolGroups())
                            groups.Add(Gate(skill, group));
                    var router = new ToolRouter(groups, _currentOptions, _endRequest);
                    foreach (string name in _activeGroups ?? Enumerable.Empty<string>())
                        router.Activate(name);
                    _activeGroups = null;
                    router.GroupRequested += name => GroupRequested?.Invoke(name);
                    _router = router;
                }
                return _router;
            }
        }

        // A disabled skill's group has no tools, so the router leaves it out (no stub either).
        private static ToolGroup Gate(ChatSkill skill, ToolGroup group) =>
            new ToolGroup(group.Name, group.Summary, group.Keywords,
                () => skill.Enabled ? group.CreateTools() : Array.Empty<AITool>(),
                group.Instructions, group.AlsoMatches, group.RerunAfterStub);

        private void InvalidateRouter()
        {
            if (_router != null)
                _activeGroups = _router.Active.ToList();
            _router = null;
        }

        /// <summary>
        /// The tools and instructions for a message: the enabled skills' (after <see cref="ChatSkill.PrepareAsync"/>), the
        /// groups the message's words need (which stay for the conversation), a stub for each other group, and the
        /// disabled skills' <see cref="ChatSkill.DisabledInstructions"/>. A skill that throws is left out of that message.
        /// Without <see cref="ChatMessageContext.ModelCanUseTools"/>, only the base instructions.
        /// </summary>
        /// <param name="context">The message</param>
        /// <param name="instructions">The base instructions (e.g. <see cref="DefaultInstructions"/>), or null</param>
        public async Task<ChatSkillComposition> ComposeAsync(ChatMessageContext context, string instructions)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var result = new ChatSkillComposition();
            if (!context.ModelCanUseTools)
            {
                result.Instructions = string.IsNullOrEmpty(instructions) ? null : instructions;
                return result;
            }
            var parts = new List<string> { instructions };
            foreach (ChatSkill skill in _skills.ToList())
            {
                if (!skill.Enabled)
                {
                    parts.Add(skill.DisabledInstructions);
                    continue;
                }
                try
                {
                    await skill.PrepareAsync(context).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
            ToolRouter router = Router;
            result.GroupsAdded = router.Route(context.Prompt);
            foreach (ChatSkill skill in _skills.ToList())
            {
                if (!skill.Enabled)
                    continue;
                try
                {
                    foreach (AITool tool in skill.Tools(context))
                        result.Tools.Add(tool);
                    parts.Add(await skill.InstructionsAsync(context).ConfigureAwait(false));
                }
                catch (Exception)
                {
                }
            }
            foreach (AITool tool in router.Tools())
                result.Tools.Add(tool);
            parts.Add(router.Instructions());
            string joined = string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p)));
            result.Instructions = joined.Length > 0 ? joined : null;
            return result;
        }

        /// <summary>Start a new conversation: no group is in use, and the skills are told.</summary>
        public void Reset()
        {
            _router?.Reset();
            _activeGroups = null;
            foreach (ChatSkill skill in _skills)
                skill.OnNewConversation();
        }

        /// <summary>Tell the skills the chat became private, or stopped being private.</summary>
        /// <param name="isPrivate">True for private</param>
        public void SetPrivate(bool isPrivate)
        {
            foreach (ChatSkill skill in _skills)
                skill.OnPrivateModeChanged(isPrivate);
        }

        /// <summary>Use a group from now on (see <see cref="ToolRouter.Activate"/>).</summary>
        /// <param name="name">The group's name</param>
        public bool Activate(string name) => Router.Activate(name);

        /// <summary>Whether a stub ended the last request so the message should be sent again (see <see cref="ToolRouter.TakeRerun"/>).</summary>
        public bool TakeRerun() => _router != null && _router.TakeRerun();

        /// <summary>
        /// What the enabled skills' tools do, in words, for a planner: their <see cref="ChatSkill.ToolSummary"/> and their
        /// groups' names.
        /// </summary>
        public string ToolSummary
        {
            get
            {
                var parts = new List<string>();
                foreach (ChatSkill skill in _skills)
                {
                    if (!skill.Enabled)
                        continue;
                    if (!string.IsNullOrEmpty(skill.ToolSummary))
                        parts.Add(skill.ToolSummary);
                    foreach (ToolGroup group in skill.ToolGroups())
                        parts.Add(group.Name.Replace('_', ' '));
                }
                return string.Join(", ", parts);
            }
        }
    }
}
