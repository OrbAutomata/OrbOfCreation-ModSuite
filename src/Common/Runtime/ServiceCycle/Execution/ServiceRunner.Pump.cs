using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

public sealed partial class ServiceRunner<TState, TAction>
{
    internal ServiceCycleStartAttempt TryStartCycleNonBlocking(
        MonotonicTimestamp now,
        int ordinal = 0,
        IServiceCycleAttemptObserver? observer = null)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _starts.TryStart(now, ordinal, observer);
    }

    internal ServiceResponseAcquisition TryAcquireResponseNonBlocking(
        MonotonicTimestamp now)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _responses.TryAcquireNonBlocking(now);
    }

    internal ServiceActionDispatch TryExecuteOneNonBlocking(
        MonotonicTimestamp now,
        int ordinal = 0,
        IServiceCycleAttemptObserver? observer = null)
    {
#if SERVICE_CYCLE_PROFILE
        var profileCoordinates = default(ServiceCycleProfileCoordinates);
        return TryExecuteOneNonBlockingProfiled(
            now,
            ordinal,
            observer,
            in profileCoordinates);
#else
        AssertOwnerThread();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _actionExecutor.TryExecuteOneNonBlocking(now, ordinal, observer);
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionDispatch TryExecuteOneNonBlockingProfiled(
        MonotonicTimestamp now,
        int ordinal,
        IServiceCycleAttemptObserver? observer,
        in ServiceCycleProfileCoordinates profileCoordinates)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _actionExecutor.TryExecuteOneNonBlockingProfiled(
                now,
                ordinal,
                observer,
                in profileCoordinates);
    }
#endif

    internal bool TryAdvancePendingMainOwnership()
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        return _batchCompletion.TryAdvancePendingMainOwnership();
    }

    internal bool RejectForEmergencyStop(
        EmergencyStopContext emergency,
        MonotonicTimestamp now,
        out BatchReceipt receipt)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        if (!emergency.IsValid)
            throw new System.ArgumentException(
                "A valid emergency context is required.",
                nameof(emergency));
        _starts.CancelPendingRequestForEmergency();
        var phase = _handoff.PhaseHint;
        if (phase is ServiceHandoffPhase.RequestReady or
            ServiceHandoffPhase.Evaluating or
            ServiceHandoffPhase.ResponseReady)
        {
            _responses.MarkOutstandingEmergency(emergency);
        }
        return _batchCompletion.RejectForEmergencyStop(
            emergency,
            now,
            out receipt);
    }
}
