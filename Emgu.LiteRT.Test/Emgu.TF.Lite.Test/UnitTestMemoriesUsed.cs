//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Which memories go into the agent's instructions, and which of them a reply drew on (AgentTools.MemoriesUsed, shown under the
// demo's replies); no model.
[TestFixture]
public class UnitTestMemoriesUsed
{
    private static readonly AgentMemory Vegetarian = new AgentMemory("1", "The user is vegetarian.");
    private static readonly AgentMemory Daughter = new AgentMemory("2", "The user's daughter is called Mia.");
    private static readonly AgentMemory Paris = new AgentMemory("3", "The user lives in Paris.");
    private static readonly AgentMemory Hiking = new AgentMemory("4", "The user likes hiking in the Alps.");
    private static readonly AgentMemory[] All = { Vegetarian, Daughter, Paris, Hiking };

    private static string[] Used(string message, string reply) => AgentTools.MemoriesUsed(All, message, reply).Select(m => m.Id).ToArray();

    [Test]
    public void AReplyUsesTheMemoriesThatSuppliedItsWords()
    {
        Assert.That(Used("Suggest a dinner for tonight.", "How about a vegetarian lasagna with spinach?"), Is.EqualTo(new[] { "1" }));
        Assert.That(Used("What's my daughter's name?", "Your daughter's name is Mia."), Is.EqualTo(new[] { "2" }));
        // Two memories at once; plural and singular match ("Alps" / "Alp").
        Assert.That(Used("Plan a weekend for us.", "Take Mia hiking in the Alps and pack vegetarian sandwiches."), Is.EqualTo(new[] { "1", "2", "4" }));
    }

    [Test]
    public void AWordTheMessageAlreadyHadIsntAMemorysDoing()
    {
        // "Paris" came from the message: one shared word isn't enough.
        Assert.That(Used("What's the weather in Paris?", "Paris is sunny, 18 °C."), Is.Empty);
        // Generic words ("likes", "lives") don't count, nor stop words ("the user").
        Assert.That(Used("Hi!", "The user likes it when you ask how they are. Where do you live?"), Is.Empty);
        Assert.That(Used("Hi!", ""), Is.Empty);
    }

    [Test]
    public void TheInstructionsTakeTheLatestMemoriesThatFit()
    {
        var memories = new List<AgentMemory> { Vegetarian, Daughter, Paris, Hiking };
        // Each is a "- " line and a newline: the latest that fit, oldest first.
        int lastTwo = Paris.Text.Length + 2 + 1 + Hiking.Text.Length + 2;
        Assert.That(AgentTools.MemoriesForInstructions(memories, lastTwo).Select(m => m.Id), Is.EqualTo(new[] { "3", "4" }));
        Assert.That(AgentTools.MemoriesForInstructions(memories, lastTwo - 1).Select(m => m.Id), Is.EqualTo(new[] { "4" }));
        Assert.That(AgentTools.MemoryInstructions(AgentTools.MemoriesForInstructions(memories)), Does.EndWith("\n- The user's daughter is called Mia.\n- The user lives in Paris.\n- The user likes hiking in the Alps."));
        Assert.That(AgentTools.MemoryInstructions(new List<AgentMemory>()), Is.Null);
    }
}
