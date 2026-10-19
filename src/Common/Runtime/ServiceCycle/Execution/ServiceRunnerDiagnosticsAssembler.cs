using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceRunnerDiagnosticsAssembler<TState, TAction>
{
    private readonly ServiceConfigurationPublisher _configuration;
    private readonly ReusableActionStore<TAction> _actions;
    private readonly ServiceCycleHandoff _handoff;
    private readonly ServiceCycleWorker<TState, TAction> _worker;
    private readonly ServiceCycleMainState _main;
    private readonly ServiceCycleStartCoordinator<TState, TAction> _starts;

    internal ServiceRunnerDiagnosticsAssembler(
        ServiceConfigurationPublisher configuration,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        ServiceCycleWorker<TState, TAction> worker,
        ServiceCycleMainState main,
        ServiceCycleStartCoordinator<TState, TAction> starts)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _handoff = handoff ?? throw new ArgumentNullException(nameof(handoff));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _main = main ?? throw new ArgumentNullException(nameof(main));
        _starts = starts ?? throw new ArgumentNullException(nameof(starts));
    }

    internal ServiceHandoffPhase HandoffPhaseHint => _handoff.PhaseHint;

    internal ServiceRunnerSnapshot Read(bool disposed)
    {
        var handoff = _handoff.Snapshot;
        return Assemble(in handoff, disposed);
    }

    internal bool TryRead(bool disposed, out ServiceRunnerSnapshot snapshot)
    {
        if (!_handoff.TrySnapshot(out var handoff))
        {
            snapshot = default;
            return false;
        }

        snapshot = Assemble(in handoff, disposed);
        return true;
    }

    internal ServiceRunnerStorageSnapshot ReadStorageNonBlocking()
    {
        if (!_handoff.TrySnapshot(out var handoff))
            return new ServiceRunnerStorageSnapshot(
                ServiceRunnerStorageEvidenceAvailability.HandoffContended,
                0, 0, 0, 0);

        if (handoff.Phase == ServiceHandoffPhase.Stopped || _handoff.WorkerExitPrepared)
        {
            var exact = _actions.Metrics;
            return new ServiceRunnerStorageSnapshot(
                ServiceRunnerStorageEvidenceAvailability.Exact,
                exact.Capacity,
                exact.HighWaterCount,
                exact.GrowthAllocationCount,
                exact.RetainedSlots);
        }

        var availability = handoff.Phase is ServiceHandoffPhase.Empty or ServiceHandoffPhase.MainOwnedBatch
            ? ServiceRunnerStorageEvidenceAvailability.Exact
            : ServiceRunnerStorageEvidenceAvailability.LastPublished;
        return new ServiceRunnerStorageSnapshot(
            availability,
            _main.ActionCapacity,
            _main.ActionHighWater,
            _main.ActionGrowthAllocations,
            _main.RetainedActionSlots);
    }

    private ServiceRunnerSnapshot Assemble(in ServiceHandoffSnapshot handoff, bool disposed)
    {
        var latest = disposed ? _main.LatestConfigGeneration : _configuration.ReadLatest().Generation;
        var projection = _main.Projection.IsPresent
            ? new ServiceProjectionPublication(_main.Projection.Context, _main.Projection.Snapshot, latest)
            : default;
        var phase = _starts.IsCapturing || _starts.IsInvokingStartCallback
            ? ServiceCyclePhase.Capturing
            : handoff.Phase switch
            {
                ServiceHandoffPhase.RequestReady or ServiceHandoffPhase.Evaluating => ServiceCyclePhase.Evaluating,
                ServiceHandoffPhase.ResponseReady or ServiceHandoffPhase.MainOwnedBatch => ServiceCyclePhase.Executing,
                _ => ServiceCyclePhase.Waiting,
            };
        return new ServiceRunnerSnapshot(
            handoff,
            phase,
            _main.InFlightCycle,
            _main.InFlightBatch,
            _main.HasInFlightCycle,
            _main.ActiveCycle,
            _main.ActiveBatch,
            _main.HasActiveBatch,
            _main.ActiveWake,
            _main.ResponsePublishedAt,
            _main.ActionCount,
            _main.ActionCursor,
            _main.ActionCapacity,
            _main.ActionHighWater,
            _main.ActionGrowthAllocations,
            _main.RetainedActionSlots,
            handoff.LastCleanupThreadId,
            _main.NextWakeDue,
            _main.HasWakeDue,
            _main.PreviousReceipt,
            projection,
            _main.LatestFault,
            _main.NativeOutcome,
            _main.CommittedCount,
            latest,
            _worker.ManagedThreadId,
            _worker.IsBackground,
            _worker.LastCycleAllocatedBytes,
            _worker.MeasuredCycleCount,
            _worker.StateFactoryContentionCount,
            _main.LastStartDecision,
            _main.LastCapture,
            _main.LastAction,
            CurrentEvaluationTiming(in handoff),
            _main.RetryGate.Snapshot);
    }

    private ServiceRunnerEvaluationTimingSnapshot CurrentEvaluationTiming(
        in ServiceHandoffSnapshot handoff)
    {
        var readSucceeded = _worker.TryReadEvaluationTiming(out var workerTiming);
        return SelectEvaluationTiming(
            readSucceeded,
            in workerTiming,
            handoff.RequestSequence,
            in _main.EvaluationTiming);
    }

    internal static ServiceRunnerEvaluationTimingSnapshot SelectEvaluationTiming(
        bool workerTimingReadSucceeded,
        in ServiceEvaluationTimingFact workerTiming,
        long requestSequence,
        in ServiceEvaluationTimingFact fallbackTiming)
    {
        if (!workerTimingReadSucceeded)
            return new ServiceRunnerEvaluationTimingSnapshot(
                ServiceRunnerEvaluationTimingAvailability.PublicationContended,
                default);

        var timing = workerTiming.IsPresent && workerTiming.RequestSequence == requestSequence
            ? workerTiming
            : fallbackTiming;
        return new ServiceRunnerEvaluationTimingSnapshot(
            timing.IsPresent
                ? ServiceRunnerEvaluationTimingAvailability.Available
                : ServiceRunnerEvaluationTimingAvailability.NotAvailable,
            timing);
    }
}
