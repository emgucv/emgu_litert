//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Base class of Google's Gemma 4 models (Apache 2.0 license) in LiteRT-LM format: Gemma4E2B and Gemma4E4B.
    /// </summary>
    /// <remarks>
    /// The models accept images and audio as well as text: send them with Chat.Send / SendAsync as ChatAttachments
    /// (e.g. ask about an image, or to transcribe a recording). The vision and audio parts of the model are only
    /// loaded when first used. They can think before answering (EnableThinking, off by default); unlike Qwen3, they
    /// report the thinking in a separate channel, which ChatReply.Thinking exposes the same way. Their chat template
    /// supports multi-turn conversations, so Chat keeps one LiteRT-LM Conversation open across messages.
    /// On the GPU (Init(backend: "gpu"), with LiteRT's GPU accelerator available - on Android the OpenCL one from the
    /// optional Emgu.LiteRT.OpenCL.runtime.maui.android package, on macOS libLiteRtMetalAccelerator.dylib), the
    /// models use 32-bit activations: with LiteRT-LM's default 16-bit ones, Gemma 4 E2B answered wrongly after a long
    /// conversation on both a Pixel Fold and an Apple M1 (e.g. "732" for 7342), while 32-bit ones were correct at
    /// about 1.2-1.5x the time. An Init configureSettings callback can set EngineSettings.ActivationDataType back.
    /// </remarks>
    public abstract class Gemma4 : LanguageModel
    {
        /// <summary>
        /// Whether new chats let the model think before answering. Thinking gives better answers to harder
        /// questions, but takes longer and uses more tokens.
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

        /// <summary>
        /// The backend of the vision encoder, used for images, e.g. "cpu" or "gpu"; null to not load it (images are
        /// then not supported). Set before Init.
        /// </summary>
        public String VisionBackend { get; set; } = "cpu";

        /// <summary>
        /// The backend of the audio encoder, used for audio, e.g. "cpu"; null to not load it (audio is then not
        /// supported). Set before Init.
        /// </summary>
        public String AudioBackend { get; set; } = "cpu";

        /// <summary>
        /// True unless VisionBackend is null
        /// </summary>
        public override bool SupportsImages
        {
            get { return VisionBackend != null; }
        }

        /// <summary>
        /// True unless AudioBackend is null
        /// </summary>
        public override bool SupportsAudio
        {
            get { return AudioBackend != null; }
        }

        /// <summary>
        /// Create the engine settings with the vision and audio backends, and 32-bit activations on the GPU.
        /// </summary>
        /// <param name="modelPath">The local path of the model file</param>
        /// <param name="backend">The backend of the main model</param>
        /// <returns>The engine settings</returns>
        protected override EngineSettings CreateEngineSettings(String modelPath, String backend)
        {
            EngineSettings settings = new EngineSettings(modelPath, backend, VisionBackend, AudioBackend);
            // 32-bit activations on the GPU (see the remarks of Gemma4).
            if (String.Equals(backend, "gpu", StringComparison.OrdinalIgnoreCase))
                settings.ActivationDataType = ActivationDataType.Float32;
            return settings;
        }

        /// <summary>
        /// True: LiteRT-LM parses Gemma 4's tool calls, and its chat template takes the tool responses
        /// </summary>
        public override bool SupportsToolCalling
        {
            get { return true; }
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
