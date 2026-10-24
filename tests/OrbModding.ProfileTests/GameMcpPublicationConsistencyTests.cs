using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpPublicationConsistencyTests
{
    [Fact]
    public void QueryRemainsPinnedWhenANewerWorldPublishes()
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(GameMcpAcceptanceFixture.SpellWorld(3, 31), new WorldGeneration(1001));
        var pinned = GameMcpAcceptanceFixture.Snapshot(publisher.ReadLatest());
        publisher.Publish(GameMcpAcceptanceFixture.SpellWorld(9, 32), new WorldGeneration(1002));

        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            pinned,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D")));

        Assert.Null(result["worldGeneration"]);
        Assert.Null(result["lifecycleGeneration"]);
        Assert.Equal(3, (int)result["row"]!["masteryLevel"]!);
    }
}

/// <summary>
/// A refusal is read by an agent, so it says what went wrong in the words the game uses. The id it
/// was handed is already the argument the caller sent, and the batch form repeats it in its own
/// field; reciting thirty-six characters of GUID mid-sentence buried the part that was news.
/// </summary>
