using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for persistent reset.</summary>
internal sealed class PrestigeGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private PrestigeNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal PrestigeGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal PrestigeSubmission Submit(in PrestigeAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return PrestigeSubmission.Reject(PrestigePreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return PrestigeSubmission.Reject(PrestigePreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Time > Reset"));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return PrestigeSubmission.Reject(PrestigePreflight.LifecycleReplaced,
                "The lifecycle epoch could not be read: " + exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return PrestigeSubmission.Reject(PrestigePreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            var manager = native.Manager();
            if (manager is null || manager.GetType() != native.ManagerType)
                return PrestigeSubmission.Reject(PrestigePreflight.ContractUnavailable,
                    "The reset is not available from here — return to a loaded save and open " +
                    "Time > Reset.");
            if (!TryCapture(native, manager, epoch, out var before, out var captureFailure))
                return PrestigeSubmission.Reject(PrestigePreflight.ContractUnavailable, captureFailure);
            if (!before.WorldCycleComplete)
                return PrestigeSubmission.Reject(PrestigePreflight.WorldCycleIncomplete,
                    "Persistent reset is unavailable until the current world cycle is complete.");
            if (!before.ChallengesFetched)
                return PrestigeSubmission.Reject(PrestigePreflight.ChallengesNotFetched,
                    "Persistent reset is unavailable until prestige challenges have been fetched and chosen.");
            if (!_tryCaptureMutationPermit())
                return PrestigeSubmission.Reject(PrestigePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(native, manager, in before);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return PrestigeSubmission.Reject(PrestigePreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Time > Reset", exception));
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private PrestigeSubmission Execute(PrestigeNativeBindings native, object manager,
        in PrestigeAdmissionState before)
    {
        var stage = PrestigeNativeStage.NativeTransaction;
        try
        {
            // PersistentReset() only schedules this transaction behind UIScreenFlash.FadeIn.
            // Tooling has no rendered-modal dependency, so invoke the exact transaction directly.
            native.Reset(manager);
            stage = PrestigeNativeStage.Verification;
            var observedEpoch = _readLifecycleEpoch();
            if (observedEpoch == checked(before.LifecycleEpoch + 1))
                return new PrestigeSubmission(PrestigePreflight.Proceeded, stage,
                    NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
                    "The persistent-reset lifecycle transition is visible.");
            return Fault(PrestigePreflight.VerificationFailed, stage,
                NativeMutationOutcome.PostconditionFailed,
                "Expected lifecycle " + checked(before.LifecycleEpoch + 1) +
                ", observed " + observedEpoch + ".");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            var observedEpoch = ReadEpochBestEffort(before.LifecycleEpoch);
            if (observedEpoch == checked(before.LifecycleEpoch + 1))
                return new PrestigeSubmission(PrestigePreflight.Proceeded,
                    PrestigeNativeStage.Verification, NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1),
                    "The lifecycle advanced before the native exception.");
            return Fault(PrestigePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                "The native reset transaction threw before the lifecycle advanced: " +
                exception.GetBaseException().Message);
        }
    }

    private static bool TryCapture(PrestigeNativeBindings native, object manager, long epoch,
        out PrestigeAdmissionState state, out string reason)
    {
        var complete = native.CycleComplete(manager);
        var fetched = native.ChallengesFetched(manager);
        if (complete is null || fetched is null)
        {
            state = default;
            reason = "The game did not answer whether a reset is allowed right now; open " +
                "Time > Reset and read it again.";
            return false;
        }
        state = new PrestigeAdmissionState(epoch, native.GetBool(complete),
            native.GetBool(fetched));
        reason = string.Empty;
        return true;
    }

    private static PrestigeSubmission Fault(PrestigePreflight preflight,
        PrestigeNativeStage stage, NativeMutationOutcome outcome,
        string reason)
    {
        var exactReason = "Prestige " + stage + " failed: " + reason;
        return new PrestigeSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(1, 1, 0), exactReason);
    }

    private long ReadEpochBestEffort(long fallback)
    {
        try { return _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception)) { return fallback; }
    }

    private void BindLifecycle()
    {
        if (PrestigeNativeBindings.TryCreate(out var bindings, out var reason,
                _resolveType, _includeContract))
        { _bindings = bindings; _bindingFailure = string.Empty; return; }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) => exception is InvalidOperationException or
        ArgumentException or TargetInvocationException or OverflowException;
}
