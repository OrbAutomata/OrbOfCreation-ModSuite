using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

internal static class ServiceCycleLifecycleDiagnosticsProjector
{
    internal static ServiceCycleLifecycleDiagnosticsSnapshot Project(
        in ServiceLifecycleSlotSnapshot source,
        long transitionCount) => new(
            source.DesiredLifecycle,
            ProjectPosition(source.Position0),
            ProjectPosition(source.Position1),
            source.LatestTerminal,
            source.ConstructionFault,
            source.ConstructionRetryDue,
            source.ConstructionAttemptCount,
            source.ConstructionContentionCount,
            transitionCount,
            source.LivePositionCount);

    internal static ServiceCycleHandoffDiagnosticsPhase ProjectPhase(ServiceHandoffPhase phase) => phase switch
    {
        ServiceHandoffPhase.Empty => ServiceCycleHandoffDiagnosticsPhase.Empty,
        ServiceHandoffPhase.RequestReady => ServiceCycleHandoffDiagnosticsPhase.RequestReady,
        ServiceHandoffPhase.Evaluating => ServiceCycleHandoffDiagnosticsPhase.Evaluating,
        ServiceHandoffPhase.ResponseReady => ServiceCycleHandoffDiagnosticsPhase.ResponseReady,
        ServiceHandoffPhase.MainOwnedBatch => ServiceCycleHandoffDiagnosticsPhase.MainOwnedBatch,
        ServiceHandoffPhase.Stopping => ServiceCycleHandoffDiagnosticsPhase.Stopping,
        ServiceHandoffPhase.Stopped => ServiceCycleHandoffDiagnosticsPhase.Stopped,
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    private static ServiceCyclePositionDiagnosticsSnapshot ProjectPosition(
        in ServiceRunnerPositionSnapshot source)
    {
        var storage = source.Storage;
        return new ServiceCyclePositionDiagnosticsSnapshot(
            source.Index,
            source.State,
            source.Lifecycle,
            ProjectPhase(source.HandoffPhase),
            ProjectStorage(in storage));
    }

    private static ServiceCycleStorageDiagnosticsSnapshot ProjectStorage(
        in ServiceRunnerStorageSnapshot source) => new(
            source.Availability switch
            {
                ServiceRunnerStorageEvidenceAvailability.Exact =>
                    ServiceCycleStorageDiagnosticsAvailability.Exact,
                ServiceRunnerStorageEvidenceAvailability.LastPublished =>
                    ServiceCycleStorageDiagnosticsAvailability.LastPublished,
                ServiceRunnerStorageEvidenceAvailability.HandoffContended =>
                    ServiceCycleStorageDiagnosticsAvailability.HandoffContended,
                _ => ServiceCycleStorageDiagnosticsAvailability.NotAvailable,
            },
            source.Capacity,
            source.HighWater,
            source.GrowthAllocations,
            source.RetainedSlots);
}
