//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Base class of the Qwen3.5 models (Apache 2.0 license) in LiteRT-LM format, from the litert-community
    /// conversions: Qwen35_0_8B, Qwen35_0_8B_VL and Qwen35_4B.
    /// </summary>
    /// <remarks>
    /// Qwen3.5 is a hybrid architecture (gated delta rule linear attention mixed with a few full attention layers), so
    /// its memory grows little with the length of the conversation. Unlike Qwen3's, the chat templates of these
    /// conversions support multi-turn LiteRT-LM conversations, so Chat keeps one Conversation open across messages.
    /// </remarks>
    public abstract class Qwen35 : LanguageModel
    {
        /// <summary>
        /// True: the chat templates of these conversions support multi-turn LiteRT-LM conversations
        /// </summary>
        public override bool SupportsMultiTurnConversation
        {
            get { return true; }
        }
    }
}
