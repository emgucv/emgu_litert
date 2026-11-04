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
        /// <summary>4096: the file is fixed at that size.</summary>
        public override int MaxSupportedContextTokens
        {
            get { return 4096; }
        }

        /// <summary>
        /// The folder, under the application's local data folder, the model file is downloaded to
        /// </summary>
        public const String LocalSubfolder = DefaultLocalSubfolder;

        /// <summary>
        /// The dynamic int4 build of Qwen3-0.6B (about 350 MB). Its chat template takes message content as text parts,
        /// which LiteRT-LM v0.18.0 always passes; with the repository's earlier qwen3_0_6b_mixed_int4.litertlm the
        /// template drops the user's text and fails on a system message, and its Qwen3-0.6B.litertlm fails loading its
        /// tokenizer. Since revision 2670e142 (2026-10-31) the file is the GPU-composite export, faster on the GPU, whose
        /// GPU path needs LiteRT-LM v0.19.0.
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    // Pinned to a revision: litert-community replaces the files in place (2026-10-31: the GPU-composite build).
                    "https://huggingface.co/litert-community/Qwen3-0.6B/resolve/722a0a54c1301a4b07a9856a9590d56362f0969b/Qwen3-0.6B_dynamic_wi4b32_afp32.litertlm",
                    LocalSubfolder,
                    "bf1aba9ce8d963124407353c6aa4cbf72eba78db21ae133de983eac672ce83e0");
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
        /// True: this file's chat template supports multi-turn LiteRT-LM conversations
        /// </summary>
        public override bool SupportsMultiTurnConversation
        {
            get { return true; }
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
