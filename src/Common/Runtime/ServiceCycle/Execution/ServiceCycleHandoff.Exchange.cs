using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleHandoff
{
    internal bool TryPublishRequestNonBlocking(
        ConfigurationPublication configuration,
        WorldPublication<GameWorldState> world,
        StrategyPublication strategy,
        in ServiceCycleContext context,
        BatchId batch,
        out long sequence)
    {
        if (PhaseHint != ServiceHandoffPhase.Empty ||
            !Monitor.TryEnter(_gate, 0))
        {
            sequence = 0;
            return false;
        }
        try
        {
            if (_phase != ServiceHandoffPhase.Empty ||
                _cleanupPending ||
                Volatile.Read(ref _stopRequested))
            {
                sequence = 0;
                return false;
            }
            if (_lifecycle.Value != 0 &&
                context.Identity.Lifecycle != _lifecycle)
            {
                throw new InvalidOperationException(
                    "The request belongs to another lifecycle generation.");
            }
            sequence = checked(++_nextSequence);
            _request = new ServiceEvaluationRequest(
                sequence,
                configuration,
                world,
                strategy,
                context,
                batch);
            TransitionTo(ServiceHandoffPhase.RequestReady);
            _workerWake.Set();
            return true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    internal ServiceWorkerWorkKind WaitForWorkerWork(
        out ServiceEvaluationRequest request,
        out int cleanupFrom,
        out int cleanupCount)
    {
        while (true)
        {
            lock (_gate)
            {
                if (_cleanupPending && !_cleanupClaimed)
                {
                    _cleanupClaimed = true;
                    request = default;
                    cleanupFrom = _cleanupFrom;
                    cleanupCount = _cleanupCount;
                    return ServiceWorkerWorkKind.ClearRejectedSuffix;
                }
                if (Volatile.Read(ref _stopRequested))
                {
                    TransitionTo(ServiceHandoffPhase.Stopping);
                    request = default;
                    cleanupFrom = 0;
                    cleanupCount = 0;
                    return ServiceWorkerWorkKind.Stop;
                }
                if (_phase == ServiceHandoffPhase.RequestReady)
                {
                    request = _request;
                    TransitionTo(ServiceHandoffPhase.Evaluating);
                    cleanupFrom = 0;
                    cleanupCount = 0;
                    return ServiceWorkerWorkKind.Evaluate;
                }
                _workerWaitCount++;
                PulseOfflineWaitersUnderGate();
            }
            _workerWake.WaitOne();
        }
    }

    internal bool TryPublishResponse(
        long sequence,
        in ServiceWorkerResponse response)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _stopRequested) ||
                _phase != ServiceHandoffPhase.Evaluating ||
                sequence != _request.Sequence ||
                response.Sequence != sequence)
                return false;
            if (_lifecycle.Value != 0 &&
                response.Cycle.Lifecycle != _lifecycle)
                return false;
            _response = response;
            _request = default;
            _responsePublishedWorkerWaitCount = _workerWaitCount;
            TransitionTo(ServiceHandoffPhase.ResponseReady);
            PulseOfflineWaitersUnderGate();
            return true;
        }
    }

    internal bool TryAcquireResponseNonBlocking(out ServiceWorkerResponse response)
    {
        if (PhaseHint != ServiceHandoffPhase.ResponseReady ||
            !Monitor.TryEnter(_gate, 0))
        {
            response = default;
            return false;
        }
        try
        {
            if (_phase != ServiceHandoffPhase.ResponseReady)
            {
                response = default;
                return false;
            }
            response = _response;
            TransitionTo(ServiceHandoffPhase.MainOwnedBatch);
            return true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    internal bool TryAcquireAuthoritativeTerminalResponseNonBlocking(
        out ServiceWorkerResponse response,
        out bool acquired)
    {
        acquired = false;
        if (PhaseHint != ServiceHandoffPhase.ResponseReady ||
            !Monitor.TryEnter(_gate, 0))
        {
            response = default;
            return false;
        }
        try
        {
            if (_phase != ServiceHandoffPhase.ResponseReady)
            {
                response = default;
                return false;
            }
            if (!_response.Succeeded || !_response.ZeroActionReceipt.IsPresent)
            {
                response = default;
                return true;
            }
            response = _response;
            TransitionTo(ServiceHandoffPhase.MainOwnedBatch);
            acquired = true;
            return true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }
}
