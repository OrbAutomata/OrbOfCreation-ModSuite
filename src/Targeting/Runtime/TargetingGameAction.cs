using System;
using System.Collections;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>The sole mutation boundary for the current native targeting request.</summary>
internal sealed class TargetingGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private TargetingNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal TargetingGameAction(Func<long> readLifecycleEpoch, Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure, Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType; _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal TargetingSubmission Submit(in TargetingAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return TargetingSubmission.Reject(TargetingActionResultCodes.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return TargetingSubmission.Reject(TargetingActionResultCodes.ContractUnavailable,
                GameActionAnswer.NotAttached("Magic > Casting", _bindingFailure));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return TargetingSubmission.Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("Magic > Casting", ex));
        }
        if (epoch != action.LifecycleEpoch)
            return TargetingSubmission.Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.RunChanged());
        try
        {
            if (!native.IsTargeting())
                return TargetingSubmission.Reject(TargetingActionResultCodes.NoPendingRequest,
                    "The game has no pending target request.");
            var link = native.GetLink();
            if (link is null || link.GetType() != native.LinkType)
                return TargetingSubmission.Reject(TargetingActionResultCodes.NoPendingRequest,
                    "The game is not showing a targeting request right now.");
            return action.Kind switch
            {
                TargetingActionKind.Submit => SubmitTarget(in action, native, link),
                TargetingActionKind.Randomize => Randomize(in action, native, link),
                TargetingActionKind.Cancel => Cancel(in action, native, link),
                _ => TargetingSubmission.Reject(TargetingActionResultCodes.ContractUnavailable, "Unknown targeting mode."),
            };
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return TargetingSubmission.Reject(TargetingActionResultCodes.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Magic > Casting", ex));
        }
    }

    private TargetingSubmission SubmitTarget(in TargetingAction action,
        TargetingNativeBindings native, object link)
    {
        var candidates = native.GetAllTargets(link);
        object? candidate = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var item = candidates[index];
            if (item is not null && item.GetType() == native.StructureType &&
                native.GetGuid(item) == action.TargetId)
            {
                if (candidate is not null)
                    return TargetingSubmission.Reject(TargetingActionResultCodes.TargetUnavailable,
                        "Target UUID is ambiguous in the live eligible-target set.");
                candidate = item;
            }
        }
        if (candidate is null)
            return TargetingSubmission.Reject(TargetingActionResultCodes.TargetUnavailable,
                "Target UUID is absent from the live eligible-target set.");
        if (!native.CheckTarget(link, candidate))
            return TargetingSubmission.Reject(TargetingActionResultCodes.NativeTargetRefused,
                "The game refuses this target.");
        if (!TryPermit(out var reason))
            return TargetingSubmission.Reject(TargetingActionResultCodes.MutationPermitUnavailable, reason);
        return MutateSubmit(in action, native, link, candidate);
    }

    private TargetingSubmission Randomize(in TargetingAction action,
        TargetingNativeBindings native, object link)
    {
        if (native.GetAllTargets(link).Count == 0)
            return TargetingSubmission.Reject(TargetingActionResultCodes.TargetUnavailable,
                "This targeting request is offering no target to pick.");
        if (!TryPermit(out var reason))
            return TargetingSubmission.Reject(TargetingActionResultCodes.MutationPermitUnavailable, reason);
        object? candidate;
        try { candidate = native.GetRandom(link); }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return Fault(TargetingActionResultCodes.PostCommitFault,
                NativeMutationOutcome.ExecutionThrew, in action,
                new NativeMutationCallOutcome(1, 1, 0),
                "The game errored while picking a random target; pick a target by id instead.");
        }
        if (candidate is null || candidate.GetType() != native.StructureType)
            return Fault(TargetingActionResultCodes.VerificationFailed,
                NativeMutationOutcome.PostconditionFailed, in action,
                new NativeMutationCallOutcome(1, 1, 0),
                "The game's randomize button did not settle on one target; pick a target " +
                "by id instead.");
        if (!native.CheckTarget(link, candidate))
            return Fault(TargetingActionResultCodes.VerificationFailed,
                NativeMutationOutcome.PostconditionFailed, in action,
                new NativeMutationCallOutcome(1, 1, 0),
                "The game's randomize button picked a target it then refused; pick a target " +
                "by id instead.");
        return MutateSubmit(in action, native, link, candidate, 1);
    }

    private TargetingSubmission MutateSubmit(in TargetingAction action,
        TargetingNativeBindings native, object link, object candidate,
        int priorCalls = 0)
    {
        var id = native.GetGuid(candidate);
        try
        {
            native.SubmitTarget(candidate);
            return Submitted(native, link, candidate)
                ? Verified(id, priorCalls + 1,
                    "The exact target was assigned and its original request left the queue.")
                : Fault(TargetingActionResultCodes.VerificationFailed,
                    NativeMutationOutcome.PostconditionFailed, in action,
                    new NativeMutationCallOutcome(priorCalls + 1, 1, 0),
                    "SubmitTarget did not assign the exact object and retire its request.");
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            if (Submitted(native, link, candidate))
                return Verified(id, priorCalls + 1,
                    "SubmitTarget threw after the exact requested outcome became observable.");
            return Fault(TargetingActionResultCodes.PostCommitFault,
                NativeMutationOutcome.ExecutionThrew, in action,
                new NativeMutationCallOutcome(priorCalls + 1, 1, 0),
                GameActionAnswer.GameErrored("Magic > Casting", ex));
        }
    }

    private TargetingSubmission Cancel(in TargetingAction action,
        TargetingNativeBindings native, object link)
    {
        var resultInfo = native.ReadResultInfo(link);
        if (resultInfo is null)
            return TargetingSubmission.Reject(TargetingActionResultCodes.CancelUnavailable,
                "This targeting request has nothing to cancel.");
        if (!TryPermit(out var reason))
            return TargetingSubmission.Reject(TargetingActionResultCodes.MutationPermitUnavailable, reason);
        try
        {
            native.Cancel(resultInfo);
            return native.IsCancelled(resultInfo) && OriginalRequestGone(native, link)
                ? Verified(Guid.Empty, 1,
                    "The cast was cancelled and its request left the action queue.")
                : Fault(TargetingActionResultCodes.VerificationFailed,
                    NativeMutationOutcome.PostconditionFailed, in action,
                    new NativeMutationCallOutcome(1, 1, 0),
                    GameActionAnswer.ChangeNotSeen("Magic > Casting"));
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            if (native.IsCancelled(resultInfo) && OriginalRequestGone(native, link))
                return Verified(Guid.Empty, 1,
                    "Cancel threw after the exact requested outcome became observable.");
            return Fault(TargetingActionResultCodes.PostCommitFault,
                NativeMutationOutcome.ExecutionThrew, in action,
                new NativeMutationCallOutcome(1, 1, 0),
                GameActionAnswer.GameErrored("Magic > Casting", ex));
        }
    }

    private static bool Submitted(TargetingNativeBindings native, object link, object candidate) =>
        native.HasTarget(link) && ReferenceEquals(native.ReadTarget(link), candidate) &&
        OriginalRequestGone(native, link);
    private static bool OriginalRequestGone(TargetingNativeBindings native, object link) =>
        !native.IsTargeting() || !ReferenceEquals(native.GetLink(), link);
    private static TargetingSubmission Verified(Guid submitted, int calls, string reason)
    {
        return new TargetingSubmission(
            NativeActionOutcome.Commit(new NativeMutationCallOutcome(calls, 1, 1), reason), submitted);
    }
    private static TargetingSubmission Fault(ServiceActionResultCode code,
        NativeMutationOutcome outcome, in TargetingAction action,
        NativeMutationCallOutcome calls, string reason)
    {
        var exactReason = "Targeting " + action.Kind +
            " could not prove its exact outcome: " + reason;
        return new TargetingSubmission(NativeActionOutcome.Fault(code, outcome, calls, exactReason), Guid.Empty);
    }
    private bool TryPermit(out string reason)
    {
        if (_tryCaptureMutationPermit()) { reason = string.Empty; return true; }
        reason = _readOwnershipFailure();
        if (string.IsNullOrWhiteSpace(reason)) reason = "The targeting action family is not owned.";
        return false;
    }
    internal void InvalidateLifecycle() { _bindings = null; _bindingFailure = string.Empty; BindLifecycle(); }
    public void Dispose() { _bindings = null; _bindingFailure = string.Empty; }
    private void BindLifecycle()
    {
        var resolve = _resolveType ?? ReflectionUtil.FindLoadedType;
        var include = _includeContract ?? (_ => true);
        if (!TargetingNativeBindings.TryCreate(resolve, include, out _bindings, out _bindingFailure)) _bindings = null;
    }
}
