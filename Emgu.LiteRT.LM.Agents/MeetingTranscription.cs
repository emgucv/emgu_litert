//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>What writes down a meeting's pieces.</summary>
    public enum MeetingTranscriber
    {
        /// <summary>The platform's speech recognizer, while recording.</summary>
        Recognizer,
        /// <summary>The chat's language model (e.g. Gemma 4, which takes audio), while recording.</summary>
        Model,
        /// <summary>The speech recognizer while recording, then the model again over every piece after Stop (a better transcript, later).</summary>
        RecognizerThenModel
    }

    /// <summary>
    /// Choosing what writes down a meeting, and the language model's part of it. Gemma 4 E2B wrote down a synthetic meeting
    /// with no mistakes clean and 2.5% of words wrong with room noise, against 0.6%/5.7% for Apple's recognizer and
    /// 3.2%/4.4% for Google's on a Pixel Fold; but it took 5-6x real time on an M1's GPU and only 0.3x on the Fold's (the
    /// recognizers ~20x and ~0.9x). So by default a desktop writes down with the model as it records, and a phone with its
    /// recognizer; a phone without one uses the model.
    /// </summary>
    public static class MeetingTranscription
    {
        /// <summary>The longest piece the model writes down, in seconds (Gemma's audio clips are at most 30 s).</summary>
        public const double ModelMaxSeconds = 28;

        /// <summary>The shortest piece cut at the quietest moment for the model, in seconds.</summary>
        public const double ModelMinSeconds = 15;

        /// <summary>
        /// How far the live transcript may fall behind, in seconds of waiting audio, before it skips ahead to the latest
        /// speech (the skipped pieces are written down after Stop). Google's recognizer runs at ~0.9x real time on a Pixel
        /// Fold and Gemma 4 E2B at ~0.3x, so on a phone the wait would otherwise keep growing.
        /// </summary>
        public const double MaxLiveDelaySeconds = 60;

        /// <summary>
        /// How many of the oldest waiting pieces to set aside so the rest wait at most <paramref name="maxDelay"/> seconds;
        /// the newest is always written down live. 0 when it isn't behind.
        /// </summary>
        /// <param name="waitingSeconds">The waiting pieces' lengths, oldest first</param>
        /// <param name="maxDelay">The longest wait, in seconds</param>
        public static int PiecesToSkip(IReadOnlyList<double> waitingSeconds, double maxDelay = MaxLiveDelaySeconds)
        {
            if (waitingSeconds == null || waitingSeconds.Count < 2) return 0;
            double waiting = waitingSeconds.Sum();
            int skip = 0;
            while (skip < waitingSeconds.Count - 1 && waiting > maxDelay)
                waiting -= waitingSeconds[skip++];
            return skip;
        }

        /// <summary>The settings key of the choice (absent: automatic).</summary>
        public const string SettingKey = "meeting_transcriber";

        /// <summary>
        /// What writes down a meeting: the user's choice when this device can do it, else the other; automatic (null) is the
        /// model on a desktop, the recognizer on a phone, and whichever there is. Null when neither can.
        /// </summary>
        /// <param name="choice">The user's choice, or null for automatic</param>
        /// <param name="hasRecognizer">Whether the platform's speech recognizer can write down recordings here</param>
        /// <param name="modelHearsAudio">Whether the chat's model takes audio</param>
        /// <param name="isDesktop">Mac or Windows (the model is fast enough to keep up there)</param>
        public static MeetingTranscriber? Choose(MeetingTranscriber? choice, bool hasRecognizer, bool modelHearsAudio, bool isDesktop)
        {
            if (!hasRecognizer && !modelHearsAudio) return null;
            if (!hasRecognizer) return MeetingTranscriber.Model;
            if (!modelHearsAudio) return MeetingTranscriber.Recognizer;
            return choice ?? (isDesktop ? MeetingTranscriber.Model : MeetingTranscriber.Recognizer);
        }

        /// <summary>The setting's stored value for a choice ("" for automatic).</summary>
        /// <param name="choice">The choice, or null</param>
        public static string ToSetting(MeetingTranscriber? choice) => choice?.ToString() ?? string.Empty;

        /// <summary>A stored setting as a choice (null - automatic - for anything else).</summary>
        /// <param name="setting">The stored value</param>
        public static MeetingTranscriber? FromSetting(string setting) =>
            Enum.TryParse(setting, out MeetingTranscriber value) && Enum.IsDefined(typeof(MeetingTranscriber), value) ? value : (MeetingTranscriber?)null;

        /// <summary>What the model is asked with each piece's audio.</summary>
        public const string Prompt = "Transcribe the speech in this recording exactly, word for word, in the language that is spoken. " +
            "Reply with only the words that are spoken. If nobody speaks, reply with nothing.";

        private static readonly Regex Nothing = new Regex(
            @"^\W*(?:no speech|silence|nothing|no audio|\(.*\)|\[.*\]|there is no speech.*|nobody speaks.*)\W*$", RegexOptions.IgnoreCase);

        // The model talking about the recording rather than writing it down ("I'm sorry, but I cannot transcribe the speech
        // in the recording. Please provide the audio." for a piece of room noise).
        private static readonly Regex AboutTheRecording = new Regex(
            @"\b(?:transcri\w*|audio|recording)\b.*\b(?:cannot|can't|can not|unable|not able|provide|don't have access|do not have access|no speech|doesn't contain|does not contain)\b" +
            @"|\b(?:cannot|can't|can not|unable|not able|no speech|doesn't contain|does not contain)\b.*\b(?:transcri\w*|audio|recording)\b",
            RegexOptions.IgnoreCase);

        // The prompt's own sentences echoed into a piece's words, mostly short or quiet ones through a phone's microphone
        // ("...see how many installs we can transcribe the speech in this recording exactly, word for word, in the language
        // that is spoken."), and "Sure, I can do that. Please provide the recording." - cut out, the words around them kept.
        private static readonly Regex Echo = new Regex(
            @"transcribe\W+the\W+speech\W+in\W+this\W+recording[^.!?]*[.!?]?" +
            @"|reply\W+with\W+only\W+the\W+words\W+that\W+are\W+spoken[.!?]?" +
            @"|if\W+nobody\W+speaks\W+reply\W+with\W+nothing[.!?]?" +
            @"|(?:sure\W+i\W+can\W+do\W+that[.!?]?\s*)?please\W+provide\W+the\W+(?:recording|audio)[^.!?]*[.!?]?",
            RegexOptions.IgnoreCase);

        private static readonly Regex Label =new Regex(@"^\s*(?:transcript(?:ion)?|here is the transcript(?:ion)?)\s*:\s*", RegexOptions.IgnoreCase);

        /// <summary>
        /// The model's answer as transcript text: without a "Transcript:" label or quotes around it, and empty when it says
        /// nothing was spoken ("(silence)", "[no speech]"...) or talks about the recording instead ("I cannot transcribe...").
        /// </summary>
        /// <param name="answer">The model's answer</param>
        public static string Clean(string answer)
        {
            string text = Regex.Replace(answer ?? string.Empty, @"\s+", " ").Trim();
            text = Label.Replace(text, string.Empty).Trim();
            if (text.Length >= 2 && (text[0] == '"' && text[text.Length - 1] == '"' || text[0] == '“' && text[text.Length - 1] == '”'))
                text = text.Substring(1, text.Length - 2).Trim();
            text = Regex.Replace(Echo.Replace(text, " "), @"\s+", " ").Trim();
            return Nothing.IsMatch(text) || AboutTheRecording.IsMatch(text) ? string.Empty : text;
        }
    }
}
