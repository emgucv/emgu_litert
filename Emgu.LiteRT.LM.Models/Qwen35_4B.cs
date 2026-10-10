//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Qwen3.5-4B, a larger chat model that can think before answering, from
    /// https://huggingface.co/litert-community/Qwen3.5-4B
    /// </summary>
    /// <remarks>
    /// Much more capable than the 0.8B model, but several times slower on the CPU. Thinking is reported in a separate
    /// channel, which ChatReply.Thinking exposes.
    /// </remarks>
    public class Qwen35_4B : Qwen35
    {
        /// <summary>
        /// The mixed int4 model file (about 2.8 GB), made for devices with less memory. The repository also has an
        /// int8 file (about 4.4 GB).
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/Qwen3.5-4B/resolve/main/Qwen3.5-4B_mixed_int4.litertlm",
                    DefaultLocalSubfolder,
                    "37209c42c4bd8108373305e7f9820a84fd6d8f8a3481549a31c48bd7880b700f");
            }
        }

        /// <summary>
        /// The model file downloaded when Init is called without one: ModelFile
        /// </summary>
        public override DownloadableFile DefaultModelFile
        {
            get { return ModelFile; }
        }

        /// <summary>
        /// Whether new chats let the model think before answering (off by default). Thinking gives better answers to
        /// harder questions, but takes longer and uses more tokens.
        /// </summary>
        public bool EnableThinking { get; set; }

        /// <summary>
        /// Start a chat with the model, using EnableThinking.
        /// </summary>
        /// <param name="systemMessage">The system message (instructions for the model), or null for none</param>
        /// <returns>The chat</returns>
        public override Chat CreateChat(String systemMessage = null)
        {
            Chat chat = base.CreateChat(systemMessage);
            chat.EnableThinking = EnableThinking;
            return chat;
        }
    }
}
