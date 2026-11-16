// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The per-question requests of the "Check homework" chip: their prompts, the reading of the replies and the shortcut that
// spares the judge request when the answers are the same.
[TestFixture]
public class UnitTestHomeworkQuestionCheck
{
    [Test]
    public void ReadsTheQuestionAndTheStudentsAnswerFromJsonInAnyWrapping()
    {
        Assert.That(HomeworkQuestionCheck.TryParseRead("```json\n{\"question\": \"Round 3.14159 to 2 places\", \"student_answer\": \"3.14\"}\n```", out string q, out string a));
        Assert.That(q, Is.EqualTo("Round 3.14159 to 2 places"));
        Assert.That(a, Is.EqualTo("3.14"));
        Assert.That(HomeworkQuestionCheck.TryParseRead("Here you go: {\"question\": \"x\", \"student_answer\": \"\",}", out _, out a));
        Assert.That(a, Is.EqualTo("[blank]"), "an empty answer is blank");
        Assert.That(HomeworkQuestionCheck.TryParseRead("{\"question\": \"x\", \"student_answer\": 42}", out _, out a));
        Assert.That(a, Is.EqualTo("42"));
        Assert.That(HomeworkQuestionCheck.TryParseRead("no json here", out _, out _), Is.False);
        Assert.That(HomeworkQuestionCheck.TryParseRead("{\"question\": \"x\"}", out _, out _), Is.False, "no answer field");
    }

    [Test]
    public void ReadsYesAndNoAndTheThinkingIsDropped()
    {
        Assert.That(HomeworkQuestionCheck.ParseYesNo("yes"), Is.True);
        Assert.That(HomeworkQuestionCheck.ParseYesNo("No."), Is.False);
        Assert.That(HomeworkQuestionCheck.ParseYesNo("  **Yes**, there is"), Is.True);
        Assert.That(HomeworkQuestionCheck.ParseYesNo("maybe"), Is.Null);
        Assert.That(HomeworkQuestionCheck.ParseYesNo(null), Is.Null);
        Assert.That(HomeworkQuestionCheck.HandwritingPrompt(), Does.Contain("yes or no"));
        Assert.That(HomeworkQuestionChecker.WithoutThinking("<think>hmm</think>\n{\"a\": 1}"), Is.EqualTo("{\"a\": 1}"));
        Assert.That(HomeworkQuestionChecker.WithoutThinking("<think>cut off"), Is.Empty);
        Assert.That(HomeworkQuestionChecker.WithoutThinking("  plain "), Is.EqualTo("plain"));
    }

    [Test]
    public void ReadsTheFinalAnswerLineOfASolution()
    {
        Assert.That(HomeworkQuestionCheck.TryParseSolution("Step 1: 17 x 20 = 340\nStep 2: 17 x 3 = 51\nAnswer: 391", out string a));
        Assert.That(a, Is.EqualTo("391"));
        Assert.That(HomeworkQuestionCheck.TryParseSolution("work...\n**Final Answer:** 24.5 cm", out a));
        Assert.That(a, Is.EqualTo("24.5 cm"));
        Assert.That(HomeworkQuestionCheck.TryParseSolution("Answer: first\nmore work\nAnswer: second", out a));
        Assert.That(a, Is.EqualTo("second"), "the last one");
        Assert.That(HomeworkQuestionCheck.TryParseSolution("no answer line", out _), Is.False);
    }

    [Test]
    public void ReadsAJudgement()
    {
        Assert.That(HomeworkQuestionCheck.TryParseJudgement(
            "{\"verdict\": \"wrong\", \"how_to_solve\": \"Divide 5.6 by root 2, square it: 15.68, round to 15.7.\", \"what_went_wrong\": \"Not rounded to 3 significant digits.\"}",
            out HomeworkVerdict verdict, out string how, out string wrong));
        Assert.That(verdict, Is.EqualTo(HomeworkVerdict.Wrong));
        Assert.That(how, Does.Contain("15.7"));
        Assert.That(wrong, Does.Contain("significant"));
        HomeworkQuestionCheck.TryParseJudgement("{\"verdict\": \"Correct\", \"how_to_solve\": \"\", \"what_went_wrong\": \"\"}", out verdict, out _, out _);
        Assert.That(verdict, Is.EqualTo(HomeworkVerdict.Correct));
        HomeworkQuestionCheck.TryParseJudgement("{\"verdict\": \"maybe\"}", out verdict, out _, out _);
        Assert.That(verdict, Is.EqualTo(HomeworkVerdict.Unclear));
        Assert.That(HomeworkQuestionCheck.TryParseJudgement("sorry", out _, out _, out _), Is.False);
    }

    [TestCase("391", "391", true)]
    [TestCase("15.7 cm²", "15.70", true)]
    [TestCase("1,234", "1234", true)]
    [TestCase("$4.65", "4.65", true)]
    [TestCase("5 hours", "5", true)]
    [TestCase("Paris", "paris", true)]
    [TestCase("15.68", "15.7", false)]
    [TestCase("24.49", "24.5", false)]
    [TestCase("3 pens for $2.88", "5 pens for $4.65", false)]
    [TestCase("[blank]", "391", false)]
    [TestCase("3 + 4", "7", false)]
    [TestCase("", "5", false)]
    public void AnswersAreTheSameWhenNothingButAUnitDiffers(string student, string solved, bool expected) =>
        Assert.That(HomeworkQuestionCheck.AnswersMatch(student, solved), Is.EqualTo(expected));

    [Test]
    public void ThePromptsKeepTheStudentsAnswerOutOfTheSolve()
    {
        Assert.That(HomeworkQuestionCheck.ReadPrompt("2a", null), Does.Contain("question 2a").And.Contain("[blank]").And.Contain("[?]").And.Not.Contain("OCR read"));
        string withText = HomeworkQuestionCheck.ReadPrompt("5", null, new[] { "5. The wind speed was 26.7 km/h", "43.5" });
        Assert.That(withText, Does.Contain("OCR read").And.Contain("[unsure]").And.Contain("2. 43.5").And.Contain("answer_lines").And.Contain("Do not solve"));
        Assert.That(withText.IndexOf("student_answer"), Is.LessThan(withText.IndexOf("\"question\"")), "the answer comes first, before the question the model might solve");
        string solve = HomeworkQuestionCheck.SolvePrompt("4b", "How much longer is the blue whale?");
        Assert.That(solve, Does.Contain("part 4b").And.Contain("Ignore any handwriting").And.Contain("use the values it gives").And.Contain("Answer:"));
        Assert.That(HomeworkQuestionCheck.SolvePrompt("3", "Which deal?"), Does.Contain("question 3"));
        string judge = HomeworkQuestionCheck.JudgePrompt("1", "Q", "15.68", "Answer: 15.7");
        Assert.That(judge, Does.Contain("15.68").And.Contain("Answer: 15.7").And.Contain("how_to_solve").And.Contain("what_went_wrong"));
    }
}
