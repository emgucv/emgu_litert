// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The "Check homework" chip's requests: what the model is asked to read, solve and compare.
[TestFixture]
public class UnitTestHomeworkCheck
{
    [Test]
    public void QuestionsOnlyDropsTheStudentsAnswers()
    {
        string read = "Here is what I see:\n\n1. Question: 17 + 25 | Student's answer: 42\n2a. Question: 9 x 7 | Student's answer: 56\n3. Question: Round 3.14159 to 2 places | Student's answer: [blank]";
        Assert.That(HomeworkCheck.QuestionsOnly(read), Is.EqualTo("Here is what I see:\n1. 17 + 25\n2a. 9 x 7\n3. Round 3.14159 to 2 places"));
        Assert.That(HomeworkCheck.QuestionsOnly("1. 12 / 4"), Is.EqualTo("1. 12 / 4"));
        Assert.That(HomeworkCheck.QuestionsOnly(null), Is.Empty);
    }

    [Test]
    public void ThePromptsCarryTheirParts()
    {
        Assert.That(HomeworkCheck.ReadPrompt("grade 3"), Does.Contain("[blank]").And.Contain("[?]").And.Contain("2a.").And.Contain("grade 3"));
        Assert.That(HomeworkCheck.ReadPrompt(null), Does.Not.Contain("The user said"));
        Assert.That(HomeworkCheck.ReadPrompt(null), Does.Contain("table").And.Contain("copy what it shows"));
        Assert.That(HomeworkCheck.SolvePrompt("1. Order the lengths"), Does.Contain("read it from the photo"));
        Assert.That(HomeworkCheck.ReadPrompt(null), Does.Contain("not \"3, 4, 21\""));
        Assert.That(HomeworkCheck.ComparePrompt("1. Question: Order | Student's answer: 3, 4, 2, 1", "1. 2.13, 2.95"), Does.Contain("numbers of the table's rows"));
        Assert.That(HomeworkCheck.SolvePrompt("1. 17 + 25"), Does.Contain("1. 17 + 25").And.Contain("Solve each question yourself"));
        string compare = HomeworkCheck.ComparePrompt("1. Question: 2+2 | Student's answer: 5", "1. 4");
        Assert.That(compare, Does.Contain("Student's answer: 5").And.Contain("1. 4").And.Not.Contain("Score"));
    }

    // Page two's 4a: the 12B read "3, 4, 2, 1" and the table right, then its comparison wrote the order as "4, 2, 1, 3" (❌).
    private const string OrderingRead =
        "4a. Question: Order the animals from shortest to longest. Table: Blue Whale 33.58 m, Great White Shark 7.92 m, Leatherback Turtle 2.13 m, Ocean Sunfish 2.95 m | Student's answer: 3, 4, 2, 1\n" +
        "4b. Question: How much longer is the shark than the turtle? | Student's answer: 5.79";

    [Test]
    public void OrderingsOfATablesRowsAreCheckedInCode()
    {
        string check = "4a. ❌ Not the same: the student wrote 3, 4, 2, 1, the answer is 4, 2, 1, 3\n4b. ✅ Correct";
        string fixedCheck = HomeworkCheck.CheckOrderings(OrderingRead, check);
        Assert.That(fixedCheck, Does.StartWith("4a. ✅ Correct (Leatherback Turtle 2.13, Ocean Sunfish 2.95, Great White Shark 7.92, Blue Whale 33.58)"));
        Assert.That(fixedCheck, Does.EndWith("\n4b. ✅ Correct"), "Other questions keep the model's line.");

        // A wrong order is ❌ with the right rows, whatever the model said; labels count as rows too.
        string wrong = HomeworkCheck.CheckOrderings(OrderingRead.Replace("3, 4, 2, 1", "4, 3, 2, 1"), "4a. ✅ Correct\n4b. ✅ Correct");
        Assert.That(wrong, Does.StartWith("4a. ❌ Not the same: the student wrote 4, 3, 2, 1, the answer is 3, 4, 2, 1"));
        string labels = HomeworkCheck.CheckOrderings(OrderingRead.Replace("3, 4, 2, 1", "turtle, sunfish, shark, whale"), "4a. ❓ Can't tell");
        Assert.That(labels, Does.StartWith("4a. ✅ Correct"));

        // Longest first, and a question the comparison left out is added.
        string down = HomeworkCheck.CheckOrderings(OrderingRead.Replace("shortest to longest", "longest to shortest").Replace("3, 4, 2, 1", "1, 2, 4, 3"), "4b. ✅ Correct");
        Assert.That(down, Is.EqualTo("4b. ✅ Correct\n4a. ✅ Correct (Blue Whale 33.58, Great White Shark 7.92, Ocean Sunfish 2.95, Leatherback Turtle 2.13)"));
    }

