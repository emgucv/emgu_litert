// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// What the demo's chat page offers its own features (Chat/Features/*Feature.cs) beyond the library's IChatPageHost: the
// demo's store, its workload policy, and the page's state and actions the library doesn't expose. The page implements
// both; a feature reaches this one through DemoFeature.Demo, and the other features through IChatPageHost.Services. Add
// members here only as a feature needs them.
internal interface IDemoHost
{
    // The saved chats, memories, goals, documents and indexes (SQLite).
    ChatStore Store { get; }

    // Waits for a cool, charged device before heavy background work, and runs one embedding at a time.
    IIndexingWorkload Workload { get; }

    // Whether the page is shown.
    bool PageVisible { get; }
}
