//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents.Skills
{
    /// <summary>
    /// The user's goals ("run three times a week", "read 20 pages a day"): the add_goal / log_progress / get_goals /
    /// remove_goal tools over an <see cref="IAgentGoalStore"/> (<see cref="AgentTools.Goals"/>), as a tool group that comes
    /// when a message is about goals, progress or habits, or mentions one of the user's goals; its instructions list the goals
    /// with this period's progress (<see cref="AgentTools.GoalInstructionsAsync"/>), so "I ran today" is logged without
    /// asking. Only while <see cref="ChatMessageContext.MemoryOn"/> and not in a private chat.
    /// </summary>
    public sealed class GoalsSkill : ChatSkill
    {
        /// <summary>The tool group's name.</summary>
        public const string GroupName = "goals";

        private static readonly string[] Keywords =
        {
            "goal", "habit", "target", "streak", "progress", "i want to", "i'd like to", "every day", "a day$", "a week$",
            "per week$", "per day$", "each week$", "times a week", "how am i doing", "on track", "keep track", "track my",
            "remind me"
        };

        private readonly int _maxLength;
        private readonly Func<DateTime> _now;
        private IList<AgentGoal> _goals = new List<AgentGoal>();
        private string _instructions;
        private bool _on;

        /// <summary>
        /// Create the skill.
        /// </summary>
        /// <param name="store">Where the goals are kept (e.g. Emgu.LiteRT.LM.Chat.Sqlite's SqliteAgentGoalStore)</param>
        /// <param name="maxLength">The most characters of goals to give with a message</param>
        /// <param name="now">The current time (for tests), or null for DateTime.Now</param>
        public GoalsSkill(IAgentGoalStore store, int maxLength = 800, Func<DateTime> now = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            _maxLength = maxLength;
            _now = now ?? (() => DateTime.Now);
        }

        /// <summary>Where the goals are kept.</summary>
        public IAgentGoalStore Store { get; }

        /// <summary>
        /// More tools for the group (e.g. counting exercises), and the instructions that go with them; or null. They come
        /// with the goals' tools rather than as a group of their own, which would add a stub to every message.
        /// </summary>
        public Func<IEnumerable<AITool>> ExtraTools { get; set; }

        /// <summary>
        /// Keeps a goal's reminder time (null: off) and schedules the notification, returning when it is next due; with it,
        /// the group has set_goal_reminder (<see cref="AgentTools.GoalReminder"/>). Null for no reminders.
        /// </summary>
        public Func<AgentGoal, TimeSpan?, Task<DateTime?>> SetReminder { get; set; }

        /// <summary>The instructions for <see cref="ExtraTools"/>, or null.</summary>
        public Func<string> ExtraInstructions { get; set; }

        /// <summary>More words that bring the group (e.g. "jumping jack"), or null.</summary>
        public IEnumerable<string> ExtraKeywords { get; set; }

        /// <summary>
        /// What the extra tools do, for the group's summary the planner reads ("count jumping jacks, squats or high knees
        /// with the camera"); null adds "and more" when there are extra tools.
        /// </summary>
        public string ExtraSummary { get; set; }

        /// <inheritdoc/>
        public override string Name => "goals";

        /// <inheritdoc/>
        public override string Title => "Your goals";

        /// <inheritdoc/>
        public override async Task PrepareAsync(ChatMessageContext context)
        {
            _on = context.MemoryOn && !context.IsPrivate;
            _goals = new List<AgentGoal>();
            _instructions = null;
            if (!_on)
                return;
            _goals = await Store.GetGoalsAsync().ConfigureAwait(false);
            _instructions = await AgentTools.GoalInstructionsAsync(Store, _now(), _maxLength).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override IEnumerable<ToolGroup> ToolGroups()
        {
            yield return new ToolGroup(GroupName,
                "save the user's goals (habits, targets), log or check their progress" + (ExtraTools != null ? ", and " + (ExtraSummary ?? "more") : ""),
                Keywords.Concat(ExtraKeywords ?? Enumerable.Empty<string>()),
                () =>
                {
                    if (!_on)
                        return Array.Empty<AITool>();
                    IEnumerable<AITool> tools = AgentTools.Goals(Store, _now).Cast<AITool>();
                    if (SetReminder != null)
                        tools = tools.Concat(new AITool[] { AgentTools.GoalReminder(Store, SetReminder) });
                    return ExtraTools != null ? tools.Concat(ExtraTools()) : tools;
                },
                () =>
                {
                    string extra = ExtraInstructions?.Invoke();
                    return string.Join("\n\n", new[] { _instructions, extra }.Where(t => !string.IsNullOrEmpty(t)));
                },
                text => AgentTools.MentionsGoal(_goals, text));
        }

        /// <inheritdoc/>
        public override string ToolSummary => null;
    }
}
