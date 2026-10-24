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

internal static class GameMcpAcceptanceFixture
{
    internal static readonly Guid SpellId =
        Guid.Parse("01234567-89ab-4cde-8f01-23456789abcd");

    internal static JObject Request(int id, string method, JObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters,
    };

    internal static IReadOnlyList<JObject> Tools()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
        var response = router.Handle(Request(1, "tools/list", new JObject()));
        return response.Body!["result"]!["tools"]!.Values<JObject>().OfType<JObject>().ToArray();
    }

    internal static string[] ToolNames() =>
        Tools().Select(tool => (string)tool["name"]!).ToArray();

    /// <summary>
    /// One tool call as a caller sees it: the page of text the protocol returns, and nothing beside
    /// it. Every tool answers this way now, so there is one helper rather than one per shape.
    /// </summary>
    internal static string CallText(
        string tool,
        JObject? arguments = null,
        GameMcpFrameContext? context = null)
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox, GameMcpToolCatalog.Shipping);
        var pinned = context ?? GameMcpTestHarness.Context();
        var response = GameMcpTestHarness.Handle(router, inbox, Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = tool,
                ["arguments"] = arguments ?? new JObject(),
            }), operation => GameMcpTestHarness.ExecuteRead(operation, pinned));
        Assert.Equal(200, response.StatusCode);
        Assert.Null(response.Body?["error"]);
        Assert.Null(response.Body!["result"]!["structuredContent"]);
        var content = Assert.Single(response.Body["result"]!["content"]!.Values<JObject>());
        Assert.Equal("text", (string?)content["type"]);
        return (string)content["text"]!;
    }

    internal static GameMcpFrameContext SpellSnapshot(int masteryLevel) =>
        Snapshot(SpellWorld(masteryLevel, 30));

    internal static GameMcpFrameContext Snapshot(GameWorldState world)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            world with { EntityIdentities = GameMcpTestHarness.EntityCatalog },
            new WorldGeneration(1001));
        return Snapshot(publisher.ReadLatest());
    }

    internal static GameMcpFrameContext Snapshot(
        WorldPublication<GameWorldState> publication) =>
        GameMcpTestHarness.Context(publication);

    internal static GameWorldState SpellWorld(int masteryLevel, long epoch) => new()
    {
        CollectedAtEpoch = epoch,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
            new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell-recipes",
                    WorldCategoryOutcome.Collected,
                    sampled: 1,
                    skipped: 0,
                    firstFailure: string.Empty),
            },
            1),
        SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(
            new[]
            {
                new WorldSpellRecipe(
                    SpellId,
                    discovered: true,
                    discRarityLevel: 0,
                    masteryXp: new BigDouble(12),
                    masteryLevel,
                    masteryLevelReady: true,
                    masteryLevelAffordable: true,
                    hiddenDiscovery: false,
                    isRequiredDiscovery: true,
                    penaltyUsageCost: 1,
                    castSpeed: 1,
                    baseCharges: 1,
                    repeatInstantEffects: false,
                    spellPowerMod: new BigDouble(1),
                    spellCostMod: new BigDouble(1),
                    spellCdSpeedMod: new BigDouble(1),
                    spellDurationMod: new BigDouble(1),
                    spellSpecialMod: new BigDouble(1),
                    spellXpMod: new BigDouble(1),
                    hasAlertedThisMastery: false),
            },
            1),
    };

    internal static GameMcpCommand NativeCommand() =>
        new(
            sequence: 1,
            GameMcpCommandKind.Purchase,
            expectedLifecycleGeneration: 12,
            expectedConfigurationGeneration: 7,
            mode: "structure",
            Guid.NewGuid(),
            Guid.Empty,
            derivedNativeType: "StructureSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false);

    internal static GameMcpFrameOperation SubmitHarvest(GameMcpFrameInbox operations) =>
        operations.Submit(new GameMcpOperationRequestBuilder
        {
            ToolName = "game_agromancy",
            Route = GameMcpTestHarness.Route("game_agromancy"),
            Uuid = KnownEntities.FruitTreePlot.Uuid,
            SecondaryUuid = KnownEntities.FruitTreeCollect.Uuid,
            Mode = "add",
        }.Freeze());
}
