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
                    "https://huggingface.co/litert-community/Qwen3.5-0.8B/resolve/main/Qwen3.5-0.8B_int8.litertlm",
                    DefaultLocalSubfolder,
                    "64ed396fcdae75e5158945c77a08142b1322ce1bbf1be4d2198783119a1169e8");
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
