//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// A fact an agent remembers about the user ("The user is vegetarian.").
    /// </summary>
    public class AgentMemory
    {
        /// <summary>
        /// Create a memory
        /// </summary>
        /// <param name="id">The store's id of the memory</param>
        /// <param name="text">The fact</param>
        public AgentMemory(String id, String text)
        {
            Id = id;
            Text = text;
        }

        /// <summary>
        /// The store's id of the memory
        /// </summary>
        public String Id { get; }

        /// <summary>
        /// The fact
        /// </summary>
        public String Text { get; }
    }

    /// <summary>
    /// Where the remember and forget tools (AgentTools.Memory) keep their facts - e.g. a database of the app, so they
    /// last across chats.
    /// </summary>
    public interface IAgentMemoryStore
    {
        /// <summary>
        /// All memories, oldest first
        /// </summary>
        Task<IList<AgentMemory>> GetMemoriesAsync();

        /// <summary>
        /// Save a memory.
        /// </summary>
        /// <param name="text">The fact</param>
        /// <returns>False if the same text is already saved</returns>
        Task<bool> AddMemoryAsync(String text);

        /// <summary>
        /// Delete a memory.
        /// </summary>
        /// <param name="id">Its id</param>
        Task DeleteMemoryAsync(String id);
    }

    /// <summary>
    /// Memories kept in memory, e.g. for a console app or a test. Thread-safe.
    /// </summary>
    public class InMemoryAgentMemoryStore : IAgentMemoryStore
    {
        private readonly List<AgentMemory> _memories = new List<AgentMemory>();
        private int _nextId = 1;

        /// <inheritdoc />
        public Task<IList<AgentMemory>> GetMemoriesAsync()
        {
            lock (_memories)
                return Task.FromResult<IList<AgentMemory>>(_memories.ToList());
        }

        /// <inheritdoc />
        public Task<bool> AddMemoryAsync(String text)
        {
            text = (text ?? String.Empty).Trim();
            lock (_memories)
            {
                if (text.Length == 0 || _memories.Any(m => String.Equals(m.Text, text, StringComparison.OrdinalIgnoreCase)))
                    return Task.FromResult(false);
                _memories.Add(new AgentMemory((_nextId++).ToString(System.Globalization.CultureInfo.InvariantCulture), text));
                return Task.FromResult(true);
            }
        }

        /// <inheritdoc />
        public Task DeleteMemoryAsync(String id)
        {
            lock (_memories)
                _memories.RemoveAll(m => m.Id == id);
            return Task.FromResult(0);
        }
    }
}
