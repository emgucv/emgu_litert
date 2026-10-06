//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Gemma 4 E2B (instruction tuned), Google's small Gemma 4 model (Apache 2.0 license), from
    /// https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm
    /// </summary>
    /// <remarks>
    /// The model also accepts image and audio input (LoadedFile.SupportsInputModality), which Chat doesn't use yet:
    /// Chat sends text only. Unlike Qwen3, it doesn't think before answering. Its chat template supports multi-turn
    /// conversations, so Chat keeps one LiteRT-LM Conversation open across messages.
    /// </remarks>
    public class Gemma4E2B : LanguageModel
    {
        /// <summary>
        /// The model file for CPU and GPU (about 2.6 GB). The repository also has GPU-only, web and NPU builds.
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm/resolve/main/gemma-4-E2B-it.litertlm",
                    DefaultLocalSubfolder,
                    "181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c");
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
        /// True: Gemma 4's chat template supports multi-turn LiteRT-LM conversations
        /// </summary>
        public override bool SupportsMultiTurnConversation
        {
            get { return true; }
        }
    }
}
