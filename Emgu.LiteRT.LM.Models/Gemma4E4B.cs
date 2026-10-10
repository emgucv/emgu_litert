//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Gemma 4 E4B (instruction tuned), a larger, more capable Gemma 4 model than Gemma4E2B, from
    /// https://huggingface.co/litert-community/gemma-4-E4B-it-litert-lm
    /// </summary>
    /// <remarks>
    /// See Gemma4 for what the model supports (images, audio, thinking, multi-turn conversations).
    /// </remarks>
    public class Gemma4E4B : Gemma4
    {
        /// <summary>
        /// The model file for CPU and GPU (about 3.7 GB). The repository also has GPU-only and web builds.
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/gemma-4-E4B-it-litert-lm/resolve/main/gemma-4-E4B-it.litertlm",
                    DefaultLocalSubfolder,
                    "0b2a8980ce155fd97673d8e820b4d29d9c7d99b8fa6806f425d969b145bd52e0");
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
