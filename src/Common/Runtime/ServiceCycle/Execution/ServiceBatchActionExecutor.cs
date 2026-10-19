using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceBatchActionExecutor<TState, TAction>
{
    private readonly ServiceBatchRuntime<TState, TAction> _runtime;
    private readonly ServiceBatchActionOutcome<TState, TAction> _outcomes;

    internal ServiceBatchActionExecutor(
        ServiceBatchRuntime<TState, TAction> runtime,
        ServiceBatchCompletion<TState, TAction> completion)
    {
        _runtime = runtime;
        _outcomes = new ServiceBatchActionOutcome<TState, TAction>(
            runtime,
            completion);
    }

    internal ServiceActionDispatch TryExecuteOneNonBlocking(
        MonotonicTimestamp now,
        int ordinal,
        IServiceCycleAttemptObserver? observer)
    {
#if SERVICE_CYCLE_PROFILE
        var coordinates = default(ServiceCycleProfileCoordinates);
        return TryExecute(
            now,
            ordinal,
            observer,
            in coordinates);
#else
        return TryExecute(now, ordinal, observer);
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionDispatch TryExecuteOneNonBlockingProfiled(
        MonotonicTimestamp now,
        int ordinal,
        IServiceCycleAttemptObserver? observer,
        in ServiceCycleProfileCoordinates coordinates) =>
        TryExecute(
            now,
            ordinal,
            observer,
            in coordinates);
#endif

    private ServiceActionDispatch TryExecute(
        MonotonicTimestamp now,
        int ordinal,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates coordinates
#endif
        )
    {
        if (!_runtime.State.HasActiveBatch || _runtime.Actions.IsComplete)
            return default;
        var configuration = _runtime.State.CycleConfiguration ??
            throw new InvalidOperationException(
                "The main-owned batch lost its pinned configuration.");
        var index = _runtime.Actions.Cursor;
        var context = new ServiceActionContext(
            _runtime.State.ActiveCycle,
            _runtime.State.ActiveBatch,
            new ActionId(checked((ulong)index + 1)),
            index,
            now
#if SERVICE_CYCLE_PROFILE
            , in coordinates
#endif
            );
        ref readonly var action = ref _runtime.Actions.GetCurrent();
        var snapshot = configuration.Snapshot;
        observer?.ActionAttempted(ordinal, in context);
        var executionStartedAt = _runtime.Clock.Now;
        var attribution = DescribeAction(in action, out var attributionFailureReason);
        var result = ExecuteAction(in action, in snapshot, in context, out var executionFailureReason);

        var observedAt = _runtime.Clock.Now;
        var actionFact = new ServiceActionFact(
            context,
            result,
            executionStartedAt,
            observedAt);
        _runtime.State.LastAction = actionFact;
        var advances =
            result.Disposition is ServiceActionDisposition.Committed or ServiceActionDisposition.Skipped;
        var pendingRecovery =
            advances
                ? _runtime.ActionFaults.PendingRecovery(observedAt)
                : default;
        var native = result.NativeCallOutcome;
        _runtime.State.NativeOutcome = ServiceActionResult.AddNativeOutcomes(
            in _runtime.State.NativeOutcome,
            in native);
        if (result.Disposition == ServiceActionDisposition.Skipped &&
            result.Effect == ServiceActionEffect.None)
            _runtime.State.PreNativeSkippedCount++;

        return advances
            ? _outcomes.Advance(
                in actionFact,
                in attribution,
                attributionFailureReason,
                in result,
                in pendingRecovery,
                observedAt)
            : _outcomes.Terminate(
                in actionFact,
                in attribution,
                attributionFailureReason,
                in result,
                in pendingRecovery,
                index,
                observedAt,
                executionFailureReason);
    }

    private ServiceActionResult ExecuteAction(
        in TAction action,
        in SuiteRuntimeConfiguration snapshot,
        in ServiceActionContext context,
        out string? failureReason)
    {
        failureReason = null;
        try
        {
            var result = _runtime.Definition.TryExecute(
                in action,
                in snapshot,
                in context);
            if (!result.IsValid)
                failureReason = "the service returned an invalid action result";
            else if (!_runtime.State.RetryGate.Allows(result.TargetInitialBackoff, result.TargetBackoffCap))
                failureReason = "the service set a target's backoff below its initial fault backoff";
            else
                return result;
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            failureReason = Logged("the action threw before it reported an outcome.", exception);
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }
    }

    private ServiceActionJournalAttribution DescribeAction(
        in TAction action,
        out string? failureReason)
    {
        try
        {
            var attribution = _runtime.Definition.DescribeAction(in action);
            if (attribution.IsValid)
            {
                failureReason = null;
                return attribution;
            }
            failureReason = "the service returned an invalid attribution value";
            return ServiceActionJournalAttribution.Failed;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            failureReason = Logged("describing the action threw.", exception);
            return ServiceActionJournalAttribution.Failed;
        }
    }

    private string Logged(string sentence, Exception exception) =>
        GameActionFaultLog.Reason(sentence, exception, _runtime.Definition.ServiceId.Value).Text;

}
