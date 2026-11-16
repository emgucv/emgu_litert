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
