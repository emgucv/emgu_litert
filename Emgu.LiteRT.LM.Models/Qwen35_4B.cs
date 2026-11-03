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
    /// GPU inference uses 32-bit activations: the Windows WebGPU backend's 16-bit path produced buffer validation
    /// errors and an empty second reply during multi-turn testing. CPU inference is unchanged.
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
                    // Pinned to a revision: litert-community replaces the files in place (2026-10-16: the tokenizer section;
                    // 2026-11-03: LiteRT-LM's qwen3_5 tool-call format in the metadata).
                    "https://huggingface.co/litert-community/Qwen3.5-4B/resolve/7a91b2850a4a8dfd8108fca33e74a6ddb6ff4fba/Qwen3.5-4B_mixed_int4.litertlm",
                    DefaultLocalSubfolder,
                    "f4d0be6c412e1310d43335d804ba4e4ed6aaad320f575ee31d0a9951b479b529");
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
        /// True: since revision 7a91b285 the file's metadata has LiteRT-LM v0.19.0's qwen3_5 tool-call format, so LiteRT-LM
        /// parses its tool calls (before, they came back as text)
        /// </summary>
        public override bool SupportsToolCalling
        {
            get { return true; }
        }

        /// <summary>Use 32-bit activations for reliable GPU multi-turn inference.</summary>
        protected override EngineSettings CreateEngineSettings(String modelPath, String backend)
        {
            EngineSettings settings = base.CreateEngineSettings(modelPath, backend);
            if (String.Equals(backend, "gpu", StringComparison.OrdinalIgnoreCase))
                settings.ActivationDataType = ActivationDataType.Float32;
            return settings;
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
