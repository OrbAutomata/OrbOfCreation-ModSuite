using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceBatchResponseHandler<TState, TAction>
{
    private readonly ServiceBatchRuntime<TState, TAction> _runtime;
    private readonly ServiceBatchCompletion<TState, TAction> _completion;

    internal ServiceBatchResponseHandler(
        ServiceBatchRuntime<TState, TAction> runtime,
        ServiceBatchCompletion<TState, TAction> completion)
    {
        _runtime = runtime;
        _completion = completion;
    }

    internal bool TryAcquire()
    {
        if (!_runtime.Handoff.TryAcquireResponse(out var response)) return false;
        Publish(in response, response.PublishedAt, nonBlockingHandoff: false, out _);
        return true;
    }

    internal ServiceResponseAcquisition TryAcquireNonBlocking(
        MonotonicTimestamp now)
    {
        if (!_runtime.Handoff.TryAcquireResponseNonBlocking(out var response))
            return default;
        Publish(in response, now, nonBlockingHandoff: true, out var terminalReceipt);
        return new ServiceResponseAcquisition(in response, terminalReceipt);
    }

    internal bool TryAcquireNonBlockingWithoutFacts(
        MonotonicTimestamp now,
        out BatchReceipt terminalReceipt)
    {
        if (!_runtime.Handoff.TryAcquireResponseNonBlocking(out var response))
        {
            terminalReceipt = default;
            return false;
        }
        Publish(in response, now, nonBlockingHandoff: true, out terminalReceipt);
        return true;
    }

    internal void PublishAuthoritativeTerminal(
        in ServiceWorkerResponse response,
        MonotonicTimestamp now) =>
        Publish(in response, now, nonBlockingHandoff: true, out _);

    internal void MarkOutstandingEmergency(EmergencyStopContext emergency)
    {
        if (!emergency.IsValid)
            throw new ArgumentException(
                "A valid emergency context is required.",
                nameof(emergency));
        if (!_runtime.OutstandingResponseEmergency.IsValid)
            _runtime.OutstandingResponseEmergency = emergency;
    }

    private void Publish(
        in ServiceWorkerResponse response,
        MonotonicTimestamp terminalNow,
        bool nonBlockingHandoff,
        out BatchReceipt terminalReceipt)
    {
        terminalReceipt = default;
        if (response.Cycle.IsValid &&
            response.Cycle.Lifecycle != _runtime.Lifecycle)
        {
            throw new InvalidOperationException(
                "The response belongs to another lifecycle generation.");
        }

        var responseEmergency = _runtime.OutstandingResponseEmergency;
        _runtime.OutstandingResponseEmergency = default;
        if (!response.Succeeded)
        {
            _runtime.PublishActionMetrics(response.ActionMetrics);
            if (!response.TransientContention)
                _runtime.State.LatestFault = response.Fault;
            _runtime.State.ScheduleWake(
                response.WakeDue,
                response.Cycle.Config,
                invalidatedByConfiguration: false,
                invalidatedByWorld: false);
            _runtime.State.CycleConfiguration = null;
            _runtime.State.HasActiveBatch = false;
            _runtime.State.HasInFlightCycle = false;
            _runtime.ClearVisibleActionBatch();
            _completion.ReturnMainOwnership(nonBlockingHandoff);
            return;
        }

        _runtime.State.ActiveCycle = response.Cycle;
        _runtime.State.ActiveBatch = response.Batch;
        _runtime.State.ActiveWake = response.WakePolicy;
        _runtime.State.ResponsePublishedAt = response.PublishedAt;
        _runtime.State.NextWakeDue = response.WakeDue;
        _runtime.State.Projection = new ServiceProjectionPublication(
            response.ProjectionContext,
            response.Projection,
            _runtime.Configuration.ReadLatest().Generation);
        _runtime.State.LatestConfigGeneration =
            _runtime.State.Projection.LatestConfiguration;
        if (_runtime.State.LatestFault.Category != ServiceFaultCategory.ActionExecution)
            _runtime.State.LatestFault = default;
        _runtime.Starts.ResetStartFaults();
        _runtime.State.NativeOutcome = default;
        _runtime.State.CommittedCount = 0;
        _runtime.State.PreNativeSkippedCount = 0;
        _runtime.PublishActionMetrics(response.ActionMetrics);

        if (response.ActionCount != 0)
        {
            _runtime.State.HasActiveBatch = true;
            if (responseEmergency.IsValid)
            {
                _completion.RejectForEmergencyStop(
                    responseEmergency,
                    terminalNow,
                    nonBlockingHandoff,
                    out terminalReceipt);
            }
            return;
        }

        _runtime.State.HasActiveBatch = false;
        _runtime.State.PreviousReceipt = response.ZeroActionReceipt;
        terminalReceipt = response.ZeroActionReceipt;
        _runtime.State.HasInFlightCycle = false;
        _runtime.Actions.CompleteSuccessfulBatch();
        _runtime.ClearVisibleActionBatch();
        _runtime.State.ScheduleWake(
            response.WakeDue,
            response.Cycle.Config,
            response.Cycle.World,
            invalidatedByWorld: _runtime.Starts.WorldEndsWait(response.WakePolicy));
        _runtime.State.CycleConfiguration = null;
        _completion.ReturnMainOwnership(nonBlockingHandoff);
    }
}
