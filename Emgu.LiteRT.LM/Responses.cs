//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// One candidate of a generation or scoring result.
    /// </summary>
    public class ResponseCandidate
    {
        /// <summary>
        /// The text, or null if the candidate has none
        /// </summary>
        public String Text { get; internal set; }

        /// <summary>
        /// The score, or null if the candidate has none
        /// </summary>
        public float? Score { get; internal set; }

        /// <summary>
        /// The number of tokens, or null if not stored
        /// </summary>
        public int? TokenLength { get; internal set; }

        /// <summary>
        /// The per-token scores, or null if not stored
        /// </summary>
        public float[] TokenScores { get; internal set; }

        /// <summary>
        /// Return the candidate text
        /// </summary>
        /// <returns>The candidate text</returns>
        public override String ToString()
        {
            return Text ?? String.Empty;
        }
    }

    /// <summary>
    /// The result of Session.GenerateContent, Session.RunDecode or Session.RunTextScoring (LiteRtLmResponses), read
    /// into managed memory.
    /// </summary>
    public class Responses
    {
        /// <summary>
        /// The candidates
        /// </summary>
        public ResponseCandidate[] Candidates { get; private set; }

        /// <summary>
        /// The text of the first candidate, or null if there is none
        /// </summary>
        public String Text
        {
            get { return Candidates.Length > 0 ? Candidates[0].Text : null; }
        }

        /// <summary>
        /// Return the text of the first candidate
        /// </summary>
        /// <returns>The text of the first candidate</returns>
        public override String ToString()
        {
            return Text ?? String.Empty;
        }

        /// <summary>
        /// Read a LiteRtLmResponses into a Responses, and delete the native responses.
        /// </summary>
        internal static Responses FromNative(IntPtr responses)
        {
            try
            {
                int count = LiteRtLmInvoke.litert_lm_responses_get_num_candidates(responses);
                ResponseCandidate[] candidates = new ResponseCandidate[count];
                for (int i = 0; i < count; i++)
                {
                    ResponseCandidate candidate = new ResponseCandidate();
                    candidate.Text = LiteRtLmInvoke.PtrToStringUtf8(LiteRtLmInvoke.litert_lm_responses_get_response_text_at(responses, i));
                    if (LiteRtLmInvoke.litert_lm_responses_has_score_at(responses, i))
                        candidate.Score = LiteRtLmInvoke.litert_lm_responses_get_score_at(responses, i);
                    if (LiteRtLmInvoke.litert_lm_responses_has_token_length_at(responses, i))
                        candidate.TokenLength = LiteRtLmInvoke.litert_lm_responses_get_token_length_at(responses, i);
                    if (LiteRtLmInvoke.litert_lm_responses_has_token_scores_at(responses, i))
                    {
                        int numScores = LiteRtLmInvoke.litert_lm_responses_get_num_token_scores_at(responses, i);
                        float[] scores = new float[numScores];
                        IntPtr scoresPtr = LiteRtLmInvoke.litert_lm_responses_get_token_scores_at(responses, i);
                        if (numScores > 0 && scoresPtr != IntPtr.Zero)
                            Marshal.Copy(scoresPtr, scores, 0, numScores);
                        candidate.TokenScores = scores;
                    }
                    candidates[i] = candidate;
                }
                return new Responses { Candidates = candidates };
            }
            finally
            {
                LiteRtLmInvoke.litert_lm_responses_delete(responses);
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_responses_delete(IntPtr responses);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_responses_get_num_candidates(IntPtr responses);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_responses_get_response_text_at(IntPtr responses, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_responses_has_score_at(IntPtr responses, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern float litert_lm_responses_get_score_at(IntPtr responses, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_responses_has_token_length_at(IntPtr responses, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_responses_get_token_length_at(IntPtr responses, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_responses_has_token_scores_at(IntPtr responses, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_responses_get_num_token_scores_at(IntPtr responses, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_responses_get_token_scores_at(IntPtr responses, int index);
    }
}
