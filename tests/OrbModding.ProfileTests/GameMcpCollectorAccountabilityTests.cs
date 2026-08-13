using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// A round needed the live row counts of two categories the collector plainly runs, found neither on
/// this page, and could only establish they existed at all by subtracting one diagnostic's collector
/// count from this page's row count.
/// </summary>
[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpCollectorAccountabilityTests
{
    [Fact]
    public void EveryCollectorTheWorldRunsHasARowOnThisPage()
    {
        var frame = new GameWorldCycleFrame
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var report = new GameWorldCollector().Collect(frame);
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(GameWorldFrameDeriver.Build(frame), new WorldGeneration(1001));

        var page = Names(GameMcpTestHarness.Context(publisher.ReadLatest()));
        var reachedThroughATable = new HashSet<string>(
            GameMcpWorldQuery.ListedCollectionReportNames(),
            StringComparer.Ordinal);

        var unaccounted = new List<string>();
        foreach (var category in report.Categories.ToArray())
        {
            var name = GameMcpWorldQuery.Normalize(category.Category);
            if (reachedThroughATable.Contains(name) || page.Contains(name)) continue;
            unaccounted.Add(name);
        }

        Assert.Equal(Array.Empty<string>(), unaccounted.ToArray());
    }

    [Fact]
    public void ACollectorWithNoTableOfItsOwnSaysSoAndSaysHowManyRowsItRead()
    {
        var page = Categories(World(
            new WorldCollectionCategoryStatus(
                "resources",
                WorldCategoryOutcome.Collected,
                sampled: 80,
                skipped: 0,
                firstFailure: string.Empty),
            new WorldCollectionCategoryStatus(
                "type modifiers",
                WorldCategoryOutcome.Collected,
                sampled: 63,
                skipped: 0,
                firstFailure: string.Empty)));

        var row = Assert.Single(page, item => (string?)item["category"] == "type-modifiers");
        Assert.Equal(63, (int)row["count"]!);
        Assert.False((bool)row["available"]!);
        Assert.Equal(
            "this collector publishes no table of its own, so world_list cannot page it; its rows " +
            "reach the wire inside the reads that carry them",
            (string?)row["reason"]);
    }

    [Fact]
    public void AnUnlistableCollectorThatDidNotBindNamesTheFailureToo()
    {
        var page = Categories(World(
            new WorldCollectionCategoryStatus(
                "crafting stations",
                WorldCategoryOutcome.Unavailable,
                sampled: 0,
                skipped: 0,
                firstFailure: "CraftingStationSO did not resolve on this build")));

        var row = Assert.Single(page, item => (string?)item["category"] == "crafting-stations");
        Assert.Equal(0, (int)row["count"]!);
        Assert.False((bool)row["available"]!);
        Assert.Equal(
            "this collector publishes no table of its own, so world_list cannot page it; its rows " +
            "reach the wire inside the reads that carry them, and it did not bind on this build: " +
            "CraftingStationSO did not resolve on this build",
            (string?)row["reason"]);
    }

    [Fact]
    public void AnUnlistableCollectorThatLostRowsSaysHowMany()
    {
        var page = Categories(World(
            new WorldCollectionCategoryStatus(
                "entity keywords",
                WorldCategoryOutcome.Collected,
                sampled: 44,
                skipped: 3,
                firstFailure: "one keyword list was unreadable")));

        var row = Assert.Single(page, item => (string?)item["category"] == "entity-keywords");
        Assert.Equal(
            "this collector publishes no table of its own, so world_list cannot page it; its rows " +
            "reach the wire inside the reads that carry them, and collection is partial: 3 native " +
            "rows were skipped; first failure: one keyword list was unreadable",
            (string?)row["reason"]);
    }

    [Fact]
    public void TheInventoryStaysOneAlphabeticalList()
    {
        var names = Names(World(
            new WorldCollectionCategoryStatus(
                "type modifiers",
                WorldCategoryOutcome.Collected,
                sampled: 63,
                skipped: 0,
                firstFailure: string.Empty)))
            .ToArray();

        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal).ToArray(), names);
        Assert.Contains("type-modifiers", names);
    }

    [Fact]
    public void ACollectorThatFeedsAListableTableGetsNoSecondRow()
    {
        var names = Names(World(
            new WorldCollectionCategoryStatus(
                "structure costs",
                WorldCategoryOutcome.Collected,
                sampled: 342,
                skipped: 0,
                firstFailure: string.Empty)))
            .ToArray();

        Assert.DoesNotContain("structure-costs", names);
        Assert.Contains("purchase-costs", names);
    }

    private static List<string> Names(GameMcpFrameContext context) =>
        Categories(context).Select(row => (string)row["category"]!).ToList();

    private static List<JObject> Categories(GameMcpFrameContext context) =>
        GameMcpTestHarness.Json(GameMcpWorldQuery.ListCategories(context))["categories"]!
            .Values<JObject>()
            .OfType<JObject>()
            .ToList();

    private static GameMcpFrameContext World(params WorldCollectionCategoryStatus[] reports) =>
        GameMcpTestHarness.Context(new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                reports,
                reports.Length),
        });
}
