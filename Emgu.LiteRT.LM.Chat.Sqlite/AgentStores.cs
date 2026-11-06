//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using SQLite;

namespace Emgu.LiteRT.LM.Chat.Sqlite
{
    /// <summary>
    /// A fact the agent remembers about the user, a row of <see cref="SqliteAgentMemoryStore"/>'s table.
    /// </summary>
    [Table("Memory")]
    public class AgentMemoryRecord
    {
        /// <summary>The row's id</summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>The fact</summary>
        public string Text { get; set; }

        /// <summary>When it was saved (UTC)</summary>
        public DateTime Created { get; set; }
    }

    /// <summary>
    /// A goal of the user's ("Run three times a week"), a row of <see cref="SqliteAgentGoalStore"/>'s table.
    /// </summary>
    [Table("Goal")]
    public class AgentGoalRecord
    {
        /// <summary>The row's id</summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>The goal in the user's words</summary>
        public string Text { get; set; }

        /// <summary>How much per period, or null if there is nothing to count</summary>
        public double? Target { get; set; }

        /// <summary>The unit of the target and the progress</summary>
        public string Unit { get; set; }

        /// <summary>A <see cref="GoalPeriod"/></summary>
        public int Period { get; set; }

        /// <summary>When the goal was set (local time)</summary>
        public DateTime Created { get; set; }
    }

    /// <summary>
    /// Progress logged toward a goal: an amount in the goal's unit at a time (local), a row of <see cref="SqliteAgentGoalStore"/>'s table.
    /// </summary>
    [Table("GoalProgress")]
    public class AgentGoalProgressRecord
    {
        /// <summary>The row's id</summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>The goal's row id</summary>
        [Indexed]
        public int GoalId { get; set; }

        /// <summary>How much, in the goal's unit</summary>
        public double Amount { get; set; }

        /// <summary>When it was done (local time)</summary>
        public DateTime When { get; set; }
    }

    /// <summary>
    /// A task the agent runs on a schedule ("every morning, the weather"), a row of <see cref="SqliteAgentTaskStore"/>'s table.
    /// </summary>
    [Table("ScheduledTask")]
    public class AgentTaskRecord
    {
        /// <summary>The row's id</summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>What to do, in the user's words</summary>
        public string Description { get; set; }

        /// <summary>When it runs (<see cref="TaskSchedule.Serialize"/>)</summary>
        public string Schedule { get; set; }

        /// <summary>When to tell the user, or null for every time</summary>
        public string NotifyWhen { get; set; }

        /// <summary>When it was made (local time)</summary>
        public DateTime Created { get; set; }

        /// <summary>Whether it is paused</summary>
        public bool Paused { get; set; }

        /// <summary>When it last ran (local time), or null</summary>
        public DateTime? LastRun { get; set; }

        /// <summary>What happened the last time</summary>
        public string LastResult { get; set; }

        /// <summary>The saved chat of its last run, or null</summary>
        public int? LastChatId { get; set; }
    }

    /// <summary>
    /// The agent's memories (Emgu.LiteRT.LM.Agents' <c>AgentTools.Memory</c>, remember and forget) in a <see cref="ChatHistoryStore"/>'s
    /// database, table <c>Memory</c>, so they last across chats. While the chats are private (<see cref="ChatHistoryStore.IsPrivate"/>),
    /// no memory is read or saved, and the database isn't opened.
    /// <code>
    /// var chats = new ChatHistoryStore(folder);
    /// var memories = new SqliteAgentMemoryStore(chats);
    /// tools.AddRange(AgentTools.Memory(memories));
    /// </code>
    /// </summary>
    public sealed class SqliteAgentMemoryStore : IAgentMemoryStore
    {
        private readonly ChatHistoryStore _chats;
        private SQLiteAsyncConnection _ready;

