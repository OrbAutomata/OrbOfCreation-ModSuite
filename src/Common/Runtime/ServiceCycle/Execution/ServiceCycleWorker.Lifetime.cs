using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal abstract partial class ServiceCycleWorker<TState, TAction>
{
    private void Run()
    {
        Volatile.Write(
            ref _managedThreadId,
            Thread.CurrentThread.ManagedThreadId);
        try
        {
            while (true)
            {
                var work = _handoff.WaitForWorkerWork(
                    out var request,
                    out var clearFrom,
                    out var clearCount);
                if (work == ServiceWorkerWorkKind.Stop) break;
                if (work == ServiceWorkerWorkKind.ClearRejectedSuffix)
                {
                    _actions.ClearRejectedSuffixOnWorker(clearFrom, clearCount);
                    _handoff.AcknowledgeCleanup(
                        Thread.CurrentThread.ManagedThreadId);
                    continue;
                }

                var before = _measureAllocations
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0;
                var continueRunning = Evaluate(in request);
                if (_measureAllocations)
                {
                    Interlocked.Exchange(
                        ref _lastCycleAllocatedBytes,
                        GC.GetAllocatedBytesForCurrentThread() - before);
                    Interlocked.Increment(ref _measuredCycleCount);
                }
                if (!continueRunning) break;
            }
        }
        finally
        {
            _actions.AbortWorkerWrite();
            _workerState.ReleaseForShutdown();
            _resourceClaims.Release(_workerDefinitionClaim);
            _handoff.PrepareWorkerExit();
            try
            {
                _exitObserver?.OnWorkerExitPrepared();
            }
            catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
            {
                GameActionFaultLog.Record(exception, _serviceId.Value + " worker exit observer");
            }
        }
    }
}
