//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// The context window a chat loads a model with, when the user may choose one (<see cref="ChatModelOption.MaxContextWindow"/>):
    /// the sizes offered, what "Automatic" means on each kind of device, and the note that explains the choice.
    /// </summary>
    public static class ChatContextWindow
    {
        /// <summary>The sizes offered, in tokens; 0 is Automatic.</summary>
        public static readonly int[] Choices = { 0, 4096, 8192, 16384, 32000 };

        /// <summary>A size as people say it: "Automatic", "4k", "32k".</summary>
        /// <param name="tokens">The size, or 0 for Automatic</param>
        public static string Name(int tokens)
        {
            // Thousands, as people say them: 4096 is "4k", 32000 "32k".
            return tokens <= 0 ? "Automatic" : (int)Math.Round(tokens / 1000.0) + "k";
        }

        /// <summary>
        /// The window to load a model with: the user's setting, or for Automatic the model's own size
        /// (<see cref="ChatModelOption.ContextWindow"/>) - on a phone only for models that call tools, whose
        /// instructions and tool definitions don't fit the default 4k - capped at what the model's file allows.
        /// </summary>
        /// <param name="setting">The user's choice in tokens, or 0 for Automatic</param>
        /// <param name="automatic">The model's own size for Automatic (<see cref="ChatModelOption.ContextWindow"/>), or 0</param>
        /// <param name="max">The largest the model's file allows (<see cref="ChatModelOption.MaxContextWindow"/>); 0 means the window can't change</param>
        /// <param name="phone">True on a phone (Android, iOS), where memory is short</param>
        /// <param name="canUseTools">Whether the model calls tools</param>
        /// <returns>The window in tokens, or 0 for the model's default</returns>
        public static int Choose(int setting, int automatic, int max, bool phone, bool canUseTools)
        {
            if (max <= 0)
                return 0;
            int wanted = setting > 0 ? setting : Automatic(automatic, phone, canUseTools);
            return wanted > 0 ? Math.Min(wanted, max) : 0;
        }

        /// <summary>What Automatic gives: the model's own size, or on a phone the default (0) for a model without tools.</summary>
        /// <param name="automatic">The model's own size</param>
        /// <param name="phone">True on a phone</param>
        /// <param name="canUseTools">Whether the model calls tools</param>
        public static int Automatic(int automatic, bool phone, bool canUseTools) => phone && !canUseTools ? 0 : automatic;

        /// <summary>The note under the choice: what a size costs, with a warning for the large ones.</summary>
        /// <param name="setting">The user's choice</param>
        /// <param name="phone">True on a phone</param>
        public static string Note(int setting, bool phone)
        {
            string note = "How much of the chat the model keeps in mind. A larger window summarizes the chat less often, " +
                "but the model takes longer to read it again (when a saved chat is opened, or after summarizing) and needs " +
                "more memory. Changing it reloads the model; the chat continues.";
            if (phone && setting >= 16384)
                note += " On a phone, 16k and more can take many minutes to read a long chat again, and may run out of memory.";
            else if (!phone && setting >= 32000)
                note += " At 32k, reading a full chat again took about 4 minutes on an Apple Silicon Mac's CPU.";
            return note;
        }
    }
}
