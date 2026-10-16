using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

/// <summary>
/// Policy tests for the stateless Auto Buy worker (<see cref="AutoBuyCycleEvaluator"/>). Frames are
/// built directly so each admission / reserve / affordability / ranking / grouping / overcommit rule
/// can be exercised in isolation; the capture→frame seam is covered separately by the capture-adapter
/// tests. The reserve/affordability numeric path is additionally cross-checked against the legacy
/// <see cref="ReservePolicy"/> (BigAmount math) as a read-only oracle, proving the BigDouble port
/// makes the same admit/reject and ranking decisions the legacy engine did.
/// </summary>
public sealed class AutoBuyCycleEvaluatorTests
{
    private static readonly Guid StructureA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StructureB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UpgradeA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid StructureC = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ---- Whole-service gates -------------------------------------------------------------------

    [Fact]
    public void WaitsForTheNextPublicationWhenThereIsNoWork()
    {
        var frame = new FrameBuilder().Build(); // no candidates
        var actions = Plan(frame, Config(), out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void NonOperationalConfigurationPlansNothing()
    {
        var resource = 0;
        var builder = new FrameBuilder();
        resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        var frame = builder.Build();

        Assert.Empty(Plan(frame, Config(mode: AutoBuyOperationMode.Disabled), out _));
        Assert.Empty(Plan(frame, Config(enabled: false), out _));
        Assert.Empty(Plan(frame, Config(includeStructures: false, includeUpgrades: false), out _));
    }

    [Fact]
    public void EmergencyStatePlansNothing()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) });

        Assert.Empty(Plan(builder.Build(), Config(emergencyDisabled: true), out _));
    }

    [Fact]
    public void ReportsWorkerInputEligibilityAndDecisionSize()
    {
        var builder = new FrameBuilder().Bulk(4);
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        builder.Structure(StructureB, new[] { (resource, 2.0) }, available: false);
        builder.Upgrade(UpgradeA, new[] { (resource, 3.0) });

        var actions = Plan(
            builder.Build(),
            Config(),
            out _,
            out var metrics);

        Assert.Equal(3, metrics.CapturedCandidates);
        Assert.Equal(2, metrics.CapturedStructures);
        Assert.Equal(1, metrics.CapturedUpgrades);
        Assert.Equal(2, metrics.EligibleCandidates);
        Assert.Equal(2, metrics.PlannedActions);
        Assert.Equal(5, metrics.RequestedLevels);
        Assert.Equal(actions.Count, metrics.PlannedActions);
        Assert.Equal(2, metrics.GroupOutcomes.FullGroups);
        Assert.Equal(0, metrics.GroupOutcomes.ReducedGroups);
        Assert.Equal(0, metrics.GroupOutcomes.LedgerStarved);
        Assert.Equal(metrics.EligibleCandidates, metrics.GroupOutcomes.TotalCandidates);
    }

    [Fact]
    public void ProjectsWorkerDecisionMetricsIntoTheJournalShape()
    {
        var state = AutoBuyCycleState.Create(new LifecycleGeneration(1));
        state.RecordDecision(new AutoBuyDecisionMetrics(2, 1, 2, 2, 5));
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var output = new ServiceStateProjectionBuilder(buffer);

        AutoBuyServiceProjection.Write(in state, output);

        var projection = output.CaptureSnapshot();
        Assert.Equal(14, projection.Count);
        Assert.Collection(
            Enumerable.Range(0, projection.Count).Select(projection.GetEntry),
            entry => AssertProjection(entry, AutoBuyServiceProjection.CapturedCandidatesKey, 3),
            entry => AssertProjection(entry, AutoBuyServiceProjection.EligibleCandidatesKey, 2),
            entry => AssertProjection(entry, AutoBuyServiceProjection.PlannedActionsKey, 2),
            entry => AssertProjection(entry, AutoBuyServiceProjection.RequestedLevelsKey, 5),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedKindNotSelectedKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedUnavailableKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedRequirementsUnmetKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedTerminalKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedUnaffordableKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedUnpriceableKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedOwningViewUnavailableKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedOwningViewRelationMissingKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedOwningViewRelationUnreadableKey, 0),
            entry => AssertProjection(entry, AutoBuyServiceProjection.ExcludedOwningViewRelationContradictoryKey, 0));
        Assert.DoesNotContain(
            Enumerable.Range(0, projection.Count).Select(projection.GetEntry),
            entry => entry.Key.Value == AutoBuyServiceProjection.RetiredOwningViewRelationAmbiguousKey);
    }

    /// <summary>
    /// The counters are not decorative: an operator reads them to find out why a plan is empty, so
    /// the numbers a real cycle produces have to reach the journal intact.
    /// </summary>
    [Fact]
    public void ProjectsTheExclusionHistogramTheWorkerProduced()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 0.0) });          // unpriceable
        builder.Structure(StructureB, new[] { (resource, 5_000.0) });      // unaffordable
        builder.Upgrade(UpgradeA, new[] { (resource, 1.0) }, available: false);

        Plan(builder.Build(), Config(), out _, out var metrics);

        var state = AutoBuyCycleState.Create(new LifecycleGeneration(1));
        state.RecordDecision(metrics);
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var output = new ServiceStateProjectionBuilder(buffer);

        AutoBuyServiceProjection.Write(in state, output);

        var projection = output.CaptureSnapshot();
        var values = Enumerable.Range(0, projection.Count)
            .Select(projection.GetEntry)
            .ToDictionary(entry => entry.Key.Value, entry => entry.Value.Integer);

        Assert.Equal(0, values[AutoBuyServiceProjection.EligibleCandidatesKey]);
        Assert.Equal(1, values[AutoBuyServiceProjection.ExcludedUnpriceableKey]);
        Assert.Equal(1, values[AutoBuyServiceProjection.ExcludedUnaffordableKey]);
        Assert.Equal(1, values[AutoBuyServiceProjection.ExcludedUnavailableKey]);
    }

    [Theory]
    [InlineData((int)AutoBuyOwningViewStatus.Unavailable, (int)AutoBuyExclusion.OwningViewUnavailable)]
    [InlineData((int)AutoBuyOwningViewStatus.RelationMissing, (int)AutoBuyExclusion.OwningViewRelationMissing)]
    [InlineData((int)AutoBuyOwningViewStatus.RelationUnreadable, (int)AutoBuyExclusion.OwningViewRelationUnreadable)]
    [InlineData((int)AutoBuyOwningViewStatus.RelationContradictory, (int)AutoBuyExclusion.OwningViewRelationContradictory)]
    public void Planner_ExcludesOwningViewFailuresWithTheirNamedReason(
        int owningViewValue,
        int reasonValue)
    {
        var owningView = (AutoBuyOwningViewStatus)owningViewValue;
        var reason = (AutoBuyExclusion)reasonValue;
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(
            StructureA,
            new[] { (resource, 1.0) },
            owningView: owningView);

        var actions = Plan(builder.Build(), Config(), out _, out var metrics);

        Assert.Empty(actions);
        Assert.Equal(1, metrics.Exclusions.For(reason));
        Assert.Equal(metrics.CapturedCandidates, metrics.Exclusions.Total);
    }

    // ---- Reserve floor -------------------------------------------------------------------------

    [Fact]
    public void ReserveFloorBlocksWhenQuantityBelowCostPlusAbsoluteReserve()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(15);
        builder.Structure(StructureA, new[] { (resource, 10.0) });
        var frame = builder.Build();

        // required = cost(10) + max(absoluteReserve, relative) — reserve 8 -> 18 > 15 blocks.
        Assert.Empty(Plan(frame, Config(absoluteReserve: "8"), out _));
        // reserve 3 -> 13 <= 15 admits.
        Assert.NotEmpty(Plan(frame, Config(absoluteReserve: "3"), out _));
    }

    [Fact]
    public void RelativeReserveMultiplierRaisesTheFloor()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(15);
        builder.Structure(StructureA, new[] { (resource, 10.0) });
        var frame = builder.Build();

        // relative floor = cost*mult = 10*0.6 = 6 -> required 16 > 15 blocks.
        Assert.Empty(Plan(frame, Config(relativeMultiplier: 0.6f), out _));
        // relative floor = 10*0.4 = 4 -> required 14 <= 15 admits.
        Assert.NotEmpty(Plan(frame, Config(relativeMultiplier: 0.4f), out _));
    }

    [Fact]
    public void EmptyOrMalformedAbsoluteReserveFallsBackToNoReserve()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(15);
        builder.Structure(StructureA, new[] { (resource, 10.0) });
        var frame = builder.Build();

        // With no parseable reserve the floor is 0, so required = cost(10) <= 15 and the buy
        // proceeds, rather than the legacy reject-everything behavior.
        Assert.NotEmpty(Plan(frame, Config(absoluteReserve: ""), out _));
        Assert.NotEmpty(Plan(frame, Config(absoluteReserve: "not-a-number"), out _));
    }

    /// <summary>
    /// Eligibility asks whether a candidate can be afforded on its own. A batch spends for real — the
    /// game deducts the cost when a purchase is queued — so the floor has to be charged as the batch
    /// is planned, not checked once against untouched quantities.
    /// </summary>
    [Fact]
    public void ABatchChargesEachPurchaseAgainstTheReserveFloor()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(25);
        builder.Structure(StructureA, new[] { (resource, 10.0) });
        builder.Structure(StructureB, new[] { (resource, 10.0) });
        var frame = builder.Build();

        // Each clears cost(10) + reserve(8) = 18 against 25 on its own. Together they cannot: after the
        // first is charged only 15 remains, and 15 < 18.
        var actions = Plan(frame, Config(absoluteReserve: "8"), out _);

        Assert.Single(actions);
    }

    [Fact]
    public void ACandidateThatCannotBePaidForDoesNotStopACheaperOneBehindIt()
    {
        var builder = new FrameBuilder();
        var scarce = builder.Resource(25);
        var plentiful = builder.Resource(1000);
        builder.Structure(StructureA, new[] { (scarce, 10.0) });   // ratio 0.4
        builder.Structure(StructureB, new[] { (scarce, 10.0) });   // ratio 0.4, ranked after A by uuid
        builder.Structure(StructureC, new[] { (plentiful, 500.0) }); // ratio 0.5, ranked last
        var frame = builder.Build();

        var actions = Plan(frame, Config(absoluteReserve: "8"), out _);

        // B is skipped for want of the scarce resource; C draws on a different one and still buys.
        Assert.Equal(
            new[] { StructureA, StructureC },
            actions.Select(action => action.Uuid).ToArray());
    }

    [Fact]
    public void AStructureBulkRequestIsChargedForEveryLevelItAsksFor()
    {
        var builder = new FrameBuilder().Bulk(3);
        var resource = builder.Resource(40);
        builder.GroupedStructure(
            StructureA,
            resource,
            nextCost: 8.0,
            exactGroupedCost: 24.0);
        builder.Upgrade(UpgradeA, new[] { (resource, 10.0) });
        var frame = builder.Build();

        // The structure asks for 3 levels, so it is charged 24, not 8. That leaves 16, which cannot
        // cover the upgrade's cost(10) + reserve(8).
        var actions = Plan(
            frame,
            Config(absoluteReserve: "8"),
            out _);

        Assert.Single(actions);
        Assert.Equal(StructureA, actions[0].Uuid);
        Assert.Equal(3, actions[0].Count);
    }

    [Fact]
    public void LiveMissedStructurePlansOneExactLevelAndRecordsReducedGroup()
    {
        // Alchemic Command's live Water arithmetic: one level was 77.4098% of stock, while the
        // exact two-level sum was 174.2981%. One-level admission therefore must result in one action.
        const double stock = 1.10713885171171e9;
        const double nextCost = 8.5703445124987e8;
        const double exactTwoLevelCost = 1.92972183318296e9;
        var builder = new FrameBuilder().Bulk(2);
        var resource = builder.Resource(stock);
        builder.GroupedStructure(
            StructureA,
            resource,
            nextCost,
            exactTwoLevelCost);

        var action = Assert.Single(Plan(builder.Build(), Config(), out _, out var metrics));

        Assert.Equal(1, action.Count);
        Assert.Equal(nextCost / stock, action.Belief.CostRatio, 6);
        Assert.Equal(0, metrics.GroupOutcomes.FullGroups);
        Assert.Equal(1, metrics.GroupOutcomes.ReducedGroups);
        Assert.Equal(1, metrics.GroupOutcomes.ReducedGroupLevels);
        Assert.Equal(0, metrics.GroupOutcomes.LedgerStarved);
        Assert.Equal(metrics.EligibleCandidates, metrics.GroupOutcomes.TotalCandidates);
    }

    [Fact]
    public void CandidateSpentOutByEarlierActionRecordsLedgerStarved()
    {
        var builder = new FrameBuilder().Bulk(1);
        var resource = builder.Resource(15);
        builder.Structure(StructureA, new[] { (resource, 10.0) });
        builder.Structure(StructureB, new[] { (resource, 10.0) });

        var action = Assert.Single(Plan(builder.Build(), Config(), out _, out var metrics));

        Assert.Equal(StructureA, action.Uuid);
        Assert.Equal(2, metrics.EligibleCandidates);
        Assert.Equal(1, metrics.PlannedActions);
        Assert.Equal(1, metrics.GroupOutcomes.FullGroups);
        Assert.Equal(0, metrics.GroupOutcomes.ReducedGroups);
        Assert.Equal(1, metrics.GroupOutcomes.LedgerStarved);
        Assert.Equal(metrics.EligibleCandidates, metrics.GroupOutcomes.TotalCandidates);
    }

    [Fact]
    public void FullyAffordableTwoLevelGroupRemainsFull()
    {
        const double stock = 3e9;
        const double nextCost = 8.5703445124987e8;
        const double exactTwoLevelCost = 1.92972183318296e9;
        var builder = new FrameBuilder().Bulk(2);
        var resource = builder.Resource(stock);
        builder.GroupedStructure(
            StructureA,
            resource,
            nextCost,
            exactTwoLevelCost);

        var action = Assert.Single(Plan(builder.Build(), Config(), out _, out var metrics));

        Assert.Equal(2, action.Count);
        Assert.Equal(exactTwoLevelCost / stock, action.Belief.CostRatio, 6);
        Assert.Equal(1, metrics.GroupOutcomes.FullGroups);
        Assert.Equal(0, metrics.GroupOutcomes.ReducedGroups);
        Assert.Equal(0, metrics.GroupOutcomes.ReducedGroupLevels);
        Assert.Equal(0, metrics.GroupOutcomes.LedgerStarved);
    }

    [Fact]
    public void FillAvailableQueueReservesTheExactRisingGroupedCost()
    {
        var builder = new FrameBuilder().Bulk(3);
        var resource = builder.Resource(65);
        builder.GroupedStructure(
            StructureA,
            resource,
            nextCost: 10.0,
            exactGroupedCost: 60.0); // successive prices 10 + 20 + 30
        builder.Upgrade(UpgradeA, new[] { (resource, 20.0) });
        var frame = builder.Build();

        // The old lower bound reserved 3 * 10 = 30, leaving 35 and admitting the structure. Native
        // payment leaves only 5 after the rising 10 + 20 + 30 curve, so the structure is predictably
        // unaffordable. FillAvailableQueue still plans the grouped upgrade, but no longer emits that
        // doomed later action.
        var action = Assert.Single(Plan(
            frame,
            Config(),
            out _));

        Assert.Equal(StructureA, action.Uuid);
        Assert.Equal(3, action.Count);
    }

    [Fact]
    public void AGroupedRequestUsesOnlyAnExactPublishedReducedCurve()
    {
        var builder = new FrameBuilder().Bulk(3);
        var resource = builder.Resource(1_000);
        builder.GroupedStructure(
            StructureA,
            resource,
            nextCost: 10.0,
            exactGroupedCost: 30.0,
            publishedGroupedLevels: 2);

        var action = Assert.Single(Plan(builder.Build(), Config(), out _, out var metrics));

        Assert.Equal(2, action.Count);
        Assert.Equal(1, metrics.GroupOutcomes.ReducedGroups);
        Assert.Equal(2, metrics.GroupOutcomes.ReducedGroupLevels);
    }

    // ---- Affordability threshold ----------------------------------------------------------------

    [Fact]
    public void AffordabilityThresholdRejectsWhenRatioExceedsMode()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(100);
        builder.Structure(StructureA, new[] { (resource, 10.0) }); // ratio = 0.1
        var frame = builder.Build();

        // Excess100 limit is 0.01; 0.1 exceeds -> rejected.
        Assert.Empty(Plan(frame, Config(structureAffordability: AutoBuyAffordabilityMode.Excess100), out _));
        // Excess10 limit is 0.1; 0.1 is not greater -> admitted.
        Assert.NotEmpty(Plan(frame, Config(structureAffordability: AutoBuyAffordabilityMode.Excess10), out _));
    }

    [Fact]
    public void UpgradeAndStructureUseTheirOwnAffordabilityMode()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(100);
        builder.Structure(StructureA, new[] { (resource, 10.0) }); // ratio 0.1
        builder.Upgrade(UpgradeA, new[] { (resource, 10.0) });     // ratio 0.1
        var frame = builder.Build();

        var actions = Plan(
            frame,
            Config(
                structureAffordability: AutoBuyAffordabilityMode.Excess100, // rejects the structure
                upgradeAffordability: AutoBuyAffordabilityMode.Excess10),   // admits the upgrade
            out _);

        Assert.All(actions, a => Assert.Equal(AutoBuyCandidateKind.Upgrade, a.Kind));
        Assert.NotEmpty(actions);
    }

    // ---- Bandwidth is paid out of the room, not the pool -----------------------------------------

    /// <summary>
    /// A full bandwidth pool affords nothing, however large it is.
    /// </summary>
    /// <remarks>
    /// The game charges a bandwidth cost against the gap between holdings and the ceiling, so a pool
    /// sitting at its ceiling has nothing to spend. Reading its quantity instead — a million, here —
    /// calls the purchase comfortably affordable and hands the action boundary a plan the game
    /// refuses outright.
    /// </remarks>
    [Fact]
    public void AFullBandwidthPoolAffordsNothing()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000_000, bandwidth: true, headroom: 0.0);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        var frame = builder.Build();

        Assert.Empty(Plan(frame, Config(), out _));
    }

    /// <summary>The mirror: an all-but-empty pool affords a cost its room covers.</summary>
    [Fact]
    public void AnEmptyBandwidthPoolAffordsWhatItsRoomCovers()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(0.0, bandwidth: true, headroom: 1_000_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        var frame = builder.Build();

        var action = Assert.Single(Plan(frame, Config(), out _));
        Assert.Equal(StructureA, action.Uuid);
    }

    /// <summary>
    /// The room, not the pool, is also what the batch ledger spends down.
    /// </summary>
    /// <remarks>
    /// Two purchases in one batch would otherwise each clear the same untouched room and the batch
    /// as a whole would overrun the ceiling — the same hole the ledger closes for ordinary
    /// resources, which stays closed only if both halves measure the same thing.
    /// </remarks>
    [Fact]
    public void TheBatchLedgerSpendsBandwidthRoomDown()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(0.0, bandwidth: true, headroom: 10.0);
        builder.Structure(StructureA, new[] { (resource, 6.0) });
        builder.Structure(StructureB, new[] { (resource, 6.0) });
        var frame = builder.Build();

        var action = Assert.Single(Plan(
            frame,
            Config(structureAffordability: AutoBuyAffordabilityMode.BuyAll),
            out _));
        Assert.Equal(StructureA, action.Uuid);
    }

    /// <summary>An ordinary resource still spends out of its holdings.</summary>
    [Fact]
    public void AnOrdinaryResourceIsUnaffectedByItsRoom()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000_000, bandwidth: false, headroom: 0.0);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        var frame = builder.Build();

        Assert.NotEmpty(Plan(frame, Config(), out _));
    }

    // ---- Ranking --------------------------------------------------------------------------------

    [Fact]
    public void RanksByCostRatioAscendingWithinEqualPriority()
    {
        var builder = new FrameBuilder();
        var cheap = builder.Resource(1_000);
        var pricey = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (pricey, 100.0) }); // ratio 0.1
        builder.Structure(StructureB, new[] { (cheap, 1.0) });    // ratio 0.001
        var frame = builder.Build();

        var actions = Plan(frame, Config(), out _);

        Assert.Equal(StructureB, actions[0].Uuid); // lower ratio first
        Assert.Contains(actions, a => a.Uuid == StructureA);
        Assert.True(
            actions.ToList().FindIndex(a => a.Uuid == StructureB) <
            actions.ToList().FindIndex(a => a.Uuid == StructureA));
    }

    // ---- Availability / lifecycle gates ---------------------------------------------------------

    [Fact]
    public void UnavailableCandidatesAreSkipped()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) }, available: false);
        var frame = builder.Build();

        Assert.Empty(Plan(frame, Config(), out _));
    }

    [Fact]
    public void TerminalFiniteLevelCandidatesAreSkipped()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) }, hasFiniteLevels: true, isMaxLevel: true, queuedLevels: 0);
        builder.Structure(StructureB, new[] { (resource, 1.0) }, hasFiniteLevels: true, isMaxQueuedLevel: true, queuedLevels: 3);
        var frame = builder.Build();

        Assert.Empty(Plan(frame, Config(), out _));
    }

    /// <summary>
    /// A candidate can be available, unfinished and affordable and still be refused by the game,
    /// because the level it would buy next is gated on something else. This is the term that was
    /// invisible until W58, and it is why Auto Buy kept planning ScribeScroll4.
    /// </summary>
    [Fact]
    public void CandidatesWhoseNextLevelIsGatedAreSkipped()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Upgrade(UpgradeA, new[] { (resource, 1.0) }, meetsNextLevelRequirements: false);
        builder.Structure(StructureA, new[] { (resource, 1.0) }, meetsNextLevelRequirements: false);
        var frame = builder.Build();

        Assert.Empty(Plan(frame, Config(), out _));
    }

    /// <summary>The gate is per candidate, not per cycle: an ungated neighbour still gets bought.</summary>
    [Fact]
    public void AGatedCandidateDoesNotHoldUpTheOnesBesideIt()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) }, meetsNextLevelRequirements: false);
        builder.Structure(StructureB, new[] { (resource, 1.0) });
        var frame = builder.Build();

        var actions = Plan(frame, Config(), out _);

        Assert.Equal(StructureB, Assert.Single(actions).Uuid);
    }

    // ---- Grouping -------------------------------------------------------------------------------

    /// <summary>
    /// Under Bulk Development a structure asks for the game's own bulk count.
    /// </summary>
    /// <remarks>
    /// The frame carried this number from the first day and nothing read it, so setting the game's
    /// bulk-build control to ten bought one level at a time and said nothing about why. The count is
    /// the game's, not the operator's: it is the same variable the in-game control writes.
    /// </remarks>
    [Fact]
    public void StructuresBulkOnBulkDevelopment()
    {
        var builder = new FrameBuilder().Bulk(4);
        var resource = builder.Resource(1_000_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        var frame = builder.Build();

        var action = Assert.Single(Plan(
            frame,
            Config(),
            out _,
            out var metrics));
        Assert.Equal(StructureA, action.Uuid);
        Assert.Equal(4, action.Count);
        Assert.Equal(4, metrics.RequestedLevels);
    }

    [Fact]
    public void BulkDevelopmentOnePreservesTheSingleLevelActionBelief()
    {
        var builder = new FrameBuilder().Bulk(1);
        var resource = builder.Resource(40);
        builder.Structure(StructureA, new[] { (resource, 10.0) });
        var frame = builder.Build();

        var action = Assert.Single(Plan(frame, Config(), out _, out var metrics));

        AssertSingleLevelAction(
            action,
            AutoBuyCandidateKind.Structure,
            StructureA,
            frame.Resources[resource].ResourceId,
            cost: 10.0,
            available: 40.0);
        Assert.Equal(1, metrics.GroupOutcomes.FullGroups);
        Assert.Equal(0, metrics.GroupOutcomes.ReducedGroups);
        Assert.Equal(0, metrics.GroupOutcomes.LedgerStarved);
    }

    /// <summary>One action stays readable: a hundred levels is the ceiling, as it was in legacy.</summary>
    [Fact]
    public void BulkDevelopmentIsCappedAtAHundredLevels()
    {
        var builder = new FrameBuilder().Bulk(5_000);
        var resource = builder.Resource(1e30);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        var frame = builder.Build();

        var action = Assert.Single(Plan(
            frame, Config(), out _));
        Assert.Equal(100, action.Count);
    }

    /// <summary>An unreadable bulk count is one level, which is always safe.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void BulkDevelopmentBelowOneRequestsOneLevel(int bulk)
    {
        var builder = new FrameBuilder().Bulk(bulk);
        var resource = builder.Resource(1_000_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        var frame = builder.Build();

        var action = Assert.Single(Plan(
            frame, Config(), out _));
        Assert.Equal(1, action.Count);
    }

    [Fact]
    public void UpgradesRemainByteForByteSingleLevelRequests()
    {
        var builder = new FrameBuilder().Bulk(4);
        var resource = builder.Resource(1_000_000);
        builder.Upgrade(UpgradeA, new[] { (resource, 1.0) });
        var frame = builder.Build();

        var action = Assert.Single(Plan(frame, Config(), out _, out var metrics));

        AssertSingleLevelAction(
            action,
            AutoBuyCandidateKind.Upgrade,
            UpgradeA,
            frame.Resources[resource].ResourceId,
            cost: 1.0,
            available: 1_000_000.0);
        Assert.Equal(1, metrics.GroupOutcomes.FullGroups);
        Assert.Equal(0, metrics.GroupOutcomes.ReducedGroups);
        Assert.Equal(0, metrics.GroupOutcomes.LedgerStarved);
    }

    // ---- One action per candidate, room- and batch-bounded --------------------------------------

    [Fact]
    public void PlansOncePerEligibleCandidate()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) }); // ratio smallest
        builder.Structure(StructureB, new[] { (resource, 2.0) });
        builder.Upgrade(UpgradeA, new[] { (resource, 3.0) });      // ratio largest
        var frame = builder.Build();

        var actions = Plan(frame, Config(), out _);

        // One action per eligible candidate, in ranked order, each requesting a single level.
        Assert.Equal(new[] { StructureA, StructureB, UpgradeA }, actions.Select(a => a.Uuid).ToArray());
        Assert.All(actions, a => Assert.Equal(1, a.Count));
    }

    [Fact]
    public void TheRankedPlanIsNotBoundedByAnyQueueEstimate()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        builder.Structure(StructureB, new[] { (resource, 2.0) });
        builder.Upgrade(UpgradeA, new[] { (resource, 3.0) });
        var frame = builder.Build();

        // All three eligible, one action each, in rank order. The worker cannot know how many slots
        // will still be free by the time each one runs, so it plans the whole ranked list; the action
        // boundary re-reads the live room, clamps, and cascade-terminates the batch when it fills.
        var actions = Plan(frame, Config(), out _);

        Assert.Equal(
            new[] { StructureA, StructureB, UpgradeA },
            actions.Select(a => a.Uuid).ToArray());
    }

    // ---- Duplicate-resource combining -----------------------------------------------------------

    [Fact]
    public void DuplicateResourceCostsAreCombinedForTheReserveFloor()
    {
        // Two cost rows draw on the SAME resource. Combined cost = 12; separately each is 6.
        var builder = new FrameBuilder();
        var resource = builder.Resource(11);
        builder.Structure(StructureA, new[] { (resource, 6.0), (resource, 6.0) });
        var frame = builder.Build();

        // Combined: required 12 > 11 -> blocked. Were the rows treated separately, 6 <= 11 would admit.
        Assert.Empty(Plan(frame, Config(), out _));

        // Bump quantity above the combined requirement to prove it is only the combined total blocking.
        var ok = new FrameBuilder();
        var okResource = ok.Resource(13);
        ok.Structure(StructureA, new[] { (okResource, 6.0), (okResource, 6.0) });
        Assert.NotEmpty(Plan(ok.Build(), Config(), out _));
    }

    // ---- Unpriceable candidates -----------------------------------------------------------------

    [Fact]
    public void ACandidateWhoseEveryCostRowIsZeroIsRefusedRatherThanTreatedAsFree()
    {
        // The cycle-1 burst, reduced to its shape. Every row zero means every row was skipped, so the
        // affordability comparison never ran once — and answering "eligible" to a question nobody
        // asked is what put 180 unaffordable purchases into a single batch.
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 0.0), (resource, 0.0) });

        Assert.Empty(Plan(builder.Build(), Config(), out _));
    }

    [Fact]
    public void AZeroRowBesideAPricedOneStillSkipsAndTheCandidateIsEvaluated()
    {
        // One free resource on an otherwise priced candidate is a fact about that resource, not a
        // failure to read the price. The candidate is still judged, on the rows that did price.
        var builder = new FrameBuilder();
        var free = builder.Resource(1_000);
        var priced = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (free, 0.0), (priced, 10.0) });

        Assert.Single(Plan(builder.Build(), Config(), out _));

        // And the priced row is what decides it: put that cost out of reach and the candidate goes.
        var poor = new FrameBuilder();
        var poorFree = poor.Resource(1_000);
        var poorPriced = poor.Resource(5);
        poor.Structure(StructureA, new[] { (poorFree, 0.0), (poorPriced, 10.0) });

        Assert.Empty(Plan(poor.Build(), Config(), out _));
    }

    [Fact]
    public void AnUnpriceableCandidateDoesNotHoldUpThePricedOnesBesideIt()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 0.0) });
        builder.Structure(StructureB, new[] { (resource, 10.0) });

        var actions = Plan(builder.Build(), Config(), out _, out var metrics);

        Assert.Equal(1, metrics.EligibleCandidates);
        Assert.Single(actions);
    }

    // ---- Why a candidate did not reach the plan ------------------------------------------------

    /// <summary>
    /// Every candidate that does not reach the plan is attributed to exactly one term, so the
    /// histogram plus the eligible count always accounts for the whole captured set. A term that
    /// starts quietly swallowing candidates shows up here as an arithmetic failure rather than as a
    /// puzzling zero in a log.
    /// </summary>
    [Fact]
    public void EveryExcludedCandidateIsAttributedToExactlyOneTerm()
    {
        var builder = new FrameBuilder();
        var rich = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (rich, 10.0) });                        // eligible
        builder.Structure(StructureB, new[] { (rich, 5_000.0) });                     // unaffordable
        builder.Structure(StructureC, new[] { (rich, 0.0) });                         // unpriceable
        builder.Upgrade(UpgradeA, new[] { (rich, 10.0) }, available: false);          // unavailable

        Plan(builder.Build(), Config(), out _, out var metrics);

        Assert.Equal(4, metrics.CapturedCandidates);
        Assert.Equal(1, metrics.EligibleCandidates);
        Assert.Equal(3, metrics.Exclusions.Total);
        Assert.Equal(1, metrics.Exclusions.Unaffordable);
        Assert.Equal(1, metrics.Exclusions.Unpriceable);
        Assert.Equal(1, metrics.Exclusions.Unavailable);
        Assert.Equal(
            metrics.CapturedCandidates,
            metrics.EligibleCandidates + metrics.Exclusions.Total);
        Assert.Equal(metrics.EligibleCandidates, metrics.GroupOutcomes.TotalCandidates);
    }

    /// <summary>
    /// One candidate arranged to trip each term in turn, so every counter is proven to be wired to
    /// the term it is named after rather than merely to be present.
    /// </summary>
    /// <remarks>
    /// A loop rather than a <c>[Theory]</c>: the term is an enum internal to the suite, and a public
    /// test method may not name one in its signature — the same reason the collector publishes enums
    /// as integers. The assertion carries the term so a failure still says which counter broke.
    /// </remarks>
    [Fact]
    public void EachAdmissionTermCountsUnderItsOwnName()
    {
        var terms = new[]
        {
            AutoBuyExclusion.KindNotSelected,
            AutoBuyExclusion.Unavailable,
            AutoBuyExclusion.RequirementsUnmet,
            AutoBuyExclusion.Terminal,
            AutoBuyExclusion.Unaffordable,
            AutoBuyExclusion.Unpriceable,
        };

        foreach (var term in terms)
        {
            var builder = new FrameBuilder();
            var resource = builder.Resource(1_000);
            var cost = term == AutoBuyExclusion.Unpriceable ? 0.0
                : term == AutoBuyExclusion.Unaffordable ? 5_000.0
                : 10.0;
            builder.Structure(
                StructureA,
                new[] { (resource, cost) },
                available: term != AutoBuyExclusion.Unavailable,
                hasFiniteLevels: term == AutoBuyExclusion.Terminal,
                isMaxLevel: term == AutoBuyExclusion.Terminal,
                meetsNextLevelRequirements: term != AutoBuyExclusion.RequirementsUnmet);

            var config = term switch
            {
                AutoBuyExclusion.KindNotSelected => Config(includeStructures: false),
                _ => Config(),
            };

            Plan(builder.Build(), config, out _, out var metrics);

            Assert.True(
                metrics.EligibleCandidates == 0 &&
                metrics.Exclusions.Total == 1 &&
                metrics.Exclusions.For(term) == 1,
                $"{term} did not account for its candidate: {metrics.EligibleCandidates} eligible, " +
                $"{metrics.Exclusions.Total} excluded, {metrics.Exclusions.For(term)} under this term.");
        }
    }

    /// <summary>
    /// A candidate that trips several terms is reported under the first one tested rather than under
    /// all of them, so the counters stay a partition and the reported reason is the one an operator
    /// would have to change to alter the outcome.
    /// </summary>
    [Fact]
    public void ACandidateThatTripsSeveralTermsCountsOnlyUnderTheFirst()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(
            StructureA,
            new[] { (resource, 5_000.0) },
            available: false,
            hasFiniteLevels: true,
            isMaxLevel: true,
            meetsNextLevelRequirements: false);

        Plan(builder.Build(), Config(), out _, out var metrics);

        Assert.Equal(1, metrics.Exclusions.Total);
        Assert.Equal(1, metrics.Exclusions.Unavailable);
    }

    /// <summary>
    /// A gate that stops the whole service reports no per-candidate exclusions, because no candidate
    /// was examined. Attributing 180 candidates to "kind not selected" when the operator merely hit
    /// the emergency stop would send the next reader down the wrong path entirely.
    /// </summary>
    [Fact]
    public void AWholeServiceGateAttributesNothingToACandidateTerm()
    {
        var builder = new FrameBuilder();
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 10.0) });

        Plan(builder.Build(), Config(emergencyDisabled: true), out _, out var metrics);

        Assert.Equal(0, metrics.Exclusions.Total);
        Assert.Equal(0, metrics.EligibleCandidates);
    }

    // ---- Numeric parity against the legacy ReservePolicy oracle ---------------------------------

    [Theory]
    [InlineData(10.0, 1_000.0, "0", 0.0)]     // ratio 0.01, no reserve -> admit
    [InlineData(10.0, 15.0, "8", 0.0)]        // reserve floor 18 > 15 -> reject
    [InlineData(10.0, 15.0, "3", 0.0)]        // reserve floor 13 <= 15 -> admit
    [InlineData(10.0, 15.0, "0", 0.6)]        // relative floor 16 > 15 -> reject
    [InlineData(50.0, 50.0, "0", 0.0)]        // exactly affordable (ratio 1.0) -> admit
    [InlineData(1.0, 1_000_000.0, "0", 0.0)]  // tiny ratio -> admit
    public void ReserveDecisionMatchesLegacyReservePolicy(
        double cost,
        double quantity,
        string absoluteReserve,
        double relativeMultiplier)
    {
        var oracle = new ReservePolicy(() => Config(
            absoluteReserve: absoluteReserve,
            relativeMultiplier: (float)relativeMultiplier));
        var oracleDecision = oracle.Evaluate(new[]
        {
            new ResourceAdmissionCost(
                StructureA.ToString(),
                "res",
                new BigAmount(cost, 0),
                new BigAmount(quantity, 0)),
        });

        var builder = new FrameBuilder();
        var resource = builder.Resource(quantity);
        builder.Structure(StructureA, new[] { (resource, cost) });
        // BuyAll keeps the affordability threshold from interfering; only the reserve gate decides.
        var admitted = Plan(
            builder.Build(),
            Config(
                structureAffordability: AutoBuyAffordabilityMode.BuyAll,
                absoluteReserve: absoluteReserve,
                relativeMultiplier: (float)relativeMultiplier),
            out _).Count > 0;

        Assert.Equal(oracleDecision.Passed, admitted);
    }

    [Fact]
    public void RankingOrderMatchesLegacyReservePolicyRatioOrder()
    {
        // Two candidates on separate resources with distinct cost ratios.
        (double cost, double quantity) a = (100.0, 1_000.0); // ratio 0.1
        (double cost, double quantity) b = (10.0, 1_000.0);  // ratio 0.01

        var oracle = new ReservePolicy(() => Config());
        var ratioA = oracle.Evaluate(new[]
        {
            new ResourceAdmissionCost(StructureA.ToString(), "a", new BigAmount(a.cost, 0), new BigAmount(a.quantity, 0)),
        }).MaxCostToQuantityRatio;
        var ratioB = oracle.Evaluate(new[]
        {
            new ResourceAdmissionCost(StructureB.ToString(), "b", new BigAmount(b.cost, 0), new BigAmount(b.quantity, 0)),
        }).MaxCostToQuantityRatio;
        Assert.True(ratioB < ratioA); // sanity: B is the cheaper ratio per the oracle

        var builder = new FrameBuilder();
        var resA = builder.Resource(a.quantity);
        var resB = builder.Resource(b.quantity);
        builder.Structure(StructureA, new[] { (resA, a.cost) });
        builder.Structure(StructureB, new[] { (resB, b.cost) });

        var actions = Plan(
            builder.Build(),
            Config(structureAffordability: AutoBuyAffordabilityMode.BuyAll),
            out _);

        // The worker must emit the oracle's lower-ratio candidate first.
        Assert.Equal(StructureB, actions[0].Uuid);
    }

    // ---- The epoch a plan is judged by ----------------------------------------------------------

    /// <summary>
    /// Every planned purchase carries the epoch of the world it was planned from, so the boundary can
    /// tell whether the game is still that run without reaching for a snapshot it cannot see.
    /// </summary>
    [Fact]
    public void EveryPlannedPurchaseCarriesTheEpochItsWorldWasCollectedUnder()
    {
        var builder = new FrameBuilder().CollectedAtEpoch(12);
        var resource = builder.Resource(1_000);
        builder.Structure(StructureA, new[] { (resource, 1.0) });
        builder.Structure(StructureB, new[] { (resource, 2.0) });

        var actions = Plan(builder.Build(), Config(), out _);

        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.Equal(12, action.CollectedAtEpoch));
    }

    // ---- Harness --------------------------------------------------------------------------------

    private static IReadOnlyList<AutoBuyCycleAction> Plan(
        AutoBuyCycleFrame frame,
        SuiteRuntimeConfiguration config,
        out WakePolicy wake) =>
        Plan(frame, config, out wake, out _);

    private static IReadOnlyList<AutoBuyCycleAction> Plan(
        AutoBuyCycleFrame frame,
        SuiteRuntimeConfiguration config,
        out WakePolicy wake,
        out AutoBuyDecisionMetrics metrics)
    {
        var store = new ReusableActionStore<AutoBuyCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoBuyCycleAction>(store);
        wake = AutoBuyCycleEvaluator.Evaluate(in frame, in config, writer, out metrics);

        var actions = new List<AutoBuyCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }

        return actions;
    }

    private static void AssertProjection(
        ServiceProjectionEntry entry,
        int expectedKey,
        long expectedValue)
    {
        Assert.Equal(expectedKey, entry.Key.Value);
        Assert.Equal(ServiceProjectionValueKind.Integer, entry.Value.Kind);
        Assert.Equal(expectedValue, entry.Value.Integer);
    }

    private static void AssertSingleLevelAction(
        in AutoBuyCycleAction action,
        AutoBuyCandidateKind expectedKind,
        Guid expectedUuid,
        Guid expectedResource,
        double cost,
        double available)
    {
        Assert.Equal(expectedKind, action.Kind);
        Assert.Equal(expectedUuid, action.Uuid);
        Assert.Equal(1, action.Count);
        Assert.Equal(1, action.CollectedAtEpoch);

        var belief = action.Belief;
        Assert.True(belief.IsAvailable);
        Assert.False(belief.HasFiniteLevels);
        Assert.False(belief.IsMaxLevel);
        Assert.False(belief.IsMaxQueuedLevel);
        Assert.Equal(0, belief.CurrentLevel);
        Assert.Equal(0, belief.QueuedLevels);
        Assert.Equal(1, belief.CostResourceCount);
        Assert.Equal(1, belief.PricedResourceCount);
        Assert.Equal(cost / available, belief.CostRatio, 12);
        Assert.Equal(expectedResource, belief.BindingResourceId);
        Assert.False(belief.BindingIsBandwidth);
        Assert.Equal(cost, belief.BindingCost.ToDouble(), 12);
        Assert.Equal(available, belief.BindingAvailable.ToDouble(), 12);
        Assert.Equal(0.0, belief.BindingReserveFloor.ToDouble(), 12);

        var plannedSpend = Assert.Single(action.PlannedSpend.AsSpan().ToArray());
        Assert.Equal(expectedResource, plannedSpend.ResourceId);
        Assert.Equal(cost, plannedSpend.Cost.ToDouble(), 12);
        Assert.Equal(available, plannedSpend.RemainingBeforeSpend.ToDouble(), 12);
        Assert.Equal(0.0, plannedSpend.ReserveFloor.ToDouble(), 12);
    }

    private static SuiteRuntimeConfiguration Config(
        bool enabled = true,
        bool emergencyDisabled = false,
        AutoBuyOperationMode mode = AutoBuyOperationMode.Active,
        bool includeStructures = true,
        bool includeUpgrades = true,
        AutoBuyAffordabilityMode structureAffordability = AutoBuyAffordabilityMode.BuyAll,
        AutoBuyAffordabilityMode upgradeAffordability = AutoBuyAffordabilityMode.BuyAll,
        string absoluteReserve = "0",
        float relativeMultiplier = 0f) =>
        new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = enabled },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = emergencyDisabled },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = mode,
                IncludeStructures = includeStructures,
                IncludeUpgrades = includeUpgrades,
                StructureAffordability = structureAffordability,
                UpgradeAffordability = upgradeAffordability,
            },
            Reserves = new AutomataReserveConfiguration
            {
                AbsoluteReserve = absoluteReserve,
                RelativeReserveMultiplier = relativeMultiplier,
            },
        };

    private sealed class FrameBuilder
    {
        private readonly List<AutoBuyCandidateRow> _candidates = new();
        private readonly List<AutoBuyResourceRow> _resources = new();
        private readonly List<AutoBuyCostRow> _costs = new();
        private int _bulk = 1;
        private long _collectedAtEpoch = 1;

        public FrameBuilder CollectedAtEpoch(long epoch)
        {
            _collectedAtEpoch = epoch;
            return this;
        }

        public FrameBuilder Bulk(int bulk)
        {
            _bulk = bulk;
            return this;
        }

        /// <param name="headroom">
        /// Room left below the ceiling. What a bandwidth cost is paid out of, and nought for an
        /// uncapped resource — so a bandwidth pool described without one affords nothing, exactly as
        /// the game's own shortfall term says.
        /// </param>
        public int Resource(double quantity, bool bandwidth = false, double headroom = 0.0)
        {
            var capped = headroom > 0.0;
            _resources.Add(new AutoBuyResourceRow(
                Guid.NewGuid(),
                bandwidth,
                quantity,
                quantity,
                bandwidth ? headroom : quantity,
                1.0,
                1.0,
                hasCapacity: capped,
                capped ? quantity + headroom : default,
                isAvailable: true));
            return _resources.Count - 1;
        }

        public FrameBuilder Structure(
            Guid uuid,
            (int resourceIndex, double cost)[] costs,
            bool available = true,
            bool hasFiniteLevels = false,
            bool isMaxLevel = false,
            bool isMaxQueuedLevel = false,
            int queuedLevels = 0,
            bool meetsNextLevelRequirements = true,
            AutoBuyOwningViewStatus owningView = AutoBuyOwningViewStatus.Available) =>
            Candidate(
                AutoBuyCandidateKind.Structure, uuid, costs, available, hasFiniteLevels,
                isMaxLevel, isMaxQueuedLevel, queuedLevels, meetsNextLevelRequirements,
                owningView: owningView);

        public FrameBuilder Upgrade(
            Guid uuid,
            (int resourceIndex, double cost)[] costs,
            bool available = true,
            bool hasFiniteLevels = false,
            bool isMaxLevel = false,
            bool isMaxQueuedLevel = false,
            int queuedLevels = 0,
            bool meetsNextLevelRequirements = true,
            AutoBuyOwningViewStatus owningView = AutoBuyOwningViewStatus.Available) =>
            Candidate(
                AutoBuyCandidateKind.Upgrade, uuid, costs, available,
                hasFiniteLevels, isMaxLevel, isMaxQueuedLevel, queuedLevels,
                meetsNextLevelRequirements, owningView: owningView);

        public FrameBuilder GroupedStructure(
            Guid uuid,
            int resourceIndex,
            double nextCost,
            double exactGroupedCost,
            int? publishedGroupedLevels = null) =>
            Candidate(
                AutoBuyCandidateKind.Structure,
                uuid,
                new[] { (resourceIndex, nextCost) },
                available: true,
                hasFiniteLevels: false,
                isMaxLevel: false,
                isMaxQueuedLevel: false,
                queuedLevels: 0,
                meetsNextLevelRequirements: true,
                new[] { exactGroupedCost },
                publishedGroupedLevels);

        private FrameBuilder Candidate(
            AutoBuyCandidateKind kind,
            Guid uuid,
            (int resourceIndex, double cost)[] costs,
            bool available,
            bool hasFiniteLevels,
            bool isMaxLevel,
            bool isMaxQueuedLevel,
            int queuedLevels,
            bool meetsNextLevelRequirements,
            double[]? exactGroupedCosts = null,
            int? publishedGroupedLevels = null,
            AutoBuyOwningViewStatus owningView = AutoBuyOwningViewStatus.Available)
        {
            var start = _costs.Count;
            var groupedLevels = publishedGroupedLevels ?? Math.Max(
                1, Math.Min(WorldPurchaseGrouping.MaximumLevels,
                    kind == AutoBuyCandidateKind.Structure ? _bulk : 1));
            for (var index = 0; index < costs.Length; index++)
            {
                var (resourceIndex, cost) = costs[index];
                var groupedCost = exactGroupedCosts is null
                    ? cost * groupedLevels
                    : exactGroupedCosts[index];
                _costs.Add(new AutoBuyCostRow(resourceIndex, cost, groupedLevels, groupedCost));
            }

            _candidates.Add(new AutoBuyCandidateRow(
                kind,
                uuid,
                owningView,
                available,
                currentLevel: 0,
                queuedLevels,
                hasFiniteLevels,
                isMaxLevel,
                isMaxQueuedLevel,
                meetsNextLevelRequirements,
                start,
                costs.Length));
            return this;
        }

        public AutoBuyCycleFrame Build()
        {
            var global = new AutoBuyGlobalRow(_bulk, _collectedAtEpoch);

            return new AutoBuyCycleFrame(
                global,
                _candidates.ToArray(),
                _candidates.Count,
                _candidates.Count(candidate => candidate.Kind == AutoBuyCandidateKind.Structure),
                _candidates.Count(candidate => candidate.Kind == AutoBuyCandidateKind.Upgrade),
                _resources.ToArray(),
                _resources.Count,
                _costs.ToArray(),
                _costs.Count);
        }
    }
}
