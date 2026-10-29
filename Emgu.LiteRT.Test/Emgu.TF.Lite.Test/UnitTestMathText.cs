// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// MathText: the LaTeX that models write for maths, as Unicode text. No model.
[TestFixture]
public class UnitTestMathText
{
    [Test]
    public void SuperscriptsAndSubscripts()
    {
        Assert.That(MathText.ToUnicode("x^2 + y^2 = r^2"), Is.EqualTo("x² + y² = r²"));
        Assert.That(MathText.ToUnicode("a_1 + a_{10}"), Is.EqualTo("a₁ + a₁₀"));
        Assert.That(MathText.ToUnicode("e^{-x}"), Is.EqualTo("e⁻ˣ"));
    }

    [Test]
    public void FractionsAndRoots()
    {
        Assert.That(MathText.ToUnicode(@"\frac{a}{b}"), Is.EqualTo("a/b"));
        Assert.That(MathText.ToUnicode(@"\frac{a+b}{2}"), Is.EqualTo("(a+b)/2"));
        Assert.That(MathText.ToUnicode(@"\frac{a + b}{2}"), Is.EqualTo("(a + b)/2"));
        Assert.That(MathText.ToUnicode(@"\sqrt{2}"), Is.EqualTo("√2"));
    }

    [Test]
    public void GreekLettersAndOperators()
    {
        Assert.That(MathText.ToUnicode(@"\alpha \le \beta"), Is.EqualTo("α ≤ β"));
        Assert.That(MathText.ToUnicode(@"\pi \approx 3.14"), Is.EqualTo("π ≈ 3.14"));
        Assert.That(MathText.ToUnicode(@"x \in \mathbb{R}"), Is.EqualTo("x ∈ ℝ"));
        Assert.That(MathText.ToUnicode(@"a \to b"), Is.EqualTo("a → b"));
    }

    [Test]
    public void TextAndUnknownCommands()
    {
        Assert.That(MathText.ToUnicode(@"O(n \log n)"), Is.EqualTo("O(n log n)"));
        Assert.That(MathText.ToUnicode(@"\text{area} = \pi r^2"), Is.EqualTo("area = π r²"));
        Assert.That(MathText.ToUnicode(@"\left( x \right)"), Is.EqualTo("(x)"));
        // A maths font's argument is maths (Gemma 4 12B's worksheet answer); \text's stays text.
        Assert.That(MathText.ToUnicode(@"\text{Volume} \approx \mathbf{1078.5 \text{ cm}^3}"), Is.EqualTo("Volume ≈ 1078.5 cm³"));
        Assert.That(MathText.ToUnicode(@"\mathrm{d}x + \operatorname{sin} x + \boldsymbol{v}"), Is.EqualTo("dx + sin x + v"));
        Assert.That(MathText.ToUnicode(@"\text{a^2 is a}"), Is.EqualTo("a^2 is a"));
        Assert.That(MathText.ToUnicode(""), Is.EqualTo(""));
        Assert.That(MathText.ToUnicode(null), Is.EqualTo(""));
    }
}
