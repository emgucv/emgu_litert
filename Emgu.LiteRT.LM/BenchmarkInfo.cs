//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// One prefill or decode turn of a benchmark.
    /// </summary>
    public struct BenchmarkTurn
    {
        /// <summary>
        /// The number of tokens processed in the turn
        /// </summary>
        public int TokenCount;

        /// <summary>
        /// The throughput of the turn, in tokens per second
        /// </summary>
        public double TokensPerSecond;
    }

    /// <summary>
    /// The benchmark timings of a session or conversation (LiteRtLmBenchmarkInfo), read into managed memory.
    /// Requires EngineSettings.EnableBenchmark.
    /// </summary>
    public class BenchmarkInfo
    {
        /// <summary>
        /// The time to the first token, in seconds
        /// </summary>
        public double TimeToFirstToken { get; private set; }

        /// <summary>
        /// The total initialization time, in seconds
        /// </summary>
        public double TotalInitTimeInSecond { get; private set; }

        /// <summary>
        /// The prefill turns
        /// </summary>
        public BenchmarkTurn[] PrefillTurns { get; private set; }

        /// <summary>
        /// The decode turns
        /// </summary>
        public BenchmarkTurn[] DecodeTurns { get; private set; }

        /// <summary>
        /// The maximum number of tokens (the context window) the executor used
        /// </summary>
        public int MaxNumTokens { get; private set; }

        /// <summary>
        /// The result of a get_benchmark_info call: the BenchmarkInfo if it returned Ok (the native one is deleted),
        /// else null - the call fails when benchmarking isn't enabled (EngineSettings.EnableBenchmark).
        /// </summary>
        internal static BenchmarkInfo FromStatus(LiteRtLmStatusCode status, IntPtr benchmarkInfo)
        {
            if (status != LiteRtLmStatusCode.Ok)
            {
                LiteRtLmInvoke.DiscardLastError();
                return null;
            }
            return FromNative(benchmarkInfo);
        }

        /// <summary>
        /// Read a LiteRtLmBenchmarkInfo into a BenchmarkInfo, and delete the native benchmark info.
        /// </summary>
        internal static BenchmarkInfo FromNative(IntPtr benchmarkInfo)
        {
            try
            {
                BenchmarkInfo info = new BenchmarkInfo();
                double seconds;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_time_to_first_token(benchmarkInfo, out seconds), "litert_lm_benchmark_info_get_time_to_first_token");
                info.TimeToFirstToken = seconds;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_total_init_time_in_second(benchmarkInfo, out seconds), "litert_lm_benchmark_info_get_total_init_time_in_second");
                info.TotalInitTimeInSecond = seconds;
                int maxNumTokens;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_max_num_tokens(benchmarkInfo, out maxNumTokens), "litert_lm_benchmark_info_get_max_num_tokens");
                info.MaxNumTokens = maxNumTokens;
                UIntPtr count;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_num_prefill_turns(benchmarkInfo, out count), "litert_lm_benchmark_info_get_num_prefill_turns");
                info.PrefillTurns = new BenchmarkTurn[checked((int)count.ToUInt64())];
                for (int i = 0; i < info.PrefillTurns.Length; i++)
                {
                    UIntPtr tokens;
                    double perSecond;
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_prefill_token_count_at(benchmarkInfo, new UIntPtr((uint)i), out tokens), "litert_lm_benchmark_info_get_prefill_token_count_at");
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_prefill_tokens_per_sec_at(benchmarkInfo, new UIntPtr((uint)i), out perSecond), "litert_lm_benchmark_info_get_prefill_tokens_per_sec_at");
                    info.PrefillTurns[i].TokenCount = checked((int)tokens.ToUInt64());
                    info.PrefillTurns[i].TokensPerSecond = perSecond;
                }
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_num_decode_turns(benchmarkInfo, out count), "litert_lm_benchmark_info_get_num_decode_turns");
                info.DecodeTurns = new BenchmarkTurn[checked((int)count.ToUInt64())];
                for (int i = 0; i < info.DecodeTurns.Length; i++)
                {
                    UIntPtr tokens;
                    double perSecond;
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_decode_token_count_at(benchmarkInfo, new UIntPtr((uint)i), out tokens), "litert_lm_benchmark_info_get_decode_token_count_at");
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_benchmark_info_get_decode_tokens_per_sec_at(benchmarkInfo, new UIntPtr((uint)i), out perSecond), "litert_lm_benchmark_info_get_decode_tokens_per_sec_at");
                    info.DecodeTurns[i].TokenCount = checked((int)tokens.ToUInt64());
                    info.DecodeTurns[i].TokensPerSecond = perSecond;
                }
                return info;
            }
            finally
            {
                LiteRtLmInvoke.litert_lm_benchmark_info_delete(benchmarkInfo);
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_benchmark_info_delete(IntPtr benchmarkInfo);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_time_to_first_token(IntPtr benchmarkInfo, out double seconds);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_total_init_time_in_second(IntPtr benchmarkInfo, out double seconds);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_max_num_tokens(IntPtr benchmarkInfo, out int maxNumTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_num_prefill_turns(IntPtr benchmarkInfo, out UIntPtr count);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_num_decode_turns(IntPtr benchmarkInfo, out UIntPtr count);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_prefill_token_count_at(IntPtr benchmarkInfo, UIntPtr index, out UIntPtr tokenCount);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_decode_token_count_at(IntPtr benchmarkInfo, UIntPtr index, out UIntPtr tokenCount);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_prefill_tokens_per_sec_at(IntPtr benchmarkInfo, UIntPtr index, out double tokensPerSecond);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_benchmark_info_get_decode_tokens_per_sec_at(IntPtr benchmarkInfo, UIntPtr index, out double tokensPerSecond);
    }
}
