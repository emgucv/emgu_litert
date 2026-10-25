// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
namespace Maui.Demo.Lite;

// The chat agent's tool groups (LiteRtLmChatPage.Tools.cs): name, what they do (for the stub tool) and the words of a
// message that need them. A keyword also matches longer words it starts ("remind" - "reminder"). Leaning towards
// including: a group a message didn't need costs some tokens, a missing one a stub call. Shared with UnitTestToolRouting.
internal static class ChatToolGroups
{
    internal const string Camera = "camera";
    internal const string CameraSummary = "use the device's live camera: look at or read something, or watch for something (a person, an animal, a car...) and beep, speak or take a picture when it appears";
    internal static readonly string[] CameraKeywords =
    {
        "camera", "you see", "can you see", "do you see", "look at", "looking at", "watch for", "keep an eye",
        "show me what", "what is this", "what's this", "what am i holding", "read this", "read the label",
        "snapshot", "take a picture", "take a photo", "take picture", "take photo", "monitor", "detect", "alert me",
        "let me know when", "let me know if", "tell me when", "tell me if", "notify me", "when someone",
        "when a person", "when a dog", "when a cat", "when a car", "if someone", "if a person",
        "appears", "shows up", "walks in", "comes in", "intruder", "visitor", "front door", "the door", "yolox", "mobilenet", "detector",
        "thumb", "gesture", "peace sign", "open palm", "fist",
        "wave", "waving", "raise", "raising", "raised", "arms up", "hands up", "stand up", "stands up", "sit down", "sits down", "lie down", "lies down", "lying down",
        "smile", "smiling", "peekaboo", "peek-a-boo", "cover my eyes", "covers her eyes", "covers his eyes", "covers their eyes", "covering", "hand over", "facepalm"
    };

    internal const string Sound = "sound_monitor";
    internal const string SoundSummary = "listen through the microphone and alert the user when a sound is HEARD (a doorbell, a dog barking, a baby crying, a smoke alarm, a knock...) - never for something seen, and not for merely playing a sound";
    internal static readonly string[] SoundKeywords =
    {
        "sound", "hear", "listen", "microphone", "noises", "loud noise", "a noise", "any noise", "strange noise", "that noise", "doorbell", "door bell", "knock", "ringing", "rings", "bark", "meow", "siren",
        "smoke alarm", "smoke detector", "baby cry", "baby crying", "cries", "crying", "cough", "snor", "glass break", "breaking glass", "honk"
    };

    internal const string DeviceActions = "clock_calendar_maps";
    internal const string DeviceActionsSummary = "set a timer or an alarm, add a calendar event or a reminder, or get directions";
    internal static readonly string[] DeviceActionsKeywords =
    {
        "timer", "alarm", "wake me", "wake up", "remind", "calendar", "schedule", "appointment", "meeting", "event",
        "book a", "put it in", "put lunch", "put dinner", "direction", "how do i get", "how to get", "get to",
        "walk to", "drive to", "take me to", "navigate", "route", "map", "minutes for", "countdown"
    };

    internal const string WriteDocument = "write_document";
    // Recording a meeting and writing its notes (record_meeting) comes with the documents: its notes are one, and a group of its own
    // would add a stub to every message.
    internal const string WriteDocumentSummary = "write a document file (Word, PDF or Markdown) for the user to open or share, or record a meeting and write its notes";
    internal static readonly string[] WriteDocumentKeywords =
    {
        "document", "word file", "word doc", "a doc", "docx", "pdf", "markdown", "write up", "write it up", "write that up",
        "write this up", "as a file", "into a file", "in a file", "save it as", "save that as", "save this as", "export",
        "printable", "print out", "handout", "report", "letter", "essay", "cv", "resume", "itinerary",
        "meeting", "the minutes", "transcri", "take notes", "taking notes", "note taking", "notes of", "notes for", "notes from",
        "record my", "record the", "record this", "record our", "record a", "recording", "lecture", "class notes", "interview",
        "dictat", "write down what"
    };

    internal const string Goals = "goals";
    internal const string GoalsSummary = "save the user's goals (habits, targets), log or check their progress, and count jumping jacks, squats or high knees with the camera";
    internal static readonly string[] GoalsKeywords =
    {
        "goal", "habit", "target", "streak", "progress", "i want to", "i'd like to", "every day", "a day$", "a week$",
        "per week$", "per day$", "each week$", "times a week", "how am i doing", "on track", "keep track", "track my",
        "jumping jack", "jumping-jack", "star jump", "squat", "high knee", "knee raise", "exercise", "workout", "work out", "reps", "count my"
    };

    internal const string Photos = "photo_search";
    internal const string PhotosSummary = "find and show the user's own photos and pictures on this device";
    internal static readonly string[] PhotosKeywords =
    {
        "photo", "picture", "pic", "selfie", "screenshot", "gallery", "album", "camera roll", "images of",
        "image of my", "my images"
    };

    internal const string Documents = "documents";
    internal const string DocumentsSummary = "search the documents (PDF, Word, text files) the user attached to earlier chats";
    internal static readonly string[] DocumentsKeywords =
    {
        "document", "file", "pdf", "report", "contract", "paper", "manual", "my notes", "attached", "the doc"
    };

    internal const string PastChats = "past_chats";
    internal const string PastChatsSummary = "search the user's earlier conversations on this device";
    internal static readonly string[] PastChatsKeywords =
    {
        "earlier", "previous", "last time", "we talked", "we discussed", "you said", "you told", "i said",
        "i told you", "i asked", "remember when", "conversation", "chat", "mentioned", "recall"
    };

    internal const string ImageGeneration = "image_generation";
    internal const string ImageGenerationSummary = "draw, paint or create a new picture from a description";
    internal static readonly string[] ImageGenerationKeywords =
    {
        "draw", "paint", "sketch", "illustrat", "generate an image", "generate a picture", "create an image",
        "create a picture", "make an image", "make a picture", "imagine", "picture of a", "image of a", "artwork"
    };
}
