//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Emgu.LiteRT.Util;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// A model an app offers in its chat: how to create it, what to call it, what it can do (so the UI shows the right
    /// controls and the agent knows whether it can use tools), and the backends it runs on.
    /// <code>
    /// new ChatModelOption("gemma4e2b", "Gemma 4 E2B", () => new Gemma4E2B())
    /// {
    ///     Detail = "~2.6 GB download. Understands images and speech, and can think before answering.",
    ///     DownloadSize = 2600L &lt;&lt; 20, CanUseTools = true, AcceptsImages = true, AcceptsAudio = true, CanSummarize = true,
    ///     ContextWindow = 8192, MaxContextWindow = 32000, Backends = { "cpu", "gpu" }
    /// }
    /// </code>
    /// A remote model - a server's, with no <see cref="LanguageModel"/> on the device - is made with the constructor
    /// without a factory (<see cref="IsRemote"/>); the chat page's app supplies its client.
    /// </summary>
    public sealed class ChatModelOption
    {
        /// <summary>
        /// Create an option for a model on the device.
        /// </summary>
        /// <param name="id">A short, lasting id (saved as the model of a chat and in the settings), e.g. "Gemma4E2B"</param>
        /// <param name="displayName">The name shown, e.g. "Gemma 4 E2B"</param>
        /// <param name="create">Creates a new, unloaded instance of the model</param>
        public ChatModelOption(string id, string displayName, Func<LanguageModel> create)
            : this(id, displayName)
        {
            Create = create ?? throw new ArgumentNullException(nameof(create));
        }

        /// <summary>
        /// Create an option for a remote model (a server's): no model is loaded on the device, and the chat page's app
        /// makes the client that talks to it.
        /// </summary>
        /// <param name="id">A short, lasting id, e.g. "Server"</param>
        /// <param name="displayName">The name shown, e.g. "Your own server"</param>
        public ChatModelOption(string id, string displayName)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("An id is needed", nameof(id));
            Id = id;
            DisplayName = displayName ?? id;
        }

        /// <summary>A short, lasting id, e.g. "Gemma4E2B".</summary>
        public string Id { get; }

        /// <summary>The name shown, e.g. "Gemma 4 E2B".</summary>
        public string DisplayName { get; }

        /// <summary>Creates a new, unloaded instance of the model; null for a remote model (<see cref="IsRemote"/>).</summary>
        public Func<LanguageModel> Create { get; }

        /// <summary>True for a remote model (a server's): nothing is loaded on the device.</summary>
        public bool IsRemote => Create == null;

        /// <summary>
        /// How a chat page reaches a remote model (<see cref="IsRemote"/>): its connection, client, summaries and
        /// one-off questions. Set by whatever offers the model (e.g. a server feature); null for a model on the device,
        /// or for a remote model whose page supplies the client itself.
        /// </summary>
        public IRemoteChatModel Remote { get; set; }

        /// <summary>A line about the model for the model list ("~2.6 GB download. Understands images..."), or null.</summary>
        public string Detail { get; set; }

        /// <summary>The size of the download, to show before it starts; 0 if unknown.</summary>
        public long DownloadSize { get; set; }

        /// <summary>
        /// The model's file, so a list can say whether it is downloaded or its download paused without creating the
        /// model (e.g. the model class's static <c>ModelFile</c>); or null.
        /// </summary>
        public DownloadableFile ModelFile { get; set; }

        /// <summary>True if the model can call tools (the agent's skills are used only then).</summary>
        public bool CanUseTools { get; set; }

        /// <summary>True if the model takes images (a remote model's app may set it after connecting).</summary>
        public bool AcceptsImages { get; set; }

        /// <summary>True if the model takes audio.</summary>
        public bool AcceptsAudio { get; set; }

        /// <summary>True if the model can think before answering (the Think toggle is shown).</summary>
        public bool CanThink { get; set; } = true;

        /// <summary>
        /// True if the model summarizes well enough to compact a long conversation by summary; otherwise earlier messages
        /// are dropped instead (<see cref="ChatCompaction.Drop"/>).
        /// </summary>
        public bool CanSummarize { get; set; }

        /// <summary>
        /// The context window in tokens to load the model with, or 0 for the model's default. With a
        /// <see cref="MaxContextWindow"/>, this is the "Automatic" size the user can change (<see cref="ChatContextWindow"/>).
        /// </summary>
        public int ContextWindow { get; set; }

        /// <summary>
        /// The largest context window the model's file allows, when the user may choose one (a chat page then shows the
        /// choice); 0 when the window is fixed.
        /// </summary>
        public int MaxContextWindow { get; set; }

        /// <summary>The LiteRT-LM backends the model runs on, "cpu" and/or "gpu" (the first is the default); "cpu" alone by default.</summary>
        public IList<string> Backends { get; } = new List<string> { "cpu" };
    }
}