    [Test]
    public void OrderingsThatCantBeToldForSureKeepTheModelsLine()
    {
        string check = "4a. ❌ Not the same: the student wrote 3, 4, 2, 1, the answer is 4, 2, 1, 3";
        // No direction, a missing or repeated row, a [?], no table, equal numbers.
        foreach (string read in new[]
        {
            OrderingRead.Replace("from shortest to longest", "by length"),
            OrderingRead.Replace("3, 4, 2, 1", "3, 4, 2"),
            OrderingRead.Replace("3, 4, 2, 1", "3, 4, 4, 1"),
            OrderingRead.Replace("3, 4, 2, 1", "3, 4, [?], 1"),
            "4a. Question: Order the animals from shortest to longest. | Student's answer: 3, 4, 2, 1",
            OrderingRead.Replace("2.95", "2.13"),
        })
            Assert.That(HomeworkCheck.CheckOrderings(read, check), Is.EqualTo(check), read);
        Assert.That(HomeworkCheck.CheckOrderings(null, check), Is.EqualTo(check));
    }

    [Test]
    public void BlankAnswersAreNotMarkedWrongAndTheScoreIsCountedHere()
    {
        string check = HomeworkCheck.TidyCheck("1a. ✅ Correct\n2. ❌ Not the same: the student wrote 24.49, the answer is 24.5\n7. ❌ Not the same: the student wrote [blank], the answer is 1786\n3. ❓ Can't tell: unreadable\nScore: 1 of 16");
        Assert.That(check, Is.EqualTo("1a. ✅ Correct\n2. ❌ Not the same: the student wrote 24.49, the answer is 24.5\n7. ❓ Not answered\n3. ❓ Can't tell: unreadable"));
        Assert.That(HomeworkCheck.Count(new[] { check, "1. ✅ Correct\n2. ✅ Correct" }), Is.EqualTo((3, 1, 2)));
        Assert.That(HomeworkCheck.Summary(new[] { check }), Is.EqualTo("**Score: 1 of 4 correct, 1 not the same, 2 not answered or can't tell**"));
        Assert.That(HomeworkCheck.Summary(new[] { "nothing" }), Is.Empty);
    }

    [Test]
    public void ABlankAnswerTheModelDescribesInWordsIsNotAnswered()
    {
        string check = HomeworkCheck.TidyCheck("7. ❓ Can't tell: the student's answer was blank.\n8a. ❓ Can't tell: the answer is blank\n3. ❓ Can't tell: unreadable");
        Assert.That(check, Is.EqualTo("7. ❓ Not answered\n8a. ❓ Not answered\n3. ❓ Can't tell: unreadable"));
        Assert.That(HomeworkCheck.TidyCheck("8b. ❓ Can't tell: the student left this blank."), Is.EqualTo("8b. ❓ Not answered"));
        // A hint that says "blank" is not a blank answer.
        Assert.That(HomeworkCheck.TidyCheck("2. ❌ Not the same: the student wrote 5, the answer is 7 (fill in the blank with 7)"), Does.Contain("❌ Not the same"));
    }
}
