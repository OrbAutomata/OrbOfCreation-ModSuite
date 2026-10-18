using System;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpExecutor
{
    private readonly SuiteFramePumpState _state;
    private readonly SuiteFramePumpControl _control;
    private readonly SuiteFramePumpTransitions _transitions;

    internal SuiteFramePumpExecutor(
        SuiteFramePumpState state,
        SuiteFramePumpControl control)
    {
        _state = state;
        _control = control;
        _transitions = new SuiteFramePumpTransitions(state, control);
    }

    internal SuiteFramePumpReport Pump(long frameIdentity)
    {
        _state.EnsureAvailable();
        if (frameIdentity < 0) throw new ArgumentOutOfRangeException(nameof(frameIdentity));
        if (_state.IsPumping)
            throw new InvalidOperationException(
                "The service-cycle frame pump cannot be entered recursively.");
        if (_state.HasAcceptedFrame &&
            frameIdentity <= _state.LastAcceptedFrameIdentity)
            return RejectFrame(frameIdentity);

        // Before the frame opens rather than inside it: PrepareFrame latches whatever the stop is at
        // that moment, so engaging here still leaves the frame's own rejection step to reject the
        // active batches and count them, which is what a reader of EmergencyRejections wants.
        ApplyConfiguredEmergencyStop();

        _state.BeginPump();
#if SERVICE_CYCLE_PROFILE
        var pumpProfile = _state.EvidenceProfiler.BeginFrame(
            ServiceCycleProfileSpan.OverallPump,
            _state.Registry.CurrentLifecycle.Value,
            frameIdentity);
        var pumpCompleted = false;
#endif
        _state.PrepareFrame();
        // Capture and action facts are reported from the runner, which has no frame to name. The
        // trace is told which frame is open so those facts can say so; a rejected frame never opens
        // one, and neither does a control transition between frames.
        var trace = _state.Traces.Dispatch;
        trace?.EnterFrame(frameIdentity);
        try
        {
            var report = PumpAcceptedFrame(frameIdentity);
#if SERVICE_CYCLE_PROFILE
            pumpCompleted = true;
#endif
            return report;
        }
        finally
        {
            trace?.LeaveFrame();
            _state.EndFrame();
#if SERVICE_CYCLE_PROFILE
            if (pumpCompleted) pumpProfile.Complete();
            else pumpProfile.Abandon();
#endif
        }
    }

    private SuiteFramePumpReport RejectFrame(long frameIdentity)
    {
        var report = new SuiteFramePumpReport(
            frameIdentity,
            false,
            _state.NextStartOrdinal,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            default,
            default,
            default,
            default);
        _state.EvidenceEmitter.RejectedPumpCompleted(in report, _state.Clock.Now);
        return report;
    }

    /// <summary>
    /// Brings the emergency stop in line with the configuration slot, which is where the suite says
    /// whether anything should act at all.
    /// </summary>
    /// <remarks>
    /// Read, not pushed. Nothing outside has to notice a setting changed and remember to tell the
    /// pump, so the state the pump is in cannot drift from what the suite is configured to do.
    /// </remarks>
    internal void ApplyConfiguredEmergencyStop() =>
        _control.ApplyConfiguredEmergencyStop(_state.Registry.ConfiguredEmergencyDisable);

    private SuiteFramePumpReport PumpAcceptedFrame(long frameIdentity)
    {
        var frameStarted = _state.Clock.Now;
        var startOrdinal = _state.NextStartOrdinal;
        var lifecycleTransitionStart =
            _state.Registry.LifecyclePositionTransitionCount;
        var reconciliationEpoch =
            _state.Registry.NextLifecycleReconciliationEpoch();
        var metrics = default(SuiteFramePumpFrameMetrics);
#if SERVICE_CYCLE_PROFILE
        _state.HeapSampler.BeginPump(_state.Traces.Dispatch is not null);
#endif

        _state.EvidenceScanner.ObservePublications(
            _state.Traces.Dispatch,
            _state.Journal.Observer,
            frameStarted,
            includeJournal: true);
        ReconcileLifecycle(frameStarted, reconciliationEpoch, frameIdentity);
        _transitions.AdvancePendingMainOwnership(startOrdinal, frameStarted);
#if SERVICE_CYCLE_PROFILE
        var acquire = BeginPhase(ServiceCycleProfileSpan.AcquireResponses, frameIdentity);
        try
        {
#endif
        _transitions.AcquireResponses(startOrdinal, frameIdentity, ref metrics);
#if SERVICE_CYCLE_PROFILE
        }
        finally { acquire.Complete(); }
#endif
        if (_control.EffectiveEmergencyStop)
        {
            metrics.EmergencyRejections += _control.RejectAllActiveBatches(
                _state.Clock.Now,
                markFrameTransitions: true);
        }
#if SERVICE_CYCLE_PROFILE
        var dispatch = BeginPhase(ServiceCycleProfileSpan.DispatchActions, frameIdentity);
        try
        {
#endif
        _transitions.DispatchActions(startOrdinal, frameIdentity, ref metrics);
#if SERVICE_CYCLE_PROFILE
        }
        finally { dispatch.Complete(); }
        var start = BeginPhase(ServiceCycleProfileSpan.StartCycles, frameIdentity);
        try
        {
#endif
        _transitions.StartCycles(startOrdinal, frameIdentity, ref metrics);
#if SERVICE_CYCLE_PROFILE
        }
        finally { start.Complete(); }
#endif
        if (metrics.WorldGateDeferrals != 0)
            _state.EvidenceScanner.ObserveWorldGate(_state.Journal.Observer, _state.Clock.Now);
#if SERVICE_CYCLE_PROFILE
        var settle = BeginPhase(ServiceCycleProfileSpan.ReconcileLifecycle, frameIdentity);
        try
        {
#endif
        _state.Registry.ReconcileLifecycle(_state.Clock.Now, reconciliationEpoch);
        _state.EvidenceScanner.ObserveLifecycle(
            _state.Traces.Dispatch,
            _state.Journal.Observer,
            _state.Clock.Now);
#if SERVICE_CYCLE_PROFILE
        }
        finally { settle.Complete(); }
#endif

        if (_state.Transitioned.Length != 0)
            _state.NextStartOrdinal =
                (startOrdinal + 1) % _state.Transitioned.Length;
        var frameEnded = _state.Clock.Now;
#if SERVICE_CYCLE_PROFILE
        var heapSampled = _state.HeapSampler.TryEndPump(frameIdentity, out var heap);
#endif
        var report = CreateReport(
            frameIdentity,
            startOrdinal,
            lifecycleTransitionStart,
            frameStarted,
            frameEnded,
            metrics);
        _state.RecordAcceptedFrame(frameIdentity);
        _state.EvidenceScanner.ObservePublications(
            _state.Traces.Dispatch,
            _state.Journal.Observer,
            frameEnded,
            includeJournal: false);
        _state.EvidenceEmitter.PumpCompleted(
            in report,
            _state.Registry.CurrentLifecycle,
            frameEnded);
#if SERVICE_CYCLE_PROFILE
        if (heapSampled)
            _state.EvidenceEmitter.PumpHeapSampled(in heap, _state.Registry.CurrentLifecycle, frameEnded);
#endif
        return report;
    }

    private void ReconcileLifecycle(
        MonotonicTimestamp observedAt,
        long reconciliationEpoch,
        long frameIdentity)
    {
#if SERVICE_CYCLE_PROFILE
        var profile = BeginPhase(ServiceCycleProfileSpan.ReconcileLifecycle, frameIdentity);
        try
        {
#endif
        _state.Registry.ReconcileLifecycle(observedAt, reconciliationEpoch);
        _state.EvidenceScanner.ObserveLifecycle(
            _state.Traces.Dispatch,
            _state.Journal.Observer,
            observedAt);
#if SERVICE_CYCLE_PROFILE
        }
        finally { profile.Complete(); }
#endif
    }

#if SERVICE_CYCLE_PROFILE
    /// <summary>
    /// Opens one of the frame's own phase spans. A frame reconciles lifecycle twice — once before it
    /// acts and once after — so that span is two occurrences per frame rather than one.
    /// </summary>
    private ServiceCycleProfileStageScope BeginPhase(
        ServiceCycleProfileSpan span,
        long frameIdentity) =>
        _state.EvidenceProfiler.BeginFrame(
            span,
            _state.Registry.CurrentLifecycle.Value,
            frameIdentity);
#endif

    private SuiteFramePumpReport CreateReport(
        long frameIdentity,
        int startOrdinal,
        long lifecycleTransitionStart,
        MonotonicTimestamp frameStarted,
        MonotonicTimestamp frameEnded,
        SuiteFramePumpFrameMetrics metrics) =>
        new(
            frameIdentity,
            true,
            startOrdinal,
            metrics.Responses,
            metrics.Actions,
            metrics.Captures,
            metrics.CyclesStarted,
            metrics.WorldGateDeferrals,
            metrics.EmergencyRejections,
            _state.Registry.LifecyclePositionTransitionCount - lifecycleTransitionStart,
            new MonotonicDuration(metrics.ResponseTicks),
            new MonotonicDuration(metrics.ActionTicks),
            new MonotonicDuration(metrics.CaptureTicks),
            new MonotonicDuration(ElapsedTicks(frameStarted, frameEnded)));

    private static long ElapsedTicks(
        MonotonicTimestamp start,
        MonotonicTimestamp end) =>
        end.Ticks >= start.Ticks ? end.Ticks - start.Ticks : 0;
}
