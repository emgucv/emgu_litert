//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Ternary-Bonsai-8B, a Qwen3-8B-class chat model whose weights are ternary (INT2), in about the size of a 4B model
    /// (Apache 2.0 license), from https://huggingface.co/litert-community/Ternary-Bonsai-8B
    /// </summary>
    public class Bonsai8B : Qwen3
    {
        /// <summary>
        /// The 4k-context INT2 build (about 2.6 GB). The repository's experimental builds have a 32k context.
        /// </summary>
        public new static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    // Pinned to a revision: litert-community replaces the files in place.
                    "https://huggingface.co/litert-community/Ternary-Bonsai-8B/resolve/8f7a778a04ba404c0889d30713b9837e33577e3a/bonsai-8b-int2pc-4k-fp16.litertlm",
                    LocalSubfolder,
                    "ba7765be2946b9edaefa909ca48b1e21a2775f50ba1e4e69167299c2d757528b");
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
        /// False: the file's chat template renders the earlier turns without the empty thinking block it adds to the last one,
        /// so a second message fails ("The new rendered template string does not start with the previous rendered template
        /// string"); the chat is rebuilt from its history instead.
        /// </summary>
        public override bool SupportsMultiTurnConversation
        {
            get { return false; }
        }
    }
}
