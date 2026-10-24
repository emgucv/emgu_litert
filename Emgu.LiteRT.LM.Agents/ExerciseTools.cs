//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// What a counted exercise session came to: how many, in how long, and whether the user stopped it.
    /// </summary>
    public class ExerciseResult
    {
        /// <summary>Create a result</summary>
        /// <param name="exercise">The exercise, e.g. "jumping jacks"</param>
        /// <param name="count">How many were counted</param>
        /// <param name="duration">From the start of counting to the end</param>
        /// <param name="ending">Why it ended, for the model: e.g. "the target was reached", "the user stopped it"</param>
        public ExerciseResult(String exercise, int count, TimeSpan duration, String ending)
        {
            Exercise = exercise;
            Count = count;
            Duration = duration;
            Ending = ending;
        }

        /// <summary>The exercise, e.g. "jumping jacks"</summary>
        public String Exercise { get; }

        /// <summary>How many were counted</summary>
        public int Count { get; }

        /// <summary>From the start of counting to the end</summary>
        public TimeSpan Duration { get; }

        /// <summary>Why it ended, e.g. "the target was reached"</summary>
        public String Ending { get; }
    }

    public static partial class AgentTools
    {
        /// <summary>The exercises count_exercise can count</summary>
        public static readonly string[] CountableExercises = { "jumping jacks", "squats", "high knees" };

        // Each countable exercise by the words people use for it.
        private static readonly (string Name, Regex Words)[] ExerciseWords =
        {
            ("jumping jacks", new Regex(@"\b(?:jumping[\s-]*jacks?|star[\s-]*jumps?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("squats", new Regex(@"\bsquat(?:s|ting)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("high knees", new Regex(@"\b(?:high[\s-]*knees?|knee[\s-]*(?:raises?|lifts?|ups?)|marching\s+(?:on\s+the\s+spot|in\s+place))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        };

        /// <summary>
        /// count_exercise: the app counts an exercise the user does in front of the camera (jumping jacks, squats, high knees) until a target, a time limit, the
        /// user stopping it or a pause, and returns the count - with what LogExerciseAsync logged toward a goal, so the model doesn't log it
        /// again. The model is too slow to judge each movement itself, so the app's count function does the counting (e.g. the body pose model
        /// on each frame) and the call lasts as long as the exercise.
        /// </summary>
        /// <param name="count">Counts: (exercise, target - 0 for none -, seconds - 0 for the app's default -, cancellation) to the result text
        /// for the model</param>
        /// <returns>The tool</returns>
        public static AIFunction CountExercise(Func<string, int, int, CancellationToken, Task<string>> count)
        {
            if (count == null)
                throw new ArgumentNullException(nameof(count));
            return AIFunctionFactory.Create(
                ([Description("The exercise: \"jumping jacks\", \"squats\" or \"high knees\"")] string exercise = "jumping jacks",
                 [Description("How many to stop at, if the user said (e.g. 30); 0 if not")] int target = 0,
                 [Description("How many seconds to count for, if the user said (e.g. 60 for \"one minute\"); 0 if not")] int seconds = 0,
                 CancellationToken cancellationToken = default) =>
                    count(String.IsNullOrWhiteSpace(exercise) ? "jumping jacks" : exercise.Trim(), Math.Max(0, target), Math.Max(0, seconds), cancellationToken),
                "count_exercise",
                "Counts jumping jacks, squats or high knees the user does in front of the camera, showing the count as they go, and returns " +
                "how many they did. Use it when the user wants to do one of them, wants them counted or wants a challenge. The count is " +
                "logged to a matching goal automatically: never call log_progress for it.");
        }

        /// <summary>
        /// Whether an exercise can be counted (count_exercise), by its name in the user's words: "jumping jacks", "star jumps", "squats",
        /// "high knees", "knee raises".
        /// </summary>
        /// <param name="exercise">The exercise</param>
        /// <returns>The name it is counted as (one of <see cref="CountableExercises"/>), or null</returns>
        public static string CountableExercise(string exercise)
        {
            if (exercise == null) return null;
            foreach (var (name, words) in ExerciseWords)
                if (words.IsMatch(exercise))
                    return name;
            return null;
        }

        /// <summary>
        /// Log a counted exercise toward the goal that names it ("30 jumping jacks a day"): its count if the goal counts the exercise, 1 if it
        /// counts times ("do jumping jacks every day"), the minutes if it counts minutes. Nothing is logged for a count of 0.
        /// </summary>
        /// <param name="store">The user's goals, or null</param>
        /// <param name="result">The session</param>
        /// <param name="now">The current local time</param>
        /// <returns>The goal's status after logging (GoalStatusAsync) with what was logged, or null if no goal names the exercise or nothing
        /// was counted</returns>
        public static async Task<string> LogExerciseAsync(IAgentGoalStore store, ExerciseResult result, DateTime now)
        {
            if (store == null || result == null || result.Count <= 0)
                return null;
            IList<AgentGoal> goals = await store.GetGoalsAsync().ConfigureAwait(false);
            AgentGoal goal = goals.FirstOrDefault(g => CountableExercise(g.Text) == result.Exercise);
            if (goal == null)
                return null;
            string unit = goal.Unit.ToLowerInvariant();
            double amount = unit == "times" ? 1
                : unit.StartsWith("min", StringComparison.Ordinal) ? Math.Round(result.Duration.TotalMinutes, 1)
                : result.Count;
            if (amount <= 0)
                return null;
            await store.AddProgressAsync(new GoalProgress(goal.Id, amount, now)).ConfigureAwait(false);
            return "Logged " + GoalParser.Amount(amount, goal.Unit) + " for \"" + goal.Text + "\". Now: "
                + await GoalStatusAsync(store, goal, now).ConfigureAwait(false);
        }

        /// <summary>
        /// The result of count_exercise for the model: what was counted, why it ended and what was logged toward a goal.
        /// </summary>
        /// <param name="result">The session</param>
        /// <param name="logged">LogExerciseAsync's text, or null</param>
        /// <param name="goalsOn">Whether goals are kept (if not, nothing was logged and the model shouldn't offer it)</param>
        /// <returns>The text</returns>
        public static string ExerciseResultText(ExerciseResult result, string logged, bool goalsOn)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            string seconds = Math.Round(result.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            string text = result.Count == 0
                ? "No " + result.Exercise + " were counted in " + seconds + " s (" + result.Ending + ")."
                : "The user did " + result.Count.ToString(CultureInfo.InvariantCulture) + " " + (result.Count == 1 ? result.Exercise.TrimEnd('s') : result.Exercise)
                  + " in " + seconds + " s (" + result.Ending + ").";
            if (logged != null)
                text += " " + logged + " It is already logged: don't call log_progress.";
            else if (result.Count > 0 && goalsOn)
                text += " No goal counts " + result.Exercise + ": if the user wants one, they can set it (add_goal).";
            return text + "\nTell the user in one or two short, encouraging sentences.";
        }
    }
}
