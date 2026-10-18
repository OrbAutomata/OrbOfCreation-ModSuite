using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpControl
{
    private readonly SuiteFramePumpState _state;

    internal SuiteFramePumpControl(SuiteFramePumpState state) => _state = state;

    internal bool EffectiveEmergencyStop => _state.Emergency.IsEffective;
    internal EmergencyStopContext EffectiveEmergencyContext =>
        _state.Emergency.EffectiveContext;

    internal bool RequestLifecycleReplacement(LifecycleGeneration generation)
    {
        _state.EnsureAvailable();
        var cancelsInFlightCycle = false;
        if (_state.Traces.HasHostTrace)
        {
            for (var ordinal = 0; ordinal < _state.Transitioned.Length; ordinal++)
            {
                if (_state.Registry.GetSlot(ordinal).IsBetweenCycles) continue;
                cancelsInFlightCycle = true;
                break;
            }
        }

        var accepted = _state.Registry.RequestLifecycle(generation);
        if (!accepted) return false;
        if (cancelsInFlightCycle)
            _state.Traces.InvalidateHostTrace();
        var observedAt = _state.Clock.Now;
        for (var ordinal = 0; ordinal < _state.Transitioned.Length; ordinal++)
            _state.EvidenceEmitter.LifecycleRequested(ordinal, generation, observedAt);
        if (!_state.IsInsideServiceCallback)
        {
            _state.Registry.ReconcileLifecycle(observedAt);
            _state.EvidenceScanner.ObserveLifecycle(
                _state.Traces.Dispatch,
                _state.Journal.Observer,
                observedAt);
        }
        return true;
    }

    /// <summary>
    /// Brings the pump's emergency stop in line with what the configuration slot says, at the top of
    /// a frame the pump is already inside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately reject-free. <see cref="SetEmergencyStop"/> rejects the active batches itself and
    /// discards the count when it runs inside a frame, which would leave the frame's own rejection
    /// step nothing to find and report zero rejections where the real number is what a reader wants.
    /// The frame's <c>EffectiveEmergencyStop</c> branch owns the rejection, so this only moves the
    /// state and emits the transition evidence.
    /// </para>
    /// <para>
    /// The configuration clears only the episode it engaged. Shutdown and safety interlocks come from
    /// somewhere the settings file knows nothing about, and every configuration now answers the
    /// emergency-disable question — the flag has a default — so an unrelated "not disabled" reading
    /// would otherwise cancel a shutdown stop and let prepared work run against a game that is going
    /// away.
    /// </para>
    /// </remarks>
    internal void ApplyConfiguredEmergencyStop(bool engaged)
    {
        var wasEngaged = _state.Emergency.IsEngaged;
        if (wasEngaged == engaged) return;
        var priorActive = _state.Emergency.Active;
        if (!engaged && priorActive.Reason != EmergencyStopReason.UserRequested) return;
        _state.Emergency.Set(engaged, EmergencyStopReason.UserRequested, _state.IsPumping);
        var observedAt = _state.Clock.Now;
        if (engaged)
        {
            var entered = _state.Emergency.Active;
            _state.EvidenceEmitter.EmergencyEntered(in entered, observedAt);
        }
        else
        {
            _state.EvidenceEmitter.EmergencyCleared(in priorActive, observedAt);
        }
    }

    internal void SetEmergencyStop(bool engaged, EmergencyStopReason reason)
    {
        _state.EnsureAvailable();
        if (reason is < EmergencyStopReason.UserRequested or > EmergencyStopReason.SuiteShutdown)
            throw new ArgumentOutOfRangeException(nameof(reason));
        var wasEngaged = _state.Emergency.IsEngaged;
        var priorActive = _state.Emergency.Active;
        _state.Emergency.Set(engaged, reason, _state.IsPumping);
        var observedAt = _state.Clock.Now;
        if (!wasEngaged && _state.Emergency.IsEngaged)
        {
            var entered = _state.Emergency.Active;
            _state.EvidenceEmitter.EmergencyEntered(in entered, observedAt);
        }
        else if (wasEngaged && !_state.Emergency.IsEngaged)
        {
            _state.EvidenceEmitter.EmergencyCleared(in priorActive, observedAt);
        }

        if (engaged && !_state.IsInsideServiceCallback)
        {
            RejectAllActiveBatches(
                observedAt,
                markFrameTransitions: _state.IsPumping);
        }
    }

    internal int RejectAllActiveBatches(
        MonotonicTimestamp now,
        bool markFrameTransitions)
    {
        var rejected = 0;
        var emergency = EffectiveEmergencyContext;
        if (!emergency.IsValid)
            throw new InvalidOperationException(
                "Emergency rejection requires an active or frame-latched context.");
        for (var ordinal = 0; ordinal < _state.Transitioned.Length; ordinal++)
        {
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed) continue;
            var phase = slot.HandoffPhaseHint;
            if (phase is ServiceHandoffPhase.RequestReady or ServiceHandoffPhase.Evaluating or
                ServiceHandoffPhase.ResponseReady or ServiceHandoffPhase.MainOwnedBatch)
            {
                _state.EvidenceEmitter.EmergencyAppliedToService(ordinal, in emergency);
            }
            if (!slot.RejectForEmergencyStop(emergency, now, out var receipt)) continue;
            _state.EvidenceEmitter.EmergencyRejected(ordinal, in receipt, now);
            rejected++;
            slot.TryAdvancePendingMainOwnership(now);
            if (markFrameTransitions) _state.Transitioned[ordinal] = true;
        }
        return rejected;
    }
}
