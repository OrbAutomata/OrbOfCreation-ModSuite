using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceBatchActionOutcome<TState, TAction>
{
    private readonly ServiceBatchRuntime<TState, TAction> _runtime;
    private readonly ServiceBatchCompletion<TState, TAction> _completion;

    internal ServiceBatchActionOutcome(
        ServiceBatchRuntime<TState, TAction> runtime,
        ServiceBatchCompletion<TState, TAction> completion)
    {
        _runtime = runtime;
        _completion = completion;
    }

    internal ServiceActionDispatch Advance(
        in ServiceActionFact actionFact,
        in ServiceActionJournalAttribution attribution,
        string? attributionFailureReason,
        in ServiceFaultRecoveryFact pendingRecovery,
        MonotonicTimestamp observedAt,
        bool committed)
    {
        _runtime.Actions.AdvanceCurrentAndClear();
        if (committed) _runtime.State.CommittedCount++;
        if (committed && ServiceRetryTarget.TryFrom(in attribution, out var target))
            _runtime.State.RetryGate.ClearOnCommit(target);
        _runtime.State.ActionCursor++;
        if (_runtime.Actions.IsComplete)
        {
            var completed = BatchReceipt.Completed(
                _runtime.State.ActiveCycle,
                _runtime.State.ActiveBatch,
                _runtime.Actions.Count,
                _runtime.State.CommittedCount,
                _runtime.State.NativeOutcome,
                observedAt,
                _runtime.State.PreNativeSkippedCount);
            _completion.CompleteBatch(
                in completed,
                observedAt,
                rejectedOrFaulted: false);
            return new ServiceActionDispatch(
                actionFact,
                attribution,
                true,
                completed,
                recoveredFault: CommitRecovery(in pendingRecovery),
                attributionFailureReason: attributionFailureReason);
        }
        if (_runtime.Lifetime.IsSuperseded)
        {
            var orphaned = BatchReceipt.Orphaned(
                _runtime.State.ActiveCycle,
                _runtime.State.ActiveBatch,
                _runtime.Actions.Count,
                _runtime.State.CommittedCount,
                _runtime.Actions.Cursor,
                _runtime.State.NativeOutcome,
                observedAt,
                _runtime.State.PreNativeSkippedCount);
            _completion.CompleteLifecycleOrphan(in orphaned);
            return new ServiceActionDispatch(
                actionFact,
                attribution,
                true,
                orphaned,
                recoveredFault: CommitRecovery(in pendingRecovery),
                attributionFailureReason: attributionFailureReason);
        }
        return new ServiceActionDispatch(
            actionFact,
            attribution,
            false,
            default,
            recoveredFault: CommitRecovery(in pendingRecovery),
            attributionFailureReason: attributionFailureReason);
    }

    internal ServiceActionDispatch Terminate(
        in ServiceActionFact actionFact,
        in ServiceActionJournalAttribution attribution,
        string? attributionFailureReason,
        in ServiceActionResult result,
        in ServiceFaultRecoveryFact pendingRecovery,
        int index,
        MonotonicTimestamp observedAt,
        string? executionFailureReason)
    {
        if (result.Disposition == ServiceActionDisposition.Faulted &&
            ServiceRetryTarget.TryFrom(in attribution, out var target))
        {
            _runtime.State.RetryGate.Record(
                target,
                result.Code,
                FailureReason(in result, executionFailureReason),
                observedAt);
        }
        var terminal = BatchReceipt.Terminated(
            _runtime.State.ActiveCycle,
            _runtime.State.ActiveBatch,
            _runtime.Actions.Count,
            _runtime.State.CommittedCount,
            index,
            result,
            _runtime.State.NativeOutcome,
            observedAt,
            preNativeSkippedCount: _runtime.State.PreNativeSkippedCount);
        _completion.CompleteBatch(
            in terminal,
            observedAt,
            rejectedOrFaulted: true);
        var fault = default(ServiceFault);
        var retryDue = default(MonotonicTimestamp);
        if (result.Disposition == ServiceActionDisposition.Faulted)
        {
            var record = _runtime.ActionFaults.Record(
                ServiceFaultCategory.ActionExecution,
                result.Code,
                observedAt);
            _runtime.State.LatestFault = record.Fault;
            _runtime.State.ScheduleWake(
                record.RetryDue,
                _runtime.State.ActiveCycle.Config,
                invalidatedByConfiguration: false,
                invalidatedByWorld: false);
            fault = record.Fault;
            retryDue = record.RetryDue;
        }
        return new ServiceActionDispatch(
            actionFact,
            attribution,
            true,
            terminal,
            fault,
            retryDue,
            CommitRecovery(in pendingRecovery),
            attributionFailureReason,
            executionFailureReason);
    }

    private static string FailureReason(in ServiceActionResult result, string? executionFailureReason)
    {
        if (!string.IsNullOrWhiteSpace(result.Reason)) return result.Reason;
        if (!string.IsNullOrWhiteSpace(executionFailureReason)) return executionFailureReason!;
        return result.Code.Name;
    }

    private ServiceFaultRecoveryFact CommitRecovery(
        in ServiceFaultRecoveryFact pendingRecovery)
    {
        if (!pendingRecovery.IsPresent) return default;
        _runtime.ActionFaults.Reset();
        if (_runtime.State.LatestFault.Category == ServiceFaultCategory.ActionExecution)
            _runtime.State.LatestFault = default;
        return pendingRecovery;
    }
}
