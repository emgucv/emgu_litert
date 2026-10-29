//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Agents.CV
{
    /// <summary>The photo-effects tool group of a chat (blur_photo_background): its name, summary, words and instructions.</summary>
    public static class ChatPhotoEffects
    {
        /// <summary>The group's name.</summary>
        public const string GroupName = "photo_effects";

        /// <summary>The group's summary, for the planner.</summary>
        public const string GroupSummary = "edit a photo the user attached: blur its background behind the person or object in front (portrait mode)";

        /// <summary>The words that bring the group.</summary>
        public static readonly string[] GroupKeywords =
        {
            "blur", "bokeh", "portrait mode", "portrait effect", "portrait look", "depth of field", "out of focus", "defocus"
        };

        /// <summary>The instructions that come with the group.</summary>
        public const string Instructions = "When the user asks to blur a photo's background or for a portrait-mode look, call blur_photo_background " +
            "(with the photo's reference when several are attached). The new photo is shown to the user, not to you.";

        /// <summary>The strengths blur_photo_background takes.</summary>
        public static readonly string[] Strengths = { "light", "medium", "strong" };

        /// <summary>The strength a model asked for, as one of <see cref="Strengths"/> ("medium" for anything else).</summary>
        /// <param name="strength">What the model passed ("Strong", "a little", "more"...)</param>
        public static string NormalizeStrength(string strength)
        {
            string s = (strength ?? string.Empty).Trim().ToLowerInvariant();
            if (s == "light" || s == "low" || s == "soft" || s == "subtle" || s == "slight" || s.Contains("little") || s.Contains("less"))
                return "light";
            if (s == "strong" || s == "high" || s == "heavy" || s == "max" || s == "maximum" || s.Contains("more") || s.Contains("very"))
                return "strong";
            return "medium";
        }

        /// <summary>
        /// The index (0-based) of the photo a reference names among <paramref name="count"/> attached photos: "image-2"
        /// or "2" is the second; empty, unknown or out of range is the last one.
        /// </summary>
        /// <param name="reference">The model's reference</param>
        /// <param name="count">How many photos there are (at least 1)</param>
        public static int ImageIndex(string reference, int count)
        {
            string digits = new string((reference ?? string.Empty).Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int n) && n >= 1 && n <= count ? n - 1 : count - 1;
        }
    }
}
