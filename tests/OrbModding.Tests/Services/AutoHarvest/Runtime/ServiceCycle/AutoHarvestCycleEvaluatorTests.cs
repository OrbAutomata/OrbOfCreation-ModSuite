using System;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

public sealed class AutoHarvestCycleEvaluatorTests
{
    [Fact]
    public void FirstEligibleCyclePlansOnlyFruitAndWaitsForTheNextPublication()
    {
        var result = Evaluate(ReadyFrame(), Configuration(), InitialState(), Context(1));

        Assert.True(result.HasAction);
        Assert.Equal(AutoHarvestPair.FruitTree, result.Action.Pair);
        Assert.True(result.State.HasPlannedAction);
        Assert.Equal(AutoHarvestPair.FruitTree, result.State.PlannedPair);
        Assert.Equal(WakePolicyKind.OnPublication, result.Wake.Kind);
    }

    [Fact]
    public void OtherwiseReadyUnlatchedCandidateReachesTheActionBoundaryOncePerPublication()
    {
        var facts = new AutoHarvestPairFacts(
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Verified,
            PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation,
            AutoHarvestEvidenceState.Verified);
        var frame = new AutoHarvestCycleFrame(
            Captured(AutoHarvestPair.FruitTree, facts),
            Captured(AutoHarvestPair.TreasureTree, facts));

        var result = Evaluate(frame, Configuration(), InitialState(), Context(1));

        Assert.True(result.HasAction);
        Assert.Equal(AutoHarvestPair.FruitTree, result.Action.Pair);
        Assert.Equal(
            PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation,
            result.Action.Facts.Prerequisites);
        Assert.Equal(WakePolicyKind.OnPublication, result.Wake.Kind);
    }

    /// <summary>
    /// The planned action carries the facts of the pair it plans, so the boundary judges the same
    /// evidence this decision was taken on rather than re-reading the game for it.
    /// </summary>
    [Fact]
    public void ThePlannedActionCarriesTheSelectedPairsFacts()
    {
        var frame = new AutoHarvestCycleFrame(
            Captured(
                AutoHarvestPair.FruitTree,
                new AutoHarvestPairFacts(
                    AutoHarvestEvidenceState.Verified,
                    AutoHarvestEvidenceState.Verified,
                    AutoHarvestEvidenceState.Verified,
                    PlotActionPrerequisiteEvidence.NativeLatchedTrue,
                    AutoHarvestEvidenceState.Rejected)),
            Captured(AutoHarvestPair.TreasureTree, ReadyFacts()));

        var result = Evaluate(frame, Configuration(), InitialState(), Context(1));

        Assert.True(result.HasAction);
        Assert.Equal(AutoHarvestPair.TreasureTree, result.Action.Pair);
        Assert.Equal(AutoHarvestEvidenceState.Verified, result.Action.Facts.Readiness);
    }

    /// <summary>
    /// The planned action also carries the safety verdict drawn for that pair, and the plan is made
    /// without regard to it.
    /// </summary>
    /// <remarks>
    /// Whether an audited-unsafe action may run is the boundary's judgement, not the evaluator's. An
    /// evaluator that filtered on it here would hide the rejection from the boundary that reports it.
    /// </remarks>
    [Fact]
    public void ThePlannedActionCarriesTheSelectedPairsSafety()
    {
        var frame = new AutoHarvestCycleFrame(
            Captured(
                AutoHarvestPair.FruitTree,
                ReadyFacts(),
                AutoHarvestActionSafetyState.Destructive),
            Captured(AutoHarvestPair.TreasureTree, ReadyFacts()));

        var result = Evaluate(frame, Configuration(), InitialState(), Context(1));

        Assert.True(result.HasAction);
        Assert.Equal(AutoHarvestPair.FruitTree, result.Action.Pair);
        Assert.Equal(AutoHarvestActionSafetyState.Destructive, result.Action.Safety);
    }

