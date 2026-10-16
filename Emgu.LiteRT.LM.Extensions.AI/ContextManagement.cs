// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;

namespace Emgu.LiteRT.LM.Extensions.AI
{
    /// <summary>Token counts and completion diagnostics only; no prompt, reasoning, or result text.</summary>
    public sealed class LiteRtLmContextInfo
    {
        /// <summary>Stage: prepare, compact, complete, or failed.</summary>
        public string Stage { get; internal set; }
        /// <summary>Configured context capacity.</summary>
        public int Capacity { get; internal set; }
        /// <summary>Native KV-cache token count at this boundary, or -1 when unavailable.</summary>
        public int CachedTokens { get; internal set; }
        /// <summary>Estimated input size including the rendered native preface.</summary>
        public int InputTokens { get; internal set; }
        /// <summary>Per-round output budget.</summary>
        public int OutputBudget { get; internal set; }
        /// <summary>Per-round thinking budget, zero when thinking is disabled.</summary>
        public int ThinkingBudget { get; internal set; }
        /// <summary>Native decode count for the last round, or -1 if benchmarking is unavailable.</summary>
        public int DecodedTokens { get; internal set; }
        /// <summary>Number of returned tool calls.</summary>
        public int ToolCalls { get; internal set; }
        /// <summary>Whether completion was stop, length, empty, or an error.</summary>
        public string Completion { get; internal set; }
        /// <summary>Compact diagnostic line suitable for host logs.</summary>
        public override string ToString() => $"{Stage}: capacity={Capacity} cached={CachedTokens} input~={InputTokens} output={OutputBudget} thinking={ThinkingBudget} decoded={DecodedTokens} calls={ToolCalls} completion={Completion}";
    }

    /// <summary>A request cannot fit even after rebuilding without completed reasoning and older exchanges.</summary>
    public sealed class LiteRtLmContextException : InvalidOperationException
    {
        /// <summary>Creates an actionable capacity error.</summary>
        public LiteRtLmContextException(string message) : base(message) { }
        /// <summary>Creates an actionable capacity error retaining the native cause.</summary>
        public LiteRtLmContextException(string message, Exception innerException) : base(message, innerException) { }
    }
}
