using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Policy;

/// <summary>
/// Auto Harvest's five world facts, read out of a snapshot the real collector built.
/// </summary>
/// <remarks>
/// Driven through the collector rather than from a hand-built <see cref="GameWorldState"/> on
/// purpose: the claim being tested is that the shared snapshot is <em>sufficient</em> for this
/// service, and a hand-built world would agree with whatever this file expected while proving
/// nothing about the pass that fills it.
/// </remarks>
public sealed class AutoHarvestWorldFactsTests : IDisposable
{
    public AutoHarvestWorldFactsTests() => Clear();

    public void Dispose() => Clear();

    private static void Clear()
    {
        OrbModding.GameStubs.GameStubState.Reset();
    }

    [Fact]
    public void AReadyPairIsVerifiedOnEveryWorldFact()
    {
        var (plot, action) = Pair();

        var facts = Facts(plot, action);

        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.Identity);
        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.PlotVisibility);
        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.ActionAvailability);
        Assert.Equal(PlotActionPrerequisiteEvidence.NativeLatchedTrue, facts.Prerequisites);
        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.Readiness);
    }

    /// <summary>
    /// A pair the world does not describe fails on identity, and does not report the rest as
    /// separately unknown.
    /// </summary>
    [Fact]
    public void APairTheWorldDoesNotHoldIsUnknownThroughout()
    {
        var facts = AutoHarvestWorldFacts.For(
            Collect(),
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.Equal(AutoHarvestEvidenceState.Unknown, facts.Identity);
        Assert.Equal(AutoHarvestEvidenceState.Unknown, facts.PlotVisibility);
        Assert.Equal(AutoHarvestEvidenceState.Unknown, facts.ActionAvailability);
        Assert.Equal(PlotActionPrerequisiteEvidence.Unknown, facts.Prerequisites);
        Assert.Equal(AutoHarvestEvidenceState.Unknown, facts.Readiness);
    }

    [Fact]
    public void AnInvisiblePlotRejectsVisibilityAndNothingElse()
    {
        var (plot, action) = Pair();
        plot.visible = false;

        var facts = Facts(plot, action);

        Assert.Equal(AutoHarvestEvidenceState.Rejected, facts.PlotVisibility);
        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.Readiness);
    }

    /// <summary>
    /// An action the plot names twice is not one the pair can be decided about.
    /// </summary>
    /// <remarks>
    /// The count exists for this. A flag would report the duplicate as ordinary availability, and the
    /// service would act on a pair whose meaning it cannot pin down.
    /// </remarks>
    [Fact]
    public void AnActionOfferedTwiceRejectsAvailability()
    {
        var (plot, action) = Pair();
        plot.availableActions.Add(action);

        Assert.Equal(
            AutoHarvestEvidenceState.Rejected,
            Facts(plot, action).ActionAvailability);
    }

    /// <summary>
    /// Without exactly one instance there is nothing for the action boundary to submit into, so the
    /// two facts that describe a run are unknown rather than rejected.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WithoutExactlyOneInstanceRunFactsAreUnknown(int instances)
    {
        var (plot, action) = Pair(instances: instances);

        var facts = Facts(plot, action);

        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.Identity);
        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.ActionAvailability);
        Assert.Equal(PlotActionPrerequisiteEvidence.Unknown, facts.Prerequisites);
        Assert.Equal(AutoHarvestEvidenceState.Unknown, facts.Readiness);
    }

    [Fact]
    public void AFalseLatchRequestsNativeValidationRatherThanClaimingFailure()
    {
        var (plot, action) = Pair();
        action.prerequisites.available = false;

        Assert.Equal(
            PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation,
            Facts(plot, action).Prerequisites);

        var decision = AutoHarvestPolicy.EvaluatePair(
            AutoHarvestPair.FruitTree,
            selected: true,
            Facts(plot, action));
        Assert.True(decision.ShouldSubmit, decision.RejectionReason.ToString());
    }

    /// <summary>
    /// An action that would spend more than one of the plot per run is not harvested.
    /// </summary>
    [Fact]
    public void AnActionCostingMoreThanOneRejectsReadiness()
    {
        var (plot, action) = Pair();
        action.elementCost = 2;

        Assert.Equal(
            AutoHarvestEvidenceState.Rejected,
            Facts(plot, action).Readiness);
    }

    /// <summary>
    /// A cost the world could not compute is not treated as a cost of one.
    /// </summary>
    /// <remarks>
    /// An action that takes its size from other nodes publishes no price, and the unscaled authored
    /// cost happens to be one here. Reading the number without checking whether it is known would
    /// call this pair ready.
    /// </remarks>
    [Fact]
    public void AnUnknownCostRejectsReadiness()
    {
        var (plot, action) = Pair();
        action.useSizeModForCost = true;
        action.sizeModNodes.Add(new PlotNodeSO());

        Assert.Equal(
            AutoHarvestEvidenceState.Rejected,
            Facts(plot, action).Readiness);
    }

    /// <summary>
    /// A plot whose quantity is already claimed by other actions has nothing left to harvest.
    /// </summary>
    [Fact]
    public void AFullyClaimedPlotRejectsReadiness()
    {
        var (plot, action) = Pair();
        plot.actionQuantityUsageMain = new ValueModifierRecord(new BigDouble(4d, 0));

        Assert.Equal(
            AutoHarvestEvidenceState.Rejected,
            Facts(plot, action).Readiness);
    }

    /// <summary>
    /// The whole point, stated as one assertion: the policy admits a pair on snapshot facts alone.
    /// </summary>
    [Fact]
    public void ThePolicyAdmitsAPairJudgedEntirelyFromTheSnapshot()
    {
        var (plot, action) = Pair();

        var decision = AutoHarvestPolicy.EvaluatePair(
            AutoHarvestPair.FruitTree,
            selected: true,
            Facts(plot, action));

        Assert.True(decision.ShouldSubmit, decision.RejectionReason.ToString());
    }

    /// <summary>
    /// Readiness is a conjunction, and each term is asserted alone.
    /// </summary>
    /// <remarks>
    /// Built from published rows rather than collected ones, because the deriver cannot produce some
    /// of these combinations and that is exactly what makes them worth stating. An unknown cost
    /// publishes zero today, so <c>ElementCostKnown</c> would be unreachable through the collector
    /// even though dropping it is the mistake W36 warns about: a later deriver that published the
    /// unscaled cost instead would make this pair look ready at a price the plot cannot pay.
    /// </remarks>
    [Theory]
    [InlineData(true, 1, true, true, 1, true)]
    [InlineData(false, 1, true, true, 1, false)]
    [InlineData(true, 0, true, true, 1, false)]
    [InlineData(true, 1, false, true, 1, false)]
    [InlineData(true, 1, true, false, 1, false)]
    [InlineData(true, 1, true, true, 0, false)]
    public void EveryReadinessTermIsLoadBearing(
        bool costKnown,
        int cost,
        bool remaining,
        bool hasEnough,
        int maximumRuns,
        bool expected)
    {
        var plotId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        Assert.Equal(
            expected,
            AutoHarvestWorldFacts.IsReady(
                WorldSamples.PlotNode(plotId, remainingQuantity: remaining ? 3 : 0),
                WorldSamples.PlotAction(
                    plotId,
                    actionId,
                    elementCost: cost,
                    elementCostKnown: costKnown,
                    hasEnoughForOneInstance: hasEnough,
                    maximumRemainingInstances: maximumRuns)));
    }

    /// <summary>
    /// A plot offering one action, running one instance of it, with four idle and nothing claimed.
    /// </summary>
    /// <remarks>
    /// Four rather than one so that a reader confusing "remaining" with "how many runs fit" would
    /// still be wrong about something, and so a stray claim shows up as a change rather than as the
    /// same zero it started from.
    /// </remarks>
    private static (PlotNodeSO Plot, PlotNodeActionSO Action) Pair(int instances = 1)
    {
        var action = new PlotNodeActionSO { elementCost = 1 };
        action.prerequisites.available = true;
        PlotNodeActionSO.All.Add(action);

        var plot = new PlotNodeSO { visible = true };
        plot.phaseInstances.Add(new PlotNodePhaseInstance(PlotNodePhases.Idle, 4));
        plot.availableActions.Add(action);
        for (var index = 0; index < instances; index++)
            plot.GetActionInstances().Add(new PlotNodeActionInstance(action));
        PlotNodeSO.All.Add(plot);

        return (plot, action);
    }

    private static AutoHarvestPairFacts Facts(PlotNodeSO plot, PlotNodeActionSO action) =>
        AutoHarvestWorldFacts.For(
            Collect(),
            plot.GetGuid(),
            action.GetGuid());

    private static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        collector.Collect();
        return collector.Build();
    }

}
