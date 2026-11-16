// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Linq;
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The whole-page check of the "Check homework" chip, cut into one item per question or part and one section per top-level
// question, so each section can show its region of the page.
[TestFixture]
public class UnitTestHomeworkPageCheck
{
    private const string Read =
        "1. Question: Between 5.63 and 5.68 | Student's answer: 5.67\n" +
        "2a. Question: Round 3.1 | Student's answer: 3\n" +
        "2b. Question: Round 4.6 | Student's answer: [blank]\n" +
        "**3.** Question: 3 pens for $2.88 or 5 for $4.65 | Student's answer: 3 pens for $2.88";
    private const string Solved = "1. 5.65\n2a. 3\n2b. 5\n3. 5 pens for $4.65";
    private const string Check =
        "1. ✅ Correct\n" +
        "2a. ✅ Correct\n" +
        "2b. ❓ Not answered\n" +
        "3. ❌ Not the same: the student wrote 3 pens for $2.88, the answer is 5 pens for $4.65 (compare the unit prices)";

    [Test]
    public void ReadsOneItemPerQuestionAndPart()
    {
        var items = HomeworkPageCheck.Parse(Read, Solved, Check);
        Assert.That(items.Select(i => i.Label), Is.EqualTo(new[] { "1", "2a", "2b", "3" }));
        Assert.That(items[0].QuestionText, Is.EqualTo("Between 5.63 and 5.68"));
        Assert.That(items[0].StudentAnswer, Is.EqualTo("5.67"));
        Assert.That(items[0].MyAnswer, Is.EqualTo("5.65"));
        Assert.That(items[0].Verdict, Is.EqualTo(HomeworkVerdict.Correct));
        Assert.That(items[1].Question, Is.EqualTo("2"));
        Assert.That(items[1].Part, Is.EqualTo("a"));
        Assert.That(items[2].StudentAnswer, Is.EqualTo("[blank]"));
        Assert.That(items[2].Verdict, Is.EqualTo(HomeworkVerdict.NotAnswered));
        Assert.That(items[3].Verdict, Is.EqualTo(HomeworkVerdict.Wrong));
        Assert.That(items[3].Detail, Does.StartWith("the student wrote 3 pens"));
        Assert.That(items[3].Detail, Does.Contain("compare the unit prices"));
    }

    [Test]
    public void ToleratesBulletsPartLettersOnTheirOwnAndMissingLines()
    {
        var items = HomeworkPageCheck.Parse(
            "- 4. Question: Find the area | Student's answer: 25\n  a) Question: Left | Student's answer: 1\n  b) Question: Right | Student's answer: (blank)",
            "4a. 1",
            "4a. ❌ Not the same: wrote 1, the answer is 2");
        Assert.That(items.Select(i => i.Label), Is.EqualTo(new[] { "4", "4a", "4b" }));
        Assert.That(items[2].StudentAnswer, Is.EqualTo("[blank]"));
        Assert.That(items[1].Verdict, Is.EqualTo(HomeworkVerdict.Wrong));
        Assert.That(items[0].Verdict, Is.EqualTo(HomeworkVerdict.Unclear), "the comparison did not list it");
        Assert.That(items[2].Verdict, Is.EqualTo(HomeworkVerdict.NotAnswered), "blank and not listed");
    }

    [Test]
    public void ADecimalAtTheStartOfALineIsNotALabelAndACheckLineWithNoReadLineIsKept()
    {
        var items = HomeworkPageCheck.Parse("1. Question: x | Student's answer: 1.5 cm\n1.5 is not a label", string.Empty, "9. ✅ Correct");
        Assert.That(items.Select(i => i.Label), Is.EqualTo(new[] { "1", "9" }));
        Assert.That(items[0].StudentAnswer, Is.EqualTo("1.5 cm"));
        Assert.That(items[1].Verdict, Is.EqualTo(HomeworkVerdict.Correct));
    }

    [Test]
    public void PartsAreOneSectionWithOneMark()
    {
        var sections = HomeworkPageCheck.Group(HomeworkPageCheck.Parse(Read, Solved, Check), new[] { "1", "2", "3" });
        Assert.That(sections.Select(s => s.Question), Is.EqualTo(new[] { "1", "2", "3" }));
        Assert.That(sections[1].Items.Count, Is.EqualTo(2), "2a and 2b together");
        Assert.That(sections[0].Verdict, Is.EqualTo(HomeworkVerdict.Correct));
        Assert.That(sections[1].Verdict, Is.EqualTo(HomeworkVerdict.Unclear), "a part correct and a part not answered");
        Assert.That(sections[2].Verdict, Is.EqualTo(HomeworkVerdict.Wrong));
        Assert.That(sections.All(s => s.HasRegion));
    }

