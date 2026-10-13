//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// What the find_photos tool (AgentTools.FindPhotos) asks the app's photo search for; every part is optional.
    /// </summary>
    public class PhotoQuery
    {
        /// <summary>What the photos show, e.g. "a beach at sunset"; null to search only by the other parts</summary>
        public String Description { get; set; }

        /// <summary>Where they were taken: a town, region or country</summary>
        public String Place { get; set; }

        /// <summary>Taken on or after</summary>
        public DateTime? From { get; set; }

        /// <summary>Taken on or before</summary>
        public DateTime? To { get; set; }

        /// <summary>"screenshot", "selfie", "panorama", "portrait" or "favorite"</summary>
        public String Kind { get; set; }

        /// <summary>The camera or phone, e.g. "Pixel"</summary>
        public String Camera { get; set; }

        /// <summary>
        /// The query in words, e.g. "of \"beach\" from Spain, 2025-06-01 to 2025-08-31"
        /// </summary>
        public override String ToString()
        {
            System.Collections.Generic.List<String> parts = new System.Collections.Generic.List<String>();
            if (Description != null)
                parts.Add("of \"" + Description + "\"");
            if (Kind != null)
                parts.Add("(" + Kind + ")");
            if (Place != null)
                parts.Add("from " + Place);
            if (From != null || To != null)
                parts.Add(From != null && To != null ? From.Value.ToString("yyyy-MM-dd") + " to " + To.Value.ToString("yyyy-MM-dd")
                    : From != null ? "since " + From.Value.ToString("yyyy-MM-dd") : "until " + To.Value.ToString("yyyy-MM-dd"));
            if (Camera != null)
                parts.Add("taken with " + Camera);
            return String.Join(" ", parts);
        }
    }
}
