using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutoHarvestMutationAdapter : IAutoHarvestMutationPort
{
    private readonly IAutoHarvestSubmissionStatePort _stateReader;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;
#endif

    public AutoHarvestMutationAdapter(
        IAutoHarvestSubmissionStatePort stateReader
#if SERVICE_CYCLE_PROFILE
        , AutomataProfileOperations profileOperations
#endif
        )
    {
        _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
#if SERVICE_CYCLE_PROFILE
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
#endif
    }

    public NativeActionOutcome Submit(
        in ResolvedAutoHarvestPair resolved,
        in AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        )
    {
        if (!_stateReader.TryResolveCurrentPair(resolved, out var current))
        {
            return NativeActionOutcome.Reject(
                AutoHarvestActionResultCodes.NativePairIdentityRevalidationRefused, string.Empty);
        }

        var prerequisiteFailure = ValidatePrerequisites(current);
        if (prerequisiteFailure != CommonActionResultCodes.Committed)
            return NativeActionOutcome.Reject(prerequisiteFailure, string.Empty);

        AutoHarvestSubmissionState before;
#if SERVICE_CYCLE_PROFILE
        var beforeSnapshot = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoHarvestActionBeforeSnapshot,
            in context,
            ServiceCycleProfileTemperature.Warm);
#endif
        try
        {
            before = _stateReader.CaptureSubmissionState(current);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return FromVerifier(NativeMutationOutcome.BeforeCaptureFailed, default);
        }
#if SERVICE_CYCLE_PROFILE
        finally { beforeSnapshot.Complete(); }
#endif

        if (!before.IsValid)
            return NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, string.Empty);

        var decision = AutoHarvestPolicy.EvaluateSubmission(
            current.Target.Pair,
            facts,
            safety,
            before);
        if (!decision.ShouldSubmit)
        {
            return NativeActionOutcome.Reject(CommonActionResultCodes.PolicyRejected, string.Empty);
        }

        object? prototype;
#if SERVICE_CYCLE_PROFILE
        var revalidation = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution,
            in context,
            ServiceCycleProfileTemperature.Warm);
#endif
        try
        {
            var admissionFailure = _stateReader.ValidateClickAdmission(
                current,
                out prototype);
            if (admissionFailure != CommonActionResultCodes.Committed)
                return NativeActionOutcome.Reject(admissionFailure, string.Empty);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, string.Empty);
        }
#if SERVICE_CYCLE_PROFILE
        finally { revalidation.Complete(); }
        var actionContext = context;
#endif

        return SubmitAdmitted(
            current.Target.ActionUuid,
            () =>
            {
#if SERVICE_CYCLE_PROFILE
                return Measure(
                    () => _stateReader.IsPairEngaged(current),
                    ServiceCycleProfileSpan.AutoHarvestActionAfterSnapshot,
                    actionContext);
#else
                return _stateReader.IsPairEngaged(current);
#endif
            },
            () =>
            {
#if SERVICE_CYCLE_PROFILE
                Measure(
                    () => current.Bindings.ActiveAddInstance(current.Shared.ActiveActions, prototype!, 1),
                    ServiceCycleProfileSpan.AutoHarvestActionNativeSubmission,
                    actionContext);
#else
                current.Bindings.ActiveAddInstance(current.Shared.ActiveActions, prototype!, 1);
#endif
            });
    }

    private static NativeActionOutcome SubmitAdmitted(
        string actionUuid,
        Func<bool> observeAfter,
        Action execute)
    {
        var evidence = NativeMutationVerifier.ExecuteAfterCapture(
            "Auto Harvest",
            actionUuid,
            "the exact native plot action is engaged",
            false,
            observeAfter,
            execute,
            static (_, after) => after);
        return FromVerifier(evidence.Outcome, NativeMutationCallOutcome.FromEvidence(evidence));
    }

    /// <summary>
    /// An add that was attempted and left the pair not engaged is named a pair fault, which backs
    /// the pair off until its retry is due.
    /// </summary>
    internal static NativeActionOutcome FromVerifier(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome calls)
    {
        if (outcome == NativeMutationOutcome.Verified)
            return NativeActionOutcome.Commit(calls, string.Empty);
        return calls.MutationAttempts != 0
            ? NativeActionOutcome.Fault(
                AutoHarvestActionResultCodes.PairFaulted, outcome, calls, NotEngagedAfterTheAdd)
            : NativeActionOutcome.Fault(CommonActionResultCodes.AdapterFault, outcome, calls, string.Empty);
    }

    private const string NotEngagedAfterTheAdd =
        "Auto Harvest asked the game to start the harvest, and it is not running.";

    /// <summary>
    /// Calls the parameterless domain validator exactly once on the UUID/type-resolved current
    /// action. Its result is the admission decision; cached latches are not reread as evidence.
    /// </summary>
    private static ServiceActionResultCode ValidatePrerequisites(
        in ResolvedAutoHarvestPair resolved)
    {
        bool result;
        try
        {
            var bindings = resolved.Bindings;
            var prerequisites = bindings.ActionPrerequisites(resolved.Target.Action);
            if (prerequisites is null)
                return AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable;

            result = bindings.PrerequisitesCheck(prerequisites);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable;
        }

        return result
            ? CommonActionResultCodes.Committed
            : AutoHarvestActionResultCodes.NativePrerequisitesCurrentlyUnmet;
    }

#if SERVICE_CYCLE_PROFILE
    private void Measure(
        Action action,
        ServiceCycleProfileSpan span,
        ServiceActionContext context)
    {
        var stage = _profileOperations.Begin(
            span,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try { action(); }
        finally { stage.Complete(); }
    }

    private T Measure<T>(
        Func<T> action,
        ServiceCycleProfileSpan span,
        ServiceActionContext context)
    {
        var stage = _profileOperations.Begin(
            span,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try { return action(); }
        finally { stage.Complete(); }
    }
#endif
}
