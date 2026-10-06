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
        /// Read a LiteRtLmBenchmarkInfo into a BenchmarkInfo, and delete the native benchmark info.
        /// </summary>
        internal static BenchmarkInfo FromNative(IntPtr benchmarkInfo)
        {
            try
            {
                BenchmarkInfo info = new BenchmarkInfo();
                info.TimeToFirstToken = LiteRtLmInvoke.litert_lm_benchmark_info_get_time_to_first_token(benchmarkInfo);
                info.TotalInitTimeInSecond = LiteRtLmInvoke.litert_lm_benchmark_info_get_total_init_time_in_second(benchmarkInfo);
                int numPrefill = LiteRtLmInvoke.litert_lm_benchmark_info_get_num_prefill_turns(benchmarkInfo);
                info.PrefillTurns = new BenchmarkTurn[Math.Max(numPrefill, 0)];
                for (int i = 0; i < info.PrefillTurns.Length; i++)
                {
                    info.PrefillTurns[i].TokenCount = LiteRtLmInvoke.litert_lm_benchmark_info_get_prefill_token_count_at(benchmarkInfo, i);
                    info.PrefillTurns[i].TokensPerSecond = LiteRtLmInvoke.litert_lm_benchmark_info_get_prefill_tokens_per_sec_at(benchmarkInfo, i);
                }
                int numDecode = LiteRtLmInvoke.litert_lm_benchmark_info_get_num_decode_turns(benchmarkInfo);
                info.DecodeTurns = new BenchmarkTurn[Math.Max(numDecode, 0)];
                for (int i = 0; i < info.DecodeTurns.Length; i++)
                {
                    info.DecodeTurns[i].TokenCount = LiteRtLmInvoke.litert_lm_benchmark_info_get_decode_token_count_at(benchmarkInfo, i);
                    info.DecodeTurns[i].TokensPerSecond = LiteRtLmInvoke.litert_lm_benchmark_info_get_decode_tokens_per_sec_at(benchmarkInfo, i);
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
        internal static extern double litert_lm_benchmark_info_get_time_to_first_token(IntPtr benchmarkInfo);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern double litert_lm_benchmark_info_get_total_init_time_in_second(IntPtr benchmarkInfo);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_benchmark_info_get_num_prefill_turns(IntPtr benchmarkInfo);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_benchmark_info_get_num_decode_turns(IntPtr benchmarkInfo);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_benchmark_info_get_prefill_token_count_at(IntPtr benchmarkInfo, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_benchmark_info_get_decode_token_count_at(IntPtr benchmarkInfo, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern double litert_lm_benchmark_info_get_prefill_tokens_per_sec_at(IntPtr benchmarkInfo, int index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern double litert_lm_benchmark_info_get_decode_tokens_per_sec_at(IntPtr benchmarkInfo, int index);
    }
}
