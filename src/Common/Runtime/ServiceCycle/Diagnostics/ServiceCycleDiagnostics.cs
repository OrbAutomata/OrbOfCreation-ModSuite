using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

/// <summary>Owner-thread diagnostics projection over Common-owned service-cycle state.</summary>
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

    /// <summary>A retained projection is useful history, but cannot establish a later configuration's first evaluation.</summary>
    internal static bool HasCurrentProjection(
        in ServiceCycleServiceDiagnosticsSnapshot service,
        long lifecycle,
        ConfigGeneration configuration) =>
        service.LatestProjection.IsPresent &&
        service.LatestProjection.Context.Cycle.Lifecycle.Value == (ulong)lifecycle &&
        service.LatestProjection.Context.Cycle.Config.Value >= configuration.Value;

    internal static bool RefreshFault(
        ServiceCycleServiceDiagnosticsSnapshot? service,
        ref ServiceFault fault)
    {
        if (service is not { } available) return false;
        var latest = available.LatestFault;
        return RefreshFault(in latest, ref fault);
    }

    internal static bool RefreshFault(in ServiceFault latest, ref ServiceFault fault)
    {
        var changed = fault.Category != latest.Category || fault.Code != latest.Code ||
            fault.OccurrenceCount != latest.OccurrenceCount || fault.Reason != latest.Reason;
        fault = latest;
        return changed;
    }

    /// <summary>Runtime retries are neutral waiting, and never hide a more specific feature block.</summary>
    internal static (FeatureStatusState State, FeatureStatusReasonCode Reason, string Summary) ProjectHealth(
        FeatureStatusState state,
        FeatureStatusReasonCode reason,
        string summary,
        in ServiceFault fault)
    {
        if (!fault.IsValid || fault.Category == ServiceFaultCategory.ActionExecution ||
            state is not (FeatureStatusState.Operational or FeatureStatusState.NotReady or FeatureStatusState.Locked))
            return (state, reason, summary);
        return (FeatureStatusState.NotReady, FeatureStatusReasonCode.RuntimeFailure, DescribeFault(in fault));
    }

    internal static string DescribeFault(in ServiceFault fault) => fault.IsValid
        ? $"The service is retrying {fault.Category} after {fault.OccurrenceCount} failed attempt(s). {fault.Reason}".TrimEnd()
        : string.Empty;
}
