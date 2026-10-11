//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Gemma 4 12B (instruction tuned), a medium-size Gemma 4 model for desktops - much more capable than Gemma4E2B
    /// and Gemma4E4B, e.g. at writing code - from https://huggingface.co/litert-community/gemma-4-12B-it-litert-lm
    /// </summary>
    /// <remarks>
    /// See Gemma4 for what the model supports (images, audio, thinking, multi-turn conversations). It needs about 8 GB
    /// of memory and is meant to run on a desktop GPU: Init(backend: "gpu"), with LiteRT's GPU accelerator available
    /// (on macOS, libLiteRtMetalAccelerator.dylib next to libLiteRt.dylib); on the CPU it is several times slower than
    /// Gemma4E4B.
    /// </remarks>
    public class Gemma4_12B : Gemma4
    {
        /// <summary>
        /// The model file (about 6.9 GB, with the vision and audio encoders). The repository also has GPU-only and web
        /// builds without them.
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/gemma-4-12B-it-litert-lm/resolve/main/gemma-4-12B-it.litertlm",
                    DefaultLocalSubfolder,
                    "58fd31b778ca2c21c80d634fb34fc5a89d11d563a38dfd3cbf1b40dbf252a8b6");
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
