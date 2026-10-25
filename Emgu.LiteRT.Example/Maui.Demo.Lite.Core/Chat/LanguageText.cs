// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Text;

namespace Maui.Demo.Lite;

// Small text helpers of the thinking translation (TranslationFeature.cs). No MAUI types: linked into the tests.
internal static class LanguageText
{
    /// <summary>The text in pieces of at most this many tokens, cut at line ends (a longer line is a piece of its own).</summary>
    public static List<string> SplitForTranslation(string text, int maxTokens, Func<string, int> countTokens)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();
        foreach (string line in (text ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            if (current.Length > 0 && countTokens(current + "\n" + line) > maxTokens)
            {
                pieces.Add(current.ToString().Trim());
                current.Clear();
            }
            if (current.Length > 0)
                current.Append('\n');
            current.Append(line);
        }
        if (current.ToString().Trim().Length > 0)
            pieces.Add(current.ToString().Trim());
        return pieces;
    }
}
