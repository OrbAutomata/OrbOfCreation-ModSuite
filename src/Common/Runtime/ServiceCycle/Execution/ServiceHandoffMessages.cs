using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal enum ServiceHandoffPhase
{
    Empty = 0,
    RequestReady = 1,
    Evaluating = 2,
    ResponseReady = 3,
    MainOwnedBatch = 4,
    Stopping = 5,
    Stopped = 6,
}

internal enum ServiceWorkerWorkKind
{
    Evaluate = 1,
    ClearRejectedSuffix = 2,
    Stop = 3,
}

internal readonly struct ServiceEvaluationRequest
{
    internal ServiceEvaluationRequest(
        long sequence,
        ConfigurationPublication configuration,
        WorldPublication<GameWorldState> world,
        StrategyPublication strategy,
        ServiceCycleContext context,
        BatchId batch)
    {
        Sequence = sequence;
        Configuration = configuration;
        World = world;
        Strategy = strategy;
        Context = context;
        Batch = batch;
    }

    internal long Sequence { get; }
    internal ConfigurationPublication Configuration { get; }

    /// <summary>
    /// The world this cycle runs against, pinned once on the owner thread at cycle start and carried
    /// across the handoff exactly as configuration is. The snapshot is immutable, so the worker reads
    /// it without synchronisation and cannot be handed a newer one mid-cycle.
    /// </summary>
    internal WorldPublication<GameWorldState> World { get; }

    /// <summary>
    /// The bulletin this cycle runs against, pinned beside the world and the configuration. It is the
    /// neutral one until a strategist exists, and a service reads it the same way either way.
    /// </summary>
    internal StrategyPublication Strategy { get; }

    internal ServiceCycleContext Context { get; }
    internal BatchId Batch { get; }
}

