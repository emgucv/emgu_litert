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
        string read = "Here is what I see:\n\n1. Question: 17 + 25 | Student's answer: 42\n2. Question: 9 x 7 | Student's answer: 56\n3. Question: Round 3.14159 to 2 places | Student's answer: [blank]";
        Assert.That(HomeworkCheck.QuestionsOnly(read), Is.EqualTo("Here is what I see:\n1. 17 + 25\n2. 9 x 7\n3. Round 3.14159 to 2 places"));
        Assert.That(HomeworkCheck.QuestionsOnly("1. 12 / 4"), Is.EqualTo("1. 12 / 4"));
        Assert.That(HomeworkCheck.QuestionsOnly(null), Is.Empty);
    }

    [Test]
    public void ThePromptsCarryTheirParts()
    {
        Assert.That(HomeworkCheck.ReadPrompt("grade 3"), Does.Contain("[blank]").And.Contain("[?]").And.Contain("grade 3"));
        Assert.That(HomeworkCheck.ReadPrompt(null), Does.Not.Contain("The user said"));
        Assert.That(HomeworkCheck.SolvePrompt("1. 17 + 25"), Does.Contain("1. 17 + 25").And.Contain("Solve each question yourself"));
        string compare = HomeworkCheck.ComparePrompt("1. Question: 2+2 | Student's answer: 5", "1. 4");
        Assert.That(compare, Does.Contain("Student's answer: 5").And.Contain("1. 4").And.Contain("Score: N of M"));
    }
}
