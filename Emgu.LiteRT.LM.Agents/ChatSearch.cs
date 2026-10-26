// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>The past-chat search's reading of a request (the passages themselves are Emgu.LiteRT.LM.Chat's ChatSearchIndex).</summary>
    public static class ChatSearch
    {
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
