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

    /// <summary>
    /// Both surfaces describe the same collectors, so a name printed by one has to be a name the
    /// other answers to. For two rounds it was not: six collectors were named for a table that had
    /// since been renamed, two more matched no table at all, and a session correlating "this one is
    /// expensive" with "this is the table I can list" was reduced to matching row counts.
    /// </summary>
    [Fact]
    public void EveryNameTheCollectionBlockPrintsIsAWorldCategoriesRow()
    {
        // The 76 collectors the world runs and the pseudo-category a degraded modifier fold
        // appends, because the block prints that one on the same terms.
        var context = World(new GameWorldCollector().CategoryNames()
            .Select(name => new WorldCollectionCategoryStatus(
                name,
                WorldCategoryOutcome.Collected,
                sampled: 1,
                skipped: 0,
                firstFailure: string.Empty,
                elapsedTicks: 1))
            .ToArray());
        var page = new HashSet<string>(Names(context), StringComparer.Ordinal);

        var spans = GameMcpCollectionSpans.Describe(context)
            .Split('\n')
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(77, spans.Length);

        var unresolved = new List<string>();
        foreach (var span in spans)
        {
            foreach (var name in PageNames(span))
            {
                if (!page.Contains(name)) unresolved.Add(name);
            }
        }

        Assert.Equal(Array.Empty<string>(), unresolved.ToArray());
    }

    /// <summary>
    /// The world_categories rows one span names: the row, or every row where the collector feeds
    /// several, less the parenthetical that separates two collectors feeding one row.
    /// </summary>
    private static string[] PageNames(string span)
    {
        var text = span.Substring(2, span.IndexOf(": ", StringComparison.Ordinal) - 2);
        var qualifier = text.IndexOf(" (", StringComparison.Ordinal);
        if (qualifier >= 0) text = text.Substring(0, qualifier);
        return text.Split('+');
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

        // The cell says the word the standing sentence above the table is keyed under, not a class
        // code: `ERR_LOCKED` here meant "this collector has no table" while the same code everywhere
        // else means "progression has not unlocked this", and a live round could not sweep the cell
        // by eye. No `available` column beside it either — it was the reason cell's own emptiness
        // spelled a second way.
        Assert.Equal("unlistable", (string?)row["reason"]);
        Assert.Null(row["reasonCode"]);
        Assert.Null(row["available"]);
    }

    [Fact]
    public void TheUnlistableSentenceIsSaidOncePerResponseAndOnlyWhereItApplies()
    {
        Assert.Equal(
            "this collector publishes no table of its own, so world_list cannot page it; its rows " +
            "reach the wire inside the reads that carry them",
            Unlistable(World(
                new WorldCollectionCategoryStatus(
                    "type modifiers",
                    WorldCategoryOutcome.Collected,
                    sampled: 63,
                    skipped: 0,
                    firstFailure: string.Empty))));

        Assert.Null(Unlistable(World(
            new WorldCollectionCategoryStatus(
                "resources",
                WorldCategoryOutcome.Collected,
                sampled: 80,
                skipped: 0,
                firstFailure: string.Empty))));
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
        Assert.Null(row["reasonCode"]);
        Assert.Equal(
            "unlistable. It did not bind on this build: CraftingStationSO did not resolve on " +
            "this build",
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
        Assert.Null(row["reasonCode"]);
        Assert.Equal(
            "unlistable. Collection is partial: 3 native rows were skipped; first failure: one " +
            "keyword list was unreadable",
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

    private static string? Unlistable(GameMcpFrameContext context) =>
        (string?)GameMcpTestHarness.Json(GameMcpWorldQuery.ListCategories(context))["unlistable"];

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
