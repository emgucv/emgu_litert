//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Qwen3.5-0.8B, a small and fast chat model (text only), from https://huggingface.co/litert-community/Qwen3.5-0.8B
    /// </summary>
    /// <remarks>
    /// It doesn't think before answering: its chat template always disables thinking. Qwen35_0_8B_VL is the same model
    /// with images.
    /// </remarks>
    public class Qwen35_0_8B : Qwen35
    {
        /// <summary>
        /// The int8 model file (about 1 GB)
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    // Pinned to a revision: litert-community replaces the files in place (2026-10-16: the tokenizer section).
                    "https://huggingface.co/litert-community/Qwen3.5-0.8B/resolve/ff11c109e8e56797cd1ee5befa856975bb465afc/Qwen3.5-0.8B_int8.litertlm",
                    DefaultLocalSubfolder,
                    "25447e2550082aed6145bbc755ec4da5d535f98f6a0c386865fdae2ccb88aed1");
            }
        }

        /// <summary>
        /// The model file downloaded when Init is called without one: ModelFile
        /// </summary>
        public override DownloadableFile DefaultModelFile
        {
            get { return ModelFile; }
        }
    }
}
