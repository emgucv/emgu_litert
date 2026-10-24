// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Counting jumping jacks (JumpingJackCounter) from made-up landmarks, and logging a count toward a goal (AgentTools.LogExerciseAsync);
// no model or native library.
[TestFixture]
public class UnitTestJumpingJacks
{
    private const int W = 200, H = 400;

    // A person in the middle of a 200 x 400 image, shoulders 40 px apart: closed - arms down, feet 28 px apart (0.7 shoulder widths) - or
    // open - wrists 40 px above the shoulders (1 shoulder width), feet 64 px apart (1.6).
    private static float[][] Person(bool open, bool ankles = true)
    {
        var p = Enumerable.Range(0, 33).Select(_ => new[] { 0.5f, 0.5f, 0f, 1f, 1f }).ToArray();
        void Set(int i, float x, float y) { p[i][0] = x; p[i][1] = y; }
        Set(0, 0.5f, 0.1f);
        Set(11, 0.4f, 0.25f); Set(12, 0.6f, 0.25f);
        Set(23, 0.43f, 0.55f); Set(24, 0.57f, 0.55f);
        if (open)
        {
            Set(15, 0.3f, 0.15f); Set(16, 0.7f, 0.15f);
            Set(27, 0.34f, 0.95f); Set(28, 0.66f, 0.95f);
        }
        else
        {
            Set(15, 0.35f, 0.5f); Set(16, 0.65f, 0.5f);
            Set(27, 0.43f, 0.95f); Set(28, 0.57f, 0.95f);
        }
        if (!ankles) { p[27][3] = 0.1f; p[28][3] = 0.1f; }
        return p;
    }

    private static int CountOf(IEnumerable<float[][]> frames)
    {
        var counter = new JumpingJackCounter();
        foreach (var frame in frames) counter.Update(frame, W, H);
        return counter.Count;
    }

    private static IEnumerable<float[][]> Jacks(int n, bool ankles = true)
    {
        yield return Person(false, ankles);
        for (int i = 0; i < n; i++)
        {
            yield return Person(true, ankles);
            yield return Person(true, ankles);
            yield return Person(false, ankles);
        }
    }

    [Test]
    public void ReadsTheTwoPositions()
    {
        Assert.That(JumpingJackCounter.Read(Person(false), W, H), Is.EqualTo(JumpingJackCounter.Position.Closed));
        Assert.That(JumpingJackCounter.Read(Person(true), W, H), Is.EqualTo(JumpingJackCounter.Position.Open));
    }

    [Test]
    public void CountsEachJumpingJackWhenItCloses()
    {
        Assert.That(CountOf(Jacks(3)), Is.EqualTo(3));
        var counter = new JumpingJackCounter();
        Assert.That(counter.Update(Person(false), W, H), Is.False);
        Assert.That(counter.Update(Person(true), W, H), Is.False);   // open: not yet
        Assert.That(counter.Update(Person(false), W, H), Is.True);   // closed again: one
        Assert.That(counter.Count, Is.EqualTo(1));
    }

    [Test]
    public void ArmsUpWithTheFeetTogetherIsNotAJumpingJack()
    {
        // Raising the arms while standing still: the feet are visible and together.
        var armsOnly = Person(true);
        armsOnly[27][0] = 0.43f; armsOnly[28][0] = 0.57f;
        Assert.That(JumpingJackCounter.Read(armsOnly, W, H), Is.EqualTo(JumpingJackCounter.Position.Unknown));
        Assert.That(CountOf(new[] { Person(false), armsOnly, Person(false), armsOnly, Person(false) }), Is.EqualTo(0));
    }

    [Test]
    public void WithoutTheFeetInViewTheArmsCount()
    {
        Assert.That(CountOf(Jacks(4, ankles: false)), Is.EqualTo(4));
    }

    [Test]
    public void FramesInBetweenChangeNothing()
    {
        // Wrists at shoulder height - neither up nor down - between every frame: still 2.
        var between = Person(false);
        between[15][1] = 0.24f; between[16][1] = 0.24f;
        Assert.That(JumpingJackCounter.Read(between, W, H), Is.EqualTo(JumpingJackCounter.Position.Unknown));
        var frames = Jacks(2).SelectMany(f => new[] { f, between }).ToArray();
        Assert.That(CountOf(frames), Is.EqualTo(2));
    }

