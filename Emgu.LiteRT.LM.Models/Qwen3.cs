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
        /// The dynamic int4 build of Qwen3-0.6B (about 350 MB). Its chat template takes message content as text parts,
        /// which LiteRT-LM v0.18.0 always passes; with the repository's earlier qwen3_0_6b_mixed_int4.litertlm the
        /// template drops the user's text and fails on a system message, and its Qwen3-0.6B.litertlm fails loading its
        /// tokenizer.
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/Qwen3-0.6B/resolve/main/Qwen3-0.6B_dynamic_wi4b32_afp32.litertlm",
                    LocalSubfolder,
                    "03e7da1eb1108b50dffaa9bb52cc7bcbad2eb0c66ca990267f480c1e545d2856");
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
