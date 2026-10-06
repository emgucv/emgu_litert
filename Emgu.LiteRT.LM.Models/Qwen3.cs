//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Qwen3-0.6B, a small chat model that can think before answering (Apache 2.0 license), from
    /// https://huggingface.co/litert-community/Qwen3-0.6B
    /// </summary>
    public class Qwen3 : LanguageModel
    {
        /// <summary>
        /// The folder, under the application's local data folder, the model file is downloaded to
        /// </summary>
        public const String LocalSubfolder = DefaultLocalSubfolder;

        /// <summary>
        /// The mixed int4 build of Qwen3-0.6B (about 500 MB). The repository's Qwen3-0.6B.litertlm needs a newer
        /// LiteRT-LM than the v0.17.1 that Emgu.LiteRT.LM is built with: creating an engine from it fails loading its
        /// tokenizer.
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/Qwen3-0.6B/resolve/main/qwen3_0_6b_mixed_int4.litertlm",
                    LocalSubfolder,
                    "7900eb4e7362d88c58782c6f9999bb7a129e03544aa98b8f338ea0cc5d8c22c1");
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
        /// Whether new chats let the model think before answering. Qwen3 thinks by default; thinking gives better
        /// answers to harder questions, but takes longer and uses more tokens.
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
