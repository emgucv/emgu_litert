// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Chat;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// ReplyMarkdown: the Markdown of model replies, read for Emgu.LiteRT.LM.Maui's ReplyView. No model.
[TestFixture]
public class UnitTestReplyMarkdown
{
    [Test]
    public void SplitsTextCodeAndDisplayMaths()
    {
        IReadOnlyList<ReplyBlock> blocks = ReplyMarkdown.SplitBlocks(
            "Intro text\n```python\nprint(1)\nprint(2)\n```\n$$x^2$$\nAfter\n\\[\na + b\n\\]\nEnd");
        Assert.That(blocks.Select(b => b.Kind), Is.EqualTo(new[]
        {
            ReplyBlockKind.Text, ReplyBlockKind.Code, ReplyBlockKind.Math, ReplyBlockKind.Text, ReplyBlockKind.Math, ReplyBlockKind.Text
        }));
        Assert.That(blocks[0].Text, Is.EqualTo("Intro text"));
        Assert.That(blocks[1].Language, Is.EqualTo("python"));
        Assert.That(blocks[1].Text, Is.EqualTo("print(1)\nprint(2)"));
        Assert.That(blocks[2].Text, Is.EqualTo("x^2"));
        Assert.That(blocks[4].Text, Is.EqualTo("a + b"));
        Assert.That(blocks[5].Text, Is.EqualTo("End"));
    }

    [Test]
    public void UnclosedBlocksRunToTheEndWhileStreaming()
    {
        IReadOnlyList<ReplyBlock> code = ReplyMarkdown.SplitBlocks("Here:\n```\nint x = 1;\nint y");
        Assert.That(code.Last().Kind, Is.EqualTo(ReplyBlockKind.Code));
        Assert.That(code.Last().Language, Is.EqualTo(string.Empty));
        Assert.That(code.Last().Text, Is.EqualTo("int x = 1;\nint y"));

        IReadOnlyList<ReplyBlock> math = ReplyMarkdown.SplitBlocks("So\n$$\n\\frac{a}{b");
        Assert.That(math.Last().Kind, Is.EqualTo(ReplyBlockKind.Math));
        Assert.That(math.Last().Text, Is.EqualTo("\\frac{a}{b"));

        // Inline $$...$$ followed by more text is maths within the line, not a block.
        Assert.That(ReplyMarkdown.SplitBlocks("$$x$$ and more").Single().Kind, Is.EqualTo(ReplyBlockKind.Text));
    }

    [Test]
    public void ReadsLines()
    {
        ReplyLine heading = ReplyMarkdown.ParseLine("## Title ##");
        Assert.That(heading.Kind, Is.EqualTo(ReplyLineKind.Heading));
        Assert.That(heading.HeadingLevel, Is.EqualTo(2));
        Assert.That(heading.Content, Is.EqualTo("Title"));

        ReplyLine bullet = ReplyMarkdown.ParseLine("    - nested item");
        Assert.That(bullet.Kind, Is.EqualTo(ReplyLineKind.Bullet));
        Assert.That(bullet.Indent, Is.EqualTo("      "));
        Assert.That(bullet.Marker, Is.EqualTo("•"));
        Assert.That(bullet.Content, Is.EqualTo("nested item"));

        ReplyLine numbered = ReplyMarkdown.ParseLine("12) twelfth");
        Assert.That(numbered.Kind, Is.EqualTo(ReplyLineKind.Numbered));
        Assert.That(numbered.Marker, Is.EqualTo("12)"));
        Assert.That(numbered.Content, Is.EqualTo("twelfth"));

        Assert.That(ReplyMarkdown.ParseLine("> quoted").Kind, Is.EqualTo(ReplyLineKind.Quote));
        Assert.That(ReplyMarkdown.ParseLine("> quoted").Content, Is.EqualTo("quoted"));
        Assert.That(ReplyMarkdown.ParseLine("* * *").Kind, Is.EqualTo(ReplyLineKind.Rule));
        Assert.That(ReplyMarkdown.ParseLine("Just text\r").Content, Is.EqualTo("Just text"));
    }

    [Test]
    public void ReadsInlineRuns()
    {
        IReadOnlyList<ReplyRun> runs = ReplyMarkdown.ParseInline(
            "Use `snake_case` with **bold $x^2$**, *it*, _it2_ and [the site](https://www.emgu.com): $a_1$.");
        Assert.That(runs.Select(r => (r.Kind, r.Text)), Is.EqualTo(new[]
        {
            (ReplyRunKind.Text, "Use "),
            (ReplyRunKind.Code, "snake_case"),
            (ReplyRunKind.Text, " with "),
            (ReplyRunKind.Bold, "bold x²"),
            (ReplyRunKind.Text, ", "),
            (ReplyRunKind.Italic, "it"),
            (ReplyRunKind.Text, ", "),
            (ReplyRunKind.Italic, "it2"),
            (ReplyRunKind.Text, " and "),
            (ReplyRunKind.Link, "the site"),
            (ReplyRunKind.Text, ": "),
            (ReplyRunKind.Math, "a₁"),
            (ReplyRunKind.Text, "."),
        }));
        Assert.That(runs.Single(r => r.Kind == ReplyRunKind.Link).Url, Is.EqualTo("https://www.emgu.com"));
    }

    [Test]
    public void LeavesPricesSnakeCaseAndUnclosedMarkupAlone()
    {
        Assert.That(ReplyMarkdown.ParseInline("It costs $5 and $10.").Single().Kind, Is.EqualTo(ReplyRunKind.Text));
        Assert.That(ReplyMarkdown.ParseInline("my_variable_name").Single().Text, Is.EqualTo("my_variable_name"));
        // Still streaming: the closing ** hasn't arrived yet.
        Assert.That(ReplyMarkdown.ParseInline("This is **bol").Single().Text, Is.EqualTo("This is **bol"));
    }

    [Test]
    public void ConvertsMathForReadingAloud()
    {
        Assert.That(ReplyMarkdown.WithMath("Area is $\\pi r^2$ and \\(e^x\\)."), Is.EqualTo("Area is π r² and eˣ."));
        Assert.That(ReplyMarkdown.WithMath(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void PreviewsTheEndWithoutMarkup()
    {
        Assert.That(ReplyMarkdown.Preview("## Plan\nI'll use **bold** and `code`."), Is.EqualTo("Plan\nI'll use bold and code."));
        string longText = string.Join(" ", Enumerable.Range(1, 100).Select(i => "word" + i));
        string preview = ReplyMarkdown.Preview(longText, 60);
        Assert.That(preview, Does.StartWith("…word"));
        Assert.That(preview, Does.EndWith("word100"));
        Assert.That(preview.Length, Is.LessThanOrEqualTo(61));
    }
}
