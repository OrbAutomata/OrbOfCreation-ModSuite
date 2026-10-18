using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpEvidenceEmitter
{
    private readonly SuiteFramePumpTraceSession _traces;
    private readonly SuiteFramePumpJournalSession _journal;
    private readonly ServiceFailureBuffer _serviceFailures;
#if SERVICE_CYCLE_PROFILE
    private readonly SuiteFramePumpEvidenceProfiler _profiler;
#endif

    internal SuiteFramePumpEvidenceEmitter(
        SuiteFramePumpTraceSession traces,
        SuiteFramePumpJournalSession journal,
        ServiceFailureBuffer serviceFailures
#if SERVICE_CYCLE_PROFILE
        , SuiteFramePumpEvidenceProfiler profiler
#endif
        )
    {
        _traces = traces;
        _journal = journal;
        _serviceFailures = serviceFailures;
#if SERVICE_CYCLE_PROFILE
        _profiler = profiler;
#endif
    }

    internal void ResponseAcquired(
        int ordinal,
        in ServiceResponseAcquisition acquisition,
        MonotonicTimestamp observedAt,
        long frameIdentity)
    {
        var trace = _traces.Dispatch;
        if (trace is not null)
        {
#if SERVICE_CYCLE_PROFILE
            var cycle = acquisition.Response.Cycle;
            var profile = _profiler.Begin(
                ServiceCycleProfileSpan.SemanticTerminal,
                ordinal,
                cycle.Lifecycle.Value,
                cycle.Cycle.Value,
                frameIdentity);
            try { trace.ResponseAcquired(ordinal, in acquisition); }
            finally { profile.Complete(); }
#else
            trace.ResponseAcquired(ordinal, in acquisition);
#endif
        }
        _journal.Observer?.ResponseAcquired(ordinal, in acquisition, observedAt);
    }

    internal void LifecycleRequested(
        int ordinal,
        LifecycleGeneration generation,
        MonotonicTimestamp observedAt)
    {
        _traces.Dispatch?.LifecycleRequested(ordinal, generation, observedAt);
        _journal.Observer?.LifecycleRequested(ordinal, generation, observedAt);
    }

    internal void EmergencyEntered(
        in EmergencyStopContext context,
        MonotonicTimestamp observedAt)
    {
        _traces.Dispatch?.EmergencyEntered(in context, observedAt);
        _journal.Observer?.EmergencyEntered(in context, observedAt);
    }

    internal void EmergencyCleared(
        in EmergencyStopContext context,
        MonotonicTimestamp observedAt)
    {
        _traces.Dispatch?.EmergencyCleared(in context, observedAt);
        _journal.Observer?.EmergencyCleared(in context, observedAt);
    }

    internal void EmergencyAppliedToService(
        int ordinal,
        in EmergencyStopContext context) =>
        _traces.Dispatch?.EmergencyAppliedToService(ordinal, in context);

    internal void EmergencyRejected(
        int ordinal,
        in BatchReceipt receipt,
        MonotonicTimestamp observedAt)
    {
        _traces.Dispatch?.EmergencyRejected(ordinal, in receipt);
        _journal.Observer?.EmergencyRejected(ordinal, in receipt, observedAt);
    }

    internal void ActionDispatched(
        int ordinal,
        in ServiceActionDispatch dispatch,
        MonotonicTimestamp observedAt,
        long frameIdentity)
    {
        var trace = _traces.Dispatch;
        if (trace is not null)
        {
#if SERVICE_CYCLE_PROFILE
            var profile = dispatch.Attempted
                ? _profiler.Begin(
                    ServiceCycleProfileSpan.SemanticTerminal,
                    ordinal,
                    dispatch.ActionFact.Context.Cycle.Lifecycle.Value,
                    dispatch.ActionFact.Context.Cycle.Cycle.Value,
                    frameIdentity)
                : default;
            try { trace.ActionDispatched(ordinal, in dispatch); }
            finally { profile.Complete(); }
#else
            trace.ActionDispatched(ordinal, in dispatch);
#endif
        }
        _journal.Observer?.ActionDispatched(ordinal, in dispatch, observedAt);
        if (dispatch.AttributionFailureReason is { } reason)
            _serviceFailures.Observe(ordinal, ServiceFailureSite.Attribution, reason);
        if (dispatch.ExecutionFailureReason is { } thrown)
            _serviceFailures.Observe(ordinal, ServiceFailureSite.Execution, thrown);
    }

    internal void StartAttemptObserved(
        int ordinal,
        in ServiceCycleStartAttempt start,
        LifecycleGeneration currentLifecycle,
        MonotonicTimestamp observedAt,
        long frameIdentity)
    {
        var trace = _traces.Dispatch;
        if (trace is not null)
        {
#if SERVICE_CYCLE_PROFILE
            var lifecycle = start.Cycle.IsValid
                ? start.Cycle.Lifecycle.Value
                : currentLifecycle.Value;
            var cycle = start.Cycle.IsValid ? start.Cycle.Cycle.Value : 0;
            var profile = _profiler.Begin(
                ServiceCycleProfileSpan.SemanticStart,
                ordinal,
                lifecycle,
                cycle,
                frameIdentity);
            try { trace.StartAttemptObserved(ordinal, in start); }
            finally { profile.Complete(); }
#else
            trace.StartAttemptObserved(ordinal, in start);
#endif
        }
        _journal.Observer?.StartAttemptObserved(ordinal, in start, observedAt);
        // The first fault of a streak carries the cause; the retries behind it back off silently until
        // the service recovers, so a read that keeps throwing names itself once, not every few seconds.
        if (start.FailureReason is { } reason && start.Fault.OccurrenceCount == 1)
            _serviceFailures.Observe(
                ordinal,
                start.Fault.Category == ServiceFaultCategory.Capture
                    ? ServiceFailureSite.Capture
                    : ServiceFailureSite.Start,
                reason);
    }

    internal void PumpCompleted(
        in SuiteFramePumpReport report,
        LifecycleGeneration currentLifecycle,
        MonotonicTimestamp observedAt)
    {
        var trace = _traces.Dispatch;
        if (trace is not null)
        {
#if SERVICE_CYCLE_PROFILE
            var profile = _profiler.Begin(
                ServiceCycleProfileSpan.SemanticPumpSummary,
                serviceOrdinal: 0,
                currentLifecycle.Value,
                cycle: 0,
                report.FrameIdentity);
            try { trace.PumpCompleted(in report, observedAt); }
            finally { profile.Complete(); }
#else
            trace.PumpCompleted(in report, observedAt);
#endif
        }
        _journal.Observer?.Advance(observedAt);
    }

#if SERVICE_CYCLE_PROFILE
    internal void PumpHeapSampled(
        in SuiteFramePumpHeapSample sample,
        LifecycleGeneration currentLifecycle,
        MonotonicTimestamp observedAt)
    {
        var trace = _traces.Dispatch;
        if (trace is null) return;
        var profile = _profiler.Begin(
            ServiceCycleProfileSpan.SemanticPumpSummary,
            serviceOrdinal: 0,
            currentLifecycle.Value,
            cycle: 0,
            sample.FrameIdentity);
        try { trace.PumpHeapSampled(in sample, observedAt); }
        finally { profile.Complete(); }
    }
#endif

    internal void RejectedPumpCompleted(
        in SuiteFramePumpReport report,
        MonotonicTimestamp observedAt) =>
        _traces.Dispatch?.PumpCompleted(in report, observedAt);
}
