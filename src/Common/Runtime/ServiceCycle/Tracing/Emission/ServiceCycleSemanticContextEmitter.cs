using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>Builds publication, lifecycle, emergency, fault, and pump payloads.</summary>
internal sealed class ServiceCycleSemanticContextEmitter
{
    private readonly ServiceCycleSemanticCausalWriter _writer;
    private readonly bool _enabled;

    internal ServiceCycleSemanticContextEmitter(ServiceCycleSemanticCausalWriter writer, bool enabled)
    {
        _writer = writer;
        _enabled = enabled;
    }

    internal void ConfigurationPublished(ConfigGeneration generation, MonotonicTimestamp observedAt)
    {
        if (!_enabled) return;
        if (!generation.IsValid)
            throw new ArgumentException("A valid configuration generation is required.", nameof(generation));
        var payload = ServiceCycleSemanticPayload.Publication(false, generation.Value, observedAt.Ticks);
        _writer.AppendSuite(ServiceCycleSemanticEventKind.ConfigurationPublished, in payload);
    }

    internal void StrategyPublished(StrategyGeneration generation, MonotonicTimestamp observedAt)
    {
        if (!_enabled) return;
        if (generation.Value == 0)
            throw new ArgumentException("A valid strategy generation is required.", nameof(generation));
        var payload = ServiceCycleSemanticPayload.Publication(true, generation.Value, observedAt.Ticks);
        _writer.AppendSuite(ServiceCycleSemanticEventKind.StrategyPublished, in payload);
    }

    internal void LifecycleRequested(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt) =>
        Lifecycle(ordinal, lifecycle, ServiceCycleSemanticEventKind.LifecycleRequested, 0, observedAt);

    internal void LifecycleActivated(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt) =>
        Lifecycle(ordinal, lifecycle, ServiceCycleSemanticEventKind.LifecycleActivated, 0, observedAt);

    internal void LifecycleRetired(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt) =>
        Lifecycle(
            ordinal,
            lifecycle,
            ServiceCycleSemanticEventKind.LifecycleRetired,
            CommonActionResultCodes.LifecycleReplaced.Value,
            observedAt);

    internal void LifecycleConstructionDeferred(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt,
        MonotonicTimestamp retryDue)
    {
        if (!_enabled) return;
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (retryDue < observedAt) throw new ArgumentOutOfRangeException(nameof(retryDue));
        var service = _writer.Identities.ForRegistrationOrdinal(ordinal);
        var payload = ServiceCycleSemanticPayload.LifecycleConstructionDeferred(
            service,
            lifecycle.Value,
            CommonServiceDecisionCodes.TransientContention.Value,
            observedAt.Ticks,
            retryDue.Ticks);
        _writer.AppendService(ordinal, ServiceCycleSemanticEventKind.LifecycleConstructionDeferred, in payload);
    }

    internal void EmergencyEntered(in EmergencyStopContext emergency, MonotonicTimestamp observedAt) =>
        Emergency(ServiceCycleSemanticEventKind.EmergencyEntered, in emergency, observedAt);

    internal void EmergencyCleared(in EmergencyStopContext emergency, MonotonicTimestamp observedAt) =>
        Emergency(ServiceCycleSemanticEventKind.EmergencyCleared, in emergency, observedAt);

    internal void FaultObserved(int ordinal, LifecycleGeneration lifecycle, in ServiceFault fault) =>
        Fault(ordinal, lifecycle, in fault, ServiceCycleSemanticEventKind.FaultObserved, default);

    internal void FaultRecovered(
        int ordinal,
        LifecycleGeneration lifecycle,
        in ServiceFault fault,
        MonotonicTimestamp recoveredAt) =>
        Fault(ordinal, lifecycle, in fault, ServiceCycleSemanticEventKind.FaultRecovered, recoveredAt);

