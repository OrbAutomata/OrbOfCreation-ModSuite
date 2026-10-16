using System;
using OrbModding.Common;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
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

    public AutoHarvestSubmissionResult Submit(
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
            return new AutoHarvestSubmissionResult(
                AutoHarvestSubmissionFailureCode.NativePairIdentityRevalidationRefused);
        }

        var prerequisiteFailure = ValidatePrerequisites(current);
        if (prerequisiteFailure != AutoHarvestSubmissionFailureCode.None)
            return new AutoHarvestSubmissionResult(prerequisiteFailure);

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
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            return new AutoHarvestSubmissionResult(
                NativeMutationOutcome.BeforeCaptureFailed,
                default);
        }
#if SERVICE_CYCLE_PROFILE
        finally { beforeSnapshot.Complete(); }
#endif

        var decision = AutoHarvestPolicy.EvaluateSubmission(
            current.Target.Pair,
            facts,
            safety,
            before);
        if (!decision.ShouldSubmit)
        {
            return new AutoHarvestSubmissionResult(
                AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected);
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
            if (admissionFailure != AutoHarvestSubmissionFailureCode.None)
            {
                return new AutoHarvestSubmissionResult(
                    admissionFailure);
            }
        }
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            return new AutoHarvestSubmissionResult(
                AutoHarvestSubmissionFailureCode.RuntimeReadFailed);
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
                    () =>
                    {
                        _profileOperations.AddReflectedMethodCall();
                        _profileOperations.AddInvocationArgumentArray();
                        current.Contract.ActiveAddInstance.Invoke(
                            current.Shared.ActiveActions,
                            new[] { prototype!, (object)1 });
                    },
                    ServiceCycleProfileSpan.AutoHarvestActionNativeSubmission,
                    actionContext);
#else
                current.Contract.ActiveAddInstance.Invoke(
                    current.Shared.ActiveActions,
                    new[] { prototype!, (object)1 });
#endif
            }
            );
    }

    internal static AutoHarvestSubmissionResult SubmitCaptured(
        string actionUuid,
        in AutoHarvestSubmissionState before,
        Func<bool> observeAfter,
        Action execute)
    {
        if (!CanSubmit(before))
        {
            return new AutoHarvestSubmissionResult(
                before.IsValid
                    ? AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected
                    : AutoHarvestSubmissionFailureCode.RuntimeReadFailed);
        }

        return SubmitAdmitted(
            actionUuid,
            observeAfter,
            execute);
    }

    private static AutoHarvestSubmissionResult SubmitAdmitted(
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
        return new AutoHarvestSubmissionResult(
            evidence.Outcome,
            NativeMutationCallOutcome.FromEvidence(evidence));
    }

    /// <summary>
    /// Calls the parameterless domain validator exactly once on the UUID/type-resolved current
    /// action. Its result is the admission decision; cached latches are not reread as evidence.
    /// </summary>
    private static AutoHarvestSubmissionFailureCode ValidatePrerequisites(
        in ResolvedAutoHarvestPair resolved)
    {
        bool result;
        try
        {
            var contract = resolved.Contract;
            var action = resolved.Target.Action;
            if (action.GetType() != contract.Types.Action)
                throw new InvalidOperationException("resolved harvest action has the wrong native type");
            var prerequisites = contract.ActionPrerequisites(action) ??
                throw new InvalidOperationException("resolved harvest action has no prerequisite container");

            result = contract.PrerequisitesCheck(prerequisites);
        }
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            return AutoHarvestSubmissionFailureCode.NativePrerequisiteValidationUnavailable;
        }

        return result
            ? AutoHarvestSubmissionFailureCode.None
            : AutoHarvestSubmissionFailureCode.NativePrerequisitesCurrentlyUnmet;
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

    internal static bool CanSubmit(in AutoHarvestSubmissionState state) =>
        state.IsValid &&
        state.NativeHasEmptyEntry &&
        state.EmptyEntryCount >= 1 &&
        state.SupportedCollectCount == 0 &&
        state.PairMatchCount == 0;

    private static bool VerifyAdmittedTransition(
        AutoHarvestSubmissionState before,
        AutoHarvestSubmissionState after) =>
        after.IsValid &&
        after.PairMatchCount == 1 &&
        after.PairEngaged;
}