    [Fact]
    public void CommittedReceiptRotatesTheNextEligiblePair()
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);
        var receipt = BatchReceipt.Completed(
            firstContext.Identity,
            new BatchId(1),
            1,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(20));

        var second = Evaluate(
            ReadyFrame(),
            Configuration(),
            first.State,
            Context(2, receipt));

        Assert.True(second.HasAction);
        Assert.Equal(AutoHarvestPair.TreasureTree, second.Action.Pair);
        Assert.Equal(AutoHarvestPair.TreasureTree, second.State.NextPair);
    }

    [Fact]
    public void RejectedReceiptDoesNotRotateFairness()
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);
        var terminal = ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
        var receipt = BatchReceipt.Terminated(
            firstContext.Identity,
            new BatchId(1),
            1,
            0,
            0,
            terminal,
            new ServiceNativeCallTotals(0, 0, 0),
            new MonotonicTimestamp(20));

        var second = Evaluate(
            ReadyFrame(),
            Configuration(),
            first.State,
            Context(2, receipt));

        Assert.True(second.HasAction);
        Assert.Equal(AutoHarvestPair.FruitTree, second.Action.Pair);
        Assert.Equal(AutoHarvestPair.FruitTree, second.State.NextPair);
    }

    /// <summary>
    /// A pair whose contract mismatch this service proved is not tried again, and its sibling still is.
    /// </summary>
    [Fact]
    public void ARememberedPairMismatchDoesNotBlockAnEligibleSibling()
    {
        var state = StateRemembering(
            new AutoHarvestFaultMemory(
                AutoHarvestFaultKind.None, AutoHarvestFaultKind.ContractUnavailable, AutoHarvestFaultKind.None));

        var result = Evaluate(ReadyFrame(), Configuration(), state, Context(1));

        Assert.True(result.HasAction);
        Assert.Equal(AutoHarvestPair.TreasureTree, result.Action.Pair);
        Assert.Equal(AutoHarvestPairHealthKind.ContractUnavailable, result.State.FruitHealth.Kind);
        Assert.False(result.State.FruitHealth.FeatureScoped);
        Assert.Equal(AutoHarvestPairHealthKind.Eligible, result.State.TreasureHealth.Kind);
    }

    /// <summary>
    /// A failure in something both pairs share stops both, however ready the world says they are.
    /// </summary>
    [Fact]
    public void ARememberedFeatureFaultBlocksAnEligibleSibling()
    {
        var state = StateRemembering(
            new AutoHarvestFaultMemory(
                AutoHarvestFaultKind.ContractUnavailable,
                AutoHarvestFaultKind.None,
                AutoHarvestFaultKind.None));

        var result = Evaluate(ReadyFrame(), Configuration(), state, Context(1));

        Assert.False(result.HasAction);
        Assert.False(result.State.HasPlannedAction);
        Assert.True(result.State.FruitHealth.FeatureScoped);
        Assert.True(result.State.TreasureHealth.FeatureScoped);
        Assert.Equal(
            AutoHarvestPairHealthKind.ContractUnavailable, result.State.TreasureHealth.Kind);
    }

    /// <summary>
    /// A world that holds no plots at all stops both pairs and is reported as the feature-wide thing
    /// it is.
    /// </summary>
    [Fact]
    public void AnUncollectedWorldBlocksBothPairs()
    {
        var frame = new AutoHarvestCycleFrame(
            AutoHarvestPairCapture.Unavailable(AutoHarvestPair.FruitTree),
            AutoHarvestPairCapture.Unavailable(AutoHarvestPair.TreasureTree));

        var result = Evaluate(frame, Configuration(), InitialState(), Context(1));

        Assert.False(result.HasAction);
        Assert.Equal(AutoHarvestPairHealthKind.RegistryNotReady, result.State.FruitHealth.Kind);
        Assert.True(result.State.FruitHealth.FeatureScoped);
    }

    /// <summary>
    /// A pair the world could not describe stops its sibling too.
    /// </summary>
    /// <remarks>
    /// The reason capture has left to report is that the game's registries have not been collected,
    /// which is not a thing one pair can suffer alone. Letting a sibling act on facts read from the
    /// same empty world would be acting on the absence of evidence.
    /// </remarks>
    [Fact]
    public void AnUnavailablePairStopsAnEligibleSibling()
    {
        var frame = new AutoHarvestCycleFrame(
            AutoHarvestPairCapture.Unavailable(AutoHarvestPair.FruitTree),
            Captured(AutoHarvestPair.TreasureTree, ReadyFacts()));

        var result = Evaluate(frame, Configuration(), InitialState(), Context(1));

        Assert.False(result.HasAction);
        Assert.Equal(AutoHarvestPairHealthKind.Eligible, result.State.TreasureHealth.Kind);
    }

    /// <summary>
    /// A code is not evidence on its own: only a batch that faulted says something went wrong.
    /// </summary>
    [Fact]
    public void ARejectedBatchCarryingAFaultCodeIsNotRemembered()
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);
        var receipt = BatchReceipt.Terminated(
            firstContext.Identity,
            new BatchId(1),
            1,
            0,
            0,
            ServiceActionResult.Rejected(AutoHarvestActionResultCodes.PairFaulted),
            new ServiceNativeCallTotals(0, 0, 0),
            new MonotonicTimestamp(20));

        var second = Evaluate(ReadyFrame(), Configuration(), first.State, Context(2, receipt));

        Assert.Equal(AutoHarvestPairHealthKind.Eligible, second.State.FruitHealth.Kind);
    }

    /// <summary>
    /// A remembered mismatch outranks what the world says: nothing in the world is evidence that the
    /// build's contract changed.
    /// </summary>
    [Fact]
    public void ARememberedMismatchOutranksAReadyWorld()
    {
        var state = StateRemembering(
            new AutoHarvestFaultMemory(
                AutoHarvestFaultKind.None,
                AutoHarvestFaultKind.ContractUnavailable,
                AutoHarvestFaultKind.None));

        var result = Evaluate(ReadyFrame(), Configuration(), state, Context(1));

        Assert.Equal(AutoHarvestPairHealthKind.ContractUnavailable, result.State.FruitHealth.Kind);
    }

    /// <summary>
    /// A faulted batch is remembered against the pair the plan named only when its code names a
    /// proven mismatch; a pair fault is left to the retry gate.
    /// </summary>
    [Theory]
    [InlineData(1027, (int)AutoHarvestPairHealthKind.Eligible, false)]
    [InlineData(1025, (int)AutoHarvestPairHealthKind.ContractUnavailable, false)]
    [InlineData(1026, (int)AutoHarvestPairHealthKind.ContractUnavailable, true)]
    public void AFaultedBatchIsRememberedAsTheFailureItsCodeNames(
        int code,
        int expectedHealth,
        bool featureScoped)
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);
        Assert.Equal(AutoHarvestPair.FruitTree, first.Action.Pair);

        var second = Evaluate(
            ReadyFrame(),
            Configuration(),
            first.State,
            Context(2, FaultedReceipt(firstContext, new ServiceActionResultCode(code, "expected"))));

        Assert.Equal((AutoHarvestPairHealthKind)expectedHealth, second.State.FruitHealth.Kind);
        Assert.Equal(featureScoped, second.State.FruitHealth.FeatureScoped);
        Assert.Equal(
            featureScoped
                ? (AutoHarvestPairHealthKind)expectedHealth
                : AutoHarvestPairHealthKind.Eligible,
            second.State.TreasureHealth.Kind);
    }

    /// <summary>
    /// A remembered mismatch stays remembered for the lifecycle, even across a batch that says nothing.
    /// </summary>
    [Fact]
    public void ARememberedMismatchSurvivesALaterCycleThatReportsNothing()
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);
        var second = Evaluate(
            ReadyFrame(),
            Configuration(),
            first.State,
            Context(2, FaultedReceipt(firstContext, AutoHarvestActionResultCodes.PairContractUnavailable)));

        var third = Evaluate(ReadyFrame(), Configuration(), second.State, Context(3));

        Assert.Equal(AutoHarvestPairHealthKind.ContractUnavailable, third.State.FruitHealth.Kind);
    }

    /// <summary>
    /// A rejection says the action was declined, not that it went wrong, and is not remembered.
    /// </summary>
    [Fact]
    public void ARejectedBatchIsNotRememberedAsAFault()
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);
        var terminal = ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
        var receipt = BatchReceipt.Terminated(
            firstContext.Identity,
            new BatchId(1),
            1,
            0,
            0,
            terminal,
            new ServiceNativeCallTotals(0, 0, 0),
            new MonotonicTimestamp(20));

        var second = Evaluate(ReadyFrame(), Configuration(), first.State, Context(2, receipt));

        Assert.Equal(AutoHarvestPairHealthKind.Eligible, second.State.FruitHealth.Kind);
    }

    /// <summary>
    /// A fault code this service does not attribute to a pair leaves the memory alone rather than
    /// blaming whichever pair happened to be planned.
    /// </summary>
    [Fact]
    public void AFaultedBatchWithAnUnattributedCodeIsNotRemembered()
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);

        var second = Evaluate(
            ReadyFrame(),
            Configuration(),
            first.State,
            Context(2, FaultedReceipt(firstContext, CommonActionResultCodes.AdapterFault)));

        Assert.Equal(AutoHarvestPairHealthKind.Eligible, second.State.FruitHealth.Kind);
    }

    private static BatchReceipt FaultedReceipt(
        in ServiceCycleContext context,
        ServiceActionResultCode code) =>
        BatchReceipt.Terminated(
            context.Identity,
            new BatchId(1),
            1,
            0,
            0,
            ServiceActionResult.Faulted(code),
            new ServiceNativeCallTotals(0, 0, 0),
            new MonotonicTimestamp(20));

    private static AutoHarvestCycleState StateRemembering(AutoHarvestFaultMemory faults) =>
        AutoHarvestCycleState.Restore(
            new LifecycleGeneration(1),
            AutoHarvestPair.FruitTree,
            hasPlannedAction: false,
            default,
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.FruitTree),
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree),
            faults);

    private static AutoHarvestCycleEvaluation Evaluate(
        in AutoHarvestCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in AutoHarvestCycleState state,
        in ServiceCycleContext context) =>
        new AutoHarvestCycleEvaluator().Evaluate(frame, config, state, context);

    private static AutoHarvestCycleFrame ReadyFrame()
    {
        var facts = ReadyFacts();
        var fruit = Captured(AutoHarvestPair.FruitTree, facts);
        var treasure = Captured(AutoHarvestPair.TreasureTree, facts);
        return new AutoHarvestCycleFrame(fruit, treasure);
    }

    private static AutoHarvestPairCapture Captured(
        AutoHarvestPair pair,
        in AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety =
            AutoHarvestActionSafetyState.NativePhaseCyclePreserving) =>
        AutoHarvestPairCapture.Captured(pair, facts, safety);

    private static AutoHarvestPairFacts ReadyFacts() => new(
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified,
        PlotActionPrerequisiteEvidence.NativeLatchedTrue,
        AutoHarvestEvidenceState.Verified);

    private static SuiteRuntimeConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: true);

    private static AutoHarvestCycleState InitialState() =>
        AutoHarvestCycleState.Create(new LifecycleGeneration(1));

    private static ServiceCycleContext Context(ulong cycle, BatchReceipt receipt = default)
    {
        var identity = new ServiceCycleIdentity(
            new ServiceId("orbautomata.auto-harvest.service-cycle"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(cycle));
        return new ServiceCycleContext(identity, receipt, new MonotonicTimestamp(10 + (long)cycle));
    }
}
