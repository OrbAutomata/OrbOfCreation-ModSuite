using System;
using System.Diagnostics;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// A round drove the game for three hours and could not read one per-category collection number:
/// the dashboard that renders them sits below a fold no verb scrolls, and the only capture verb
/// cannot resolve their text. The numbers were measured, published and unreachable.
/// </summary>
public sealed class GameMcpCollectionSpansTests
{
    [Fact]
    public void ThePassSaysWhatEachCategoryCostDearestFirst()
    {
        var text = GameMcpCollectionSpans.Describe(GameMcpTestHarness.Context(Pass()));

        Assert.Equal(
            "collection: 13.834 ms across 5 categories, 370 rows, world publication 1001\n" +
            "  resources: 8.204 ms, 80 rows\n" +
            "  structures: 5.118 ms, 180 rows\n" +
            "  type-modifiers: 0.512 ms, 63 rows\n" +
            "  charged nothing this pass: entity-keywords\n" +
            "  unavailable: crafting-stations",
            text);
    }

    /// <summary>
    /// A span names the table the reader can list, not the collector behind it: the two vocabularies
    /// disagreed on sixteen of seventy-four collectors, and a round that needed the join could only
    /// guess it by matching row counts.
    /// </summary>
    [Fact]
    public void ASpanIsNamedForTheTableTheReaderCanList()
    {
        var text = GameMcpCollectionSpans.Describe(GameMcpTestHarness.Context(new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Charged("loadouts", 18, 6.0),
                    Charged("concept instances", 46, 5.0),
                    Charged("structure costs", 342, 4.0),
                    Charged("upgrade costs", 120, 3.0),
                    Charged("harvest actions", 6, 2.0),
                    Charged("consumable inventory", 9, 1.0),
                    Charged("harvest types", 3, 0d),
                    new WorldCollectionCategoryStatus(
                        "plot actions",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "PlotNodeSO did not resolve on this build"),
                },
                8),
        }));

        Assert.Equal(
            "collection: 21.000 ms across 8 categories, 544 rows, world publication 1001\n" +
            "  player-loadout-entries+player-loadouts+snapshot-entries+snapshot-loadouts+" +
            "snapshot-slots: 6.000 ms, 18 rows\n" +
            "  alchemy-costs+alchemy-instances+concept-recipes: 5.000 ms, 46 rows\n" +
            "  purchase-costs (structure-costs): 4.000 ms, 342 rows\n" +
            "  purchase-costs (upgrade-costs): 3.000 ms, 120 rows\n" +
            "  agromancy-actions: 2.000 ms, 6 rows\n" +
            "  consumables (consumable-inventory): 1.000 ms, 9 rows\n" +
            "  charged nothing this pass: agromancy-element-types\n" +
            "  unavailable: agromancy-plot-actions",
            text);
    }

    [Fact]
    public void WithNoWorldThePageSaysWhyRatherThanZero()
    {
        var text = GameMcpCollectionSpans.Describe(GameMcpTestHarness.Context());

        Assert.Equal(
            "collection: unavailable\n" +
            "collection reason: no world is published, so no collection pass has reported what it " +
            "cost",
            text);
    }

    [Fact]
    public void APublishedWorldWithNoReportSaysThatInsteadOfAnEmptyTable()
    {
        var text = GameMcpCollectionSpans.Describe(GameMcpTestHarness.Context(new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        }));

        Assert.Equal(
            "collection: unavailable\n" +
            "collection reason: the published world carries no collection report",
            text);
    }

    [Fact]
    public void TraceHealthCarriesTheSpansWhileTheWriterIsRecording()
    {
        using var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(Pass(), new WorldGeneration(1001));
        var context = GameMcpTestHarness.Context(
            publisher.ReadLatest(),
            trace: new DecisionJournalStatus(
                DecisionJournalStatusState.Recording,
                acceptedRecords: 4,
                writtenRecords: 4,
                discardedRecords: 0,
                bytesWritten: 512,
                writtenSegments: 2,
                retainedSegments: 2,
                evictedSegments: 0,
                startupPrunedSegments: 0,
                incompatibleSegmentsPruned: 0,
                staleTemporaryFilesRemoved: 0,
                pendingBlocks: 0,
                peakPendingBlocks: 0,
                firstIncompleteSequence: 0,
                DecisionJournalStatusResult.None,
                "journal"));

        Assert.Equal(
            "available\n" +
            "trace writer: recording\n" +
            "records: accepted 4, written 4, discarded 0\n" +
            "bytes written: 512\n" +
            "segments: written 2, retained 2\n" +
            "pending blocks: 0 (peak 0)\n" +
            "artifact: journal\n" +
            "revision: 0\n" +
            "collection: 13.834 ms across 5 categories, 370 rows, world publication 1001\n" +
            "  resources: 8.204 ms, 80 rows\n" +
            "  structures: 5.118 ms, 180 rows\n" +
            "  type-modifiers: 0.512 ms, 63 rows\n" +
            "  charged nothing this pass: entity-keywords\n" +
            "  unavailable: crafting-stations",
            Plugin.ProjectGameMcpTraceHealthText(context));
    }

    [Fact]
    public void AnAbsentWriterStillAnswersWhatTheLastPassCost()
    {
        Assert.Equal(
            "unavailable\n" +
            "reason: the decision journal writer is not active in this runtime\n" +
            "collection: 13.834 ms across 5 categories, 370 rows, world publication 1001\n" +
            "  resources: 8.204 ms, 80 rows\n" +
            "  structures: 5.118 ms, 180 rows\n" +
            "  type-modifiers: 0.512 ms, 63 rows\n" +
            "  charged nothing this pass: entity-keywords\n" +
            "  unavailable: crafting-stations",
            Plugin.ProjectGameMcpTraceHealthText(GameMcpTestHarness.Context(Pass())));
    }

    private static GameWorldState Pass() => new()
    {
        CollectedAtEpoch = 9,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
            new[]
            {
                new WorldCollectionCategoryStatus(
                    "resources",
                    WorldCategoryOutcome.Collected,
                    sampled: 80,
                    skipped: 0,
                    firstFailure: string.Empty,
                    elapsedTicks: Ticks(8.204)),
                new WorldCollectionCategoryStatus(
                    "entity keywords",
                    WorldCategoryOutcome.Collected,
                    sampled: 47,
                    skipped: 0,
                    firstFailure: string.Empty),
                new WorldCollectionCategoryStatus(
                    "type modifiers",
                    WorldCategoryOutcome.Collected,
                    sampled: 63,
                    skipped: 0,
                    firstFailure: string.Empty,
                    elapsedTicks: Ticks(0.512)),
                new WorldCollectionCategoryStatus(
                    "crafting stations",
                    WorldCategoryOutcome.Unavailable,
                    sampled: 0,
                    skipped: 0,
                    firstFailure: "CraftingStationSO did not resolve on this build"),
                new WorldCollectionCategoryStatus(
                    "structures",
                    WorldCategoryOutcome.Collected,
                    sampled: 180,
                    skipped: 0,
                    firstFailure: string.Empty,
                    elapsedTicks: Ticks(5.118)),
            },
            5),
    };

    private static WorldCollectionCategoryStatus Charged(
        string category,
        int sampled,
        double milliseconds) =>
        new(
            category,
            WorldCategoryOutcome.Collected,
            sampled,
            skipped: 0,
            firstFailure: string.Empty,
            elapsedTicks: Ticks(milliseconds));

    /// <summary>
    /// A duration in the raw stopwatch ticks the collector charges, so the page renders the exact
    /// milliseconds these cases name on any host clock.
    /// </summary>
    private static long Ticks(double milliseconds) =>
        (long)Math.Round(Stopwatch.Frequency * milliseconds / 1000.0);
}
