using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// The worker thread and everything a cycle does on it once the evaluation has answered.
/// </summary>
/// <remarks>
/// Abstract on one member: what an evaluation reads. Every other part of a worker's cycle — the
/// state, the fault transaction, the projection, the response, the exit — is the same whichever
/// shape the service is, so the two shapes differ by an override rather than by a class.
/// </remarks>
internal abstract partial class ServiceCycleWorker<TState, TAction>
{
    private readonly IServiceCycleWorkerStateDefinition<TState> _definition;
    private readonly ReusableActionStore<TAction> _actions;
    private readonly ServiceCycleHandoff _handoff;
    private readonly ServiceStateProjectionWriteBuffer _projectionScratch;
    private readonly ServiceRetryReportBuffer _retryReports;
    private readonly ServiceFaultTracker _faults;
    private readonly ServiceId _serviceId;
    private readonly IMonotonicClock _clock;
    private readonly WakePolicy _defaultWakePolicy;
    private readonly Thread _thread;
    private readonly bool _isBackground;
    private readonly bool _measureAllocations;
    private readonly LifecycleGeneration _lifecycle;
    private readonly ServiceResourceClaimLedger _resourceClaims;
    private readonly ServiceResourceClaim _workerDefinitionClaim;
    private readonly IServiceCycleWorkerExitObserver? _exitObserver;
    private ServiceCycleWorkerState<TState> _workerState;
    private (ServiceFaultCategory Category, string Reason)? _pendingRecoveryFault;
    private ulong _nextPublication;
    private int _managedThreadId;
    private long _lastCycleAllocatedBytes;
    private long _measuredCycleCount;
    private ServiceEvaluationTimingPublication _evaluationTiming;

    private protected ServiceCycleWorker(
        IServiceCycleWorkerStateDefinition<TState> definition,
        ServiceId serviceId,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        IMonotonicClock clock,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        bool measureAllocations,
        LifecycleGeneration lifecycle,
        ServiceResourceClaimLedger resourceClaims,
        ServiceResourceClaim workerDefinitionClaim,
        IServiceCycleWorkerExitObserver? exitObserver)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _handoff = handoff ?? throw new ArgumentNullException(nameof(handoff));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _defaultWakePolicy = defaultWakePolicy;
        _serviceId = serviceId;
        _lifecycle = lifecycle;
        _resourceClaims = resourceClaims ?? throw new ArgumentNullException(nameof(resourceClaims));
        _workerDefinitionClaim = workerDefinitionClaim ??
            throw new ArgumentNullException(nameof(workerDefinitionClaim));
        _exitObserver = exitObserver;
        if (_handoff.Lifecycle.Value != 0 && _handoff.Lifecycle != lifecycle)
            throw new InvalidOperationException("Worker resources must share one lifecycle generation.");
        _actions.ValidateLifecycle(lifecycle);
        _projectionScratch = new ServiceStateProjectionWriteBuffer(ServiceStateProjectionSnapshot.MaximumEntryCount);
        _faults = new ServiceFaultTracker(faultRecoveryPolicy);
        _retryReports = new ServiceRetryReportBuffer(faultRecoveryPolicy);
        _measureAllocations = measureAllocations;
        _workerState = new ServiceCycleWorkerState<TState>(
            _definition,
            _resourceClaims,
            _clock,
            serviceId.Value);
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = ServiceCycleWorkerIdentity.Create(serviceId, lifecycle),
        };
        _isBackground = _thread.IsBackground;
    }

    internal string Name => _thread.Name ?? string.Empty;
    internal bool IsBackground => _isBackground;
    internal int ManagedThreadId => Volatile.Read(ref _managedThreadId);
    internal long LastCycleAllocatedBytes => Interlocked.Read(ref _lastCycleAllocatedBytes);
    internal long MeasuredCycleCount => Interlocked.Read(ref _measuredCycleCount);
    internal long StateFactoryContentionCount => _workerState.ContentionTotal;
    internal bool TryReadEvaluationTiming(out ServiceEvaluationTimingFact timing) =>
        _evaluationTiming.TryRead(out timing);

    internal bool TryAcknowledgeExit()
    {
        if (!_handoff.WorkerExitPrepared || _thread.IsAlive) return false;
        return _handoff.TryAcknowledgeWorkerExited();
    }

    internal bool WaitForThreadExit(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout), "A finite bounded timeout is required.");
        return _thread.Join(timeout);
    }

    internal void Start(IServiceCycleWorkerStarter? starter = null)
    {
        if (starter is null)
            _thread.Start();
        else
            starter.Start(_thread);
    }

    /// <summary>What this shape's evaluation reads, and the decision it comes back with.</summary>
    private protected abstract WakePolicy EvaluateDefinition(
        in SuiteRuntimeConfiguration config,
        World.GameWorldState world,
        Strategy.SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref TState state,
        ServiceActionWriter<TAction> actions);

}
