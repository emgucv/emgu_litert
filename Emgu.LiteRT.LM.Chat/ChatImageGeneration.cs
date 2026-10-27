//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// The image generation tool group of a chat (the generate_image tool, Emgu.LiteRT.LM.Agents' <c>AgentTools.GenerateImage</c>):
    /// its name, what it does for the stub tool, the words of a message that need it, and its instructions. A keyword
    /// also matches longer words it starts ("illustrat" - "illustrate", "illustration").
    /// </summary>
    public static class ChatImageGeneration
    {
        /// <summary>The group's name.</summary>
        public const string GroupName = "image_generation";

        /// <summary>What the group does, for the stub tool.</summary>
        public const string GroupSummary = "draw, paint or create a new picture from a description";

        /// <summary>The words of a message that need the group.</summary>
        public static readonly string[] GroupKeywords =
        {
            "draw", "paint", "sketch", "illustrat", "generate an image", "generate a picture", "create an image",
            "create a picture", "make an image", "make a picture", "imagine", "picture of a", "image of a", "artwork"
        };

        /// <summary>The instructions that come with the group.</summary>
        public const string Instructions = "When the user asks you to draw, paint or create a picture, use the generate_image tool with a short English " +
            "description; the picture is shown to the user, not to you.";
    }
}
