//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
    ///     Backends = { "cpu", "gpu" }
    /// }
    /// </code>
    /// </summary>
    public sealed class ChatModelOption
    {
        /// <summary>
        /// Create an option.
        /// </summary>
        /// <param name="id">A short, lasting id (saved as the model of a chat and in the settings), e.g. "Gemma4E2B"</param>
        /// <param name="displayName">The name shown, e.g. "Gemma 4 E2B"</param>
        /// <param name="create">Creates a new, unloaded instance of the model</param>
        public ChatModelOption(string id, string displayName, Func<LanguageModel> create)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("An id is needed", nameof(id));
            Id = id;
            DisplayName = displayName ?? id;
            Create = create ?? throw new ArgumentNullException(nameof(create));
        }

        /// <summary>A short, lasting id, e.g. "Gemma4E2B".</summary>
        public string Id { get; }

        /// <summary>The name shown, e.g. "Gemma 4 E2B".</summary>
        public string DisplayName { get; }

        /// <summary>Creates a new, unloaded instance of the model.</summary>
        public Func<LanguageModel> Create { get; }

        /// <summary>A line about the model for the model list ("~2.6 GB download. Understands images..."), or null.</summary>
        public string Detail { get; set; }

        /// <summary>The size of the download, to show before it starts; 0 if unknown.</summary>
        public long DownloadSize { get; set; }

        /// <summary>True if the model can call tools (the agent's skills are used only then).</summary>
        public bool CanUseTools { get; set; }

        /// <summary>True if the model takes images.</summary>
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

        /// <summary>The context window in tokens to load the model with, or 0 for the model's default.</summary>
        public int ContextWindow { get; set; }

        /// <summary>The LiteRT-LM backends the model runs on, "cpu" and/or "gpu" (the first is the default); "cpu" alone by default.</summary>
        public IList<string> Backends { get; } = new List<string> { "cpu" };
    }
}
