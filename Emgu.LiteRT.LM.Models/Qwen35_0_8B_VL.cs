//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Qwen3.5-0.8B with its vision encoder: a small chat model that also accepts images, from
    /// https://huggingface.co/litert-community/Qwen3.5-0.8B
    /// </summary>
    /// <remarks>
    /// Send images with Chat.Send / SendAsync as ChatAttachments; the model sees them at 512x512. It doesn't accept
    /// audio, and doesn't think before answering.
    /// </remarks>
    public class Qwen35_0_8B_VL : Qwen35
    {
        /// <summary>
        /// The int8 model file with the vision encoder (about 1.3 GB)
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/Qwen3.5-0.8B/resolve/main/Qwen3.5-0.8B-VL_int8.litertlm",
                    DefaultLocalSubfolder,
                    "e3360b658c929ff35ab740a21f5e4b688096a72e351b8d347e26ccda314121b7");
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
        /// The backend of the vision encoder, e.g. "cpu" or "gpu"; null to not load it (images are then not supported).
        /// Set before Init.
        /// </summary>
        public String VisionBackend { get; set; } = "cpu";

        /// <summary>
        /// True unless VisionBackend is null
        /// </summary>
        public override bool SupportsImages
        {
            get { return VisionBackend != null; }
        }

        /// <summary>
        /// Create the engine settings with the vision backend.
        /// </summary>
        /// <param name="modelPath">The local path of the model file</param>
        /// <param name="backend">The backend of the main model</param>
        /// <returns>The engine settings</returns>
        protected override EngineSettings CreateEngineSettings(String modelPath, String backend)
        {
            return new EngineSettings(modelPath, backend, VisionBackend, null);
        }
    }
}