internal readonly struct ServiceWorkerResponse
{
    private ServiceWorkerResponse(
        long sequence,
        bool succeeded,
        bool transientContention,
        ServiceCycleIdentity cycle,
        BatchId batch,
        MonotonicTimestamp evaluationStartedAt,
        MonotonicTimestamp evaluationCompletedAt,
        WakePolicy wakePolicy,
        MonotonicTimestamp publishedAt,
        MonotonicTimestamp wakeDue,
        ServiceProjectionContext projectionContext,
        ServiceStateProjectionSnapshot projection,
        ServiceFault fault,
        MonotonicTimestamp retryDue,
        ServiceFaultRecoveryFact recoveredFault,
        ServiceActionStoreMetrics actionMetrics,
        int actionCount,
        BatchReceipt zeroActionReceipt,
        bool hasEvaluationOutcome,
        WakePolicy evaluationWakePolicy,
        int evaluatedActionCount,
        ServiceRetryReport[]? retryReports)
    {
        Sequence = sequence;
        Succeeded = succeeded;
        TransientContention = transientContention;
        Cycle = cycle;
        Batch = batch;
        EvaluationStartedAt = evaluationStartedAt;
        EvaluationCompletedAt = evaluationCompletedAt;
        WakePolicy = wakePolicy;
        PublishedAt = publishedAt;
        WakeDue = wakeDue;
        ProjectionContext = projectionContext;
        Projection = projection;
        Fault = fault;
        RetryDue = retryDue;
        RecoveredFault = recoveredFault;
        ActionMetrics = actionMetrics;
        ActionCount = actionCount;
        ZeroActionReceipt = zeroActionReceipt;
        HasEvaluationOutcome = hasEvaluationOutcome;
        EvaluationWakePolicy = evaluationWakePolicy;
        EvaluatedActionCount = evaluatedActionCount;
        RetryReports = retryReports;
    }

    internal long Sequence { get; }
    internal bool Succeeded { get; }
    internal bool TransientContention { get; }
    internal ServiceCycleIdentity Cycle { get; }
    internal BatchId Batch { get; }
    internal MonotonicTimestamp EvaluationStartedAt { get; }
    internal MonotonicTimestamp EvaluationCompletedAt { get; }
    internal WakePolicy WakePolicy { get; }
    internal MonotonicTimestamp PublishedAt { get; }
    internal MonotonicTimestamp WakeDue { get; }
    internal ServiceProjectionContext ProjectionContext { get; }
    internal ServiceStateProjectionSnapshot Projection { get; }
    internal ServiceFault Fault { get; }
    internal MonotonicTimestamp RetryDue { get; }
    internal ServiceFaultRecoveryFact RecoveredFault { get; }
    internal ServiceActionStoreMetrics ActionMetrics { get; }
    internal int ActionCount { get; }
    internal BatchReceipt ZeroActionReceipt { get; }
    internal bool HasEvaluationOutcome { get; }
    internal WakePolicy EvaluationWakePolicy { get; }
    internal int EvaluatedActionCount { get; }
    internal ServiceRetryReport[]? RetryReports { get; }

    internal static ServiceWorkerResponse Success(
        long sequence,
        ServiceCycleIdentity cycle,
        BatchId batch,
        MonotonicTimestamp evaluationStartedAt,
        MonotonicTimestamp evaluationCompletedAt,
        WakePolicy wakePolicy,
        MonotonicTimestamp publishedAt,
        MonotonicTimestamp wakeDue,
        ServiceProjectionContext projectionContext,
        in ServiceStateProjectionSnapshot projection,
        ServiceActionStoreMetrics actionMetrics,
        int actionCount,
        BatchReceipt zeroActionReceipt,
        ServiceFaultRecoveryFact recoveredFault,
        ServiceRetryReport[]? retryReports = null) => new(
            sequence, true, false, cycle, batch, evaluationStartedAt, evaluationCompletedAt,
            wakePolicy, publishedAt, wakeDue, projectionContext, projection, default, default,
            recoveredFault, actionMetrics, actionCount, zeroActionReceipt,
            false, default, 0, retryReports);

    internal static ServiceWorkerResponse Failure(
        long sequence,
        ServiceCycleIdentity cycle,
        BatchId batch,
        MonotonicTimestamp evaluationStartedAt,
        MonotonicTimestamp evaluationCompletedAt,
        ServiceFault fault,
        MonotonicTimestamp retryDue,
        ServiceActionStoreMetrics actionMetrics,
        bool hasEvaluationOutcome = false,
        WakePolicy evaluationWakePolicy = default,
        int evaluatedActionCount = 0) => new(
            sequence, false, false, cycle, batch, evaluationStartedAt, evaluationCompletedAt,
            WakePolicy.At(retryDue), fault.ObservedAt, retryDue, default, default, fault, retryDue,
            default, actionMetrics, 0, default,
            hasEvaluationOutcome, evaluationWakePolicy, evaluatedActionCount, null);

    internal static ServiceWorkerResponse Contention(
        long sequence,
        ServiceCycleIdentity cycle,
        BatchId batch,
        MonotonicTimestamp evaluationStartedAt,
        MonotonicTimestamp evaluationCompletedAt,
        MonotonicTimestamp observedAt,
        MonotonicTimestamp retryDue,
        ServiceActionStoreMetrics actionMetrics) => new(
            sequence, false, true, cycle, batch, evaluationStartedAt, evaluationCompletedAt,
            WakePolicy.At(retryDue), observedAt, retryDue, default, default, default, retryDue,
            default, actionMetrics, 0, default,
            false, default, 0, null);
}

internal readonly struct ServiceHandoffSnapshot
{
    internal ServiceHandoffSnapshot(
        ServiceHandoffPhase phase,
        long requestSequence,
        long transitionCount,
        long workerWaitCount,
        long cleanupRequestCount,
        long cleanupAcknowledgementCount,
        int lastCleanupThreadId,
        bool cleanupPending,
        bool stopRequested)
    {
        Phase = phase;
        RequestSequence = requestSequence;
        TransitionCount = transitionCount;
        WorkerWaitCount = workerWaitCount;
        CleanupRequestCount = cleanupRequestCount;
        CleanupAcknowledgementCount = cleanupAcknowledgementCount;
        LastCleanupThreadId = lastCleanupThreadId;
        CleanupPending = cleanupPending;
        StopRequested = stopRequested;
    }

    internal ServiceHandoffPhase Phase { get; }
    internal long RequestSequence { get; }
    internal long TransitionCount { get; }
    internal long WorkerWaitCount { get; }
    internal long CleanupRequestCount { get; }
    internal long CleanupAcknowledgementCount { get; }
    internal int LastCleanupThreadId { get; }
    internal bool CleanupPending { get; }
    internal bool StopRequested { get; }
}
