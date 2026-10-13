//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Qwen3's byte-level BPE (vocab.json and merges.txt of Tongyi-MAI/Z-Image-Turbo's tokenizer) with Z-Image's chat
    /// template, for ZImageTurbo. Ported from QwenTokenizer.kt of https://github.com/alexeyw/android-image-generator
    /// (Apache-2.0), itself adapted from the Bonsai Image Android sample of https://github.com/john-rocky/hf-to-litertlm
    /// (Copyright 2026 Daisuke Majima, Apache-2.0). Changed: every merges.txt line but the "#version" header is a merge
    /// (merges such as "# #" also start with '#'), and .NET's Unicode \s is used, as in Hugging Face's tokenizers.
    /// </summary>
    internal sealed class QwenTokenizer
    {
        public const int PadId = 151643; // <|endoftext|>
        public const int ImStartId = 151644; // <|im_start|>

        // <|im_end|>\n<|im_start|>assistant\n
        private static readonly int[] SuffixIds = { 151645, 198, 151644, 77091, 198 };

        /// <summary>
        /// The tokens the template itself takes: &lt;|im_start|&gt;, "user", "\n" and the suffix.
        /// </summary>
        public static readonly int TemplateTokens = 1 + 2 + SuffixIds.Length;

        // Qwen2's pre-tokenization pattern, from tokenizer.json.
        private static readonly Regex PreToken = new Regex(
            @"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+",
            RegexOptions.CultureInvariant);

        // GPT-2's bytes-to-unicode: every byte maps to a distinct printable character.
        private static readonly char[] ByteChar = CreateByteChars();

        private readonly Dictionary<String, int> _vocab;
        private readonly Dictionary<String, int> _ranks = new Dictionary<String, int>(160000);
        private readonly Dictionary<String, int[]> _cache = new Dictionary<String, int[]>();

        public QwenTokenizer(String vocabJsonFile, String mergesFile)
        {
            using (FileStream stream = File.OpenRead(vocabJsonFile))
            using (JsonDocument doc = JsonDocument.Parse(stream))
            {
                _vocab = new Dictionary<String, int>(160000);
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                    _vocab[property.Name] = property.Value.GetInt32();
            }
            bool first = true;
            int rank = 0;
            foreach (String line in File.ReadLines(mergesFile))
            {
                bool header = first && line.StartsWith("#version", StringComparison.Ordinal);
                first = false;
                if (header || line.Length == 0)
                    continue;
                // A merge's rank is its position; the first of duplicates wins.
                if (!_ranks.ContainsKey(line))
                    _ranks[line] = rank;
                rank++;
            }
        }

        private static char[] CreateByteChars()
        {
            List<int> bs = new List<int>();
            for (int b = 33; b <= 126; b++) bs.Add(b);
            for (int b = 161; b <= 172; b++) bs.Add(b);
            for (int b = 174; b <= 255; b++) bs.Add(b);
            List<int> cs = new List<int>(bs);
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                if (!bs.Contains(b))
                {
                    bs.Add(b);
                    cs.Add(256 + n);
                    n++;
                }
            }
            char[] table = new char[256];
            for (int i = 0; i < bs.Count; i++)
                table[bs[i]] = (char)cs[i];
            return table;
        }

        /// <summary>
        /// The whole token sequence of a prompt, chat template included (not padded).
        /// </summary>
        public int[] EncodePrompt(String prompt)
        {
            List<int> ids = new List<int>();
            ids.Add(ImStartId);
            ids.AddRange(Encode("user\n" + prompt)); // "user\n" with the prompt: whitespace merges across them
            ids.AddRange(SuffixIds);
            return ids.ToArray();
        }

        /// <summary>
        /// Byte-level BPE of plain text (special-token strings are plain text too), NFC-normalized like tokenizer.json.
        /// </summary>
        public int[] Encode(String text)
        {
            String nfc = text.Normalize(NormalizationForm.FormC);
            List<int> ids = new List<int>(nfc.Length / 3 + 8);
            foreach (Match match in PreToken.Matches(nfc))
            {
                if (match.Value.Length > 0)
                    ids.AddRange(Bpe(match.Value));
            }
            return ids.ToArray();
        }

        private int[] Bpe(String preToken)
        {
            lock (_cache)
            {
                int[] cached;
                if (_cache.TryGetValue(preToken, out cached))
                    return cached;
                byte[] bytes = Encoding.UTF8.GetBytes(preToken);
                List<String> word = new List<String>(bytes.Length);
                foreach (byte b in bytes)
                    word.Add(ByteChar[b].ToString());
                while (word.Count > 1)
                {
                    int best = int.MaxValue;
                    int at = -1;
                    for (int i = 0; i < word.Count - 1; i++)
                    {
                        int rank;
                        if (_ranks.TryGetValue(word[i] + " " + word[i + 1], out rank) && rank < best)
                        {
                            best = rank;
                            at = i;
                        }
                    }
                    if (at < 0)
                        break;
                    String a = word[at];
                    String c = word[at + 1];
                    List<String> merged = new List<String>(word.Count);
                    for (int i = 0; i < word.Count; i++)
                    {
                        if (i < word.Count - 1 && word[i] == a && word[i + 1] == c)
                        {
                            merged.Add(a + c);
                            i++;
                        }
                        else
                        {
                            merged.Add(word[i]);
                        }
                    }
                    word = merged;
                }
                // The byte-level alphabet: every symbol and every merge result is in the vocabulary.
                List<int> ids = new List<int>(word.Count);
                foreach (String symbol in word)
                {
                    int id;
                    if (_vocab.TryGetValue(symbol, out id))
                        ids.Add(id);
                }
                int[] result = ids.ToArray();
                _cache[preToken] = result;
                return result;
            }
        }

        /// <summary>
        /// Remove the characters that are invisible in a prompt but still take tokens of the 32-token caption: zero width
        /// spaces, word joiners, byte order marks and soft hyphens always; zero width (non-)joiners at word edges (between
        /// two visible characters they glue emoji sequences and shape Persian and Indic words); no-break and other special
        /// spaces become spaces. Text copied from web pages and messengers carries them routinely. From PromptText.kt of
        /// the same project.
        /// </summary>
        public static String CleanPrompt(String raw)
        {
            if (raw == null)
                return String.Empty;
            StringBuilder s = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (c != '​' && c != '⁠' && c != '﻿' && c != '­')
                    s.Append(c);
            }
            StringBuilder result = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == ' ' || c == ' ')
                {
                    result.Append(' ');
                }
                else if (c == '‍' || c == '‌')
                {
                    if (i > 0 && i < s.Length - 1 && !Char.IsWhiteSpace(s[i - 1]) && !Char.IsWhiteSpace(s[i + 1]))
                        result.Append(c);
                }
                else
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }
    }
}
