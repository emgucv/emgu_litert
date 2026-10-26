//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>An action on a saved chat, as a button on its row in the chat list (e.g. "Export").</summary>
    public sealed class ChatAction
    {
        /// <summary>Create an action.</summary>
        /// <param name="text">The button's text</param>
        /// <param name="run">What it does with the chat</param>
        public ChatAction(string text, Func<ChatHistoryEntry, Task> run)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Run = run ?? throw new ArgumentNullException(nameof(run));
        }

        /// <summary>The button's text.</summary>
        public string Text { get; }

        /// <summary>What it does with the chat.</summary>
        public Func<ChatHistoryEntry, Task> Run { get; }
    }

    /// <summary>
    /// What a <see cref="ChatPage"/> is made of: the models, where chats and memories are kept, the features, the
    /// instructions and the settings.
    /// </summary>
    public sealed class ChatPageOptions
    {
        /// <summary>The page's title, shown on wide screens; "Chat" by default.</summary>
        public string Title { get; set; } = "Chat";

        /// <summary>The models the user can choose from (the first is the default); at least one.</summary>
        public IList<ChatModelOption> Models { get; } = new List<ChatModelOption>();

        /// <summary>Where chats are saved and listed (Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore), or null for no saved chats.</summary>
        public IChatHistoryStore History { get; set; }

        /// <summary>
        /// True (the default) to save each exchange into <see cref="History"/> as it completes. False when the app saves
        /// the chats itself (with its own extras) and tells the page which chat it is in (<c>ChatPage.SetCurrentChat</c>).
        /// </summary>
        public bool AutoSave { get; set; } = true;

        /// <summary>Buttons on each saved chat's row in the chat list (e.g. Export), besides opening and deleting it.</summary>
        public IList<ChatAction> ChatActions { get; } = new List<ChatAction>();

        /// <summary>True to show a back button in the header (for a page pushed without a navigation bar); false by default.</summary>
        public bool ShowBackButton { get; set; }

        /// <summary>Where memories about the user are kept (SqliteAgentMemoryStore), for <see cref="Features.MemoryFeature"/>; or null.</summary>
        public IAgentMemoryStore Memory { get; set; }

        /// <summary>The features: the tools and what the page shows for them. <see cref="ChatPage.DefaultFeatures"/> for the built-in ones.</summary>
        public IList<ChatFeature> Features { get; } = new List<ChatFeature>();

        /// <summary>The agent's instructions, for a model that can use tools; <see cref="ChatSkillSet.DefaultInstructions"/> by default.</summary>
        public string Instructions { get; set; } = ChatSkillSet.DefaultInstructions;

        /// <summary>
        /// The instructions for a model that can't use tools (<see cref="ChatModelOption.CanUseTools"/> off), which mustn't be
        /// told about tools it doesn't have; <see cref="ChatSkillSet.PlainInstructions"/> by default.
        /// </summary>
        public string InstructionsWithoutTools { get; set; } = ChatSkillSet.PlainInstructions;

        /// <summary>The agent's instructions in a private chat; <see cref="ChatSkillSet.PrivateInstructions"/> by default.</summary>
        public string PrivateInstructions { get; set; } = ChatSkillSet.PrivateInstructions;

        /// <summary>
        /// Wraps or replaces the model's chat client before the page adds function invocation (e.g. to log requests, or
        /// to use a server instead of the model; the model is still loaded unless <see cref="Models"/> is empty); or null.
        /// </summary>
        public Func<IChatClient, IChatClient> ConfigureClient { get; set; }

        /// <summary>Where the page's settings are kept; MAUI's Preferences with the "litert_lm_chat_" prefix by default.</summary>
        public IChatPageSettings Settings { get; set; } = new PreferencesChatPageSettings();

        /// <summary>True to show the model button and list (when there is more than one model); true by default.</summary>
        public bool ShowModelPicker { get; set; } = true;

        /// <summary>True to show the Think chip for models that can think; true by default.</summary>
        public bool ShowThinkToggle { get; set; } = true;

        /// <summary>True to offer a private chat (with <see cref="History"/>); true by default.</summary>
        public bool ShowPrivateChat { get; set; } = true;

        /// <summary>
        /// True to show the context window choice in the settings for models with a <see cref="ChatModelOption.MaxContextWindow"/>
        /// (with the "Conversation memory" section's usage and Compact now); true by default.
        /// </summary>
        public bool ShowContextWindow { get; set; } = true;

        /// <summary>True to show the CPU threads slider in the settings (<see cref="CpuThreadsView"/>; reloads the model); false by default.</summary>
        public bool ShowCpuThreads { get; set; }

        /// <summary>Where attachments come from, or null for <see cref="ChatView"/>'s default (the photo library, the camera, an audio file).</summary>
        public IChatAttachmentSource AttachmentSource { get; set; }

        /// <summary>Reads replies aloud, or null for <see cref="ChatView"/>'s default (the platform's text to speech).</summary>
        public IReplySpeaker ReplySpeaker { get; set; }

        /// <summary>The CPU threads the models run with, or 0 for <see cref="CpuThreadSettings.Count"/>.</summary>
        public int CpuThreads { get; set; }

        /// <summary>Adjusts a model's engine settings before it loads (after the threads and the context window), or null.</summary>
        public Action<Emgu.LiteRT.LM.EngineSettings> ConfigureEngine { get; set; }
    }
}
