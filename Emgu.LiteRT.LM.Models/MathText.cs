//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Turns the LaTeX the models write for maths ($O(n)$, $x^2 + y^2 = r^2$, \frac{a}{b}, \sqrt{2}, \alpha, \le, ...)
    /// into plain Unicode text (O(n), x² + y² = r², a/b, √2, α, ≤) that a Label can show. It doesn't typeset:
    /// fractions are written a/b, and anything it doesn't know is shown without its backslash.
    /// </summary>
    public static class MathText
    {
        /// <summary>
        /// Convert LaTeX maths (without its $ or \( \) delimiters) to Unicode text.
        /// </summary>
        /// <param name="latex">The LaTeX, e.g. "x^2 + \frac{1}{2}"</param>
        /// <returns>The text, e.g. "x² + 1/2"; empty for null or empty input</returns>
        public static string ToUnicode(string latex)
        {
            if (string.IsNullOrEmpty(latex))
                return string.Empty;
            int position = 0;
            string text = Convert(latex, ref position, '\0');
            // LaTeX ignores spaces in maths; keep the ones that separate words, once.
            StringBuilder result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (result.Length > 0 && result[result.Length - 1] != ' ')
                        result.Append(' ');
                }
                else
                {
                    result.Append(c);
                }
            }
            // "\left( x \right)" leaves spaces inside the parentheses.
            return result.ToString().Trim().Replace("( ", "(").Replace(" )", ")").Replace("[ ", "[").Replace(" ]", "]");
        }

        // Convert until the end, or the closing character (a group's '}').
        private static string Convert(string s, ref int i, char close)
        {
            StringBuilder result = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == close)
                {
                    i++;
                    break;
                }
                switch (c)
                {
                    case '\\':
                        result.Append(Command(s, ref i));
                        break;
                    case '^':
                        i++;
                        result.Append(Script(ReadArgument(s, ref i), Superscripts, "^"));
                        break;
                    case '_':
                        i++;
                        result.Append(Script(ReadArgument(s, ref i), Subscripts, "_"));
                        break;
                    case '{':
                        i++;
                        result.Append(Convert(s, ref i, '}'));
                        break;
                    case '}':
                        i++;
                        break;
                    case '~':
                        result.Append(' ');
                        i++;
                        break;
                    default:
                        result.Append(c);
                        i++;
                        break;
                }
            }
            return result.ToString();
        }

        // An argument: a {group}, a \command, or a single character - converted.
        private static string ReadArgument(string s, ref int i)
        {
            while (i < s.Length && s[i] == ' ')
                i++;
            if (i >= s.Length)
                return string.Empty;
            if (s[i] == '{')
            {
                i++;
                return Convert(s, ref i, '}');
            }
            if (s[i] == '\\')
                return Command(s, ref i);
            return s[i++].ToString();
        }

        // A {group}'s text as it is (for \text{...}).
        private static string ReadRawArgument(string s, ref int i)
        {
            while (i < s.Length && s[i] == ' ')
                i++;
            if (i >= s.Length || s[i] != '{')
                return ReadArgument(s, ref i);
            int depth = 0;
            int start = i + 1;
            for (; i < s.Length; i++)
            {
                if (s[i] == '{')
                    depth++;
                else if (s[i] == '}' && --depth == 0)
                {
                    string raw = s.Substring(start, i - start);
                    i++;
                    return raw;
                }
            }
            return s.Substring(start);
        }

        private static string Command(string s, ref int i)
        {
            i++; // the backslash
            if (i >= s.Length)
                return "\\";
            if (!char.IsLetter(s[i]))
            {
                // \{ \} \% \$ \_ \& \# \| and spacing (\, \; \! \ )
                char symbol = s[i++];
                switch (symbol)
                {
                    case ',': case ';': case ':': case ' ': return " ";
                    case '!': return string.Empty;
                    case '|': return "‖";
                    case '\\': return " ";
                    default: return symbol.ToString();
                }
            }
            int start = i;
            while (i < s.Length && char.IsLetter(s[i]))
                i++;
            string name = s.Substring(start, i - start);
            switch (name)
            {
                case "frac":
                case "dfrac":
                case "tfrac":
                case "cfrac":
                    {
                        string numerator = ReadArgument(s, ref i);
                        string denominator = ReadArgument(s, ref i);
                        return Wrap(numerator) + "/" + Wrap(denominator);
                    }
                case "binom":
                    {
                        string n = ReadArgument(s, ref i);
                        string k = ReadArgument(s, ref i);
                        return "C(" + n + ", " + k + ")";
                    }
                case "sqrt":
                    {
                        string index = null;
                        if (i < s.Length && s[i] == '[')
                        {
                            int end = s.IndexOf(']', i);
                            if (end > i)
                            {
                                index = s.Substring(i + 1, end - i - 1);
                                i = end + 1;
                            }
                        }
                        string radicand = ReadArgument(s, ref i);
                        string root = index == "3" ? "∛" : index == "4" ? "∜" : (index != null ? Script(index, Superscripts, "^") : string.Empty) + "√";
                        return root + Wrap(radicand);
                    }
                case "text":
                case "textrm":
                case "textit":
                case "textbf":
                case "mathrm":
                case "mathit":
                case "mathbf":
                case "boldsymbol":
                case "operatorname":
                case "mbox":
                    return ReadRawArgument(s, ref i).Replace("\\", string.Empty);
                case "mathbb":
                    {
                        string letter = ReadRawArgument(s, ref i);
                        return Blackboard.TryGetValue(letter, out string bb) ? bb : letter;
                    }
                case "mathcal":
                    return ReadArgument(s, ref i);
                case "left":
                case "right":
                case "big":
                case "Big":
                case "bigg":
                case "Bigg":
                case "bigl":
                case "bigr":
                case "Bigl":
                case "Bigr":
                    // The delimiter that follows stays ("\left(" is "("); "\left." is none.
                    if (i < s.Length && s[i] == '.')
                        i++;
                    return string.Empty;
                case "quad":
                case "qquad":
                    return " ";
                case "overline":
                case "bar":
                    return CombineEach(ReadArgument(s, ref i), '̅');
                case "hat":
                case "widehat":
                    return CombineEach(ReadArgument(s, ref i), '̂');
                case "vec":
                    return CombineEach(ReadArgument(s, ref i), '⃗');
                case "dot":
                    return CombineEach(ReadArgument(s, ref i), '̇');
                case "tilde":
                    return CombineEach(ReadArgument(s, ref i), '̃');
                case "begin":
                case "end":
                    // Environments (matrices, cases, aligned): drop the markers, keep the content.
                    ReadRawArgument(s, ref i);
                    return " ";
            }
            if (Symbols.TryGetValue(name, out string symbolText))
                return symbolText;
            if (Limits.Contains(name) && i < s.Length && s[i] == '_')
            {
                // lim_{x \to 0} f(x): "lim x→0 f(x)" rather than a subscript most of it has no characters for.
                i++;
                return name + " " + ReadArgument(s, ref i).Replace(" ", string.Empty) + " ";
            }
            return name;
        }

        // A fraction's part: in parentheses when it is more than one term (has spaces or operators), so a/b reads
        // right: (a + b)/2, but √π/2 and 2a.
        private static string Wrap(string part)
        {
            part = part.Trim();
            return part.IndexOfAny(TermSeparators) >= 0 ? "(" + part + ")" : part;
        }

        private static readonly char[] TermSeparators = " +-±∓×·÷/=<>≤≥≠≈,".ToCharArray();

        // Superscript / subscript characters, or ^(...) / _(...) when one of them has none.
        private static string Script(string text, Dictionary<char, char> map, string marker)
        {
            text = text.Trim();
            if (text.Length == 0)
                return string.Empty;
            StringBuilder result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (map.TryGetValue(c, out char mapped))
                    result.Append(mapped);
                else if (c != ' ')
                    return marker + (text.Length == 1 ? text : "(" + text + ")");
            }
            return result.ToString();
        }

        private static string CombineEach(string text, char mark)
        {
            StringBuilder result = new StringBuilder();
            foreach (char c in text)
            {
                result.Append(c);
                if (char.IsLetterOrDigit(c))
                    result.Append(mark);
            }
            return result.ToString();
        }

        private static readonly Dictionary<char, char> Superscripts = Map(
            "0123456789+-=()niabcdefghjklmoprstuvwxyzT",
            "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼⁽⁾ⁿⁱᵃᵇᶜᵈᵉᶠᵍʰʲᵏˡᵐᵒᵖʳˢᵗᵘᵛʷˣʸᶻᵀ");

        private static readonly Dictionary<char, char> Subscripts = Map(
            "0123456789+-=()aehijklmnoprstuvx",
            "₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓ");

        private static Dictionary<char, char> Map(string from, string to)
        {
            Dictionary<char, char> map = new Dictionary<char, char>();
            for (int i = 0; i < from.Length; i++)
                map[from[i]] = to[i];
            return map;
        }

        private static readonly Dictionary<string, string> Blackboard = new Dictionary<string, string>
        {
            { "N", "ℕ" }, { "Z", "ℤ" }, { "Q", "ℚ" }, { "R", "ℝ" }, { "C", "ℂ" }, { "P", "ℙ" }
        };

        // Operators whose subscript is written below them (lim_{x \to 0}).
        private static readonly HashSet<string> Limits = new HashSet<string> { "lim", "max", "min", "sup", "inf", "argmax", "argmin" };

        private static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>
        {
            // Greek
            { "alpha", "α" }, { "beta", "β" }, { "gamma", "γ" }, { "delta", "δ" }, { "epsilon", "ε" }, { "varepsilon", "ε" },
            { "zeta", "ζ" }, { "eta", "η" }, { "theta", "θ" }, { "vartheta", "ϑ" }, { "iota", "ι" }, { "kappa", "κ" },
            { "lambda", "λ" }, { "mu", "μ" }, { "nu", "ν" }, { "xi", "ξ" }, { "pi", "π" }, { "varpi", "ϖ" }, { "rho", "ρ" },
            { "sigma", "σ" }, { "varsigma", "ς" }, { "tau", "τ" }, { "upsilon", "υ" }, { "phi", "φ" }, { "varphi", "φ" },
            { "chi", "χ" }, { "psi", "ψ" }, { "omega", "ω" },
            { "Gamma", "Γ" }, { "Delta", "Δ" }, { "Theta", "Θ" }, { "Lambda", "Λ" }, { "Xi", "Ξ" }, { "Pi", "Π" },
            { "Sigma", "Σ" }, { "Upsilon", "Υ" }, { "Phi", "Φ" }, { "Psi", "Ψ" }, { "Omega", "Ω" },
            // Operators and relations
            { "times", "×" }, { "cdot", "·" }, { "div", "÷" }, { "pm", "±" }, { "mp", "∓" }, { "ast", "∗" }, { "star", "⋆" },
            { "le", "≤" }, { "leq", "≤" }, { "ge", "≥" }, { "geq", "≥" }, { "ne", "≠" }, { "neq", "≠" }, { "approx", "≈" },
            { "equiv", "≡" }, { "sim", "∼" }, { "simeq", "≃" }, { "cong", "≅" }, { "propto", "∝" }, { "ll", "≪" }, { "gg", "≫" },
            { "infty", "∞" }, { "partial", "∂" }, { "nabla", "∇" }, { "sum", "∑" }, { "prod", "∏" }, { "int", "∫" },
            { "iint", "∬" }, { "oint", "∮" }, { "coprod", "∐" },
            // Arrows
            { "to", "→" }, { "rightarrow", "→" }, { "leftarrow", "←" }, { "gets", "←" }, { "Rightarrow", "⇒" },
            { "Leftarrow", "⇐" }, { "leftrightarrow", "↔" }, { "Leftrightarrow", "⇔" }, { "iff", "⇔" }, { "implies", "⇒" },
            { "mapsto", "↦" }, { "uparrow", "↑" }, { "downarrow", "↓" }, { "longrightarrow", "⟶" },
            // Sets and logic
            { "in", "∈" }, { "notin", "∉" }, { "ni", "∋" }, { "subset", "⊂" }, { "subseteq", "⊆" }, { "supset", "⊃" },
            { "supseteq", "⊇" }, { "cup", "∪" }, { "cap", "∩" }, { "setminus", "∖" }, { "emptyset", "∅" }, { "varnothing", "∅" },
            { "forall", "∀" }, { "exists", "∃" }, { "neg", "¬" }, { "lnot", "¬" }, { "land", "∧" }, { "wedge", "∧" },
            { "lor", "∨" }, { "vee", "∨" }, { "oplus", "⊕" }, { "otimes", "⊗" },
            // Misc
            { "cdots", "⋯" }, { "ldots", "…" }, { "dots", "…" }, { "vdots", "⋮" }, { "ddots", "⋱" }, { "circ", "∘" },
            { "degree", "°" }, { "angle", "∠" }, { "perp", "⊥" }, { "parallel", "∥" }, { "mid", "|" }, { "vert", "|" },
            { "Vert", "‖" }, { "lfloor", "⌊" }, { "rfloor", "⌋" }, { "lceil", "⌈" }, { "rceil", "⌉" }, { "langle", "⟨" },
            { "rangle", "⟩" }, { "backslash", "\\" }, { "prime", "′" }, { "hbar", "ℏ" }, { "ell", "ℓ" }, { "Re", "ℜ" },
            { "Im", "ℑ" }, { "aleph", "ℵ" }, { "therefore", "∴" }, { "because", "∵" }, { "checkmark", "✓" },
            { "lbrace", "{" }, { "rbrace", "}" }, { "displaystyle", "" }, { "textstyle", "" }, { "limits", "" },
            { "nolimits", "" }
        };
    }
}

