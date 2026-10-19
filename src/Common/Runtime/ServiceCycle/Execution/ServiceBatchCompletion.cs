using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceBatchCompletion<TState, TAction>
{
    private readonly ServiceBatchRuntime<TState, TAction> _runtime;

    internal ServiceBatchCompletion(
        ServiceBatchRuntime<TState, TAction> runtime) =>
        _runtime = runtime;

    internal bool RejectForEmergencyStop(
        EmergencyStopContext emergency,
        MonotonicTimestamp now,
        out BatchReceipt receipt)
    {
        if (!emergency.IsValid)
            throw new ArgumentException(
                "A valid emergency context is required.",
                nameof(emergency));
        if (!_runtime.State.HasActiveBatch || _runtime.Actions.IsComplete)
        {
            receipt = default;
            return false;
        }

        var terminal = ServiceActionResult.Rejected(CommonActionResultCodes.EmergencyStop);
        receipt = BatchReceipt.Terminated(
            _runtime.State.ActiveCycle,
            _runtime.State.ActiveBatch,
            _runtime.Actions.Count,
            _runtime.State.CommittedCount,
            _runtime.Actions.Cursor,
            terminal,
            _runtime.State.NativeOutcome,
            now,
            emergency,
            _runtime.State.PreNativeSkippedCount);
        CompleteBatch(in receipt, now, true);
        return true;
    }

    internal bool TryAdvancePendingMainOwnership()
    {
        switch (_runtime.PendingCompletion)
        {
            case PendingMainOwnershipCompletion.None:
                return false;
            case PendingMainOwnershipCompletion.ReturnEmpty:
                if (!_runtime.Handoff.TryCompleteMainOwnershipNonBlocking()) return false;
                break;
            case PendingMainOwnershipCompletion.WorkerCleanup:
                if (!_runtime.Handoff.TryCompleteMainOwnershipWithWorkerCleanupNonBlocking(
                        _runtime.PendingCleanupFrom,
                        _runtime.PendingCleanupCount))
                    return false;
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown pending main-ownership completion.");
        }

        _runtime.PendingCompletion = PendingMainOwnershipCompletion.None;
        _runtime.PendingCleanupFrom = 0;
        _runtime.PendingCleanupCount = 0;
        return true;
    }

    internal void ReleaseForShutdown()
    {
        _runtime.State.CycleConfiguration = null;
        _runtime.State.HasActiveBatch = false;
        _runtime.State.HasInFlightCycle = false;
        _runtime.ClearVisibleActionBatch();
    }

    internal ServiceRunnerRetirement OrphanForLifecycle(
        MonotonicTimestamp now,
        ServiceCyclePhase phase,
        ServiceCycleIdentity acquiredResponseCycle,
        BatchId acquiredResponseBatch,
        ServiceWorkerResponse authoritativeResponse,
        BatchReceipt authoritativeReceipt)
    {
        if (authoritativeReceipt.IsPresent)
        {
            ClearActiveCycle();
            return new ServiceRunnerRetirement(
                phase,
                authoritativeReceipt.Cycle,
                authoritativeReceipt.Batch,
                authoritativeResponse,
                authoritativeReceipt);
        }

        var previous = _runtime.State.PreviousReceipt;
        if (previous.IsPresent &&
            previous.Cycle.Lifecycle == _runtime.Lifecycle &&
            previous.Disposition == BatchTerminalDisposition.Orphaned)
        {
            return new ServiceRunnerRetirement(
                phase,
                previous.Cycle,
                previous.Batch,
                default,
                previous);
        }

        var cycle = _runtime.State.HasInFlightCycle
            ? _runtime.State.InFlightCycle
            : acquiredResponseCycle;
        var batch = _runtime.State.HasInFlightCycle
            ? _runtime.State.InFlightBatch
            : acquiredResponseBatch;
        if (_runtime.State.HasActiveBatch && !_runtime.Actions.IsComplete)
        {
            var receipt = BatchReceipt.Orphaned(
                _runtime.State.ActiveCycle,
                _runtime.State.ActiveBatch,
                _runtime.Actions.Count,
                _runtime.State.CommittedCount,
                _runtime.Actions.Cursor,
                _runtime.State.NativeOutcome,
                now,
                _runtime.State.PreNativeSkippedCount);
            CompleteLifecycleOrphan(in receipt);
            return new ServiceRunnerRetirement(
                phase,
                receipt.Cycle,
                receipt.Batch,
                authoritativeResponse,
                receipt);
        }

        ClearActiveCycle();
        return new ServiceRunnerRetirement(
            phase,
            cycle,
            batch,
            authoritativeResponse,
            default);
    }

    internal void CompleteBatch(
        in BatchReceipt receipt,
        MonotonicTimestamp terminalAt,
        bool rejectedOrFaulted)
    {
        _runtime.State.PreviousReceipt = receipt;
        _runtime.State.ScheduleWake(
            ServiceWakeSchedule.AtBatchTerminal(
                _runtime.State.ActiveWake,
                _runtime.State.ResponsePublishedAt,
                terminalAt),
            _runtime.State.ActiveCycle.Config,
            _runtime.State.ActiveCycle.World,
            invalidatedByWorld: _runtime.Starts.WorldEndsWait(_runtime.State.ActiveWake));
        _runtime.State.CycleConfiguration = null;
        _runtime.State.HasActiveBatch = false;
        _runtime.State.HasInFlightCycle = false;
        if (rejectedOrFaulted)
        {
            if (_runtime.Actions.ReleaseRejectedBatchForWorkerCleanup(
                    out var from,
                    out var count))
                SetPendingCompletion(PendingMainOwnershipCompletion.WorkerCleanup, from, count);
            else
                ReturnMainOwnership();
            _runtime.ClearVisibleActionBatch();
            return;
        }

        _runtime.ActionFaults.Reset();
        if (_runtime.State.LatestFault.Category == ServiceFaultCategory.ActionExecution)
            _runtime.State.LatestFault = default;
        _runtime.Actions.CompleteSuccessfulBatch();
        _runtime.ClearVisibleActionBatch();
        ReturnMainOwnership();
    }

    internal void CompleteLifecycleOrphan(in BatchReceipt receipt)
    {
        _runtime.State.PreviousReceipt = receipt;
        ClearActiveCycle();
    }

    internal void ReturnMainOwnership() =>
        SetPendingCompletion(PendingMainOwnershipCompletion.ReturnEmpty, 0, 0);

    private void SetPendingCompletion(
        PendingMainOwnershipCompletion completion,
        int from,
        int count)
    {
        if (_runtime.PendingCompletion != PendingMainOwnershipCompletion.None)
            throw new InvalidOperationException(
                "Main ownership is already awaiting a deferred handback.");
        _runtime.PendingCompletion = completion;
        _runtime.PendingCleanupFrom = from;
        _runtime.PendingCleanupCount = count;
    }

    private void ClearActiveCycle()
    {
        _runtime.State.CycleConfiguration = null;
        _runtime.State.HasActiveBatch = false;
        _runtime.State.HasInFlightCycle = false;
        _runtime.ClearVisibleActionBatch();
    }
}