        /// <summary>
        /// Keep the memories in a chat store's database.
        /// </summary>
        /// <param name="chats">The chat store</param>
        public SqliteAgentMemoryStore(ChatHistoryStore chats)
        {
            _chats = chats ?? throw new ArgumentNullException(nameof(chats));
        }

        // The database, with the table created the first time (on each connection the store opens).
        private async Task<SQLiteAsyncConnection> DbAsync()
        {
            SQLiteAsyncConnection db = await _chats.GetConnectionAsync().ConfigureAwait(false);
            if (!ReferenceEquals(db, _ready))
            {
                await db.CreateTableAsync<AgentMemoryRecord>().ConfigureAwait(false);
                _ready = db;
            }
            return db;
        }

        /// <summary>The memories as rows, oldest first (none while private).</summary>
        public async Task<List<AgentMemoryRecord>> GetRecordsAsync()
        {
            if (_chats.IsPrivate) return new List<AgentMemoryRecord>();
            return await (await DbAsync().ConfigureAwait(false)).Table<AgentMemoryRecord>().OrderBy(m => m.Id).ToListAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IList<AgentMemory>> GetMemoriesAsync()
        {
            return (await GetRecordsAsync().ConfigureAwait(false))
                .Select(m => new AgentMemory(m.Id.ToString(CultureInfo.InvariantCulture), m.Text)).ToList();
        }

        /// <summary>
        /// Remember something.
        /// </summary>
        /// <param name="text">The fact</param>
        /// <returns>False if it is empty, the same text (ignoring case) is already remembered, or the chats are private</returns>
        public async Task<bool> AddMemoryAsync(string text)
        {
            if (_chats.IsPrivate) return false;
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
                return false;
            SQLiteAsyncConnection db = await DbAsync().ConfigureAwait(false);
            foreach (AgentMemoryRecord existing in await db.Table<AgentMemoryRecord>().ToListAsync().ConfigureAwait(false))
                if (string.Equals(existing.Text, text, StringComparison.OrdinalIgnoreCase))
                    return false;
            await db.InsertAsync(new AgentMemoryRecord { Text = text, Created = DateTime.UtcNow }).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Forget a memory; nothing happens while private, or if the id isn't a memory's.
        /// </summary>
        /// <param name="id">Its id (<see cref="AgentMemory.Id"/>)</param>
        public async Task DeleteMemoryAsync(string id)
        {
            if (_chats.IsPrivate || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int row)) return;
            await (await DbAsync().ConfigureAwait(false)).DeleteAsync<AgentMemoryRecord>(row).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The user's goals and their progress (Emgu.LiteRT.LM.Agents' <c>AgentTools.Goals</c>) in a <see cref="ChatHistoryStore"/>'s
    /// database, tables <c>Goal</c> and <c>GoalProgress</c>. While the chats are private (<see cref="ChatHistoryStore.IsPrivate"/>),
    /// nothing is read, adding a goal or progress throws, the other changes do nothing, and the database isn't opened.
    /// </summary>
    public sealed class SqliteAgentGoalStore : IAgentGoalStore
    {
        private readonly ChatHistoryStore _chats;
        private SQLiteAsyncConnection _ready;

        /// <summary>
        /// Keep the goals in a chat store's database.
        /// </summary>
        /// <param name="chats">The chat store</param>
        public SqliteAgentGoalStore(ChatHistoryStore chats)
        {
            _chats = chats ?? throw new ArgumentNullException(nameof(chats));
        }

        private async Task<SQLiteAsyncConnection> DbAsync()
        {
            SQLiteAsyncConnection db = await _chats.GetConnectionAsync().ConfigureAwait(false);
            if (!ReferenceEquals(db, _ready))
            {
                await db.CreateTableAsync<AgentGoalRecord>().ConfigureAwait(false);
                await db.CreateTableAsync<AgentGoalProgressRecord>().ConfigureAwait(false);
                _ready = db;
            }
            return db;
        }

        private static AgentGoal ToGoal(AgentGoalRecord r) =>
            new AgentGoal(r.Id.ToString(CultureInfo.InvariantCulture), r.Text, r.Target, r.Unit, (GoalPeriod)r.Period, r.Created);

        private static int GoalId(string id) => int.Parse(id, CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public async Task<IList<AgentGoal>> GetGoalsAsync()
        {
            if (_chats.IsPrivate) return new List<AgentGoal>();
            return (await (await DbAsync().ConfigureAwait(false)).Table<AgentGoalRecord>().OrderBy(g => g.Id).ToListAsync().ConfigureAwait(false))
                .Select(ToGoal).ToList();
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The chats are private</exception>
        public async Task<string> AddGoalAsync(AgentGoal goal)
        {
            if (_chats.IsPrivate) throw new InvalidOperationException("Goals aren't saved in a private chat.");
            AgentGoalRecord record = new AgentGoalRecord { Text = goal.Text, Target = goal.Target, Unit = goal.Unit, Period = (int)goal.Period, Created = goal.Created };
            await (await DbAsync().ConfigureAwait(false)).InsertAsync(record).ConfigureAwait(false);
            return record.Id.ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public async Task UpdateGoalAsync(AgentGoal goal)
        {
            if (_chats.IsPrivate) return;
            await (await DbAsync().ConfigureAwait(false)).UpdateAsync(new AgentGoalRecord
            {
                Id = GoalId(goal.Id), Text = goal.Text, Target = goal.Target, Unit = goal.Unit, Period = (int)goal.Period, Created = goal.Created
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteGoalAsync(string id)
        {
            if (_chats.IsPrivate) return;
            int goalId = GoalId(id);
            await (await DbAsync().ConfigureAwait(false)).RunInTransactionAsync(connection =>
            {
                connection.Execute("DELETE FROM GoalProgress WHERE GoalId = ?", goalId);
                connection.Delete<AgentGoalRecord>(goalId);
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IList<GoalProgress>> GetProgressAsync(string goalId, DateTime since)
        {
            if (_chats.IsPrivate) return new List<GoalProgress>();
            int id = GoalId(goalId);
            List<AgentGoalProgressRecord> records = await (await DbAsync().ConfigureAwait(false)).Table<AgentGoalProgressRecord>()
                .Where(p => p.GoalId == id && p.When >= since).OrderBy(p => p.When).ToListAsync().ConfigureAwait(false);
            return records.Select(p => new GoalProgress(goalId, p.Amount, p.When)).ToList();
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The chats are private</exception>
        public async Task AddProgressAsync(GoalProgress progress)
        {
            if (_chats.IsPrivate) throw new InvalidOperationException("Progress isn't saved in a private chat.");
            await (await DbAsync().ConfigureAwait(false)).InsertAsync(new AgentGoalProgressRecord
            {
                GoalId = GoalId(progress.GoalId), Amount = progress.Amount, When = progress.When
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Undo the latest progress logged toward a goal.
        /// </summary>
        /// <param name="goalId">The goal's id</param>
        /// <returns>False if it has none, or the chats are private</returns>
        public async Task<bool> DeleteLastProgressAsync(string goalId)
        {
            if (_chats.IsPrivate) return false;
            SQLiteAsyncConnection db = await DbAsync().ConfigureAwait(false);
            int id = GoalId(goalId);
            AgentGoalProgressRecord last = await db.Table<AgentGoalProgressRecord>().Where(p => p.GoalId == id)
                .OrderByDescending(p => p.When).FirstOrDefaultAsync().ConfigureAwait(false);
            if (last == null)
                return false;
            await db.DeleteAsync(last).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// The scheduled tasks (Emgu.LiteRT.LM.Agents' <c>AgentTools.ScheduledTasks</c>) in a <see cref="ChatHistoryStore"/>'s database,
    /// table <c>ScheduledTask</c>. While the chats are private (<see cref="ChatHistoryStore.IsPrivate"/>), nothing is read,
    /// adding a task throws and the other changes do nothing - except saving a run (<see cref="UpdateTaskAsync"/>), which a
    /// task's run does on its own and which isn't the private chat's.
    /// </summary>
    public sealed class SqliteAgentTaskStore : IAgentTaskStore
    {
        private readonly ChatHistoryStore _chats;
        private SQLiteAsyncConnection _ready;

        /// <summary>
        /// Keep the tasks in a chat store's database.
        /// </summary>
        /// <param name="chats">The chat store</param>
        public SqliteAgentTaskStore(ChatHistoryStore chats)
        {
            _chats = chats ?? throw new ArgumentNullException(nameof(chats));
        }

        private async Task<SQLiteAsyncConnection> DbAsync()
        {
            SQLiteAsyncConnection db = await _chats.GetConnectionAsync().ConfigureAwait(false);
            if (!ReferenceEquals(db, _ready))
            {
                await db.CreateTableAsync<AgentTaskRecord>().ConfigureAwait(false);
                _ready = db;
            }
            return db;
        }

        // A row whose schedule can't be read (written by a newer version) is left out.
        private static ScheduledTask ToTask(AgentTaskRecord r)
        {
            TaskSchedule schedule = TaskSchedule.Parse(r.Schedule);
            if (schedule == null)
                return null;
            return new ScheduledTask(r.Id.ToString(CultureInfo.InvariantCulture), r.Description, schedule, r.NotifyWhen, r.Created)
            {
                Paused = r.Paused, LastRun = r.LastRun, LastResult = r.LastResult, LastChatId = r.LastChatId
            };
        }

        private static AgentTaskRecord ToRecord(ScheduledTask t, int id) => new AgentTaskRecord
        {
            Id = id, Description = t.Description, Schedule = t.Schedule.Serialize(), NotifyWhen = t.NotifyWhen, Created = t.Created,
            Paused = t.Paused, LastRun = t.LastRun, LastResult = t.LastResult, LastChatId = t.LastChatId
        };

        private static bool TryId(string id, out int value) =>
            int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        /// <inheritdoc />
        public async Task<IList<ScheduledTask>> GetTasksAsync()
        {
            if (_chats.IsPrivate) return new List<ScheduledTask>();
            return (await (await DbAsync().ConfigureAwait(false)).Table<AgentTaskRecord>().OrderBy(t => t.Id).ToListAsync().ConfigureAwait(false))
                .Select(ToTask).Where(t => t != null).ToList();
        }

        /// <summary>
        /// A task, or null if it is gone - read while the chats are private too, for a run that is due.
        /// </summary>
        /// <param name="id">Its id</param>
        public async Task<ScheduledTask> GetTaskAsync(string id)
        {
            if (!TryId(id, out int taskId))
                return null;
            AgentTaskRecord record = await (await DbAsync().ConfigureAwait(false)).Table<AgentTaskRecord>()
                .Where(t => t.Id == taskId).FirstOrDefaultAsync().ConfigureAwait(false);
            return record == null ? null : ToTask(record);
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The chats are private</exception>
        public async Task<string> AddTaskAsync(ScheduledTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (_chats.IsPrivate) throw new InvalidOperationException("Tasks aren't scheduled from a private chat.");
            AgentTaskRecord record = ToRecord(task, 0);
            await (await DbAsync().ConfigureAwait(false)).InsertAsync(record).ConfigureAwait(false);
            return record.Id.ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public async Task UpdateTaskAsync(ScheduledTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (!TryId(task.Id, out int id)) return;
            await (await DbAsync().ConfigureAwait(false)).UpdateAsync(ToRecord(task, id)).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteTaskAsync(string id)
        {
            if (_chats.IsPrivate || !TryId(id, out int taskId)) return;
            await (await DbAsync().ConfigureAwait(false)).DeleteAsync<AgentTaskRecord>(taskId).ConfigureAwait(false);
        }
    }
}
