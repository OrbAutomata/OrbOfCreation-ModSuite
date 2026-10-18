using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoHarvestPolicyTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteSerializedContractValuesNeverMatch(double actual)
    {
        Assert.False(AutoHarvestContractValues.IsFiniteNear(actual, 1.0));
    }

    [Fact]
    public void FiniteSerializedContractValuesRequirePositiveNearEquality()
    {
        Assert.True(AutoHarvestContractValues.IsFiniteNear(3.00005, 3.0));
        Assert.False(AutoHarvestContractValues.IsFiniteNear(3.01, 3.0));
        Assert.False(AutoHarvestContractValues.IsFiniteNear(3.0, 3.0, -0.1));
    }

    [Fact]
    public void SupportedUuidPairOnReplacementReferencesIsContradictory()
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            AutoHarvestKnownIds.FruitTreePlot,
            AutoHarvestKnownIds.FruitTreeCollect,
            exactFruitReferences: false,
            exactTreasureReferences: false,
            supportedActionReference: false);

        Assert.Equal(AutoHarvestObservedPair.Contradictory, observed);
    }

    [Theory]
    [InlineData("", AutoHarvestKnownIds.FruitTreeCollect)]
    [InlineData("not-a-uuid", AutoHarvestKnownIds.FruitTreeCollect)]
    [InlineData(AutoHarvestKnownIds.FruitTreePlot, "")]
    [InlineData(AutoHarvestKnownIds.FruitTreePlot, "not-a-uuid")]
    public void UnreadableObservedIdentityIsContradictory(string plotUuid, string actionUuid)
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            plotUuid,
            actionUuid,
            exactFruitReferences: false,
            exactTreasureReferences: false,
            supportedActionReference: false);

        Assert.Equal(AutoHarvestObservedPair.Contradictory, observed);
    }

    [Fact]
    public void SupportedActionReferenceOnUnexpectedPlotIsContradictory()
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            exactFruitReferences: false,
            exactTreasureReferences: false,
            supportedActionReference: true);

        Assert.Equal(AutoHarvestObservedPair.Contradictory, observed);
    }

    [Fact]
    public void UnrelatedActionOnSupportedPlotRemainsUnrelated()
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            AutoHarvestKnownIds.FruitTreePlot,
            Guid.NewGuid().ToString("D"),
            exactFruitReferences: false,
            exactTreasureReferences: false,
            supportedActionReference: false);

        Assert.Equal(AutoHarvestObservedPair.Unrelated, observed);
    }

    [Fact]
    public void ExactCurrentReferencesRemainRecognized()
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            AutoHarvestKnownIds.FruitTreePlot,
            AutoHarvestKnownIds.FruitTreeCollect,
            exactFruitReferences: true,
            exactTreasureReferences: false,
            supportedActionReference: true);

        Assert.Equal(AutoHarvestObservedPair.FruitTree, observed);
    }

    [Fact]
    public void PolicyConstantsMatchGeneratedTypedIdentities()
    {
        Assert.Equal(AutoHarvestKnownIds.FruitTreePlot, KnownEntities.FruitTreePlot.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.FruitTreeCollect, KnownEntities.FruitTreeCollect.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.TreasureTreePlot, KnownEntities.TreasureTreePlot.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.TreasureTreeCollect, KnownEntities.TreasureTreeCollect.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.ActivePlotNodeActions, KnownEntities.ActivePlotNodeActions.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.CompletionScalingWeight, KnownEntities.CompletionScalingWeight.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.TreasureTreeRewardPool, KnownEntities.TreasureTreeRewardPool.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.FruitTreeRewardPool, KnownEntities.FruitTreeRewardPool.Uuid.ToString("D"));
    }

    [Theory]
    [InlineData((int)AutoHarvestPair.FruitTree)]
    [InlineData((int)AutoHarvestPair.TreasureTree)]
    public void SelectedReadyNativePhaseCyclePairIsSubmitted(int pairValue)
    {
        var decision = Evaluate(ReadyFacts(), (AutoHarvestPair)pairValue);

        Assert.True(decision.ShouldSubmit);
        Assert.Equal(AutoHarvestRejectionReason.None, decision.RejectionReason);
    }

    [Fact]
    public void UnsupportedPairIsRejected()
    {
        var decision = Evaluate(ReadyFacts(), (AutoHarvestPair)99);

        AssertRejected(decision, AutoHarvestRejectionReason.UnsupportedPair);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected)]
    public void IdentityMustBePositivelyVerified(int identityValue)
    {
        var decision = Evaluate(ReadyFacts(
            identity: (AutoHarvestEvidenceState)identityValue));

        AssertRejected(decision, AutoHarvestRejectionReason.IdentityUnverified);
    }

    [Fact]
    public void CandidateMustBeExplicitlySelected()
    {
        var decision = Evaluate(ReadyFacts(), selected: false);

        AssertRejected(decision, AutoHarvestRejectionReason.NotSelected);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown, (int)AutoHarvestRejectionReason.PlotVisibilityUnknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected, (int)AutoHarvestRejectionReason.PlotNotVisible)]
    public void PlotVisibilityFailsClosed(int stateValue, int expectedValue)
    {
        var decision = Evaluate(ReadyFacts(
            plotVisibility: (AutoHarvestEvidenceState)stateValue));

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown, (int)AutoHarvestRejectionReason.ActionAvailabilityUnknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected, (int)AutoHarvestRejectionReason.ActionUnavailable)]
    public void ActionAvailabilityFailsClosed(int stateValue, int expectedValue)
    {
        var decision = Evaluate(ReadyFacts(
            actionAvailability: (AutoHarvestEvidenceState)stateValue));

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    /// <summary>
    /// A genuinely missing reading refuses. A false latch is different: it is a bounded request for
    /// exact native validation at the action boundary, so an otherwise-ready candidate proceeds.
    /// </summary>
    [Fact]
    public void UnknownPrerequisitesFailClosed()
    {
        var decision = Evaluate(ReadyFacts(
            prerequisites: PlotActionPrerequisiteEvidence.Unknown));

        AssertRejected(decision, AutoHarvestRejectionReason.PrerequisitesUnknown);
    }

    [Fact]
    public void AnUnlatchedCandidateProceedsToNativeValidation()
    {
        var decision = Evaluate(ReadyFacts(
            prerequisites: PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation));

        Assert.True(decision.ShouldSubmit);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown, (int)AutoHarvestRejectionReason.ReadinessUnknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected, (int)AutoHarvestRejectionReason.NotReady)]
    public void NativeReadinessFailsClosed(int stateValue, int expectedValue)
    {
        var decision = Evaluate(ReadyFacts(
            readiness: (AutoHarvestEvidenceState)stateValue));

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    /// <summary>
    /// Safety is judged at the action boundary, against the live objects, and before the queue.
    /// </summary>
    /// <remarks>
    /// Before the queue because an unsafe action is unsafe whether or not there is room to run it,
    /// and a rejection naming the full queue would read as "try again later" for something that must
    /// never be submitted at all.
    /// </remarks>
    [Theory]
    [InlineData((int)AutoHarvestActionSafetyState.Unknown, (int)AutoHarvestRejectionReason.PreservationUnknown)]
    [InlineData((int)AutoHarvestActionSafetyState.Destructive, (int)AutoHarvestRejectionReason.DestructiveAction)]
    [InlineData((int)AutoHarvestActionSafetyState.ResourceDrain, (int)AutoHarvestRejectionReason.ResourceDrainPresent)]
    [InlineData((int)AutoHarvestActionSafetyState.UnsafeCompletionEffects, (int)AutoHarvestRejectionReason.UnsafeCompletionEffects)]
    public void OnlyAuditedNativePhaseCycleActionsAreSafe(int stateValue, int expectedValue)
    {
        var safety = (AutoHarvestActionSafetyState)stateValue;

        AssertRejected(
            Submit(ReadyFacts(), Queue(), safety: safety),
            (AutoHarvestRejectionReason)expectedValue);
        AssertRejected(
            Submit(ReadyFacts(), Queue(empty: 0), safety: safety),
            (AutoHarvestRejectionReason)expectedValue);
    }

    /// <summary>
    /// The world facts alone cannot tell whether the action is one the suite audited.
    /// </summary>
    [Fact]
    public void AnUnsafeActionIsStillAdmittedByTheWorldFactsAlone()
    {
        Assert.True(Evaluate(ReadyFacts()).ShouldSubmit);
        AssertRejected(
            Submit(ReadyFacts(), Queue(), safety: AutoHarvestActionSafetyState.Destructive),
            AutoHarvestRejectionReason.DestructiveAction);
    }

    /// <summary>
    /// The queue is judged only at the action boundary, and only after every world fact has passed.
    /// </summary>
    /// <remarks>
    /// Ordering matters here: a busy or full queue must not mask a pair that was never going to be
    /// harvested anyway, or the rejection reason names the wrong thing and the diagnostics send
    /// someone looking at the action list.
    /// </remarks>
    [Fact]
    public void ADecisionWithoutTheQueueIsAdmittedAndTheBoundaryStillAsks()
    {
        Assert.True(Evaluate(ReadyFacts()).ShouldSubmit);

        AssertRejected(
            Submit(ReadyFacts(), Queue(supported: 1)),
            AutoHarvestRejectionReason.AlreadyQueuedOrRunning);
        AssertRejected(
            Submit(ReadyFacts(), Queue(empty: 0)),
            AutoHarvestRejectionReason.NoActionSlot);
        AssertRejected(
            Submit(ReadyFacts(readiness: AutoHarvestEvidenceState.Rejected), Queue(empty: 0)),
            AutoHarvestRejectionReason.NotReady);
    }

    [Fact]
    public void ANativeEmptyEntryDenialRejectsTheSlotEvenWithEntriesCounted()
    {
        AssertRejected(
            Submit(ReadyFacts(), Queue(empty: 1, nativeHasEmptyEntry: false)),
            AutoHarvestRejectionReason.NoActionSlot);
    }

    [Fact]
    public void AQueueWithRoomAndNothingRunningAdmitsTheSubmission()
    {
        Assert.True(Submit(ReadyFacts(), Queue()).ShouldSubmit);
    }

    private static AutoHarvestPairDecision Submit(
        AutoHarvestPairFacts facts,
        in AutoHarvestSubmissionState queue,
        AutoHarvestPair pair = AutoHarvestPair.FruitTree,
        AutoHarvestActionSafetyState safety =
            AutoHarvestActionSafetyState.NativePhaseCyclePreserving) =>
        AutoHarvestPolicy.EvaluateSubmission(pair, in facts, safety, in queue);

    private static AutoHarvestSubmissionState Queue(
        int empty = 1,
        int supported = 0,
        bool nativeHasEmptyEntry = true) =>
        new(
            isValid: true,
            usedEntryCount: 0,
            emptyEntryCount: empty,
            nativeHasEmptyEntry,
            supportedCollectCount: supported,
            pairMatchCount: 0,
            pairQuantity: 0,
            pairEngaged: false);

    private static AutoHarvestPairDecision Evaluate(
        AutoHarvestPairFacts facts,
        AutoHarvestPair pair = AutoHarvestPair.FruitTree,
        bool selected = true) =>
        AutoHarvestPolicy.EvaluatePair(pair, selected, in facts);

    private static AutoHarvestPairFacts ReadyFacts(
        AutoHarvestEvidenceState identity = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState plotVisibility = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState actionAvailability = AutoHarvestEvidenceState.Verified,
        PlotActionPrerequisiteEvidence prerequisites =
            PlotActionPrerequisiteEvidence.NativeLatchedTrue,
        AutoHarvestEvidenceState readiness = AutoHarvestEvidenceState.Verified) =>
        new(
            identity,
            plotVisibility,
            actionAvailability,
            prerequisites,
            readiness);

    private static void AssertRejected(
        AutoHarvestPairDecision decision,
        AutoHarvestRejectionReason expectedReason)
    {
        Assert.False(decision.ShouldSubmit);
        Assert.Equal(expectedReason, decision.RejectionReason);
    }
}
