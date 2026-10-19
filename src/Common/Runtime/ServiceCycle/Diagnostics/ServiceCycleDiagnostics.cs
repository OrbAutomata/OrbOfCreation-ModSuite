using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

/// <summary>Owner-thread, allocation-free diagnostics projection over Common-owned service-cycle state.</summary>
public static class ServiceCycleDiagnostics
{
    public static ServiceCycleDiagnosticsCopyResult CopyServices(
        SuiteFramePump pump,
        Span<ServiceCycleServiceDiagnosticsSnapshot> destination)
    {
        if (pump is null) throw new ArgumentNullException(nameof(pump));
        var registry = pump.DiagnosticsRegistry;
        registry.AssertDiagnosticsRead();
        var observedAt = pump.DiagnosticsNow;

        var required = registry.OrdinalCount;
        var written = Math.Min(required, destination.Length);
        var unavailable = 0;
        for (var ordinal = 0; ordinal < required; ordinal++)
        {
            var snapshot = ServiceCycleServiceDiagnosticsProjector.Project(
                registry.GetSlot(ordinal),
                observedAt,
                pump.IsEmergencyStopEngaged);
            if (snapshot.Availability != ServiceCycleDiagnosticsAvailability.Available)
                unavailable++;
            if (ordinal < written) destination[ordinal] = snapshot;
        }
        return new ServiceCycleDiagnosticsCopyResult(required, written, unavailable);
    }

    /// <summary>
    /// The service's snapshot when it carries runner evidence; null when there is no fresh read, as
    /// while the worker holds the handoff. Every runner fact of a snapshot without evidence is blank,
    /// its retry gate included, so a caller keeps what it last read rather than act on one.
    /// </summary>
    public static ServiceCycleServiceDiagnosticsSnapshot? ReadAvailableService(
        SuiteFramePump pump,
        ServiceId serviceId)
    {
        if (pump is null) throw new ArgumentNullException(nameof(pump));
        var registry = pump.DiagnosticsRegistry;
        registry.AssertDiagnosticsRead();
        for (var ordinal = 0; ordinal < registry.OrdinalCount; ordinal++)
        {
            var slot = registry.GetSlot(ordinal);
            if (!slot.ServiceId.Equals(serviceId)) continue;
            var snapshot = ServiceCycleServiceDiagnosticsProjector.Project(
                slot,
                pump.DiagnosticsNow,
                pump.IsEmergencyStopEngaged);
            return snapshot.HasRunnerEvidence ? snapshot : null;
        }
        return null;
    }
}
