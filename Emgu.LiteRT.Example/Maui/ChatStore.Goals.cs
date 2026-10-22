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

namespace Maui.Demo.Lite
{
    /// <summary>
    /// A goal of the user's ("Run three times a week"), kept across chats like the memories.
    /// </summary>
    [Table("Goal")]
    public class GoalRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Text { get; set; }

        /// <summary>How much per period, or null if there is nothing to count</summary>
        public double? Target { get; set; }

        public string Unit { get; set; }

        /// <summary>A GoalPeriod</summary>
        public int Period { get; set; }

        /// <summary>When the goal was set (local time)</summary>
        public DateTime Created { get; set; }
    }

    /// <summary>
    /// Progress logged toward a goal: an amount in the goal's unit on a day (local time).
    /// </summary>
    [Table("GoalProgress")]
    public class GoalProgressRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int GoalId { get; set; }

        public double Amount { get; set; }

        public DateTime When { get; set; }
    }

    // The goals for the agent's goal tools (Emgu.LiteRT.LM.Agents' AgentTools.Goals). Like the memories, nothing is read
    // or written in a private chat.
    internal sealed partial class ChatStore : IAgentGoalStore
    {
        private static AgentGoal ToGoal(GoalRecord r) =>
            new AgentGoal(r.Id.ToString(CultureInfo.InvariantCulture), r.Text, r.Target, r.Unit, (GoalPeriod)r.Period, r.Created);

        private static int GoalId(string id) => int.Parse(id, CultureInfo.InvariantCulture);

        public async Task<IList<AgentGoal>> GetGoalsAsync()
        {
            if (IsPrivateSession) return new List<AgentGoal>();
            return (await (await Db).Table<GoalRecord>().OrderBy(g => g.Id).ToListAsync()).Select(ToGoal).ToList();
        }

        public async Task<string> AddGoalAsync(AgentGoal goal)
        {
            if (IsPrivateSession) throw new InvalidOperationException("Goals aren't saved in a private chat.");
            GoalRecord record = new GoalRecord { Text = goal.Text, Target = goal.Target, Unit = goal.Unit, Period = (int)goal.Period, Created = goal.Created };
            await (await Db).InsertAsync(record);
            return record.Id.ToString(CultureInfo.InvariantCulture);
        }

        public async Task UpdateGoalAsync(AgentGoal goal)
        {
            if (IsPrivateSession) return;
            await (await Db).UpdateAsync(new GoalRecord
            {
                Id = GoalId(goal.Id), Text = goal.Text, Target = goal.Target, Unit = goal.Unit, Period = (int)goal.Period, Created = goal.Created
            });
        }

        public async Task DeleteGoalAsync(string id)
        {
            if (IsPrivateSession) return;
            SQLiteAsyncConnection db = await Db;
            int goalId = GoalId(id);
            await db.ExecuteAsync("DELETE FROM GoalProgress WHERE GoalId = ?", goalId);
            await db.DeleteAsync<GoalRecord>(goalId);
        }

        public async Task<IList<GoalProgress>> GetProgressAsync(string goalId, DateTime since)
        {
            if (IsPrivateSession) return new List<GoalProgress>();
            int id = GoalId(goalId);
            List<GoalProgressRecord> records = await (await Db).Table<GoalProgressRecord>()
                .Where(p => p.GoalId == id && p.When >= since).OrderBy(p => p.When).ToListAsync();
            return records.Select(p => new GoalProgress(goalId, p.Amount, p.When)).ToList();
        }

        public async Task AddProgressAsync(GoalProgress progress)
        {
            if (IsPrivateSession) throw new InvalidOperationException("Progress isn't saved in a private chat.");
            await (await Db).InsertAsync(new GoalProgressRecord { GoalId = GoalId(progress.GoalId), Amount = progress.Amount, When = progress.When });
        }

        // The latest progress entry of a goal, for undoing one in Settings.
        public async Task<bool> DeleteLastProgressAsync(string goalId)
        {
            if (IsPrivateSession) return false;
            SQLiteAsyncConnection db = await Db;
            int id = GoalId(goalId);
            GoalProgressRecord last = await db.Table<GoalProgressRecord>().Where(p => p.GoalId == id)
                .OrderByDescending(p => p.When).FirstOrDefaultAsync();
            if (last == null)
                return false;
            await db.DeleteAsync(last);
            return true;
        }
    }
}
