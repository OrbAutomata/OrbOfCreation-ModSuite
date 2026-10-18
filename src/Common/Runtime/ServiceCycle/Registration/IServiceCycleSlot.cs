using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal interface IServiceCycleSlot : IDisposable
{
    long RegistrationToken { get; }
    int Ordinal { get; }
    ServiceId ServiceId { get; }
    ServiceActionDispatchPolicy ActionDispatchPolicy { get; }
    bool IsDisposed { get; }
    long LifecyclePositionTransitionCount { get; }
    long LifecycleSemanticVersion { get; }
    ServiceHandoffPhase HandoffPhaseHint { get; }
    bool IsBetweenCycles { get; }
    ServiceLifecycleSlotSnapshot LifecycleSnapshot { get; }
    /// <summary>
    /// The last frame the world freshness gate held this service, read without the cost of the whole
    /// lifecycle snapshot because the always-on journal scans it every frame.
    /// </summary>
    ServiceWorldGateDeferralFact LatestWorldGateDeferral { get; }
    ConfigGeneration LatestConfiguration { get; }
    StrategyGeneration LatestStrategy { get; }
    bool TryGetRunnerSnapshot(out ServiceRunnerSnapshot snapshot);
    ServiceResponseAcquisition TryAcquireResponse(MonotonicTimestamp now);
    bool TryAdvancePendingMainOwnership(MonotonicTimestamp now);
    ServiceActionDispatch TryExecuteOne(
        MonotonicTimestamp now,
        long frameIdentity,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        );
    /// <summary>
    /// Opens a cycle unless the world freshness gate holds this service, which
    /// <paramref name="deferredForWorld"/> reports so a held frame is distinguishable from an idle one.
    /// </summary>
    ServiceCycleStartAttempt TryStartCycle(
        MonotonicTimestamp now,
        long frameIdentity,
        IServiceCycleAttemptObserver? observer,
        out bool deferredForWorld);
    bool RejectForEmergencyStop(
        EmergencyStopContext emergency,
        MonotonicTimestamp now,
        out BatchReceipt receipt);
    void RequestLifecycle(LifecycleGeneration generation);
    bool ReconcileLifecycle(MonotonicTimestamp now, long reconciliationEpoch);
}