    [Test]
    public void AnyWrongPartMakesTheQuestionWrongAndAllBlankMakesItNotAnswered()
    {
        var wrong = HomeworkPageCheck.Group(HomeworkPageCheck.Parse(
            "1a. Question: a | Student's answer: 1\n1b. Question: b | Student's answer: 2", string.Empty,
            "1a. ✅ Correct\n1b. ❌ Not the same: wrote 2, the answer is 3"), new[] { "1" });
        Assert.That(wrong.Single().Verdict, Is.EqualTo(HomeworkVerdict.Wrong));
        var blank = HomeworkPageCheck.Group(HomeworkPageCheck.Parse(
            "7a. Question: a | Student's answer: [blank]\n7b. Question: b | Student's answer: [blank]", string.Empty, "7a. ❓ Not answered\n7b. ❓ Not answered"), new[] { "7" });
        Assert.That(blank.Single().Verdict, Is.EqualTo(HomeworkVerdict.NotAnswered));
    }

    [Test]
    public void ARegionTheCheckDidNotListAndAnItemWithNoRegionAreBothKept()
    {
        var sections = HomeworkPageCheck.Group(HomeworkPageCheck.Parse("2. Question: x | Student's answer: 1", string.Empty, "2. ✅ Correct"), new[] { "1", "3" });
        Assert.That(sections.Select(s => s.Question), Is.EqualTo(new[] { "1", "3", "2" }), "regions first, then the unmatched item");
        Assert.That(sections[0].HasRegion && sections[0].Items.Count == 0);
        Assert.That(sections[0].Verdict, Is.EqualTo(HomeworkVerdict.Unclear));
        Assert.That(sections[2].HasRegion, Is.False);
        Assert.That(sections[2].Verdict, Is.EqualTo(HomeworkVerdict.Correct));
    }

    [Test]
    public void TheScoreCountsOneMarkPerSection()
    {
        var sections = HomeworkPageCheck.Group(HomeworkPageCheck.Parse(Read, Solved, Check), new[] { "1", "2", "3" });
        Assert.That(HomeworkCheck.Summary(new[] { HomeworkPageCheck.Marks(sections) }), Is.EqualTo("**Score: 1 of 3 correct, 1 not the same, 1 not answered or can't tell**"));
    }

    [Test]
    public void AStemLineWithNoAnswerIsNotAPartToMark()
    {
        var sections = HomeworkPageCheck.Group(HomeworkPageCheck.Parse(
            "1. Question: Order these numbers\n1a. Question: a | Student's answer: 5.67\n1b. Question: b | Student's answer: 2.71",
            string.Empty, "1a. ✅ Correct\n1b. ✅ Correct"), new[] { "1" });
        Assert.That(sections.Single().Items.Select(i => i.Label), Is.EqualTo(new[] { "1a", "1b" }));
        Assert.That(sections.Single().Verdict, Is.EqualTo(HomeworkVerdict.Correct));
    }

    [Test]
    public void AStemMarkedBlankIsNotAPartEither()
    {
        var sections = HomeworkPageCheck.Group(HomeworkPageCheck.Parse(
            "1. Question: Write a decimal for each description. | Student's answer: [blank]\n1a. Question: a | Student's answer: 5.67",
            string.Empty, "1. ❓ Not answered\n1a. ✅ Correct"), new[] { "1" });
        Assert.That(sections.Single().Items.Select(i => i.Label), Is.EqualTo(new[] { "1a" }));
        Assert.That(sections.Single().Verdict, Is.EqualTo(HomeworkVerdict.Correct));
    }

    [Test]
    public void AStemTheModelSolvedAndCalledCantTellIsNotAPartEither()
    {
        var sections = HomeworkPageCheck.Group(HomeworkPageCheck.Parse(
            "1. Question: Write a decimal for each description. | Student's answer: [blank]\n1a. Question: a | Student's answer: 5.67",
            "1. a decimal for each description\n1a. 5.65", "1. ❓ Can't tell: no answer\n1a. ✅ Correct"), new[] { "1" });
        Assert.That(sections.Single().Items.Select(i => i.Label), Is.EqualTo(new[] { "1a" }));
        Assert.That(sections.Single().Verdict, Is.EqualTo(HomeworkVerdict.Correct));
    }
}
