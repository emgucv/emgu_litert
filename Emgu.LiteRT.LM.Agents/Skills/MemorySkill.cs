//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents.Skills
{
    /// <summary>
    /// Memories about the user: the remember and forget tools over an <see cref="IAgentMemoryStore"/>, and the saved
    /// memories in each message's instructions (<see cref="AgentTools.MemoryInstructionsAsync"/>). Only while
    /// <see cref="ChatMessageContext.MemoryOn"/> and not in a private chat.
    /// </summary>
    public sealed class MemorySkill : ChatSkill
    {
        private readonly int _maxLength;

        /// <summary>
        /// Create the skill.
        /// </summary>
        /// <param name="store">Where the memories are kept (e.g. Emgu.LiteRT.LM.Chat.Sqlite's SqliteAgentMemoryStore)</param>
        /// <param name="maxLength">The most characters of memories to give with a message</param>
        public MemorySkill(IAgentMemoryStore store, int maxLength = 1200)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            _maxLength = maxLength;
        }

        /// <summary>Where the memories are kept.</summary>
        public IAgentMemoryStore Store { get; }

        /// <summary>The memories given with the last message (to show which ones a reply drew on, <see cref="AgentTools.MemoriesUsed"/>), or null.</summary>
        public IList<AgentMemory> LastGiven { get; private set; }

        /// <inheritdoc/>
        public override string Name => "memory";

        /// <inheritdoc/>
        public override string Title => "Memories about you";

        private static bool On(ChatMessageContext context) => context.MemoryOn && !context.IsPrivate;

        /// <inheritdoc/>
        public override IEnumerable<AITool> Tools(ChatMessageContext context) =>
            On(context) ? (IEnumerable<AITool>)AgentTools.Memory(Store) : Array.Empty<AITool>();

        /// <inheritdoc/>
        public override async Task<string> InstructionsAsync(ChatMessageContext context)
        {
            LastGiven = null;
            if (!On(context))
                return null;
            LastGiven = AgentTools.MemoriesForInstructions(await Store.GetMemoriesAsync().ConfigureAwait(false), _maxLength);
            return AgentTools.MemoryInstructions(LastGiven);
        }

        /// <inheritdoc/>
        public override string ToolSummary => "memories about the user";
    }
}
