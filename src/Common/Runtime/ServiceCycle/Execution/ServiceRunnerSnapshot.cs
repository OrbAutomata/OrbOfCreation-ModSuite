using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal readonly struct ServiceRunnerSnapshot
{
    internal ServiceRunnerSnapshot(
        ServiceHandoffSnapshot handoff,
        ServiceCyclePhase phase,
        ServiceCycleIdentity inFlightCycle,
        BatchId inFlightBatch,
        bool hasInFlightCycle,
        ServiceCycleIdentity activeCycle,
        BatchId activeBatch,
        bool hasActiveBatch,
        WakePolicy activeWake,
        MonotonicTimestamp responsePublishedAt,
        int actionCount,
        int actionCursor,
        int actionCapacity,
        int actionHighWater,
        long actionGrowthAllocations,
        int retainedActionSlots,
        int lastCleanupThreadId,
        MonotonicTimestamp nextWakeDue,
        bool hasWakeDue,
        BatchReceipt previousReceipt,
        ServiceProjectionPublication projection,
        ServiceFault fault,
        ServiceNativeCallTotals nativeOutcome,
        int committedCount,
        ConfigGeneration latestConfiguration,
        int workerThreadId,
        bool workerIsBackground,
        long workerCycleAllocatedBytes,
        long measuredWorkerCycleCount,
        long workerStateConstructionContentionCount,
        ServiceStartDecisionFact lastStartDecision,
        ServiceCaptureFact lastCapture,
        ServiceActionFact lastAction,
        ServiceRunnerEvaluationTimingSnapshot evaluationTiming,
        ServiceRetryGateSnapshot retryGate)
    {
        Handoff = handoff;
        Phase = phase;
        InFlightCycle = inFlightCycle;
        InFlightBatch = inFlightBatch;
        HasInFlightCycle = hasInFlightCycle;
        ActiveCycle = activeCycle;
        ActiveBatch = activeBatch;
        HasActiveBatch = hasActiveBatch;
        ActiveWake = activeWake;
        ResponsePublishedAt = responsePublishedAt;
        ActionCount = actionCount;
        ActionCursor = actionCursor;
        ActionCapacity = actionCapacity;
        ActionHighWater = actionHighWater;
        ActionGrowthAllocations = actionGrowthAllocations;
        RetainedActionSlots = retainedActionSlots;
        LastCleanupThreadId = lastCleanupThreadId;
        NextWakeDue = nextWakeDue;
        HasWakeDue = hasWakeDue;
        PreviousReceipt = previousReceipt;
        Projection = projection;
        Fault = fault;
        NativeOutcome = nativeOutcome;
        CommittedCount = committedCount;
        LatestConfiguration = latestConfiguration;
        WorkerThreadId = workerThreadId;
        WorkerIsBackground = workerIsBackground;
        WorkerCycleAllocatedBytes = workerCycleAllocatedBytes;
        MeasuredWorkerCycleCount = measuredWorkerCycleCount;
        WorkerStateConstructionContentionCount =
            workerStateConstructionContentionCount;
        LastStartDecision = lastStartDecision;
        LastCapture = lastCapture;
        LastAction = lastAction;
        EvaluationTiming = evaluationTiming;
        RetryGate = retryGate;
    }

    internal ServiceHandoffSnapshot Handoff { get; }
    internal ServiceCyclePhase Phase { get; }
    internal ServiceCycleIdentity InFlightCycle { get; }
    internal BatchId InFlightBatch { get; }
    internal bool HasInFlightCycle { get; }
    internal ServiceCycleIdentity ActiveCycle { get; }
    internal BatchId ActiveBatch { get; }
    internal bool HasActiveBatch { get; }
    internal WakePolicy ActiveWake { get; }
    internal MonotonicTimestamp ResponsePublishedAt { get; }
    internal int ActionCount { get; }
    internal int ActionCursor { get; }
    internal int ActionCapacity { get; }
    internal int ActionHighWater { get; }
    internal long ActionGrowthAllocations { get; }
    internal int RetainedActionSlots { get; }
    internal int LastCleanupThreadId { get; }
    internal MonotonicTimestamp NextWakeDue { get; }
    internal bool HasWakeDue { get; }
    internal BatchReceipt PreviousReceipt { get; }
    internal ServiceProjectionPublication Projection { get; }
    internal ServiceFault Fault { get; }
    internal ServiceNativeCallTotals NativeOutcome { get; }
    internal int CommittedCount { get; }
    internal ConfigGeneration LatestConfiguration { get; }
    internal int WorkerThreadId { get; }
    internal bool WorkerIsBackground { get; }
    internal long WorkerCycleAllocatedBytes { get; }
    internal long MeasuredWorkerCycleCount { get; }
    internal long WorkerStateConstructionContentionCount { get; }
    internal ServiceStartDecisionFact LastStartDecision { get; }
    internal ServiceCaptureFact LastCapture { get; }
    internal ServiceActionFact LastAction { get; }
    internal ServiceRunnerEvaluationTimingSnapshot EvaluationTiming { get; }
    internal ServiceRetryGateSnapshot RetryGate { get; }
}
