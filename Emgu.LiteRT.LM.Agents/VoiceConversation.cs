//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Emgu.LiteRT.LM.Agents.Audio
{
    /// <summary>
    /// What <see cref="VoiceActivityDetector.Process"/> found in a stretch of microphone input.
    /// </summary>
    public enum VoiceActivity
    {
        None,
        /// <summary>Speech began (it has lasted <see cref="VoiceActivityDetector.MinSpeech"/>).</summary>
        SpeechStarted,
        /// <summary>Speech ended: <see cref="VoiceActivityDetector.EndSilence"/> of quiet, or the length limit.</summary>
        SpeechEnded
    }

    /// <summary>
    /// Finds where speech starts and ends from the microphone's level (dBFS), measured every few tens of milliseconds:
    /// the level the platform's recorder reports (AVAudioRecorder's average power) or the RMS of the 16 kHz samples
    /// (Android). Speech is a level well above the background noise, which is tracked while nobody speaks - it follows
    /// a quieter level at once and a louder one slowly, so speech doesn't become the new background. No MAUI types, so
    /// the unit tests use it too.
    /// </summary>
    public sealed class VoiceActivityDetector
    {
        /// <summary>How far above the background a level must be to count as speech starting.</summary>
        public double StartMarginDb { get; set; } = 12;

        /// <summary>How far above the background a level must stay for speech to continue.</summary>
        public double ContinueMarginDb { get; set; } = 9;

        /// <summary>Quieter than this is never speech, however quiet the room.</summary>
        public double MinSpeechLevelDb { get; set; } = -50;

        /// <summary>How long speech must last before it counts (a cough or a click doesn't).</summary>
        public TimeSpan MinSpeech { get; set; } = TimeSpan.FromSeconds(0.2);

        /// <summary>The pause that ends an utterance.</summary>
        public TimeSpan EndSilence { get; set; } = TimeSpan.FromSeconds(0.9);

        /// <summary>The longest utterance; speech is ended there.</summary>
        public TimeSpan MaxUtterance { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Quieter than this is not a real microphone's background but digital silence - e.g. the first moments of a
        /// recording - so it doesn't change the background (lowering it would leave only <see cref="MinSpeechLevelDb"/>
        /// to tell speech from quiet).
        /// </summary>
        public double MinNoiseFloorDb { get; set; } = -90;

        /// <summary>The background level (dBFS); NaN until the first level.</summary>
        public double NoiseFloorDb { get; private set; } = double.NaN;

        public bool InSpeech { get; private set; }

        /// <summary>Speech, or loud input that may become speech.</summary>
        public bool Hearing => InSpeech || _candidate > TimeSpan.Zero;

        /// <summary>How long the current utterance has lasted, from the start of its speech.</summary>
        public TimeSpan UtteranceLength { get; private set; }

        private TimeSpan _candidate;   // loud input not yet long enough to be speech
        private TimeSpan _candidateGap; // a dip inside it
        private TimeSpan _silence;      // quiet since the last speech, within an utterance
        private bool _lastLoud;         // the previous frame was loud, within an utterance

        /// <summary>
        /// Start again, e.g. after the reply was read out (the background is kept).
        /// </summary>
        public void Reset()
        {
            InSpeech = false;
            UtteranceLength = TimeSpan.Zero;
            _candidate = _candidateGap = _silence = TimeSpan.Zero;
            _lastLoud = false;
        }

        /// <summary>
        /// Take the level of the latest stretch of input.
        /// </summary>
        /// <param name="levelDb">Its level in dBFS (0 is full scale; silence is very negative)</param>
        /// <param name="duration">How long the stretch was</param>
        public VoiceActivity Process(double levelDb, TimeSpan duration)
        {
            if (double.IsNaN(levelDb) || double.IsInfinity(levelDb))
                levelDb = -160;
            levelDb = Math.Max(-160, levelDb);
            if (double.IsNaN(NoiseFloorDb))
            {
                if (levelDb < MinNoiseFloorDb)
                    return VoiceActivity.None;
                NoiseFloorDb = levelDb;
            }

            if (InSpeech)
            {
                UtteranceLength += duration;
                // A pause ends with sound lasting two frames: one loud frame (a breath, a click) leaves it running.
                bool loud = levelDb >= Math.Max(NoiseFloorDb + ContinueMarginDb, MinSpeechLevelDb);
                if (!loud)
                    _silence += duration;
                else if (_lastLoud)
                    _silence = TimeSpan.Zero;
                _lastLoud = loud;
                if (_silence >= EndSilence || UtteranceLength >= MaxUtterance)
                {
                    Reset();
                    return VoiceActivity.SpeechEnded;
                }
                return VoiceActivity.None;
            }

            if (levelDb >= Math.Max(NoiseFloorDb + StartMarginDb, MinSpeechLevelDb))
            {
                _candidate += duration;
                _candidateGap = TimeSpan.Zero;
                if (_candidate >= MinSpeech)
                {
                    InSpeech = true;
                    UtteranceLength = _candidate;
                    _candidate = TimeSpan.Zero;
                    _silence = TimeSpan.Zero;
                    _lastLoud = true;
                    return VoiceActivity.SpeechStarted;
                }
                return VoiceActivity.None;
            }

            // Not speech: a short dip doesn't end a candidate, a longer one does.
            if (_candidate > TimeSpan.Zero)
            {
                _candidateGap += duration;
                if (_candidateGap > TimeSpan.FromSeconds(0.15))
                    _candidate = _candidateGap = TimeSpan.Zero;
            }
            // Follow the background: down at once, up slowly (about 3 dB a second), so a steady noise such as a fan
            // becomes the background within seconds, but speech doesn't. Below MinSpeechLevelDb nothing is speech, so
            // there it rises quickly (20 dB a second), and a level more than StartMarginDb above the background - which
            // only gets here by being below MinSpeechLevelDb - pulls it up at once to StartMarginDb below: a background
            // left too low would leave only MinSpeechLevelDb to tell speech from a noisy room, whose peaks then keep an
            // utterance from ending. Digital silence (below MinNoiseFloorDb) leaves it as it is.
            if (levelDb < MinNoiseFloorDb)
                return VoiceActivity.None;
            if (levelDb < NoiseFloorDb)
                NoiseFloorDb = levelDb;
            else
            {
                double rate = levelDb < MinSpeechLevelDb ? 20 : 3;
                NoiseFloorDb = Math.Min(levelDb, Math.Max(NoiseFloorDb + rate * duration.TotalSeconds, levelDb - StartMarginDb));
            }
            return VoiceActivity.None;
        }

        /// <summary>
        /// The level (dBFS) of 16-bit samples: the RMS relative to full scale.
        /// </summary>
        public static double LevelDb(double sumOfSquares, int count)
        {
            if (count <= 0)
                return -160;
            double rms = Math.Sqrt(sumOfSquares / count) / 32768.0;
            return rms <= 0 ? -160 : Math.Max(-160, 20 * Math.Log10(rms));
        }
    }

    /// <summary>
    /// The microphone, as a voice conversation uses it: <see cref="VoiceRecorder"/>, or a recording played in its place
    /// (the demo's self-tests).
    /// </summary>
    public interface IVoiceInput
    {
        bool IsRecording { get; }

        /// <summary>Start recording; false if microphone access was denied.</summary>
        System.Threading.Tasks.Task<bool> StartAsync();

        /// <summary>The level (dBFS) since the last call.</summary>
        double ReadLevelDb();

        /// <summary>Stop recording and return it as a 16 kHz mono WAV file.</summary>
        System.Threading.Tasks.Task<byte[]> StopAsync();
    }

    /// <summary>
    /// Cuts a reply into pieces to read aloud while it is still being written: whole sentences, and the ends of lines
    /// (list items, headings). Code blocks and table rows are skipped - read aloud they are noise. Give it the reply's
    /// text so far each time it grows (<see cref="Add"/>) and the whole reply at the end (<see cref="Finish"/>); each call
    /// returns the pieces completed since the last. The pieces keep their Markdown, which
    /// <see cref="ReplySpeech.CleanForSpeech"/> removes. No MAUI types, so the unit tests use it too.
    /// </summary>
    public sealed class SpeechSegmenter
    {
        private int _lineStart;   // where the line being read starts
        private int _spoken;      // how much of that line has been returned
        private bool _inCode;     // inside a ``` block

        public List<string> Add(string text) => Read(text ?? string.Empty, false);

        public List<string> Finish(string text) => Read(text ?? string.Empty, true);

        private List<string> Read(string text, bool final)
        {
            List<string> pieces = new List<string>();
            // The text is expected to grow at its end; anything else (it was replaced) starts again.
            if (_lineStart + _spoken > text.Length)
                _lineStart = _spoken = 0;
            while (_lineStart < text.Length)
            {
                int newline = text.IndexOf('\n', _lineStart);
                bool complete = newline >= 0 || final;
                int end = newline >= 0 ? newline : text.Length;
                string line = text.Substring(_lineStart, end - _lineStart);
                string trimmed = line.TrimStart();

                // A line's kind is known from its start; wait for a few characters of a new line.
                if (!complete && _spoken == 0 && trimmed.Length < 3 && (trimmed.Length == 0 || trimmed[0] == '`' || trimmed[0] == '|'))
                    break;
                if (_spoken == 0 && trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    if (!complete)
                        break;
                    _inCode = !_inCode;
                }
                else if (_inCode || (_spoken == 0 && trimmed.StartsWith("|", StringComparison.Ordinal)))
                {
                    if (!complete)
                        break;
                }
                else
                {
                    int from = _spoken;
                    for (int i = _spoken; i < line.Length; i++)
                    {
                        if (!IsSentenceEnd(line, i, from))
                            continue;
                        Emit(pieces, line.Substring(from, i + 1 - from));
                        from = i + 1;
                    }
                    _spoken = from;
                    if (!complete)
                        break;
                    Emit(pieces, line.Substring(_spoken));
                }
                _lineStart = end + 1;
                _spoken = 0;
            }
            return pieces;
        }

        // A sentence ends at . ! ? (or their full-width forms, which need no space after them) followed by a space - not
        // inside 3.14 or a list number such as "1." at the start of a line.
        private static bool IsSentenceEnd(string line, int i, int from)
        {
            char c = line[i];
            if (c == '。' || c == '！' || c == '？')
                return true;
            if (c != '.' && c != '!' && c != '?')
                return false;
            if (i + 1 >= line.Length || !char.IsWhiteSpace(line[i + 1]))
                return false;
            if (c == '.')
            {
                string before = line.Substring(from, i - from).Trim();
                bool number = before.Length > 0;
                foreach (char d in before)
                    number &= char.IsDigit(d);
                if (number)
                    return false;
            }
            return true;
        }

        private static void Emit(List<string> pieces, string piece)
        {
            if (!string.IsNullOrWhiteSpace(piece))
                pieces.Add(piece.Trim());
        }
    }
}
