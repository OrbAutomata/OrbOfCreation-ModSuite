using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal abstract partial class ServiceCycleWorker<TState, TAction>
{
    private bool Evaluate(in ServiceEvaluationRequest request)
    {
        var evaluationStartedAt = _clock.Now;
        BeginEvaluation(request.Sequence, evaluationStartedAt);
        if (request.Context.Identity.Lifecycle != _lifecycle)
        {
            CompleteEvaluation(_clock.Now);
            return false;
        }
        _actions.ValidateLifecycle(_lifecycle);
        if (!EnsureState(
                in request,
                evaluationStartedAt,
                out var continueRunning))
            return continueRunning;
        if (_pendingRecoveryFaultCategory is { } pendingRecoveryFault)
        {
            _pendingRecoveryFaultCategory = null;
            var observedAt = _clock.Now;
            var record = _faults.Record(pendingRecoveryFault, observedAt);
            var pendingResponse = ServiceWorkerResponse.Failure(
                request.Sequence,
                request.Context.Identity,
                request.Batch,
                evaluationStartedAt,
                observedAt,
                record.Fault,
                record.RetryDue,
                _actions.Metrics);
            return PublishResponse(request.Sequence, in pendingResponse);
        }

        var stage = ServiceFaultCategory.Evaluation;
        var hasEvaluationOutcome = false;
        var evaluationWakePolicy = default(WakePolicy);
        var evaluatedActionCount = 0;
        try
        {
            _actions.BeginWrite();
            _projectionScratch.Reset();
            _retryReports.Reset();
            var configuration = request.Configuration.Snapshot;
            var context = request.Context;
            var writer = new ServiceActionWriter<TAction>(_actions, _retryReports);
            ref var workerState =
                ref ServiceCycleWorkerState<TState>.BorrowValue(ref _workerState);

            stage = ServiceFaultCategory.Evaluation;
            var requestedWake = EvaluateDefinition(
                in configuration,
                request.World.Snapshot,
                request.Strategy.Bulletin,
                in context,
                ref workerState,
                writer);

            stage = ServiceFaultCategory.ResponseValidation;
            evaluationWakePolicy = ServiceWakeSchedule.Resolve(
                requestedWake,
                _defaultWakePolicy);
            evaluatedActionCount = _actions.Count;
            hasEvaluationOutcome = true;

            stage = ServiceFaultCategory.StateProjection;
            var projectedAt = _clock.Now;
            var publication =
                new StatePublicationId(checked(++_nextPublication));
            var projectionContext = new ServiceProjectionContext(
                request.Context.Identity,
                publication,
                projectedAt);
            var projectionWriter =
                new ServiceStateProjectionBuilder(_projectionScratch);
            _definition.ProjectState(
                in workerState,
                in projectionContext,
                projectionWriter);
            var projection = _projectionScratch.CreateSnapshot();

            stage = ServiceFaultCategory.ResponseValidation;
            var publishedAt = _clock.Now;
            var wakeDue = ServiceWakeSchedule.AtResponse(
                evaluationWakePolicy,
                publishedAt,
                _actions.Count == 0);
            var zeroReceipt = _actions.Count == 0
                ? BatchReceipt.Completed(
                    request.Context.Identity,
                    request.Batch,
                    0,
                    default(ServiceNativeCallTotals),
                    publishedAt)
                : default;

            var recoveredFault = _faults.PendingRecovery(publishedAt);
            var response = ServiceWorkerResponse.Success(
                request.Sequence,
                request.Context.Identity,
                request.Batch,
                evaluationStartedAt,
                publishedAt,
                evaluationWakePolicy,
                publishedAt,
                wakeDue,
                projectionContext,
                in projection,
                _actions.Metrics,
                _actions.Count,
                zeroReceipt,
                recoveredFault,
                _retryReports.ToArrayOrNull());
            ValidateResponse(in response);
            if (PublishResponse(request.Sequence, in response))
            {
                _faults.Reset();
                return true;
            }

            _actions.AbortWorkerWrite();
            return false;
        }
        catch (Exception ex)
        {
            if (ex is OverflowException or OutOfMemoryException)
                stage = ServiceFaultCategory.Storage;
            _actions.AbortWorkerWrite();
            var recovered = _workerState.Recreate(_lifecycle);
            if (recovered == ServiceCycleWorkerStateCreationResult.Contended)
            {
                _pendingRecoveryFaultCategory = stage;
                return PublishStateFactoryContention(
                    in request,
                    evaluationStartedAt);
            }
            if (recovered != ServiceCycleWorkerStateCreationResult.Created)
            {
                _pendingRecoveryFaultCategory = null;
                stage = ServiceFaultCategory.StateFactory;
            }
            var observedAt = _clock.Now;
            var record = _faults.Record(stage, observedAt);
            var response = ServiceWorkerResponse.Failure(
                request.Sequence,
                request.Context.Identity,
                request.Batch,
                evaluationStartedAt,
                observedAt,
                record.Fault,
                record.RetryDue,
                _actions.Metrics,
                hasEvaluationOutcome:
                    stage == ServiceFaultCategory.StateProjection &&
                    hasEvaluationOutcome,
                evaluationWakePolicy: evaluationWakePolicy,
                evaluatedActionCount: evaluatedActionCount);
            return PublishResponse(request.Sequence, in response);
        }
    }


    private void BeginEvaluation(
        long requestSequence,
        MonotonicTimestamp startedAt) =>
        _evaluationTiming.Begin(requestSequence, startedAt);

    private void CompleteEvaluation(MonotonicTimestamp completedAt) =>
        _evaluationTiming.Complete(completedAt);

    private bool PublishResponse(
        long requestSequence,
        in ServiceWorkerResponse response)
    {
        CompleteEvaluation(response.EvaluationCompletedAt);
        return _handoff.TryPublishResponse(requestSequence, in response);
    }

    private static void ValidateResponse(in ServiceWorkerResponse response)
    {
        if (!response.Succeeded ||
            !response.Cycle.IsValid ||
            !response.Batch.IsValid ||
            !response.WakePolicy.IsValid ||
            response.WakePolicy.Kind == WakePolicyKind.Default ||
            !response.ProjectionContext.Publication.IsValid ||
            response.ActionCount < 0)
        {
            throw new InvalidOperationException(
                "The evaluator produced an invalid service response.");
        }
        if (response.ActionCount == 0 &&
            !response.ZeroActionReceipt.IsPresent)
        {
            throw new InvalidOperationException(
                "A zero-action response must be terminal at publication.");
        }
        if (response.ActionCount != 0 &&
            response.ZeroActionReceipt.IsPresent)
        {
            throw new InvalidOperationException(
                "A non-empty response cannot carry a zero-action receipt.");
        }
    }
}