    [Test]
    public void LosingThePersonDropsTheJumpingJackUnderWayButKeepsTheCount()
    {
        var frames = Jacks(2).Concat(new[] { Person(true), null, Person(false), Person(true), Person(false) });
        // The open position before the lost frame doesn't count when the person comes back closed; the next one does.
        Assert.That(CountOf(frames), Is.EqualTo(3));
    }

    // A Saturday morning.
    private static readonly DateTime Now = new DateTime(2026, 10, 24, 9, 0, 0);

    [Test]
    public async Task LogsTheCountTowardTheGoalThatNamesIt()
    {
        var store = new InMemoryAgentGoalStore();
        await store.AddGoalAsync(AgentGoal.FromText("Read 20 pages a day", Now));
        await store.AddGoalAsync(AgentGoal.FromText("30 jumping jacks per day", Now));
        string logged = await AgentTools.LogExerciseAsync(store, new ExerciseResult("jumping jacks", 18, TimeSpan.FromSeconds(25), "the user pressed Done"), Now);
        Assert.That(logged, Does.StartWith("Logged 18 jumping jacks for \"30 jumping jacks per day\""));
        Assert.That(logged, Does.Contain("18 of 30 jumping jacks today"));
        logged = await AgentTools.LogExerciseAsync(store, new ExerciseResult("jumping jacks", 14, TimeSpan.FromSeconds(20), "the user pressed Done"), Now);
        Assert.That(logged, Does.Contain("32 of 30 jumping jacks today (reached)"));
        string text = AgentTools.ExerciseResultText(new ExerciseResult("jumping jacks", 14, TimeSpan.FromSeconds(20), "the user pressed Done"), logged, true);
        Assert.That(text, Does.Contain("The user did 14 jumping jacks in 20 s"));
        Assert.That(text, Does.Contain("don't call log_progress"));
    }

    [Test]
    public async Task AGoalCountedInTimesOrMinutesGetsOneOrTheMinutes()
    {
        var store = new InMemoryAgentGoalStore();
        await store.AddGoalAsync(AgentGoal.FromText("Do star jumps every day", Now));
        string logged = await AgentTools.LogExerciseAsync(store, new ExerciseResult("jumping jacks", 40, TimeSpan.FromSeconds(50), "x"), Now);
        Assert.That(logged, Does.StartWith("Logged 1 time"));

        store = new InMemoryAgentGoalStore();
        await store.AddGoalAsync(AgentGoal.FromText("10 minutes of jumping jacks a day", Now));
        logged = await AgentTools.LogExerciseAsync(store, new ExerciseResult("jumping jacks", 90, TimeSpan.FromSeconds(120), "x"), Now);
        Assert.That(logged, Does.StartWith("Logged 2 minutes"));
    }

    [Test]
    public async Task NothingIsLoggedWithoutAGoalOrACount()
    {
        var store = new InMemoryAgentGoalStore();
        await store.AddGoalAsync(AgentGoal.FromText("Run three times a week", Now));
        Assert.That(await AgentTools.LogExerciseAsync(store, new ExerciseResult("jumping jacks", 20, TimeSpan.FromSeconds(30), "x"), Now), Is.Null);
        await store.AddGoalAsync(AgentGoal.FromText("30 jumping jacks a day", Now));
        Assert.That(await AgentTools.LogExerciseAsync(store, new ExerciseResult("jumping jacks", 0, TimeSpan.FromSeconds(30), "x"), Now), Is.Null);
        Assert.That(await AgentTools.LogExerciseAsync(null, new ExerciseResult("jumping jacks", 5, TimeSpan.FromSeconds(30), "x"), Now), Is.Null);
        string text = AgentTools.ExerciseResultText(new ExerciseResult("jumping jacks", 1, TimeSpan.FromSeconds(4), "x"), null, true);
        Assert.That(text, Does.Contain("The user did 1 jumping jack in 4 s"));
        Assert.That(text, Does.Contain("add_goal"));
    }

    [Test]
    public void KnowsWhichExercisesItCounts()
    {
        Assert.That(AgentTools.CountableExercise("Jumping Jacks"), Is.EqualTo("jumping jacks"));
        Assert.That(AgentTools.CountableExercise("star jumps"), Is.EqualTo("jumping jacks"));
        Assert.That(AgentTools.CountableExercise("jumping-jack"), Is.EqualTo("jumping jacks"));
        Assert.That(AgentTools.CountableExercise("push ups"), Is.Null);
    }
}
