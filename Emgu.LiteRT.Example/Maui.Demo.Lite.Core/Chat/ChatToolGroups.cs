// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
namespace Maui.Demo.Lite;

// The chat agent's tool groups (LiteRtLmChatPage.Tools.cs): name, what they do (for the stub tool) and the words of a
// message that need them. A keyword also matches longer words it starts ("remind" - "reminder"). Leaning towards
// including: a group a message didn't need costs some tokens, a missing one a stub call. Shared with UnitTestToolRouting.
internal static class ChatToolGroups
{
    // The device actions group is the library's too (Emgu.LiteRT.LM.Chat's ChatDeviceActions).

    // The goals group is the library's (Emgu.LiteRT.LM.Agents' GoalsSkill, with the exercise counter's tools and words as its
    // extras); the name is kept for the page's token estimate.
    internal const string Goals = Emgu.LiteRT.LM.Agents.Skills.GoalsSkill.GroupName;

    internal const string ImageGeneration = "image_generation";
    internal const string ImageGenerationSummary = "draw, paint or create a new picture from a description";
    internal static readonly string[] ImageGenerationKeywords =
    {
        "draw", "paint", "sketch", "illustrat", "generate an image", "generate a picture", "create an image",
        "create a picture", "make an image", "make a picture", "imagine", "picture of a", "image of a", "artwork"
    };
}
