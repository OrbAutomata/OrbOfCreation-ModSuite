using System;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

public sealed class AutoHarvestSemanticParityTests
{
    [Theory]
    [InlineData(DecisionScenario.BothReady, true, 0,
        FeatureStatusState.Operational, FeatureStatusReasonCode.None)]
    [InlineData(DecisionScenario.FruitLocked, true, 1,
        FeatureStatusState.Operational, FeatureStatusReasonCode.None)]
    [InlineData(DecisionScenario.FruitPairUnavailable, true, 1,
        FeatureStatusState.Degraded, FeatureStatusReasonCode.PartialCapabilityUnavailable)]
    [InlineData(DecisionScenario.FeatureUnavailable, false, -1,
        FeatureStatusState.NotReady, FeatureStatusReasonCode.RegistryNotReady)]
    [InlineData(DecisionScenario.BothNativeBusy, false, -1,
        FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.NativeBusy)]
    [InlineData(DecisionScenario.OnlyTreasureSelected, true, 1,
        FeatureStatusState.Operational, FeatureStatusReasonCode.None)]
    public void FrozenLegacyDecisionMatrixMatchesServiceCycle(
        DecisionScenario scenario,
        bool expectedAction,
        int expectedPair,
        FeatureStatusState expectedHealth,
        FeatureStatusReasonCode expectedReason)
    {
        var input = CreateInput(scenario);
        var result = Evaluate(input, InitialState(scenario), Context(1));
        var health = AutoHarvestFeatureStatusProjector.Project(
            result.State.FruitHealth,
            result.State.TreasureHealth);
        var pair = result.HasAction ? (int)result.Action.Pair : -1;

        Assert.Equal(expectedAction, result.HasAction);
        Assert.Equal(expectedPair, pair);
        Assert.Equal(expectedHealth, health.State);
        Assert.Equal(expectedReason, health.Reason);
    }

    [Theory]
    [InlineData(true, (int)AutoHarvestPair.TreasureTree)]
    [InlineData(false, (int)AutoHarvestPair.FruitTree)]
    public void FrozenLegacyFairnessRotatesOnlyAfterCommittedMutation(
        bool committed,
        int expectedNext)
    {
        var input = CreateInput(DecisionScenario.BothReady);
        var firstContext = Context(1);
        var first = Evaluate(input, InitialState(DecisionScenario.BothReady), firstContext);
        var receipt = committed
            ? BatchReceipt.Completed(
                firstContext.Identity,
                new BatchId(1),
                1,
                new ServiceNativeCallTotals(1, 1, 1),
                new MonotonicTimestamp(20))
            : BatchReceipt.Terminated(
                firstContext.Identity,
                new BatchId(1),
                1,
                0,
                0,
                ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected),
                new ServiceNativeCallTotals(0, 0, 0),
                new MonotonicTimestamp(20));

        var second = Evaluate(input, first.State, Context(2, receipt));

        Assert.True(second.HasAction);
        Assert.Equal(expectedNext, (int)second.Action.Pair);
    }

    private static AutoHarvestCycleEvaluation Evaluate(
        in CycleInput input,
        in AutoHarvestCycleState state,
        in ServiceCycleContext context) =>
        new AutoHarvestCycleEvaluator().Evaluate(
            input.Frame,
            input.Config,
            state,
            context);

    private static CycleInput CreateInput(DecisionScenario scenario)
    {
        var fruitSelected = scenario != DecisionScenario.OnlyTreasureSelected;
        var fruit = scenario switch
        {
            DecisionScenario.FruitLocked => Captured(
                AutoHarvestPair.FruitTree,
                Facts(actionAvailability: AutoHarvestEvidenceState.Rejected)),
            DecisionScenario.FeatureUnavailable =>
                AutoHarvestPairCapture.Unavailable(AutoHarvestPair.FruitTree),
            DecisionScenario.BothNativeBusy => Captured(
                AutoHarvestPair.FruitTree,
                Facts(readiness: AutoHarvestEvidenceState.Rejected)),
            DecisionScenario.OnlyTreasureSelected =>
                AutoHarvestPairCapture.NotSelected(AutoHarvestPair.FruitTree),
            _ => Captured(AutoHarvestPair.FruitTree, Facts()),
        };
        var treasureFacts = scenario == DecisionScenario.BothNativeBusy
            ? Facts(readiness: AutoHarvestEvidenceState.Rejected)
            : Facts();
        var frame = new AutoHarvestCycleFrame(
            fruit,
            Captured(AutoHarvestPair.TreasureTree, treasureFacts));
        var config = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected,
            treasureSelected: true);
        return new CycleInput(frame, config);
    }

    private static AutoHarvestPairCapture Captured(
        AutoHarvestPair pair,
        in AutoHarvestPairFacts facts) =>
        AutoHarvestPairCapture.Captured(
            pair, facts, AutoHarvestActionSafetyState.NativePhaseCyclePreserving);

    private static AutoHarvestPairFacts Facts(
        AutoHarvestEvidenceState actionAvailability = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState readiness = AutoHarvestEvidenceState.Verified) => new(
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified,
        actionAvailability,
        PlotActionPrerequisiteEvidence.NativeLatchedTrue,
        readiness);

    /// <summary>
    /// The state each scenario starts from.
    /// </summary>
    /// <remarks>
    /// One scenario needs a state rather than a frame: a pair that is unavailable on its own is a
    /// failure this service remembers about itself, not something the world reports, so it starts
    /// from a remembered fault. See W45.
    /// </remarks>
    private static AutoHarvestCycleState InitialState(DecisionScenario scenario)
    {
        var fresh = AutoHarvestCycleState.Create(new LifecycleGeneration(1));
        return scenario == DecisionScenario.FruitPairUnavailable
            ? AutoHarvestCycleState.Restore(
                fresh.Lifecycle,
                fresh.NextPair,
                fresh.HasPlannedAction,
                fresh.PlannedPair,
                fresh.FruitHealth,
                fresh.TreasureHealth,
                fresh.Faults.With(AutoHarvestPair.FruitTree, AutoHarvestFaultKind.Faulted))
            : fresh;
    }

    private readonly struct CycleInput
    {
        internal CycleInput(in AutoHarvestCycleFrame frame, SuiteRuntimeConfiguration config)
        {
            Frame = frame;
            Config = config;
        }

        internal AutoHarvestCycleFrame Frame { get; }
        internal SuiteRuntimeConfiguration Config { get; }
    }

    private static ServiceCycleContext Context(ulong cycle, BatchReceipt receipt = default)
    {
        var identity = new ServiceCycleIdentity(
            AutoHarvestServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(cycle));
        return new ServiceCycleContext(
            identity,
            receipt,
            new MonotonicTimestamp(10 + (long)cycle));
    }

    public enum DecisionScenario
    {
        BothReady,
        FruitLocked,
        FruitPairUnavailable,
        FeatureUnavailable,
        BothNativeBusy,
        OnlyTreasureSelected,
    }
}
