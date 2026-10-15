// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>Text preparation shared by local saved-chat indexes.</summary>
    public static class ChatSearch
    {
        /// <summary>Split message text into overlapping passages, preserving all text.</summary>
        /// <param name="text">The message (not tool steps, reasoning or attachment bytes).</param>
        /// <param name="length">Maximum characters in a passage.</param>
        /// <param name="overlap">Characters repeated between passages.</param>
        public static IEnumerable<string> Chunk(string text, int length = 900, int overlap = 150)
        {
            if (length < 1 || overlap < 0 || overlap >= length)
                throw new ArgumentOutOfRangeException(nameof(overlap));
            text = (text ?? string.Empty).Trim();
            for (int start = 0; start < text.Length; start += length - overlap)
            {
                int count = Math.Min(length, text.Length - start);
                yield return text.Substring(start, count);
                if (start + count == text.Length)
                    yield break;
            }
        }

        /// <summary>Extract dates using the photo search's date rules, preserving chat subjects and place names.</summary>
        /// <param name="request">The user's search.</param>
        /// <param name="now">Local time.</param>
        public static PhotoQuery Parse(string request, DateTime now)
        {
            var query = new PhotoQuery();
            query.Description = PhotoRequest.ParseDates(request ?? string.Empty, now, query).Trim();
            return query;
        }
    }
}
