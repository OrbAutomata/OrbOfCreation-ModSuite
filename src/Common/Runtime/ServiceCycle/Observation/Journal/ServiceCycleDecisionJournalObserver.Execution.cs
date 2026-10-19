using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed partial class ServiceCycleDecisionJournalObserver
{
    public void StartAttemptObserved(
        int ordinal,
        in ServiceCycleStartAttempt attempt,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted || !attempt.StartInvocation.IsPresent && !attempt.Queued) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            var invocation = attempt.StartInvocation;
            if (invocation.IsPresent)
                EnsureConfiguration(invocation.Context.LatestConfig, observedAt);
            ObserveFaultTransition(
                ref service,
                attempt.RecoveredFault,
                attempt.Fault,
                observedAt);
            // A cycle exists exactly when the attempt has an identity. For a source that means its
            // capture succeeded; for an ordinary service there was never a capture to succeed.
            if (attempt.Cycle.IsValid)
                EnsureStrategy(attempt.Cycle.Strategy, observedAt);

            if (service.PendingMatches(attempt.Cycle))
            {
                if (attempt.Queued) service.MarkPendingQueued();
                return;
            }
            if (service.HasPending)
            {
                var prior = service.CompleteWithoutTerminal(observedAt);
                _journal.Observe(in prior);
            }
            if (attempt.Cycle.IsValid)
            {
                service.BeginCycle(in attempt, observedAt);
                return;
            }
            if (attempt.Queued)
                throw new InvalidOperationException("A queued journal cycle has no identity.");
            var immediate = service.Immediate(in attempt, observedAt);
            _journal.Observe(in immediate);
        }
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(StartAttemptObserved)); }
    }

    public void ResponseAcquired(
        int ordinal,
        in ServiceResponseAcquisition acquisition,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted || !acquisition.Acquired) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            var response = acquisition.Response;
            ObserveFaultTransition(
                ref service,
                response.RecoveredFault,
                response.Fault,
                observedAt);
            if (service.ApplyResponse(in acquisition, observedAt, out var decision))
                _journal.Observe(in decision);
        }
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(ResponseAcquired)); }
    }

    public void ActionDispatched(
        int ordinal,
        in ServiceActionDispatch dispatch,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted || !dispatch.Attempted) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            var actionFact = dispatch.ActionFact;
            // The recovery happened when the action completed, which the pump stamps before its
            // own later observation time.
            ObserveFaultTransition(
                ref service,
                dispatch.RecoveredFault,
                dispatch.Fault,
                actionFact.CompletedAt);
            var attribution = dispatch.Attribution;
            var action = new DecisionJournalActionObservation(
                service.Service,
                in actionFact,
                in attribution);
            _journal.ObserveAction(in action);
            if (!dispatch.BatchTerminal) return;
            if (service.ApplyTerminal(dispatch.Receipt, dispatch.Fault, observedAt, out var decision))
                _journal.Observe(in decision);
        }
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(ActionDispatched)); }
    }

    public void EmergencyRejected(
        int ordinal,
        in BatchReceipt receipt,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted || !receipt.IsPresent) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            var fault = default(ServiceFault);
            if (service.ApplyTerminal(receipt, fault, observedAt, out var decision))
                _journal.Observe(in decision);
        }
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(EmergencyRejected)); }
    }
}
