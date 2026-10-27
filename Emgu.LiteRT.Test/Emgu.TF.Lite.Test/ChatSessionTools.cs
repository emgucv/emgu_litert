// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;

namespace Emgu.TF.Lite.Test;

// A test fixture: a realistic set of the chat agent's tools that need no device (date and time, calculator, units, and
// the memory, search, photo and image tools when given), for the routing and context-size tests. The apps build their
// tools from the ChatPage features; this was the demo's list before its page moved onto them.
internal static class ChatSessionTools
{
    internal static IList<AITool> Create(bool isPrivate, IAgentMemoryStore memories,
        Func<string, CancellationToken, Task<string>> findPhotos,
        Func<string, CancellationToken, Task<string>> generateImage,
        Func<string, CancellationToken, Task<string>> searchChats,
        Func<string, string, CancellationToken, Task<string>> searchDocuments = null)
    {
        var tools = new List<AITool> { AgentTools.CurrentDateTime(), AgentTools.Calculate(), AgentTools.ConvertUnits() };
        if (!isPrivate && memories != null) tools.AddRange(AgentTools.Memory(memories));
        if (!isPrivate && searchChats != null) tools.Add(AgentTools.SearchChats(searchChats));
        if (!isPrivate && searchDocuments != null) tools.Add(AgentTools.SearchDocuments(searchDocuments));
        if (findPhotos != null) tools.Add(AgentTools.FindPhotos(findPhotos));
        if (generateImage != null) tools.Add(AgentTools.GenerateImage(generateImage));
        return tools;
    }
}
