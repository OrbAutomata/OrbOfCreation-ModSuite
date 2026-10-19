using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

public readonly struct ServiceCycleContextDiagnosticsSnapshot
{
    internal ServiceCycleContextDiagnosticsSnapshot(
        ServiceCycleIdentity currentCycle,
        BatchId currentBatch,
        bool hasCurrentCycle,
        ConfigGeneration latestConfiguration,
        ServiceCycleDiagnosticsValueAvailability latestConfigurationAvailability,
        StrategyGeneration latestStrategy,
        ServiceCycleDiagnosticsValueAvailability latestStrategyAvailability)
    {
        CurrentCycle = currentCycle;
        CurrentBatch = currentBatch;
        HasCurrentCycle = hasCurrentCycle;
        LatestConfiguration = latestConfiguration;
        LatestConfigurationAvailability = latestConfigurationAvailability;
        LatestStrategy = latestStrategy;
        LatestStrategyAvailability = latestStrategyAvailability;
    }

    public ServiceCycleIdentity CurrentCycle { get; }
    public BatchId CurrentBatch { get; }
    public bool HasCurrentCycle { get; }
    public ConfigGeneration LatestConfiguration { get; }
    public ServiceCycleDiagnosticsValueAvailability LatestConfigurationAvailability { get; }
    public ServiceCycleDiagnosticsValueAvailability LatestStrategyAvailability { get; }
    public StrategyGeneration LatestStrategy { get; }
}

public readonly struct ServiceCycleServiceDiagnosticsSnapshot
{
    internal ServiceCycleServiceDiagnosticsSnapshot(
        long registrationInstance,
        int ordinal,
        ServiceId serviceId,
        ServiceCycleDiagnosticsAvailability availability,
        ServiceCycleOperationalPhase phase,
        ServiceCycleLifecycleDiagnosticsSnapshot lifecycle,
        ServiceCycleContextDiagnosticsSnapshot context,
        ServiceCycleBatchDiagnosticsSnapshot activeBatch,
        ServiceCycleHandoffDiagnosticsSnapshot handoff,
        ServiceCycleWorkerDiagnosticsSnapshot worker,
        ServiceProjectionPublication latestProjection,
        ServiceFault latestFault,
        BatchReceipt previousReceipt,
        MonotonicTimestamp nextWakeDue,
        bool hasWakeDue,
        ServiceCycleStorageDiagnosticsSnapshot storage,
        ServiceCycleStartDecisionDiagnosticsFact lastStartDecision,
        ServiceCycleCaptureDiagnosticsFact lastCapture,
        ServiceCycleActionDiagnosticsFact lastAction,
        ServiceCycleTimingDiagnosticsSnapshot timing,
        ServiceRetryGateSnapshot? retryGate)
    {
        RegistrationInstance = registrationInstance;
        Ordinal = ordinal;
        ServiceId = serviceId;
        Availability = availability;
        Phase = phase;
        Lifecycle = lifecycle;
        Context = context;
        ActiveBatch = activeBatch;
        Handoff = handoff;
        Worker = worker;
        LatestProjection = latestProjection;
        LatestFault = latestFault;
        PreviousReceipt = previousReceipt;
        NextWakeDue = nextWakeDue;
        HasWakeDue = hasWakeDue;
        Storage = storage;
        LastStartDecision = lastStartDecision;
        LastCapture = lastCapture;
        LastAction = lastAction;
        Timing = timing;
        _retryGate = retryGate;
    }

    private readonly ServiceRetryGateSnapshot? _retryGate;

    /// <summary>Stable identity for this registration instance, independent of reused ordinals.</summary>
    public long RegistrationInstance { get; }
    public int Ordinal { get; }
    public ServiceId ServiceId { get; }
    public ServiceCycleDiagnosticsAvailability Availability { get; }
    public bool HasRunnerEvidence => Availability == ServiceCycleDiagnosticsAvailability.Available;
    public ServiceCycleOperationalPhase Phase { get; }
    public ServiceCycleLifecycleDiagnosticsSnapshot Lifecycle { get; }
    public ServiceCycleContextDiagnosticsSnapshot Context { get; }
    public ServiceCycleBatchDiagnosticsSnapshot ActiveBatch { get; }
    public ServiceCycleHandoffDiagnosticsSnapshot Handoff { get; }
    public ServiceCycleWorkerDiagnosticsSnapshot Worker { get; }
    public ServiceProjectionPublication LatestProjection { get; }
    /// <summary>Latest category-agnostic fault evidence; this is not a keyed fault episode.</summary>
    public ServiceFault LatestFault { get; }
    public BatchReceipt PreviousReceipt { get; }
    public MonotonicTimestamp NextWakeDue { get; }
    public bool HasWakeDue { get; }
    public ServiceCycleStorageDiagnosticsSnapshot Storage { get; }
    public ServiceCycleStartDecisionDiagnosticsFact LastStartDecision { get; }
    public ServiceCycleCaptureDiagnosticsFact LastCapture { get; }
    public ServiceCycleActionDiagnosticsFact LastAction { get; }
    public ServiceCycleTimingDiagnosticsSnapshot Timing { get; }

    /// <summary>The targets this service is backing off, each with its reason and retry time.</summary>
    public ServiceRetryGateSnapshot RetryGate => _retryGate ?? ServiceRetryGateSnapshot.Empty;
}

public readonly struct ServiceCycleDiagnosticsCopyResult
{
    internal ServiceCycleDiagnosticsCopyResult(int requiredCount, int writtenCount, int unavailableCount)
    {
        RequiredCount = requiredCount;
        WrittenCount = writtenCount;
        UnavailableCount = unavailableCount;
    }

    public int RequiredCount { get; }
    public int WrittenCount { get; }
    public int UnavailableCount { get; }
    public bool IsComplete => WrittenCount == RequiredCount;
}
