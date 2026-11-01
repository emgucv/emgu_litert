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
                UIntPtr numCandidates;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_responses_get_num_candidates(responses, out numCandidates), "litert_lm_responses_get_num_candidates");
                int count = checked((int)numCandidates.ToUInt64());
                ResponseCandidate[] candidates = new ResponseCandidate[count];
                for (int i = 0; i < count; i++)
                {
                    UIntPtr index = new UIntPtr((uint)i);
                    ResponseCandidate candidate = new ResponseCandidate();
                    IntPtr text;
                    if (LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_responses_get_response_text_at(responses, index, out text), "litert_lm_responses_get_response_text_at"))
                        candidate.Text = LiteRtLmInvoke.PtrToStringUtf8(text);
                    bool has;
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_responses_has_score_at(responses, index, out has), "litert_lm_responses_has_score_at");
                    if (has)
                    {
                        float score;
                        LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_responses_get_score_at(responses, index, out score), "litert_lm_responses_get_score_at");
                        candidate.Score = score;
                    }
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_responses_has_token_length_at(responses, index, out has), "litert_lm_responses_has_token_length_at");
                    if (has)
                    {
                        UIntPtr length;
                        LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_responses_get_token_length_at(responses, index, out length), "litert_lm_responses_get_token_length_at");
                        candidate.TokenLength = checked((int)length.ToUInt64());
                    }
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_responses_has_token_scores_at(responses, index, out has), "litert_lm_responses_has_token_scores_at");
                    if (has)
                    {
                        UIntPtr numScores;
                        LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_responses_get_num_token_scores_at(responses, index, out numScores), "litert_lm_responses_get_num_token_scores_at");
                        float[] scores = new float[checked((int)numScores.ToUInt64())];
                        IntPtr scoresPtr;
                        LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_responses_get_token_scores_at(responses, index, out scoresPtr), "litert_lm_responses_get_token_scores_at");
                        if (scores.Length > 0 && scoresPtr != IntPtr.Zero)
                            Marshal.Copy(scoresPtr, scores, 0, scores.Length);
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
        internal static extern LiteRtLmStatusCode litert_lm_responses_get_num_candidates(IntPtr responses, out UIntPtr numCandidates);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_responses_get_response_text_at(IntPtr responses, UIntPtr index, out IntPtr text);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_responses_has_score_at(
            IntPtr responses, UIntPtr index, [MarshalAs(BoolMarshalType)] out bool hasScore);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_responses_get_score_at(IntPtr responses, UIntPtr index, out float score);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_responses_has_token_length_at(
            IntPtr responses, UIntPtr index, [MarshalAs(BoolMarshalType)] out bool hasTokenLength);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_responses_get_token_length_at(IntPtr responses, UIntPtr index, out UIntPtr tokenLength);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_responses_has_token_scores_at(
            IntPtr responses, UIntPtr index, [MarshalAs(BoolMarshalType)] out bool hasTokenScores);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_responses_get_num_token_scores_at(IntPtr responses, UIntPtr index, out UIntPtr numTokenScores);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_responses_get_token_scores_at(IntPtr responses, UIntPtr index, out IntPtr tokenScores);
    }
}
