//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// Evaluates arithmetic exactly, for the calculate tool (AgentTools.Calculator), since small language models get
    /// arithmetic wrong: + - * / ^ (power) % (remainder), parentheses, unary minus, the constants pi and e, and the
    /// functions sqrt, cbrt, abs, exp, ln, log (base 10, or log(x, base)), log10, log2, sin/cos/tan/asin/acos/atan
    /// (radians), sinh/cosh/tanh, floor, ceil, round (round(x, digits)), min, max, pow, hypot. Also accepts × ÷ − and ·
    /// as models write them, ** for power, and a trailing % as percent ("15% of 80" is written 15% * 80).
    /// </summary>
    public static class Calculator
    {
        /// <summary>
        /// Evaluate an expression.
        /// </summary>
        /// <param name="expression">The expression, e.g. "(12.5 + 7) * 3 / 4"</param>
        /// <returns>The value</returns>
        /// <exception cref="FormatException">The expression can't be read</exception>
        /// <exception cref="ArithmeticException">The result is not a number</exception>
        public static double Evaluate(string expression)
        {
            Parser parser = new Parser(Normalize(expression));
            double value = parser.ParseExpression();
            parser.SkipSpaces();
            if (!parser.AtEnd)
                throw new FormatException("Unexpected '" + parser.Rest + "'");
            if (double.IsNaN(value))
                throw new ArithmeticException("The result is not a number");
            return value;
        }

        /// <summary>
        /// The value as text: up to 12 significant digits, without exponent for ordinary sizes.
        /// </summary>
        /// <param name="value">The value</param>
        /// <returns>The text</returns>
        public static string Format(double value)
        {
            if (double.IsInfinity(value))
                return value > 0 ? "infinity" : "-infinity";
            double rounded = double.Parse(value.ToString("G12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e15)
                return rounded.ToString("0", CultureInfo.InvariantCulture);
            return Math.Abs(rounded) >= 1e-6 && Math.Abs(rounded) < 1e15
                ? rounded.ToString("0.###########", CultureInfo.InvariantCulture)
                : rounded.ToString("G12", CultureInfo.InvariantCulture);
        }

        private static string Normalize(string expression)
        {
            return (expression ?? string.Empty)
                .Replace('×', '*').Replace('·', '*').Replace('÷', '/').Replace('−', '-').Replace("**", "^");
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s;
            }

            public bool AtEnd => _i >= _s.Length;
            public string Rest => _s.Substring(_i);

            public void SkipSpaces()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                    _i++;
            }

            private bool Accept(char c)
            {
                SkipSpaces();
                if (_i < _s.Length && _s[_i] == c)
                {
                    _i++;
                    return true;
                }
                return false;
            }

            // expression = term { ("+" | "-") term }
            public double ParseExpression()
            {
                double value = ParseTerm();
                while (true)
                {
                    if (Accept('+'))
                        value += ParseTerm();
                    else if (Accept('-'))
                        value -= ParseTerm();
                    else
                        return value;
                }
            }

            // term = unary { ("*" | "/" | "%") unary }
            private double ParseTerm()
            {
                double value = ParseUnary();
                while (true)
                {
                    if (Accept('*'))
                        value *= ParseUnary();
                    else if (Accept('/'))
                        value /= ParseUnary();
                    else if (PeekRemainder())
                    {
                        _i++;
                        value %= ParseUnary();
                    }
                    else
                        return value;
                }
            }

            // "a % b" is a remainder; a "%" not followed by an operand is a percent sign (handled in ParseUnary).
            private bool PeekRemainder()
            {
                SkipSpaces();
                if (_i >= _s.Length || _s[_i] != '%')
                    return false;
                int j = _i + 1;
                while (j < _s.Length && char.IsWhiteSpace(_s[j]))
                    j++;
                return j < _s.Length && (char.IsLetterOrDigit(_s[j]) || _s[j] == '(' || _s[j] == '.');
            }

            // unary = ("-" | "+") unary | power   - so -2 ^ 2 is -(2 ^ 2), as in mathematics
            private double ParseUnary()
            {
                if (Accept('-'))
                    return -ParseUnary();
                if (Accept('+'))
                    return ParseUnary();
                return ParsePower();
            }

            // power = postfix [ "^" unary ]   (right-associative; the exponent may be negative: 2 ^ -1)
            private double ParsePower()
            {
                double value = ParsePostfix();
                if (Accept('^'))
                    value = Math.Pow(value, ParseUnary());
                return value;
            }

            // postfix = primary [ "%" ]   (a percent sign)
            private double ParsePostfix()
            {
                double value = ParsePrimary();
                SkipSpaces();
                if (_i < _s.Length && _s[_i] == '%' && !PeekRemainder())
                {
                    _i++;
                    value /= 100.0;
                }
                return value;
            }

            private double ParsePrimary()
            {
                SkipSpaces();
                if (Accept('('))
                {
                    double value = ParseExpression();
                    if (!Accept(')'))
                        throw new FormatException("Missing ')'");
                    return value;
                }
                if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
                    return ParseNumber();
                if (_i < _s.Length && char.IsLetter(_s[_i]))
                    return ParseName();
                throw new FormatException(AtEnd ? "The expression ended early" : "Unexpected '" + Rest + "'");
            }

            private double ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == '_'))
                    _i++;
                // An exponent: 1e6, 2.5E-3
                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E') && _i + 1 < _s.Length
                    && (char.IsDigit(_s[_i + 1]) || ((_s[_i + 1] == '-' || _s[_i + 1] == '+') && _i + 2 < _s.Length && char.IsDigit(_s[_i + 2]))))
                {
                    _i += 2;
                    while (_i < _s.Length && char.IsDigit(_s[_i]))
                        _i++;
                }
                string text = _s.Substring(start, _i - start).Replace("_", string.Empty);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new FormatException("Not a number: " + text);
                return value;
            }

            private double ParseName()
            {
                int start = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
                    _i++;
                string name = _s.Substring(start, _i - start).ToLowerInvariant();
                switch (name)
                {
                    case "pi":
                    case "π":
                        return Math.PI;
                    case "e":
                        return Math.E;
                }
                if (!Accept('('))
                    throw new FormatException("Unknown name '" + name + "'");
                List<double> args = new List<double>();
                if (!Accept(')'))
                {
                    do
                        args.Add(ParseExpression());
                    while (Accept(','));
                    if (!Accept(')'))
                        throw new FormatException("Missing ')' after the arguments of " + name);
                }
                return Call(name, args);
            }

            private static double Call(string name, List<double> a)
            {
                double One()
                {
                    if (a.Count != 1)
                        throw new FormatException(name + " takes one argument");
                    return a[0];
                }
                double Two()
                {
                    if (a.Count != 2)
                        throw new FormatException(name + " takes two arguments");
                    return 0;
                }
                switch (name)
                {
                    case "sqrt": return Math.Sqrt(One());
                    case "cbrt": { double x = One(); return x < 0 ? -Math.Pow(-x, 1.0 / 3) : Math.Pow(x, 1.0 / 3); }
                    case "abs": return Math.Abs(One());
                    case "exp": return Math.Exp(One());
                    case "ln": return Math.Log(One());
                    case "log": return a.Count == 2 ? Math.Log(a[0], a[1]) : Math.Log10(One());
                    case "log10": return Math.Log10(One());
                    case "log2": return Math.Log(One(), 2);
                    case "sin": return Math.Sin(One());
                    case "cos": return Math.Cos(One());
                    case "tan": return Math.Tan(One());
                    case "asin": return Math.Asin(One());
                    case "acos": return Math.Acos(One());
                    case "atan": return Math.Atan(One());
                    case "sinh": return Math.Sinh(One());
                    case "cosh": return Math.Cosh(One());
                    case "tanh": return Math.Tanh(One());
                    case "floor": return Math.Floor(One());
                    case "ceil": return Math.Ceiling(One());
                    case "round": return a.Count == 2 ? Math.Round(a[0], (int)a[1], MidpointRounding.AwayFromZero) : Math.Round(One(), MidpointRounding.AwayFromZero);
                    case "min": if (a.Count == 0) throw new FormatException("min needs arguments"); return Min(a);
                    case "max": if (a.Count == 0) throw new FormatException("max needs arguments"); return Max(a);
                    case "pow": Two(); return Math.Pow(a[0], a[1]);
                    case "hypot": Two(); return Math.Sqrt(a[0] * a[0] + a[1] * a[1]);
                    default: throw new FormatException("Unknown function '" + name + "'");
                }
            }

            private static double Min(List<double> values)
            {
                double m = values[0];
                foreach (double v in values)
                    m = Math.Min(m, v);
                return m;
            }

            private static double Max(List<double> values)
            {
                double m = values[0];
                foreach (double v in values)
                    m = Math.Max(m, v);
                return m;
            }
        }
    }
}

