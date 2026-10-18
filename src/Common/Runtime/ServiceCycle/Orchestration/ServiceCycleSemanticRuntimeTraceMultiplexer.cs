using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class ServiceCycleSemanticRuntimeTraceMultiplexer : IServiceCycleAttemptObserver
{
    private readonly ServiceCycleSemanticRuntimeTrace _first;
    private readonly ServiceCycleSemanticRuntimeTrace? _second;

    internal ServiceCycleSemanticRuntimeTraceMultiplexer(
        ServiceCycleSemanticRuntimeTrace first,
        ServiceCycleSemanticRuntimeTrace? second = null)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        if (ReferenceEquals(first, second))
            throw new ArgumentException("Semantic trace slots must be independently owned.", nameof(second));
        _second = second;
    }

    internal void ObservePublications(
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        _first.ObservePublications(configuration, strategy, observedAt);
        _second?.ObservePublications(configuration, strategy, observedAt);
    }

    internal void LifecycleRequested(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt)
    {
        _first.LifecycleRequested(ordinal, lifecycle, observedAt);
        _second?.LifecycleRequested(ordinal, lifecycle, observedAt);
    }

    internal bool NeedsLifecycleObservation(int ordinal, long lifecycleSemanticVersion) =>
        _first.NeedsLifecycleObservation(ordinal, lifecycleSemanticVersion) ||
        _second?.NeedsLifecycleObservation(ordinal, lifecycleSemanticVersion) == true;

    internal void ObserveLifecycle(
        int ordinal,
        in ServiceLifecycleSlotSnapshot snapshot,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt)
    {
        if (_first.NeedsLifecycleObservation(ordinal, lifecycleSemanticVersion))
            _first.ObserveLifecycle(ordinal, in snapshot, lifecycleSemanticVersion, observedAt);
        if (_second?.NeedsLifecycleObservation(ordinal, lifecycleSemanticVersion) == true)
            _second.ObserveLifecycle(ordinal, in snapshot, lifecycleSemanticVersion, observedAt);
    }

    internal void EmergencyEntered(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        _first.EmergencyEntered(in emergency, observedAt);
        _second?.EmergencyEntered(in emergency, observedAt);
    }

    internal void EmergencyCleared(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        _first.EmergencyCleared(in emergency, observedAt);
        _second?.EmergencyCleared(in emergency, observedAt);
    }

    internal void EmergencyAppliedToService(int ordinal, in EmergencyStopContext emergency)
    {
        _first.EmergencyAppliedToService(ordinal, in emergency);
        _second?.EmergencyAppliedToService(ordinal, in emergency);
    }

    internal void EmergencyRejected(int ordinal, in BatchReceipt receipt)
    {
        _first.EmergencyRejected(ordinal, in receipt);
        _second?.EmergencyRejected(ordinal, in receipt);
    }

    internal void EnterFrame(long frameIdentity)
    {
        _first.EnterFrame(frameIdentity);
        _second?.EnterFrame(frameIdentity);
    }

    internal void LeaveFrame()
    {
        _first.LeaveFrame();
        _second?.LeaveFrame();
    }

    internal void PumpCompleted(in SuiteFramePumpReport report, MonotonicTimestamp observedAt)
    {
        _first.PumpCompleted(in report, observedAt);
        _second?.PumpCompleted(in report, observedAt);
    }

#if SERVICE_CYCLE_PROFILE
    internal void PumpHeapSampled(in SuiteFramePumpHeapSample sample, MonotonicTimestamp observedAt)
    {
        _first.PumpHeapSampled(in sample, observedAt);
        _second?.PumpHeapSampled(in sample, observedAt);
    }
#endif

    internal void StartAttemptObserved(int ordinal, in ServiceCycleStartAttempt attempt)
    {
        _first.StartAttemptObserved(ordinal, in attempt);
        _second?.StartAttemptObserved(ordinal, in attempt);
    }

    internal void ResponseAcquired(int ordinal, in ServiceResponseAcquisition acquisition)
    {
        _first.ResponseAcquired(ordinal, in acquisition);
        _second?.ResponseAcquired(ordinal, in acquisition);
    }

    internal void ActionDispatched(int ordinal, in ServiceActionDispatch dispatch)
    {
        _first.ActionDispatched(ordinal, in dispatch);
        _second?.ActionDispatched(ordinal, in dispatch);
    }

    public void StartAttempted(
        int ordinal,
        in ServiceCycleStartContext context,
        MonotonicTimestamp observedAt)
    {
        _first.StartAttempted(ordinal, in context, observedAt);
        _second?.StartAttempted(ordinal, in context, observedAt);
    }

    public void StartReady(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        _first.StartReady(ordinal, in context, in decision, observedAt, duration);
        _second?.StartReady(ordinal, in context, in decision, observedAt, duration);
    }

    public void CaptureStarted(int ordinal, in ServiceCaptureContext context)
    {
        _first.CaptureStarted(ordinal, in context);
        _second?.CaptureStarted(ordinal, in context);
    }

    public void ActionAttempted(int ordinal, in ServiceActionContext context)
    {
        _first.ActionAttempted(ordinal, in context);
        _second?.ActionAttempted(ordinal, in context);
    }
}
