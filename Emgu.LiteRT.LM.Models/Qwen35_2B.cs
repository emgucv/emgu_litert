//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Qwen3.5-2B, a mid-sized chat model (text only), from https://huggingface.co/litert-community/Qwen3.5-2B
    /// </summary>
    /// <remarks>
    /// Between Qwen35_0_8B and Qwen35_4B in size and ability. It doesn't think before answering: its chat template always
    /// disables thinking. Its file has no tool-call format, so it doesn't call tools.
    /// </remarks>
    public class Qwen35_2B : Qwen35
    {
        /// <summary>
        /// The int8 model file (about 2.1 GB). The repository also has one with the vision encoder (about 3.2 GB).
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    // Pinned to a revision: litert-community replaces the files in place (2026-10-16: the tokenizer section).
                    "https://huggingface.co/litert-community/Qwen3.5-2B/resolve/7537f3109665ab021e1c16e7ded2b3fda01de267/Qwen3.5-2B_int8.litertlm",
                    DefaultLocalSubfolder,
                    "fa4ac3259404567c7f82533746549eed94e2e1c8222095706c63cce17653c68a");
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
