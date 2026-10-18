using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpTransitions
{
    private readonly SuiteFramePumpState _state;
    private readonly SuiteFramePumpControl _control;

    internal SuiteFramePumpTransitions(
        SuiteFramePumpState state,
        SuiteFramePumpControl control)
    {
        _state = state;
        _control = control;
    }

    internal void AdvancePendingMainOwnership(
        int startOrdinal,
        MonotonicTimestamp observedAt)
    {
        for (var offset = 0; offset < _state.Transitioned.Length; offset++)
        {
            var ordinal = OrdinalAt(startOrdinal, offset);
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed || !slot.TryAdvancePendingMainOwnership(observedAt)) continue;
            _state.Transitioned[ordinal] = true;
        }
    }

    internal void AcquireResponses(
        int startOrdinal,
        long frameIdentity,
        ref SuiteFramePumpFrameMetrics metrics)
    {
        for (var offset = 0; offset < _state.Transitioned.Length; offset++)
        {
            var ordinal = OrdinalAt(startOrdinal, offset);
            if (_state.Transitioned[ordinal]) continue;
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed || slot.HandoffPhaseHint != ServiceHandoffPhase.ResponseReady)
                continue;
            var started = _state.Clock.Now;
            var acquisition = slot.TryAcquireResponse(started);
            var ended = _state.Clock.Now;
            if (acquisition.Acquired)
            {
                _state.EvidenceEmitter.ResponseAcquired(
                    ordinal,
                    in acquisition,
                    ended,
                    frameIdentity);
                metrics.Responses++;
                _state.Transitioned[ordinal] = true;
                if (acquisition.EmergencyRejected)
                    metrics.EmergencyRejections++;
                else if (_control.EffectiveEmergencyStop &&
                         slot.RejectForEmergencyStop(
                             _control.EffectiveEmergencyContext,
                             _state.Clock.Now,
                             out var receipt))
                {
                    _state.EvidenceEmitter.EmergencyRejected(
                        ordinal,
                        in receipt,
                        receipt.CompletedAt);
                    metrics.EmergencyRejections++;
                }
            }
            metrics.ResponseTicks = AddElapsed(metrics.ResponseTicks, started, ended);
        }
    }

    /// <summary>
    /// Dispatches publishing services before mutating ones, each class in the ordinary rotation.
    /// </summary>
    /// <remarks>
    /// Two passes rather than one so that a snapshot handed back by a worker this frame is live
    /// before any service acts on it, instead of a frame behind. Fairness is preserved within each
    /// class; only the classes are ordered.
    /// </remarks>
    internal void DispatchActions(
        int startOrdinal,
        long frameIdentity,
        ref SuiteFramePumpFrameMetrics metrics)
    {
        if (_control.EffectiveEmergencyStop) return;
        DispatchClass(ServiceActionDispatchClass.Publication, startOrdinal, frameIdentity, ref metrics);
        if (_control.EffectiveEmergencyStop) return;
        DispatchClass(ServiceActionDispatchClass.GameMutation, startOrdinal, frameIdentity, ref metrics);
    }

    private void DispatchClass(
        ServiceActionDispatchClass dispatchClass,
        int startOrdinal,
        long frameIdentity,
        ref SuiteFramePumpFrameMetrics metrics)
    {
        for (var offset = 0; offset < _state.Transitioned.Length; offset++)
        {
            var ordinal = OrdinalAt(startOrdinal, offset);
            if (_state.Transitioned[ordinal]) continue;
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed) continue;
            if (slot.ActionDispatchPolicy.DispatchClass != dispatchClass) continue;
            var attempted = false;
#if SERVICE_CYCLE_PROFILE
            var profileCoordinates = new ServiceCycleProfileCoordinates(ordinal, frameIdentity);
#endif
            for (var action = 0;
                 action < slot.ActionDispatchPolicy.MaximumActionsPerFrame;
                 action++)
            {
                var started = _state.Clock.Now;
                ServiceActionDispatch dispatch;
                _state.EnterServiceCallback();
                try
                {
                    dispatch = slot.TryExecuteOne(
                        started,
                        frameIdentity,
                        _state.Traces.Dispatch
#if SERVICE_CYCLE_PROFILE
                        , in profileCoordinates
#endif
                        );
                }
                finally { _state.ExitServiceCallback(); }
                var ended = _state.Clock.Now;
                _state.EvidenceEmitter.ActionDispatched(
                    ordinal,
                    in dispatch,
                    ended,
                    frameIdentity);
                if (!dispatch.Attempted) break;
                attempted = true;
                metrics.Actions++;
                metrics.ActionTicks = AddElapsed(
                    metrics.ActionTicks,
                    dispatch.ActionFact.StartedAt,
                    dispatch.ActionFact.CompletedAt);
                if (_control.EffectiveEmergencyStop)
                {
                    metrics.EmergencyRejections += _control.RejectAllActiveBatches(
                        ended,
                        markFrameTransitions: true);
                    break;
                }
                if (dispatch.BatchTerminal) break;
            }
            if (attempted) _state.Transitioned[ordinal] = true;
            if (_control.EffectiveEmergencyStop) break;
        }
    }

    internal void StartCycles(
        int startOrdinal,
        long frameIdentity,
        ref SuiteFramePumpFrameMetrics metrics)
    {
        if (_control.EffectiveEmergencyStop) return;
        for (var offset = 0; offset < _state.Transitioned.Length; offset++)
        {
            var ordinal = OrdinalAt(startOrdinal, offset);
            if (_state.Transitioned[ordinal]) continue;
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed) continue;
            var started = _state.Clock.Now;
            ServiceCycleStartAttempt start;
            bool deferredForWorld;
#if SERVICE_CYCLE_PROFILE
            var heap = _state.HeapSampler.BeginCapture();
#endif
            _state.EnterServiceCallback();
            try
            {
                start = slot.TryStartCycle(
                    started,
                    frameIdentity,
                    _state.Traces.Dispatch,
                    out deferredForWorld
                    );
            }
            finally { _state.ExitServiceCallback(); }
            if (deferredForWorld) metrics.WorldGateDeferrals++;
            var ended = _state.Clock.Now;
            _state.EvidenceEmitter.StartAttemptObserved(
                ordinal,
                in start,
                _state.Registry.CurrentLifecycle,
                ended,
                frameIdentity);
            if (start.CaptureAttempted)
            {
#if SERVICE_CYCLE_PROFILE
                _state.HeapSampler.EndCapture(in heap);
#endif
                metrics.Captures++;
                metrics.CaptureTicks = AddElapsed(
                    metrics.CaptureTicks,
                    start.CaptureFact.StartedAt,
                    start.CaptureFact.CompletedAt);
            }
            // A service whose start decision said yes has had its turn this frame, whether or not it
            // captured — the ordinary shape has no capture stage to attempt, and before it lost one
            // this was the same condition. Keyed on the batch because that is minted exactly when the
            // runtime opens the cycle, and on the invocation so a deferred request republished this
            // frame does not count as a fresh turn.
            if (start.StartInvocation.IsPresent && start.Batch.IsValid)
            {
                metrics.CyclesStarted++;
                _state.Transitioned[ordinal] = true;
            }
            if (!_control.EffectiveEmergencyStop) continue;
            metrics.EmergencyRejections += _control.RejectAllActiveBatches(
                ended,
                markFrameTransitions: true);
            break;
        }
    }

    private int OrdinalAt(int startOrdinal, int offset) =>
        _state.Transitioned.Length == 0
            ? 0
            : (startOrdinal + offset) % _state.Transitioned.Length;

    private static long AddElapsed(
        long total,
        MonotonicTimestamp start,
        MonotonicTimestamp end)
    {
        var elapsed = end.Ticks >= start.Ticks ? end.Ticks - start.Ticks : 0;
        return elapsed > long.MaxValue - total ? long.MaxValue : total + elapsed;
    }
}
