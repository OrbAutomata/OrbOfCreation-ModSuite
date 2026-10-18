using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

public sealed partial class ServiceCycleSemanticRecorder
{
    public void ConfigurationPublished(ConfigGeneration generation, MonotonicTimestamp observedAt) =>
        _context.ConfigurationPublished(generation, observedAt);

    public void StrategyPublished(StrategyGeneration generation, MonotonicTimestamp observedAt) =>
        _context.StrategyPublished(generation, observedAt);

    public void LifecycleRequested(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt) =>
        _context.LifecycleRequested(ordinal, lifecycle, observedAt);

    public void LifecycleActivated(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt) =>
        _context.LifecycleActivated(ordinal, lifecycle, observedAt);

    public void LifecycleRetired(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt) =>
        _context.LifecycleRetired(ordinal, lifecycle, observedAt);

    public void LifecycleConstructionDeferred(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt,
        MonotonicTimestamp retryDue) =>
        _context.LifecycleConstructionDeferred(ordinal, lifecycle, observedAt, retryDue);

    public void EmergencyEntered(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt) =>
        _context.EmergencyEntered(in emergency, observedAt);

    public void EmergencyCleared(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt) =>
        _context.EmergencyCleared(in emergency, observedAt);

    internal void RetainEmergencyForService(int ordinal, in EmergencyStopContext emergency)
    {
        if (!Enabled) return;
        _writer.RetainEmergency(ordinal, in emergency);
    }

    public void FaultObserved(int ordinal, LifecycleGeneration lifecycle, in ServiceFault fault) =>
        _context.FaultObserved(ordinal, lifecycle, in fault);

    public void FaultRecovered(
        int ordinal,
        LifecycleGeneration lifecycle,
        in ServiceFault fault,
        MonotonicTimestamp recoveredAt) =>
        _context.FaultRecovered(ordinal, lifecycle, in fault, recoveredAt);

    public void RetryScheduled(
        int ordinal,
        LifecycleGeneration lifecycle,
        in ServiceFault fault,
        MonotonicTimestamp retryDue) =>
        _context.RetryScheduled(ordinal, lifecycle, in fault, retryDue);

    public void PumpCompleted(in SuiteFramePumpReport report, MonotonicTimestamp observedAt) =>
        _context.PumpCompleted(in report, observedAt);

#if SERVICE_CYCLE_PROFILE
    internal void PumpHeapSampled(in SuiteFramePumpHeapSample sample, MonotonicTimestamp observedAt) =>
        _context.PumpHeapSampled(in sample, observedAt);
#endif

    internal void ClearRetainedEmergencyForService(int ordinal)
    {
        if (!Enabled) return;
        _writer.ClearRetainedEmergency(ordinal);
    }
}
