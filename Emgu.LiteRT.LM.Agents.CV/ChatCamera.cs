//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Agents.CV
{
    /// <summary>
    /// The camera and sound-monitor tool groups of a chat: their names, summaries and words, and the camera tools'
    /// instructions.
    /// </summary>
    public static class ChatCamera
    {
        /// <summary>The camera tool group's name.</summary>
        public const string GroupName = "camera";

        /// <summary>The camera tool group's summary, for the planner.</summary>
        public const string GroupSummary = "use the device's live camera: look at or read something, or watch for something (a person, an animal, a car...) and beep, speak or take a picture when it appears";

        /// <summary>The words that bring the camera tool group.</summary>
        public static readonly string[] GroupKeywords =
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

        /// <summary>The instructions that go with the live camera tools (open_camera, capture_camera_frame).</summary>
        public const string Instructions = "Use open_camera when asked to open the live camera. " +
            "When asked what you see in the camera, to read a label through the camera, or inspect the current camera view, " +
            "call capture_camera_frame. It opens the preview if needed and returns a new image. Examine that image and answer " +
            "the user's question. Never describe the camera from memory or claim to see it without a successful capture. " +
            "Capture once per camera question, then answer rather than calling the tool again. " +
            "An image is one captured moment; you are not continuously watching the preview. Do not capture without a user request. " +
            "The user can tap Close on the preview to stop the camera.";

        /// <summary>The sound-monitor tool group's name.</summary>
        public const string SoundGroupName = "sound_monitor";

        /// <summary>The sound-monitor tool group's summary, for the planner.</summary>
        public const string SoundGroupSummary = "listen through the microphone and alert the user when a sound is HEARD (a doorbell, a dog barking, a baby crying, a smoke alarm, a knock...) - never for something seen, and not for merely playing a sound";

        /// <summary>The words that bring the sound-monitor tool group.</summary>
        public static readonly string[] SoundGroupKeywords =
        {
            "sound", "hear", "listen", "microphone", "noises", "loud noise", "a noise", "any noise", "strange noise", "that noise", "doorbell", "door bell", "knock", "ringing", "rings", "bark", "meow", "siren",
            "smoke alarm", "smoke detector", "baby cry", "baby crying", "cries", "crying", "cough", "snor", "glass break", "breaking glass", "honk"
        };
    }
}
