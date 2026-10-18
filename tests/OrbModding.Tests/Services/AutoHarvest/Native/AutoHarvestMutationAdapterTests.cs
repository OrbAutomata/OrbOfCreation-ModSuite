using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestMutationAdapterTests
{
    [Fact]
    public void InitialCaptureFailurePreservesNativeFailureEvidence()
    {
        var adapter = new AutoHarvestMutationAdapter(new ThrowingStatePort());

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving, NotQuarantined);

        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.Equal(NativeMutationOutcome.BeforeCaptureFailed, result.Mutation);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
        var mapped = result.ToResult();
        Assert.Equal(ServiceActionDisposition.Faulted, mapped.Disposition);
        Assert.True(mapped.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.BeforeCaptureFailed, mapped.NativeEvidence.Outcome);
    }

    /// <summary>
    /// The facts the boundary judges are the ones the action carries. A rejected carried plan stops
    /// before the additional live click-time terms are read.
    /// </summary>
    /// <remarks>
    /// A pair the world said was not ready is refused without a second opinion. The prerequisite is
    /// deliberately different because a false published latch is not an answer; it is validated by
    /// the domain container directly, never through <c>PlotNodeActionInstance.IsVisible()</c>.
    /// </remarks>
    [Fact]
    public void CarriedFactsAreWhatTheSubmissionDecisionJudges()
    {
        var state = new RecordingStatePort();
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(
            ResolvedPair(),
            ReadyFacts(readiness: AutoHarvestEvidenceState.Rejected),
            Preserving, NotQuarantined);

        Assert.Equal(CommonActionResultCodes.PolicyRejected, result.Code);
        Assert.Equal(1, state.CaptureCount);
        Assert.Equal(0, state.PrototypeCount);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    [Fact]
    public void FreshFalsePrerequisiteResultRefusesBeforeQueueOrQuantityMutation()
    {
        var state = new RecordingStatePort();
        var action = new PlotNodeActionSO();
        action.prerequisites.NativeCheckResult = false;
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(
            ResolvedPair(action),
            ReadyFacts(PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation),
            Preserving, NotQuarantined);

        Assert.Equal(
            AutoHarvestActionResultCodes.NativePrerequisitesCurrentlyUnmet,
            result.Code);
        Assert.Equal(1, action.prerequisites.CheckCalls);
        Assert.Equal(0, state.CaptureCount);
        Assert.Equal(0, state.PrototypeCount);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    [Fact]
    public void SuccessfulColdValidationIsCalledOnceBeforePolicyRejection()
    {
        var state = new RecordingStatePort();
        var action = new PlotNodeActionSO();
        action.prerequisites.NativeCheckResult = true;
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(
            ResolvedPair(action),
            ReadyFacts(
                PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation,
                readiness: AutoHarvestEvidenceState.Rejected),
            Preserving, NotQuarantined);

        Assert.Equal(CommonActionResultCodes.PolicyRejected, result.Code);
        Assert.Equal(1, action.prerequisites.CheckCalls);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    [Fact]
    public void UnreadablePrerequisiteContainerHasItsOwnPenaltyFreeRefusal()
    {
        var state = new RecordingStatePort();
        var action = new PlotNodeActionSO { prerequisites = null! };
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(action), ReadyFacts(), Preserving, NotQuarantined);

        Assert.Equal(
            AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable,
            result.Code);
        Assert.False(result.ToResult().HasNativeEvidence);
        Assert.Equal(0, state.CaptureCount);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    /// <summary>
    /// An action carrying no facts submits nothing.
    /// </summary>
    /// <remarks>
    /// The evidence states default to unknown, and unknown is a rejection rather than a pass — so an
    /// action that reached the boundary without a plan behind it cannot mutate the game.
    /// </remarks>
    [Fact]
    public void AnActionWithoutFactsIsRefused()
    {
        var state = new RecordingStatePort();
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), default, Preserving, NotQuarantined);

        Assert.Equal(CommonActionResultCodes.PolicyRejected, result.Code);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    /// <summary>
    /// A plot whose one instance of the action cannot be resolved is refused, however good the facts.
    /// </summary>
    [Fact]
    public void AMissingPrototypeIsRefusedEvenWhenEveryFactAgrees()
    {
        var state = new RecordingStatePort(resolvesPrototype: false);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving, NotQuarantined);

        Assert.Equal(
            AutoHarvestActionResultCodes.NativeOfferedInstanceMembershipRefused,
            result.Code);
        Assert.Equal(1, state.PrototypeCount);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    /// <summary>
    /// The queue snapshot the transition verifier will compare against is also the one the policy
    /// judges, and it is captured once.
    /// </summary>
    /// <remarks>
    /// Every world fact says go here; only the live queue says the pair is already running. An
    /// adapter that judged without it would sail past the policy and reach the native submission,
    /// which this fixture cannot perform — so the rejection is the evidence.
    /// </remarks>
    [Fact]
    public void TheLiveQueueIsWhatTheFinalRevalidationAddsToTheWorldFacts()
    {
        var state = new RecordingStatePort(supported: 1);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving, NotQuarantined);

        Assert.Equal(CommonActionResultCodes.PolicyRejected, result.Code);
        Assert.Equal(1, state.CaptureCount);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    [Theory]
    [InlineData(1031)]
    [InlineData(1032)]
    [InlineData(1033)]
    [InlineData(1034)]
    [InlineData(1035)]
    public void AClickTermMovingAfterPlanningRefusesWithItsExactNamedCode(int failure)
    {
        var expected = new ServiceActionResultCode(failure, "expected");
        var state = new RecordingStatePort(admissionFailure: expected);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving, NotQuarantined);

        Assert.Equal(expected, result.Code);
        Assert.Equal(1, state.CaptureCount);
        Assert.Equal(1, state.PrototypeCount);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    [Fact]
    public void PairIdentityMovingAfterPlanningRefusesBeforeAnyLiveGateOrMutation()
    {
        var state = new RecordingStatePort(resolvesCurrentPair: false);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving, NotQuarantined);

        Assert.Equal(
            AutoHarvestActionResultCodes.NativePairIdentityRevalidationRefused,
            result.Code);
        Assert.Equal(0, state.CaptureCount);
        Assert.Equal(0, state.PrototypeCount);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    [Fact]
    public void EveryLiveClickTermHoldingCommitsTheExistingExactTransition()
    {
        var active = new PlotNodeActionInstanceListVariable();
        var state = new RecordingStatePort(activeActions: active);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(activeActions: active), ReadyFacts(), Preserving, NotQuarantined);

        Assert.True(result.Verified);
        var added = Assert.Single(active.value);
        Assert.Same(state.Prototype, added);
        Assert.Equal(1, added.GetActualQuantity());
        Assert.Equal(1, state.PrototypeCount);
    }

    /// <summary>
    /// The game throwing after its add landed is still the add the player would have made.
    /// </summary>
    [Fact]
    public void AnAddThatLandsBeforeTheGameThrowsCommits()
    {
        var active = new PlotNodeActionInstanceListVariable { BreakAfterMutation = true };
        var state = new RecordingStatePort(activeActions: active);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(activeActions: active), ReadyFacts(), Preserving, NotQuarantined);

        Assert.True(result.Verified);
        Assert.Same(state.Prototype, Assert.Single(active.value));
    }

    /// <summary>
    /// A throw from the add that leaves nothing visible may still have moved the game, so the pair
    /// is faulted for the lifecycle rather than refused in a way the next cycle would retry.
    /// </summary>
    [Fact]
    public void AnAddThatThrowsWithoutLandingFaultsThePair()
    {
        var active = new PlotNodeActionInstanceListVariable { SuppressMutation = true, BreakAfterMutation = true };
        var state = new RecordingStatePort(activeActions: active);
        var adapter = new AutoHarvestMutationAdapter(state);
        var quarantines = 0;

        var result = adapter.Submit(ResolvedPair(activeActions: active), ReadyFacts(), Preserving, () => quarantines++);

        Assert.Equal(1, quarantines);
        Assert.Equal(AutoHarvestActionResultCodes.PairFaulted, result.Code);
        Assert.Equal(NativeMutationOutcome.ExecutionThrew, result.Mutation);
        Assert.Equal(1, result.CallOutcome.MutationAttempts);
        Assert.Equal(ServiceActionDisposition.Faulted, result.ToResult().Disposition);
        Assert.Empty(active.value);
    }

    /// <summary>
    /// The audited safety the adapter judges is the one the action carried, not an assumed one.
    /// </summary>
    /// <remarks>
    /// The verdict is drawn on the worker, from the snapshot the plan was made against, and travels
    /// with the action. An adapter that supplied a constant instead would pass every test about the
    /// ordinary path and submit into an action the audit rejected.
    /// </remarks>
    [Theory]
    [InlineData((int)AutoHarvestActionSafetyState.Unknown)]
    [InlineData((int)AutoHarvestActionSafetyState.Destructive)]
    [InlineData((int)AutoHarvestActionSafetyState.ResourceDrain)]
    [InlineData((int)AutoHarvestActionSafetyState.UnsafeCompletionEffects)]
    public void AnUnauditedActionIsRejectedOnTheSafetyItCarried(int safetyValue)
    {
        var state = new RecordingStatePort();
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(
            ResolvedPair(),
            ReadyFacts(),
            (AutoHarvestActionSafetyState)safetyValue,
            NotQuarantined);

        Assert.Equal(CommonActionResultCodes.PolicyRejected, result.Code);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    /// <summary>
    /// The game never stores a null, foreign or half-built entry in the action queue, and counts its
    /// used slots with the same emptiness the reader uses, so an unreadable queue is a broken game
    /// contract and faults; a full or duplicate queue is an ordinary refusal.
    /// </summary>
    [Theory]
    [InlineData(false, 1, true, 0, 0, false)]
    [InlineData(true, 0, false, 0, 0, true)]
    [InlineData(true, 1, true, 1, 1, true)]
    public void FinalInvalidFullOrDuplicateStateDoesNotInvokeNativeMutation(
        bool valid,
        int empty,
        bool nativeHasEmptyEntry,
        int supported,
        int matches,
        bool policyRejected)
    {
        var active = new PlotNodeActionInstanceListVariable();
        var state = new RecordingStatePort(
            activeActions: active,
            captured: new AutoHarvestSubmissionState(
                valid,
                usedEntryCount: 2,
                empty,
                nativeHasEmptyEntry,
                supported,
                matches,
                pairQuantity: matches,
                pairEngaged: matches != 0));
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(activeActions: active), ReadyFacts(), Preserving, NotQuarantined);

        Assert.Empty(active.value);
        Assert.Equal(0, state.PrototypeCount);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
        Assert.Equal(
            policyRejected
                ? CommonActionResultCodes.PolicyRejected
                : CommonActionResultCodes.AdapterFault,
            result.Code);
    }

    [Theory]
    [InlineData(NativeMutationOutcome.Verified, 1, 1, 1, ServiceActionDisposition.Committed)]
    [InlineData(NativeMutationOutcome.BeforeCaptureFailed, 0, 0, 0, ServiceActionDisposition.Faulted)]
    [InlineData(NativeMutationOutcome.ExecutionThrew, 1, 1, 0, ServiceActionDisposition.Faulted)]
    [InlineData(NativeMutationOutcome.AfterCaptureFailed, 1, 1, 0, ServiceActionDisposition.Faulted)]
    [InlineData(NativeMutationOutcome.PostconditionFailed, 1, 1, 0, ServiceActionDisposition.Faulted)]
    public void FrozenLegacyNativeEvidenceMatrixMatchesServiceCycle(
        NativeMutationOutcome outcome,
        int calls,
        int attempts,
        int commits,
        ServiceActionDisposition expectedDisposition)
    {
        var result = AutoHarvestMutationAdapter.FromVerifier(
            outcome,
            new NativeMutationCallOutcome(calls, attempts, commits)).ToResult();

        Assert.Equal(expectedDisposition, result.Disposition);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(outcome, result.NativeEvidence.Outcome);
        Assert.Equal(calls, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(attempts, result.NativeCallOutcome.MutationAttempts);
        Assert.Equal(commits, result.NativeCallOutcome.MutationsCommitted);
    }

    [Theory]
    [InlineData(1028)]
    [InlineData(1029)]
    [InlineData(1030)]
    [InlineData(1031)]
    [InlineData(1032)]
    [InlineData(1033)]
    [InlineData(1034)]
    [InlineData(1035)]
    public void AdmissionRefusalsArePenaltyFreeAndKeepTheirExactCode(int expectedCode)
    {
        var expected = new ServiceActionResultCode(expectedCode, "expected");
        PlotNodeActionSO? action = null;
        if (expected == AutoHarvestActionResultCodes.NativePrerequisitesCurrentlyUnmet)
        {
            action = new PlotNodeActionSO();
            action.prerequisites.NativeCheckResult = false;
        }
        else if (expected == AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable)
        {
            action = new PlotNodeActionSO { prerequisites = null! };
        }
        var state = new RecordingStatePort(
            admissionFailure: expectedCode >= 1031 ? expected : null,
            resolvesCurrentPair:
                expected != AutoHarvestActionResultCodes.NativePairIdentityRevalidationRefused);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(action), ReadyFacts(), Preserving, NotQuarantined).ToResult();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(expected, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    private static AutoHarvestPairFacts ReadyFacts(
        PlotActionPrerequisiteEvidence prerequisites =
            PlotActionPrerequisiteEvidence.NativeLatchedTrue,
        AutoHarvestEvidenceState readiness = AutoHarvestEvidenceState.Verified) =>
        new(
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Verified,
            prerequisites,
            readiness);

    private static AutoHarvestSubmissionState State(
        int used,
        int empty,
        bool nativeHasEmptyEntry,
        int supported,
        int matches,
        int quantity,
        bool engaged) =>
        new(
            isValid: true,
            usedEntryCount: used,
            emptyEntryCount: empty,
            nativeHasEmptyEntry,
            supportedCollectCount: supported,
            pairMatchCount: matches,
            pairQuantity: quantity,
            pairEngaged: engaged);

    private const AutoHarvestActionSafetyState Preserving =
        AutoHarvestActionSafetyState.NativePhaseCyclePreserving;

    private static readonly Action NotQuarantined = () => Assert.Fail("The pair was quarantined.");

    private static ResolvedAutoHarvestPair ResolvedPair(
        PlotNodeActionSO? action = null,
        PlotNodeActionInstanceListVariable? activeActions = null)
    {
        if (action is null)
        {
            action = new PlotNodeActionSO();
            action.prerequisites.available = true;
        }
        Assert.True(AutoHarvestNativeBindings.TryCreate(out var bindings, out var reason), reason);
        var target = new AutoHarvestPairBinding(
            AutoHarvestPair.FruitTree,
            new PlotNodeSO(),
            action,
            AutoHarvestKnownIds.FruitTreePlot,
            AutoHarvestKnownIds.FruitTreeCollect,
            new TreasurePoolSO(),
            null!,
            null!,
            null!);
        var shared = new AutoHarvestSharedBinding(
            activeActions ?? new PlotNodeActionInstanceListVariable(), null!, null!, 1);
        return new ResolvedAutoHarvestPair(bindings!, shared, target, target, null);
    }

    private sealed class ThrowingStatePort : IAutoHarvestSubmissionStatePort
    {
        public bool TryResolveCurrentPair(
            in ResolvedAutoHarvestPair resolved,
            out ResolvedAutoHarvestPair current)
        {
            current = resolved;
            return true;
        }

        public AutoHarvestSubmissionState CaptureSubmissionState(in ResolvedAutoHarvestPair resolved) =>
            throw new InvalidOperationException("capture failed");

        public bool IsPairEngaged(in ResolvedAutoHarvestPair resolved) =>
            throw new InvalidOperationException("capture failed");

        public object? ReadPrototype(in ResolvedAutoHarvestPair resolved) =>
            throw new NotSupportedException();

        public ServiceActionResultCode ValidateClickAdmission(
            in ResolvedAutoHarvestPair resolved,
            out object? prototype) => throw new NotSupportedException();
    }

    private sealed class RecordingStatePort : IAutoHarvestSubmissionStatePort
    {
        private readonly PlotNodeActionInstance? _prototype;
        private readonly ServiceActionResultCode _admissionFailure;
        private readonly bool _resolvesCurrentPair;
        private readonly PlotNodeActionInstanceListVariable? _activeActions;

        internal RecordingStatePort(
            int supported = 0,
            bool resolvesPrototype = true,
            ServiceActionResultCode? admissionFailure = null,
            bool resolvesCurrentPair = true,
            PlotNodeActionInstanceListVariable? activeActions = null,
            AutoHarvestSubmissionState? captured = null)
        {
            _prototype = resolvesPrototype ? new PlotNodeActionInstance(new PlotNodeSO(), new PlotNodeActionSO()) : null;
            _admissionFailure = admissionFailure ?? CommonActionResultCodes.Committed;
            _resolvesCurrentPair = resolvesCurrentPair;
            _activeActions = activeActions;
            Captured = captured ?? State(
                used: 2,
                empty: 1,
                nativeHasEmptyEntry: true,
                supported,
                matches: 0,
                quantity: 0,
                engaged: false);
        }

        public AutoHarvestSubmissionState Captured { get; }
        public PlotNodeActionInstance? Prototype => _prototype;

        public int CaptureCount { get; private set; }
        public int PrototypeCount { get; private set; }

        public bool TryResolveCurrentPair(
            in ResolvedAutoHarvestPair resolved,
            out ResolvedAutoHarvestPair current)
        {
            current = resolved;
            return _resolvesCurrentPair;
        }

        public AutoHarvestSubmissionState CaptureSubmissionState(
            in ResolvedAutoHarvestPair resolved)
        {
            CaptureCount++;
            return Landed
                ? State(
                    used: 3,
                    empty: 0,
                    nativeHasEmptyEntry: false,
                    supported: 1,
                    matches: 1,
                    quantity: 1,
                    engaged: true)
                : Captured;
        }

        public bool IsPairEngaged(in ResolvedAutoHarvestPair resolved) => Landed;

        private bool Landed => _activeActions?.value.Exists(instance => instance.IsEngaged()) == true;

        public object? ReadPrototype(in ResolvedAutoHarvestPair resolved)
        {
            PrototypeCount++;
            return _prototype;
        }

        public ServiceActionResultCode ValidateClickAdmission(
            in ResolvedAutoHarvestPair resolved,
            out object? prototype)
        {
            prototype = ReadPrototype(resolved);
            return _admissionFailure != CommonActionResultCodes.Committed
                ? _admissionFailure
                : prototype is null
                    ? AutoHarvestActionResultCodes.NativeOfferedInstanceMembershipRefused
                    : CommonActionResultCodes.Committed;
        }
    }
}
