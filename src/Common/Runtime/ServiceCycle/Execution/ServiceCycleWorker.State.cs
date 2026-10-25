using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal abstract partial class ServiceCycleWorker<TState, TAction>
{
    private bool EnsureState(
        in ServiceEvaluationRequest request,
        MonotonicTimestamp evaluationStartedAt,
        out bool continueRunning)
    {
        if (_workerState.HasValue)
        {
            continueRunning = true;
            return true;
        }
        try
        {
            var creation =
                _workerState.TryCreate(request.Context.Identity.Lifecycle);
            if (creation == ServiceCycleWorkerStateCreationResult.Contended)
            {
                continueRunning = PublishStateFactoryContention(
                    in request,
                    evaluationStartedAt);
                return false;
            }
            if (creation != ServiceCycleWorkerStateCreationResult.Created)
                throw new InvalidOperationException(
                    "The state factory returned a state that could not be claimed by this live generation.");
            continueRunning = true;
            return true;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            _workerState.ResetAfterCreationFailure();
            _pendingRecoveryFault = null;
            var observedAt = _clock.Now;
            var record = _faults.RecordException(
                ServiceFaultCategory.StateFactory,
                observedAt,
                exception,
                _serviceId.Value);
            var response = ServiceWorkerResponse.Failure(
                request.Sequence,
                request.Context.Identity,
                request.Batch,
                evaluationStartedAt,
                observedAt,
                record.Fault,
                record.RetryDue,
                _actions.Metrics);
            continueRunning = PublishResponse(
                request.Sequence,
                in response);
            return false;
        }
    }

    private bool PublishStateFactoryContention(
        in ServiceEvaluationRequest request,
        MonotonicTimestamp evaluationStartedAt)
    {
        var milliseconds = _workerState.RecordContention();
        var observedAt = _clock.Now;
        var response = ServiceWorkerResponse.Contention(
            request.Sequence,
            request.Context.Identity,
            request.Batch,
            evaluationStartedAt,
            observedAt,
            observedAt,
            observedAt + MonotonicDuration.FromTimeSpan(
                TimeSpan.FromMilliseconds(milliseconds)),
            _actions.Metrics);
        return PublishResponse(request.Sequence, in response);
    }
}
