using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

public readonly struct ServiceCycleStorageDiagnosticsSnapshot
{
    internal ServiceCycleStorageDiagnosticsSnapshot(
        ServiceCycleStorageDiagnosticsAvailability availability,
        int capacity,
        int highWater,
        long growthAllocations,
        int retainedSlots)
    {
        Availability = availability;
        Capacity = capacity;
        HighWater = highWater;
        GrowthAllocations = growthAllocations;
        RetainedSlots = retainedSlots;
    }

    public ServiceCycleStorageDiagnosticsAvailability Availability { get; }
    public bool HasEvidence => Availability is
        ServiceCycleStorageDiagnosticsAvailability.Exact or
        ServiceCycleStorageDiagnosticsAvailability.LastPublished;
    public bool IsExact => Availability == ServiceCycleStorageDiagnosticsAvailability.Exact;
    public int Capacity { get; }
    public int HighWater { get; }
    public long GrowthAllocations { get; }
    public int RetainedSlots { get; }
}

public readonly struct ServiceCyclePositionDiagnosticsSnapshot
{
    internal ServiceCyclePositionDiagnosticsSnapshot(
        int index,
        ServiceRunnerPositionState state,
        LifecycleGeneration lifecycle,
        ServiceCycleHandoffDiagnosticsPhase handoffPhaseHint,
        ServiceCycleStorageDiagnosticsSnapshot storage)
    {
        Index = index;
        State = state;
        Lifecycle = lifecycle;
        HandoffPhaseHint = handoffPhaseHint;
        Storage = storage;
    }

    public int Index { get; }
    public ServiceRunnerPositionState State { get; }
    public LifecycleGeneration Lifecycle { get; }
    public ServiceCycleHandoffDiagnosticsPhase HandoffPhaseHint { get; }
    public ServiceCycleStorageDiagnosticsSnapshot Storage { get; }
}

public readonly struct ServiceCycleLifecycleDiagnosticsSnapshot
{
    internal ServiceCycleLifecycleDiagnosticsSnapshot(
        LifecycleGeneration desiredLifecycle,
        ServiceCyclePositionDiagnosticsSnapshot position0,
        ServiceCyclePositionDiagnosticsSnapshot position1,
        ServiceLifecycleTerminalFact latestTerminal,
        ServiceFault latestConstructionFault,
        MonotonicTimestamp constructionRetryDue,
        long constructionAttemptCount,
        long constructionContentionCount,
        long positionTransitionCount,
        int livePositionCount)
    {
        DesiredLifecycle = desiredLifecycle;
        Position0 = position0;
        Position1 = position1;
        LatestTerminal = latestTerminal;
        LatestConstructionFault = latestConstructionFault;
        ConstructionRetryDue = constructionRetryDue;
        ConstructionAttemptCount = constructionAttemptCount;
        ConstructionContentionCount = constructionContentionCount;
        PositionTransitionCount = positionTransitionCount;
        LivePositionCount = livePositionCount;
    }

    public LifecycleGeneration DesiredLifecycle { get; }
    public ServiceCyclePositionDiagnosticsSnapshot Position0 { get; }
    public ServiceCyclePositionDiagnosticsSnapshot Position1 { get; }
    /// <summary>The latest retained terminal fact, not lifecycle event history.</summary>
    public ServiceLifecycleTerminalFact LatestTerminal { get; }
    public long LatestTerminalSequence => LatestTerminal.Sequence;
    public ServiceFault LatestConstructionFault { get; }
    public MonotonicTimestamp ConstructionRetryDue { get; }
    public long ConstructionAttemptCount { get; }
    public long ConstructionContentionCount { get; }
    public long PositionTransitionCount { get; }
    public int LivePositionCount { get; }
}
