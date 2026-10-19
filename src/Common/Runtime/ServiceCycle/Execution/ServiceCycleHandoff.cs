using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleHandoff
{
    private readonly object _gate = new();
    private readonly AutoResetEvent _workerWake = new(false);
    private ServiceHandoffPhase _phase;
    private int _phaseHint;
    private ServiceEvaluationRequest _request;
    private ServiceWorkerResponse _response;
    private long _nextSequence;
    private long _transitionCount;
    private long _workerWaitCount;
    private long _responsePublishedWorkerWaitCount;
    private long _cleanupRequestCount;
    private long _cleanupAcknowledgementCount;
    private int _lastCleanupThreadId;
    private bool _cleanupPending;
    private bool _cleanupClaimed;
    private int _cleanupFrom;
    private int _cleanupCount;
    private bool _stopRequested;
    private int _workerWakeDisposed;
    private int _workerExitPrepared;
    private int _responseWaiterCount;
    private int _workerReadyWaiterCount;
    private int _responseWakePulseCount;
    private readonly LifecycleGeneration _lifecycle;

    internal ServiceCycleHandoff(LifecycleGeneration lifecycle = default)
    {
        _lifecycle = lifecycle;
        _phase = ServiceHandoffPhase.Empty;
        _phaseHint = (int)ServiceHandoffPhase.Empty;
    }

    internal ServiceHandoffPhase PhaseHint =>
        (ServiceHandoffPhase)Volatile.Read(ref _phaseHint);
    internal bool WorkerWakeDisposed =>
        Volatile.Read(ref _workerWakeDisposed) != 0;
    internal bool WorkerExitPrepared =>
        Volatile.Read(ref _workerExitPrepared) != 0;
    internal LifecycleGeneration Lifecycle => _lifecycle;
    internal int OfflineResponseWaiterCount =>
        Volatile.Read(ref _responseWaiterCount);
    internal int OfflineResponseWakePulseCount =>
        Volatile.Read(ref _responseWakePulseCount);

    internal ServiceHandoffSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CreateSnapshot();
            }
        }
    }

    internal bool TrySnapshot(out ServiceHandoffSnapshot snapshot)
    {
        if (!Monitor.TryEnter(_gate, 0))
        {
            snapshot = default;
            return false;
        }
        try
        {
            snapshot = CreateSnapshot();
            return true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void TransitionTo(ServiceHandoffPhase phase)
    {
        _phase = phase;
        Volatile.Write(ref _phaseHint, (int)phase);
        _transitionCount++;
    }

    private ServiceHandoffSnapshot CreateSnapshot() => new(
        _phase,
        _nextSequence,
        _transitionCount,
        _workerWaitCount,
        _cleanupRequestCount,
        _cleanupAcknowledgementCount,
        _lastCleanupThreadId,
        _cleanupPending,
        Volatile.Read(ref _stopRequested));
}
