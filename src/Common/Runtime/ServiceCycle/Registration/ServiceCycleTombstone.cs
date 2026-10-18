using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif
using OrbModding.Common.Runtime.ServiceCycle.Configuration;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal sealed class ServiceCycleTombstone : IServiceCycleSlot
{
    internal ServiceCycleTombstone(
        long registrationToken,
        int ordinal,
        ServiceId serviceId,
        long lifecyclePositionTransitionCount,
        ServiceLifecycleSlotSnapshot lifecycleSnapshot)
    {
        RegistrationToken = registrationToken;
        Ordinal = ordinal;
        ServiceId = serviceId;
        LifecyclePositionTransitionCount = lifecyclePositionTransitionCount;
        LifecycleSnapshot = lifecycleSnapshot;
    }

    public long RegistrationToken { get; }
    public int Ordinal { get; }
    public ServiceId ServiceId { get; }
    public ServiceActionDispatchPolicy ActionDispatchPolicy => ServiceActionDispatchPolicy.Single;
    public bool IsDisposed => true;
    public long LifecyclePositionTransitionCount { get; }
    public long LifecycleSemanticVersion => 0;
    public ServiceHandoffPhase HandoffPhaseHint => ServiceHandoffPhase.Stopped;
    public bool IsBetweenCycles => true;
    public ServiceLifecycleSlotSnapshot LifecycleSnapshot { get; }
    public ServiceWorldGateDeferralFact LatestWorldGateDeferral => default;
    public ConfigGeneration LatestConfiguration => default;
    public StrategyGeneration LatestStrategy => default;
    public bool TryGetRunnerSnapshot(out ServiceRunnerSnapshot snapshot)
    {
        snapshot = default;
        return false;
    }
    public ServiceResponseAcquisition TryAcquireResponse(OrbModding.Common.Runtime.MonotonicTimestamp now) => default;
    public bool TryAdvancePendingMainOwnership(OrbModding.Common.Runtime.MonotonicTimestamp now) => false;
    public ServiceActionDispatch TryExecuteOne(
        OrbModding.Common.Runtime.MonotonicTimestamp now,
        long frameIdentity,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        ) => default;
    public ServiceCycleStartAttempt TryStartCycle(
        OrbModding.Common.Runtime.MonotonicTimestamp now,
        long frameIdentity,
        IServiceCycleAttemptObserver? observer,
        out bool deferredForWorld)
    {
        deferredForWorld = false;
        return default;
    }
    public bool RejectForEmergencyStop(
        EmergencyStopContext emergency,
        OrbModding.Common.Runtime.MonotonicTimestamp now,
        out BatchReceipt receipt)
    {
        receipt = default;
        return false;
    }
    public void RequestLifecycle(OrbModding.Common.Runtime.LifecycleGeneration generation) { }

    public bool ReconcileLifecycle(
        OrbModding.Common.Runtime.MonotonicTimestamp now,
        long reconciliationEpoch) => false;
    public void Dispose() { }
}