    internal void RetryScheduled(
        int ordinal,
        LifecycleGeneration lifecycle,
        in ServiceFault fault,
        MonotonicTimestamp retryDue) =>
        Fault(ordinal, lifecycle, in fault, ServiceCycleSemanticEventKind.RetryScheduled, retryDue);

    internal void PumpCompleted(in SuiteFramePumpReport report, MonotonicTimestamp observedAt)
    {
        if (!_enabled) return;
        var payload = ServiceCycleSemanticPayload.Pump(
            report.FrameIdentity,
            report.Accepted,
            report.StartingOrdinal,
            report.ResponsesAcquired,
            report.ActionsAttempted,
            report.CapturesAttempted,
            report.CyclesStarted,
            report.WorldGateDeferrals,
            report.EmergencyBatchesRejected,
            report.LifecyclePositionTransitions,
            report.ResponseDuration.Ticks,
            report.ActionDuration.Ticks,
            report.CaptureDuration.Ticks,
            report.TotalDuration.Ticks,
            observedAt.Ticks);
        _writer.AppendSuite(ServiceCycleSemanticEventKind.PumpCompleted, in payload);
    }

#if SERVICE_CYCLE_PROFILE
    internal void PumpHeapSampled(in SuiteFramePumpHeapSample sample, MonotonicTimestamp observedAt)
    {
        if (!_enabled) return;
        var payload = ServiceCycleSemanticPayload.PumpHeapFact(
            sample.FrameIdentity,
            observedAt.Ticks,
            sample.CollectionsAtStart,
            sample.CollectionsAtEnd,
            sample.HeapAtStart,
            sample.HeapAtEnd,
            sample.CaptureGrowth,
            sample.CapturesMeasured,
            sample.CapturesAcrossCollection,
            sample.PreviousFrameAllocated,
            sample.Census);
        _writer.AppendSuite(ServiceCycleSemanticEventKind.PumpHeapSampled, in payload);
    }
#endif

    private void Lifecycle(
        int ordinal,
        LifecycleGeneration lifecycle,
        ServiceCycleSemanticEventKind kind,
        int code,
        MonotonicTimestamp observedAt)
    {
        if (!_enabled) return;
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        var service = _writer.Identities.ForRegistrationOrdinal(ordinal);
        var payload = ServiceCycleSemanticPayload.LifecycleFact(
            service,
            lifecycle.Value,
            code,
            observedAt.Ticks);
        _writer.AppendService(ordinal, kind, in payload);
    }

    private void Emergency(
        ServiceCycleSemanticEventKind kind,
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        if (!_enabled) return;
        if (!emergency.IsValid)
            throw new ArgumentException("A valid emergency context is required.", nameof(emergency));
        var occurrence = checked((int)emergency.Episode.Value);
        var payload = ServiceCycleSemanticPayload.Emergency(
            (int)emergency.Reason,
            occurrence,
            observedAt.Ticks);
        _writer.AppendEmergency(kind, in emergency, in payload);
    }

    private void Fault(
        int ordinal,
        LifecycleGeneration lifecycle,
        in ServiceFault fault,
        ServiceCycleSemanticEventKind kind,
        MonotonicTimestamp terminalTime)
    {
        if (!_enabled) return;
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        EnsureFault(in fault);
        var service = _writer.Identities.ForRegistrationOrdinal(ordinal);
        var deadline = kind == ServiceCycleSemanticEventKind.RetryScheduled ? terminalTime.Ticks : 0;
        var observedAt = kind == ServiceCycleSemanticEventKind.FaultRecovered ? terminalTime : fault.ObservedAt;
        var payload = ServiceCycleSemanticPayload.FaultOrRetry(
            service,
            lifecycle.Value,
            (int)fault.Category,
            fault.Code.Value,
            fault.OccurrenceCount,
            observedAt.Ticks,
            deadline);
        _writer.AppendService(ordinal, kind, in payload);
    }

    private static void EnsureFault(in ServiceFault fault)
    {
        if (!fault.IsValid) throw new ArgumentException("A valid service fault is required.", nameof(fault));
    }
}
