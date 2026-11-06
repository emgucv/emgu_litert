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
    /// Scheduled tasks ("every morning at 7, tell me the weather in Toronto"): the schedule_task / list_tasks / remove_task
    /// tools over an <see cref="IAgentTaskStore"/> (<see cref="AgentTools.ScheduledTasks"/>), as a tool group that comes when
    /// a message asks for something regularly or mentions its tasks. The app runs the tasks and arms their next runs
    /// (<see cref="Changed"/>). Not in a private chat.
    /// </summary>
    public sealed class ScheduledTasksSkill : ChatSkill
    {
        /// <summary>The tool group's name ("task" is task mode's).</summary>
        public const string GroupName = "schedule";

        private static readonly string[] Keywords =
        {
            // Not a bare "every day": a goal's words ("read every day") would bring the group too.
            "every morning", "every evening", "every night", "every day at", "every hour", "every week", "each morning", "each day at",
            "daily", "every monday", "every weekday", "check every", "check regularly", "keep checking", "keep an eye on",
            "tell me when", "let me know when", "notify me", "schedule", "scheduled task", "my tasks"
        };

        private readonly Func<DateTime> _now;
        private bool _on;

        /// <summary>
        /// Create the skill.
        /// </summary>
        /// <param name="store">Where the tasks are kept (e.g. Emgu.LiteRT.LM.Chat.Sqlite's SqliteAgentTaskStore)</param>
        /// <param name="now">The current time (for tests), or null for DateTime.Now</param>
        public ScheduledTasksSkill(IAgentTaskStore store, Func<DateTime> now = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            _now = now ?? (() => DateTime.Now);
        }

        /// <summary>Where the tasks are kept.</summary>
        public IAgentTaskStore Store { get; }

        /// <summary>
        /// Called after a task is added or removed (the store no longer has a removed one): the app schedules or cancels its
        /// run. Null schedules nothing.
        /// </summary>
        public Func<ScheduledTask, Task> Changed { get; set; }

        /// <inheritdoc/>
        public override string Name => "scheduled_tasks";

        /// <inheritdoc/>
        public override string Title => "Scheduled tasks";

        /// <inheritdoc/>
        public override Task PrepareAsync(ChatMessageContext context)
        {
            _on = !context.IsPrivate;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override IEnumerable<ToolGroup> ToolGroups()
        {
            yield return new ToolGroup(GroupName,
                "run a task on its own every day or every few hours and tell the user with a notification",
                Keywords,
                () => _on ? AgentTools.ScheduledTasks(Store, Changed, _now).Cast<AITool>() : Array.Empty<AITool>());
        }
    }
}
