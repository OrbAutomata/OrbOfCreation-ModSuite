using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

/// <summary>Owner-thread, allocation-free diagnostics projection over Common-owned service-cycle state.</summary>
public static class ServiceCycleDiagnostics
{
    /// <summary>
    /// The service's snapshot when its current runner can be read; null when there is no fresh read,
    /// as while the worker holds the handoff, the service has no current runner, or it was released.
    /// A caller then keeps what it last read rather than act on facts it does not have.
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
            return ServiceCycleServiceDiagnosticsProjector.TryProject(
                slot,
                pump.DiagnosticsNow,
                pump.IsEmergencyStopEngaged,
                out var snapshot)
                ? snapshot
                : null;
        }
        return null;
    }
}
