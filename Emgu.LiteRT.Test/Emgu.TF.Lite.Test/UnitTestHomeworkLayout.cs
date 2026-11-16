// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Grouping the text lines of a photographed homework page into questions and parts (the "Check homework" chip). The lines are
// the measurements of two real worksheet pages (1200 x 1600 photos; x, y, width, height of each line's box and what the
// recognizer read at its start, a name left out), so the layout is tried on real handwriting, skew and gaps.
[TestFixture]
public class UnitTestHomeworkLayout
{
    // "index x y width height|start|left start" per line; the left start (a little to the left of the line too, where a number the
    // detector left out of the line sits) is read for the lines at the left margin only.
    private const string PageTwo = @"
4 139 351 771 37|Write a decimal for eac|1. Write a dec
5 970 373 37 35|2
6 450 380 147 44|5C7
7 609 385 271 36|b) Between 2.70 and
8 141 393 252 39|a) Between 5.63 and 5.6|a) Between
9 1022 427 44 36|6
10 1062 427 41 36|5
11 971 428 36 31|O
12 494 439 97 42|—C
13 430 440 81 41|N.
14 609 443 245 35|d) Between 0.6 and
15 140 446 301 41|c) Between 21.75 and 21.8|c) Between 2
16 1064 481 45 40|9
17 1009 482 47 37|a
18 961 491 33 31|5
19 494 494 105 43|.69
20 604 496 339 36|f) One hundredth les
21 452 499 54 34|4.
22 145 503 292 34|e) One tenth greater th|e) One ten
23 721 551 21 21|4
24 143 566 614 41|Put a decimal in each num|2.Put a decima
25 847 602 65 34|d) 4
26 611 605 130 32|c) 15479
27 379 606 92 32|b) 504
28 146 609 90 32|a) 348|a) 348
29 850 627 78 41|0.4
30 410 638 84 37|50.4
31 636 638 126 33|15.479
32 162 645 93 33|3.48|3.48
33 148 676 741 31|Under which deal do|3.Under wh
34 111 688 26 28|3.|3
35 181 708 111 33|3 pens|3pens
36 313 708 61 26|for
37 423 710 100 31|$2.88
38 149 741 570 30|The chart shows th|4The char
39 114 744 30 30|4.|14
40 993 751 111 33|Length (m)
41 862 752 77 32|Animal
42 150 767 136 28|sea creatures.|sea crea
43 1017 792 61 31|33.58
44 850 795 100 28|Blue Whale
45 148 803 556 32|a) Order the length|a) Order
46 1022 832 54 34|7.92
47 821 836 156 23|Great White Sha
48 151 841 668 31|b) How much longe|b) How n
49 817 869 166 29|Pacific Leatherback
50 150 875 548 35|c) About how many ti|c) About 
51 1021 876 53 38|2.13
52 873 893 56 26|Turtle
53 185 902 126 28|white shark?|white sh
54 368 903 53 28|3x
55 1019 927 54 34|2.95
56 835 930 131 28|Ocean Sunfish
57 151 939 635 30|d) About how long|d) Abou
58 585 979 108 31|100.74
59 150 1001 737 34|The wind speed in Va|5.The wind
60 117 1004 26 29|5.|.5.
61 153 1027 744 32|and 2.4 km/h on Wec|and 2.4 km
62 485 1065 82 27|43.5
63 151 1091 752 34|Jacob walks 0.05 kilo|6Jacob wal
64 151 1120 149 30|to walk 15 km?|to walk 1
65 418 1141 249 34|300m=550
66 151 1180 694 32|Encke's Comet appe|7.Encke's
67 119 1187 29 26|7.|7.
68 152 1205 731 35|When was the last time|When was
69 154 1234 159 29|Show your work.|Show yo
70 156 1294 699 33|Our ecological footp|8.Our ecolo
71 155 1319 716 36|clothing, and other reso|clothing, an
72 154 1353 754 38|a) In Canada, the avera|a) In Cana
73 188 1379 722 33|In Sudbury, the ecolo|In Sudbun
74 191 1401 320 34|is this than the Canadi|is this than
75 157 1436 757 37|b) The world average is|b) The wor
76 192 1467 304 29|footprint than the w|footprint
77 187 1495 674 38|There are 33.2 million C|c) There are 3
78 165 1505 25 22|c)|c)
79 195 1525 118 28|of hectares?|of hectar";

    private static List<LayoutLine> Parse(string table) =>
        table.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(row =>
        {
            string[] halves = row.Split('|');
            double[] n = halves[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(double.Parse).ToArray();
            return new LayoutLine((int)n[0], n[1], n[2], n[3], n[4], halves[1], halves.Length > 2 ? halves[2] : null);
        }).ToList();

    [Test]
    public void ReadsLabelsAndRejectsDecimalsAndHandwrittenDigits()
    {
        Assert.That(HomeworkLayout.TryParseNumber("3.", out string n) && n == "3");
        Assert.That(HomeworkLayout.TryParseNumber("1.A square i", out n) && n == "1");
        Assert.That(HomeworkLayout.TryParseNumber("12) Find", out n) && n == "12");
        Assert.That(HomeworkLayout.TryParseNumber("3.48", out _), Is.False, "a decimal is not a label");
        Assert.That(HomeworkLayout.TryParseNumber("5.6=xx", out _), Is.False);
        Assert.That(HomeworkLayout.TryParseNumber("5.68com", out _), Is.False);
        Assert.That(HomeworkLayout.TryParseNumber("3 pens", out _), Is.False);
        Assert.That(HomeworkLayout.TryParsePart("a) Between 5.63", out string letter) && letter == "a");
        Assert.That(HomeworkLayout.TryParsePart("(c) About", out letter) && letter == "c");
        Assert.That(HomeworkLayout.TryParsePart("3x", out _), Is.False);
    }

    [Test]
    public void SplitsPageTwoIntoItsQuestionsAndParts()
    {
        var units = HomeworkLayout.Segment(Parse(PageTwo), 1200, 1600);
        Assert.That(units.Select(u => u.Label), Is.EqualTo(new[]
        {
            "1a", "1b", "1c", "1d", "1e", "1f", "2a", "2b", "2c", "2d", "3", "4a", "4b", "4c", "4d", "5", "6", "7", "8a", "8b", "8c"
        }));
    }

    [Test]
    public void AnAnswerInTheGapBetweenTwoQuestionsBelongsToTheOneAbove()
    {
        var units = HomeworkLayout.Segment(Parse(PageTwo), 1200, 1600).ToDictionary(u => u.Label);
        Assert.That(units["4d"].Lines, Does.Contain(58), "100.74 sits just above question 5 and is part d's answer");
        Assert.That(units["5"].Lines, Does.Not.Contain(58));
        Assert.That(units["5"].Lines, Does.Contain(62), "43.5 is under question 5's text");
        Assert.That(units["3"].Lines, Is.SupersetOf(new[] { 33, 34, 35, 36, 37 }), "3 pens for $2.88 belongs to question 3");
        Assert.That(units["6"].Lines, Does.Contain(65), "300 m = 5 hours");
        Assert.That(units["4c"].Lines, Does.Contain(54), "3x");
        Assert.That(units["7"].Lines, Is.EquivalentTo(new[] { 66, 67, 68, 69 }), "a blank answer: only the printed lines");
    }

    [Test]
    public void PartsSideBySideAreToldApartByTheirPosition()
    {
        var units = HomeworkLayout.Segment(Parse(PageTwo), 1200, 1600).ToDictionary(u => u.Label);
        Assert.That(units["1a"].Lines, Is.SupersetOf(new[] { 8, 6 }), "the boxes for 5.67 follow label a");
        Assert.That(units["1b"].Lines, Is.SupersetOf(new[] { 7, 5 }), "2.71 sits right of label b");
        Assert.That(units["2a"].Lines, Does.Contain(32), "3.48 is under a) 348");
        Assert.That(units["2b"].Lines, Does.Contain(30), "50.4");
        Assert.That(units["2c"].Lines, Does.Contain(31), "15.479");
        Assert.That(units["2d"].Lines, Does.Contain(29), "0.4");
        Assert.That(units["1f"].Lines, Is.SupersetOf(new[] { 20, 16, 17, 18 }), "5.99");
    }

    [Test]
    public void APartBesideAnotherGetsItsSlotOfTheRow()
    {
        var units = HomeworkLayout.Segment(Parse(PageTwo), 1200, 1600).ToDictionary(u => u.Label);
        Assert.That(units["1a"].SlotLeft, Is.EqualTo(141));
        Assert.That(units["1a"].SlotRight, Is.EqualTo(609), "up to label b");
        Assert.That(units["1b"].SlotLeft, Is.EqualTo(609));
        Assert.That(units["1b"].SlotRight, Is.GreaterThan(1100), "the rest of the row: the answer boxes the detector missed");
        Assert.That(units["2c"].SlotRight, Is.EqualTo(847), "up to label d");
        Assert.That(units["4a"].SlotLeft, Is.Null, "alone on its row");
        Assert.That(units["3"].SlotRight, Is.Null);
    }

    [Test]
    public void AUnitKnowsItsWholeQuestionForContext()
    {
        var units = HomeworkLayout.Segment(Parse(PageTwo), 1200, 1600).ToDictionary(u => u.Label);
        // Question 4's table spreads over its parts; each part still knows the stem and the whole table.
        Assert.That(units["4b"].ParentLines, Is.SupersetOf(new[] { 38, 39, 41, 43, 45, 47, 48, 57 }));
        Assert.That(units["4b"].Lines, Does.Not.Contain(45));
        Assert.That(units["3"].ParentLines, Is.EqualTo(units["3"].Lines));
    }

    [Test]
    public void PageOneSplitsTheTwoQuestionsAndLeavesTheHeaderAndFooter()
    {
        // The lines of the first photo (1200 x 1600): the number of question 1 is read to the left of its text, question 2's
        // is a box of its own; the handwriting is full of digits and equals signs that are not labels.
        var lines = Parse(@"
1 693 243 67 27|Date:
3 175 260 84 37|Name:
4 318 390 615 56|The Real Num|
5 188 519 220 31|Solve. Show your we|Solve.She
6 258 565 679 31|A square is enclosed|1.A square i
7 261 588 318 31|square correct to 3 si|square con
8 263 620 352 32|Hint: Diagonal of Squa|Hint: Diage
9 564 661 200 43|5.6=xx
10 656 708 59 26|5,6
11 572 735 58 31|x=
12 669 756 42 25|V2
13 918 773 82 37|31.36
14 864 807 47 37|A=
15 576 818 53 26|#=
16 935 830 26 26|2
17 645 871 143 37|5.68com
18 362 882 51 21|5.6 cm
19 283 1045 668 30|Given that the area o|2.Given that
20 215 1055 25 22|2.|2.
21 283 1069 533 30|places, find the lengt|places, fin
23 286 1112 433 30|Hint: For any integers|Hint: For a
26 636 1216 126 43|24.49
27 335 1219 240 48|600=105
28 739 1417 219 29|Enrichment 8A Chap");
        var units = HomeworkLayout.Segment(lines, 1200, 1600);
        Assert.That(units.Select(u => u.Label), Is.EqualTo(new[] { "1", "2" }));
        Assert.That(units[0].Lines, Is.SupersetOf(new[] { 6, 7, 8, 9, 13, 17, 18 }));
        Assert.That(units[0].Lines, Does.Not.Contain(4).And.Not.Contain(5), "the title and the instruction are the header");
        Assert.That(units[1].Lines, Is.EquivalentTo(new[] { 19, 20, 21, 23, 26, 27 }), "the footer is far below the last line and belongs to nothing");
    }

    [Test]
    public void ADottedNumberTheRecognizerLostCountsWhenItFitsBetweenItsNeighbours()
    {
        var units = HomeworkLayout.Segment(Parse(PageTwo), 1200, 1600).ToDictionary(u => u.Label);
        Assert.That(units.ContainsKey("6"), "6Jacob... lost its dot, between 5 and 7");
        Assert.That(units["6"].Lines, Is.SupersetOf(new[] { 63, 64, 65 }));
        // "3pens" is a handwritten 3 pens at the margin, not a question 3: 3 is already a question above it.
        Assert.That(units["3"].Lines, Does.Contain(35));
        Assert.That(HomeworkLayout.Segment(Parse(PageTwo), 1200, 1600).Count(u => u.Question == "3"), Is.EqualTo(1));
    }

    [Test]
    public void NoLabelsMeansNoUnits()
    {
        var lines = Parse(@"
1 300 100 600 40|A photo of a beach
2 320 200 500 40|with some waves");
        Assert.That(HomeworkLayout.Segment(lines, 1200, 1600), Is.Empty);
        Assert.That(HomeworkLayout.Segment(new List<LayoutLine>(), 1200, 1600), Is.Empty);
    }
}
