using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

internal static class ServiceCycleServiceDiagnosticsProjector
{
    internal static ServiceCycleServiceDiagnosticsSnapshot Project(
        IServiceCycleSlot slot,
        MonotonicTimestamp observedAt,
        bool emergencyStopEngaged)
    {
        var lifecycle = ServiceCycleLifecycleDiagnosticsProjector.Project(
            slot.LifecycleSnapshot,
            slot.LifecyclePositionTransitionCount,
            slot.IsDisposed
                ? ServiceCycleLifecycleEvidenceKind.RetainedAtDisposal
                : ServiceCycleLifecycleEvidenceKind.Current);
        if (slot.IsDisposed)
            return Unavailable(
                slot,
                ServiceCycleDiagnosticsAvailability.Disposed,
                ServiceCycleOperationalPhase.Disposed,
                in lifecycle,
                observedAt);
        if (!slot.TryGetRunnerSnapshot(out var runner))
        {
            var availability = ServiceCycleLifecycleDiagnosticsProjector.HasCurrentPosition(in lifecycle)
                ? ServiceCycleDiagnosticsAvailability.HandoffContended
                : ServiceCycleDiagnosticsAvailability.NoCurrentRunner;
            var phase = availability == ServiceCycleDiagnosticsAvailability.HandoffContended
                ? ServiceCycleOperationalPhase.Unavailable
                : ServiceCycleLifecycleDiagnosticsProjector.ProjectRunnerlessPhase(in lifecycle, observedAt);
            return Unavailable(slot, availability, phase, in lifecycle, observedAt);
        }

        var currentCycle = runner.HasActiveBatch ? runner.ActiveCycle : runner.InFlightCycle;
        var currentBatch = runner.HasActiveBatch ? runner.ActiveBatch : runner.InFlightBatch;
        var context = new ServiceCycleContextDiagnosticsSnapshot(
            currentCycle,
            currentBatch,
            runner.HasActiveBatch || runner.HasInFlightCycle,
            runner.LatestConfiguration,
            ServiceCycleDiagnosticsValueAvailability.Available,
            slot.LatestStrategy,
            ServiceCycleDiagnosticsValueAvailability.Available);
        var activeBatch = runner.HasActiveBatch
            ? new ServiceCycleBatchDiagnosticsSnapshot(
                runner.ActiveCycle,
                runner.ActiveBatch,
                runner.ActiveWake,
                runner.ResponsePublishedAt,
                Elapsed(runner.ResponsePublishedAt, observedAt),
                runner.ActionCount,
                runner.ActionCursor,
                runner.ActionCapacity,
                runner.ActionHighWater,
                runner.ActionGrowthAllocations,
                runner.RetainedActionSlots,
                runner.CommittedCount,
                runner.NativeOutcome,
                true)
            : default;
        var storageAvailability = runner.Handoff.Phase is
            ServiceHandoffPhase.RequestReady or
            ServiceHandoffPhase.Evaluating or
            ServiceHandoffPhase.ResponseReady
                ? ServiceCycleStorageDiagnosticsAvailability.LastPublished
                : ServiceCycleStorageDiagnosticsAvailability.Exact;
        var storage = new ServiceCycleStorageDiagnosticsSnapshot(
            storageAvailability,
            runner.ActionCapacity,
            runner.ActionHighWater,
            runner.ActionGrowthAllocations,
            runner.RetainedActionSlots);
        var lastStart = new ServiceCycleStartDecisionDiagnosticsFact(
            runner.LastStartDecision.Decision,
            runner.LastStartDecision.ObservedAt,
            runner.LastStartDecision.IsPresent);
        var lastCapture = new ServiceCycleCaptureDiagnosticsFact(
            runner.LastCapture.Result,
            runner.LastCapture.StartedAt,
            runner.LastCapture.CompletedAt,
            runner.LastCapture.IsPresent
                ? Elapsed(runner.LastCapture.StartedAt, runner.LastCapture.CompletedAt)
                : default,
            runner.LastCapture.IsPresent);
        var lastAction = new ServiceCycleActionDiagnosticsFact(
            runner.LastAction.Context,
            runner.LastAction.Result,
            runner.LastAction.CompletedAt,
            runner.LastAction.IsPresent
                ? Elapsed(runner.LastAction.StartedAt, runner.LastAction.CompletedAt)
                : default,
            runner.LastAction.IsPresent);
        var handoff = new ServiceCycleHandoffDiagnosticsSnapshot(
            ServiceCycleLifecycleDiagnosticsProjector.ProjectPhase(runner.Handoff.Phase),
            runner.Handoff.RequestSequence,
            runner.Handoff.TransitionCount,
            runner.Handoff.WorkerWaitCount,
            runner.Handoff.CleanupRequestCount,
            runner.Handoff.CleanupAcknowledgementCount,
            runner.Handoff.LastCleanupThreadId,
            runner.Handoff.CleanupPending,
            runner.Handoff.StopRequested);
        var worker = new ServiceCycleWorkerDiagnosticsSnapshot(
            runner.WorkerThreadId,
            runner.WorkerIsBackground,
            runner.WorkerCycleAllocatedBytes,
            runner.MeasuredWorkerCycleCount,
            runner.WorkerStateConstructionContentionCount);
        return new ServiceCycleServiceDiagnosticsSnapshot(
            slot.RegistrationToken,
            slot.Ordinal,
            slot.ServiceId,
            ServiceCycleDiagnosticsAvailability.Available,
            ProjectOperationalPhase(in runner, observedAt, emergencyStopEngaged),
            lifecycle,
            context,
            activeBatch,
            handoff,
            worker,
            runner.Projection,
            runner.Fault,
            runner.PreviousReceipt,
            runner.NextWakeDue,
            runner.HasWakeDue,
            storage,
            lastStart,
            lastCapture,
            lastAction,
            ProjectTiming(in runner, observedAt),
            runner.RetryGate);
    }

