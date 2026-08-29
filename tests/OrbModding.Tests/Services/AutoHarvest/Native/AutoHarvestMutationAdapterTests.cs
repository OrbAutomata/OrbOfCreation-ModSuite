using System;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestMutationAdapterTests
{
    [Fact]
    public void InitialCaptureFailurePreservesNativeFailureEvidence()
    {
        var adapter = new AutoHarvestMutationAdapter(new ThrowingStatePort());

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving);

        Assert.True(result.HasNativeMutationOutcome);
        Assert.Equal(NativeMutationOutcome.BeforeCaptureFailed, result.NativeMutationOutcome);
        Assert.False(result.MutationAttempted);
        Assert.Equal(AutoHarvestSubmissionFailureCode.None, result.FailureCode);
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
            Preserving);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.Equal(1, state.CaptureCount);
        Assert.Equal(0, state.PrototypeCount);
        Assert.False(result.MutationAttempted);
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
            Preserving);

        Assert.Equal(
            AutoHarvestSubmissionFailureCode.NativePrerequisitesCurrentlyUnmet,
            result.FailureCode);
        Assert.Equal(1, action.prerequisites.CheckCalls);
        Assert.Equal(0, state.CaptureCount);
        Assert.Equal(0, state.PrototypeCount);
        Assert.False(result.MutationAttempted);
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
            Preserving);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.Equal(1, action.prerequisites.CheckCalls);
        Assert.False(result.MutationAttempted);
    }

    [Fact]
    public void UnreadablePrerequisiteContainerHasItsOwnPenaltyFreeRefusal()
    {
        var state = new RecordingStatePort();
        var action = new PlotNodeActionSO { prerequisites = null! };
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(action), ReadyFacts(), Preserving);

        Assert.Equal(
            AutoHarvestSubmissionFailureCode.NativePrerequisiteValidationUnavailable,
            result.FailureCode);
        Assert.False(result.HasNativeMutationOutcome);
        Assert.Equal(0, state.CaptureCount);
        Assert.False(result.MutationAttempted);
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

        var result = adapter.Submit(ResolvedPair(), default, Preserving);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.False(result.MutationAttempted);
    }

    /// <summary>
    /// A plot whose one instance of the action cannot be resolved is refused, however good the facts.
    /// </summary>
    [Fact]
    public void AMissingPrototypeIsRefusedEvenWhenEveryFactAgrees()
    {
        var state = new RecordingStatePort(resolvesPrototype: false);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving);

        Assert.Equal(
            AutoHarvestSubmissionFailureCode.NativeOfferedInstanceMembershipRefused,
            result.FailureCode);
        Assert.Equal(1, state.PrototypeCount);
        Assert.False(result.MutationAttempted);
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

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.Equal(1, state.CaptureCount);
        Assert.False(result.MutationAttempted);
    }

    [Theory]
    [InlineData((int)AutoHarvestSubmissionFailureCode.NativePlotVisibilityRefused)]
    [InlineData((int)AutoHarvestSubmissionFailureCode.NativeOfferedInstanceMembershipRefused)]
    [InlineData((int)AutoHarvestSubmissionFailureCode.NativeActionRowVisibilityRefused)]
    [InlineData((int)AutoHarvestSubmissionFailureCode.NativeHasEnoughForOneInstanceRefused)]
    [InlineData((int)AutoHarvestSubmissionFailureCode.NativeMaximumRemainingInstancesRefused)]
    public void AClickTermMovingAfterPlanningRefusesWithItsExactNamedCode(int failure)
    {
        var expected = (AutoHarvestSubmissionFailureCode)failure;
        var state = new RecordingStatePort(admissionFailure: expected);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving);

        Assert.Equal(expected, result.FailureCode);
        Assert.Equal(1, state.CaptureCount);
        Assert.Equal(1, state.PrototypeCount);
        Assert.False(result.MutationAttempted);
    }

    [Fact]
    public void PairIdentityMovingAfterPlanningRefusesBeforeAnyLiveGateOrMutation()
    {
        var state = new RecordingStatePort(resolvesCurrentPair: false);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving);

        Assert.Equal(
            AutoHarvestSubmissionFailureCode.NativePairIdentityRevalidationRefused,
            result.FailureCode);
        Assert.Equal(0, state.CaptureCount);
        Assert.Equal(0, state.PrototypeCount);
        Assert.False(result.MutationAttempted);
    }

    [Fact]
    public void EveryLiveClickTermHoldingCommitsTheExistingExactTransition()
    {
        var active = new TestActiveActions();
        var state = new RecordingStatePort(activeActions: active);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(activeActions: active), ReadyFacts(), Preserving);

        Assert.True(result.Verified);
        Assert.Equal(1, active.AddCalls);
        Assert.Equal(1, state.PrototypeCount);
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
            (AutoHarvestActionSafetyState)safetyValue);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.False(result.MutationAttempted);
    }

    [Theory]
    [InlineData(false, 1, true, 0, 0, (int)AutoHarvestSubmissionFailureCode.RuntimeReadFailed)]
    [InlineData(true, 0, false, 0, 0, (int)AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected)]
    [InlineData(true, 1, true, 1, 1, (int)AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected)]
    public void FinalInvalidFullOrDuplicateStateDoesNotInvokeNativeMutation(
        bool valid,
        int empty,
        bool nativeHasEmptyEntry,
        int supported,
        int matches,
        int expectedFailure)
    {
        var executed = false;
        var afterCaptured = false;
        var before = new AutoHarvestSubmissionState(
            valid,
            usedEntryCount: 2,
            empty,
            nativeHasEmptyEntry,
            supported,
            matches,
            pairQuantity: matches,
            pairEngaged: matches != 0);

        var result = AutoHarvestMutationAdapter.SubmitCaptured(
            "test-action",
            before,
            () =>
            {
                afterCaptured = true;
                return true;
            },
            () => executed = true);

        Assert.False(executed);
        Assert.False(afterCaptured);
        Assert.False(result.MutationAttempted);
        Assert.Equal((AutoHarvestSubmissionFailureCode)expectedFailure, result.FailureCode);
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

    private static ResolvedAutoHarvestPair ResolvedPair(
        PlotNodeActionSO? action = null,
        TestActiveActions? activeActions = null)
    {
        if (action is null)
        {
            action = new PlotNodeActionSO();
            action.prerequisites.available = true;
        }
        var contract = PrerequisiteContract();
        if (activeActions is not null)
        {
            Set(
                contract,
                nameof(AutoHarvestReflectionContract.ActiveAddInstance),
                typeof(TestActiveActions).GetMethod(nameof(TestActiveActions.AddInstance))!);
        }
        var target = new AutoHarvestPairBinding(
            AutoHarvestPair.FruitTree,
            new PlotNodeSO(),
            action,
            AutoHarvestKnownIds.FruitTreePlot,
            AutoHarvestKnownIds.FruitTreeCollect,
            new object(),
            null!,
            null!,
            null!);
        var shared = new AutoHarvestSharedBinding(activeActions ?? new object(), null!, null!, 1);
        return new ResolvedAutoHarvestPair(contract, shared, target, target, null);
    }

    private static AutoHarvestReflectionContract PrerequisiteContract()
    {
        var types = (AutoHarvestReflectionTypes)Activator.CreateInstance(
            typeof(AutoHarvestReflectionTypes),
            nonPublic: true)!;
        Set(types, nameof(AutoHarvestReflectionTypes.Action), typeof(PlotNodeActionSO));
        var constructor = typeof(AutoHarvestReflectionContract).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(AutoHarvestReflectionTypes) },
            modifiers: null)!;
        var contract = (AutoHarvestReflectionContract)constructor.Invoke(new object[] { types });
        Set(
            contract,
            nameof(AutoHarvestReflectionContract.ActionPrerequisites),
            (Func<object, object?>)(source => ((PlotNodeActionSO)source).prerequisites));
        Set(
            contract,
            nameof(AutoHarvestReflectionContract.PrerequisitesAvailable),
            (Func<object, bool>)(source => ((Prerequisites.Container)source).available));
        Set(
            contract,
            nameof(AutoHarvestReflectionContract.PrerequisitesCheck),
            (Func<object, bool>)(source => ((Prerequisites.Container)source).Check()));
        return contract;
    }

    private static void Set(object target, string property, object value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

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

        public AutoHarvestSubmissionFailureCode ValidateClickAdmission(
            in ResolvedAutoHarvestPair resolved,
            out object? prototype) => throw new NotSupportedException();
    }

    private sealed class RecordingStatePort : IAutoHarvestSubmissionStatePort
    {
        private readonly object? _prototype;
        private readonly AutoHarvestSubmissionFailureCode _admissionFailure;
        private readonly bool _resolvesCurrentPair;
        private readonly TestActiveActions? _activeActions;

        internal RecordingStatePort(
            int supported = 0,
            bool resolvesPrototype = true,
            AutoHarvestSubmissionFailureCode admissionFailure = AutoHarvestSubmissionFailureCode.None,
            bool resolvesCurrentPair = true,
            TestActiveActions? activeActions = null)
        {
            _prototype = resolvesPrototype ? new object() : null;
            _admissionFailure = admissionFailure;
            _resolvesCurrentPair = resolvesCurrentPair;
            _activeActions = activeActions;
            Captured = State(
                used: 2,
                empty: 1,
                nativeHasEmptyEntry: true,
                supported,
                matches: 0,
                quantity: 0,
                engaged: false);
        }

        public AutoHarvestSubmissionState Captured { get; }

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
            return _activeActions?.AddCalls > 0
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

        public bool IsPairEngaged(in ResolvedAutoHarvestPair resolved) =>
            _activeActions?.AddCalls > 0;

        public object? ReadPrototype(in ResolvedAutoHarvestPair resolved)
        {
            PrototypeCount++;
            return _prototype;
        }

        public AutoHarvestSubmissionFailureCode ValidateClickAdmission(
            in ResolvedAutoHarvestPair resolved,
            out object? prototype)
        {
            prototype = ReadPrototype(resolved);
            return _admissionFailure != AutoHarvestSubmissionFailureCode.None
                ? _admissionFailure
                : prototype is null
                    ? AutoHarvestSubmissionFailureCode.NativeOfferedInstanceMembershipRefused
                    : AutoHarvestSubmissionFailureCode.None;
        }
    }

    private sealed class TestActiveActions
    {
        public int AddCalls { get; private set; }

        public void AddInstance(object prototype, int quantity)
        {
            Assert.NotNull(prototype);
            Assert.Equal(1, quantity);
            AddCalls++;
        }
    }
}