    private static ServiceCycleServiceDiagnosticsSnapshot Unavailable(
        IServiceCycleSlot slot,
        ServiceCycleDiagnosticsAvailability availability,
        ServiceCycleOperationalPhase phase,
        in ServiceCycleLifecycleDiagnosticsSnapshot lifecycle,
        MonotonicTimestamp observedAt)
    {
        var latestConfiguration = slot.LatestConfiguration;
        var latestStrategy = slot.LatestStrategy;
        var context = new ServiceCycleContextDiagnosticsSnapshot(
            default,
            default,
            false,
            latestConfiguration,
            latestConfiguration.IsValid
                ? ServiceCycleDiagnosticsValueAvailability.Available
                : ServiceCycleDiagnosticsValueAvailability.NotAvailable,
            latestStrategy,
            latestStrategy.Value != 0
                ? ServiceCycleDiagnosticsValueAvailability.Available
                : ServiceCycleDiagnosticsValueAvailability.NotAvailable);
        return new ServiceCycleServiceDiagnosticsSnapshot(
            slot.RegistrationToken,
            slot.Ordinal,
            slot.ServiceId,
            availability,
            phase,
            lifecycle,
            context,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            false,
            default,
            default,
            default,
            default,
            new ServiceCycleTimingDiagnosticsSnapshot(
                observedAt,
                default, default, default, default,
                ServiceCycleEvaluationTimingAvailability.NotAvailable,
                false, default, false, default, false),
            null);
    }

    private static ServiceCycleOperationalPhase ProjectOperationalPhase(
        in ServiceRunnerSnapshot runner,
        MonotonicTimestamp observedAt,
        bool emergencyStopEngaged)
    {
        if (emergencyStopEngaged) return ServiceCycleOperationalPhase.EmergencyStopped;
        if (runner.Phase == ServiceCyclePhase.Capturing) return ServiceCycleOperationalPhase.Capturing;
        if (runner.Handoff.Phase is ServiceHandoffPhase.RequestReady or
            ServiceHandoffPhase.Evaluating or ServiceHandoffPhase.ResponseReady)
            return ServiceCycleOperationalPhase.Evaluating;
        if (runner.HasActiveBatch) return ServiceCycleOperationalPhase.DrainingBatch;
        if (runner.Fault.IsValid)
            return runner.HasWakeDue && runner.NextWakeDue > observedAt
                ? ServiceCycleOperationalPhase.RetryBackoff
                : ServiceCycleOperationalPhase.Faulted;
        return ServiceCycleOperationalPhase.Idle;
    }

    private static ServiceCycleTimingDiagnosticsSnapshot ProjectTiming(
        in ServiceRunnerSnapshot runner,
        MonotonicTimestamp observedAt)
    {
        var evaluationSnapshot = runner.EvaluationTiming;
        var evaluation = evaluationSnapshot.Fact;
        var evaluationAvailability = evaluationSnapshot.Availability switch
        {
            ServiceRunnerEvaluationTimingAvailability.Available =>
                ServiceCycleEvaluationTimingAvailability.Available,
            ServiceRunnerEvaluationTimingAvailability.PublicationContended =>
                ServiceCycleEvaluationTimingAvailability.Contended,
            _ => ServiceCycleEvaluationTimingAvailability.NotAvailable,
        };
        var evaluationDuration = evaluation.IsPresent
            ? Elapsed(
                evaluation.StartedAt,
                evaluation.IsComplete ? evaluation.CompletedAt : observedAt)
            : default;
        var evaluationAge = evaluation.IsPresent
            ? Elapsed(evaluation.StartedAt, observedAt)
            : default;
        var hasResponseAge = runner.HasActiveBatch;
        var responseAge = hasResponseAge
            ? Elapsed(runner.ResponsePublishedAt, observedAt)
            : default;
        var wakeIsLate = runner.HasWakeDue && observedAt > runner.NextWakeDue;
        return new ServiceCycleTimingDiagnosticsSnapshot(
            observedAt,
            evaluation.StartedAt,
            evaluation.CompletedAt,
            evaluationDuration,
            evaluationAge,
            evaluationAvailability,
            evaluation.IsComplete,
            responseAge,
            hasResponseAge,
            wakeIsLate ? Elapsed(runner.NextWakeDue, observedAt) : default,
            wakeIsLate);
    }

    private static MonotonicDuration Elapsed(MonotonicTimestamp start, MonotonicTimestamp end) =>
        end.Ticks >= start.Ticks ? new MonotonicDuration(end.Ticks - start.Ticks) : default;
}
