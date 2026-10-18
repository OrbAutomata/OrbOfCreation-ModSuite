using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>
/// Owner-thread facade for scheduling and controlling a sealed service-cycle registry.
/// </summary>
public sealed class SuiteFramePump : IDisposable
{
    private readonly SuiteFramePumpState _state;
    private readonly SuiteFramePumpControl _control;
    private readonly SuiteFramePumpExecutor _executor;

    public SuiteFramePump(ServiceCycleRegistry registry)
        : this(registry, null) { }

    public SuiteFramePump(
        ServiceCycleRegistry registry,
        ServiceCycleSemanticRecorder? semanticRecorder)
#if SERVICE_CYCLE_PROFILE
        : this(registry, semanticRecorder, null, new ServiceCycleProfileProbe()) { }

    internal SuiteFramePump(
        ServiceCycleRegistry registry,
        ServiceCycleSemanticRecorder? semanticRecorder,
        ServiceActionOutcomeWindowRegistry? outcomeWindows,
        ServiceCycleProfileProbe profileProbe)
#else
        : this(registry, semanticRecorder, null) { }

    internal SuiteFramePump(
        ServiceCycleRegistry registry,
        ServiceCycleSemanticRecorder? semanticRecorder,
        ServiceActionOutcomeWindowRegistry? outcomeWindows)
#endif
    {
        _state = new SuiteFramePumpState(
            registry,
            semanticRecorder,
            outcomeWindows
#if SERVICE_CYCLE_PROFILE
            , profileProbe
#endif
            );
        _control = new SuiteFramePumpControl(_state);
        _executor = new SuiteFramePumpExecutor(_state, _control);
    }

    public bool IsEmergencyStopEngaged => _state.Emergency.IsEngaged;
    public EmergencyStopTransitionGeneration EmergencyTransition => _state.Emergency.Transition;
    public EmergencyStopContext ActiveEmergency => _state.Emergency.Active;
    public EmergencyStopContext LatestEmergency => _state.Emergency.Latest;
    public long AcceptedFrameCount => _state.Observability.AcceptedFrameCount;
    public bool HasAcceptedFrame => _state.Observability.HasAcceptedFrame;
    public long LastAcceptedFrameIdentity => _state.Observability.LastAcceptedFrameIdentity;
    public bool IsDisposed => _state.IsDisposed;
    public LifecycleGeneration CurrentLifecycle => _state.Registry.CurrentLifecycle;
    public ServiceCycleSemanticTraceSource? SemanticTrace => _state.Traces.HostTraceSource;
    internal int ServiceCapacity => _state.Transitioned.Length;

    internal ServiceCycleSemanticTraceCloseResult TryCloseSemanticTraceAtSettledBoundary()
    {
        EnsureIdle("Semantic trace admission cannot close while a frame is being pumped.");
        return _state.Traces.TryCloseHostTraceAtSettledBoundary();
    }

    internal void DiscardSemanticTrace()
    {
        EnsureIdle("Semantic trace admission cannot close while a frame is being pumped.");
        _state.Traces.DiscardHostTrace();
    }

    internal bool TryAttachManualSemanticTrace(
        ServiceCycleSemanticRecorder recorder,
        out ServiceCycleSemanticRuntimeTrace? attached)
    {
        EnsureIdle("Manual semantic tracing cannot attach while a frame is being pumped.");
        return _state.Traces.TryAttachManual(
            recorder,
            _state.Emergency.IsEngaged,
            out attached);
    }

    internal bool TryDetachManualSemanticTrace(ServiceCycleSemanticRuntimeTrace attached)
    {
        EnsureIdle("Manual semantic tracing cannot detach while a frame is being pumped.");
        return _state.Traces.TryDetachManual(attached);
    }

    internal void DiscardManualSemanticTrace(ServiceCycleSemanticRuntimeTrace attached)
    {
        EnsureIdle("Manual semantic tracing cannot detach while a frame is being pumped.");
        _state.Traces.DiscardManual(attached);
    }

    internal bool TryAttachDecisionJournal(
        IServiceCycleDecisionJournalObserver observer,
        DecisionJournalServiceBaseline[] baselines)
    {
        EnsureIdle("The decision journal cannot attach while a frame is being pumped.");
        return _state.Journal.TryAttach(
            observer,
            baselines,
            _state.Emergency.IsEngaged);
    }

    internal void ClaimDecisionJournalRuntime(object owner)
    {
        EnsureIdle("Decision-journal ownership cannot change while a frame is being pumped.");
        _state.Journal.ClaimRuntime(owner);
    }

    internal void ReleaseDecisionJournalRuntime(object owner)
    {
        EnsureIdle("Decision-journal ownership cannot change while a frame is being pumped.");
        _state.Journal.ReleaseRuntime(owner);
    }

    internal void DetachDecisionJournal(IServiceCycleDecisionJournalObserver observer)
    {
        EnsureIdle("The decision journal cannot detach while a frame is being pumped.");
        _state.Journal.Detach(observer);
    }

    internal void DisposeOwnedByDecisionJournal(object owner)
    {
        EnsureIdle("The frame pump cannot be disposed while a frame is being pumped.");
        _state.Journal.ValidateOwnedPumpDisposal(owner);
        _state.DisposeRegistry();
        _state.Journal.CompleteOwnedPumpDisposal(owner);
        _state.MarkDisposed();
    }

    public bool RequestLifecycleReplacement(LifecycleGeneration generation) =>
        _control.RequestLifecycleReplacement(generation);

    public bool ReplaceLifecycle(LifecycleGeneration generation) =>
        RequestLifecycleReplacement(generation);

    public void SetEmergencyStop(bool engaged) =>
        SetEmergencyStop(engaged, EmergencyStopReason.UserRequested);

    public void SetEmergencyStop(bool engaged, EmergencyStopReason reason) =>
        _control.SetEmergencyStop(engaged, reason);

    public SuiteFramePumpReport PumpFrame(long frameIdentity) =>
        _executor.Pump(frameIdentity);

#if SERVICE_CYCLE_PROFILE
    /// <summary>
    /// Has every traced pump sample the managed heap from now on, or says why the host's readings
    /// cannot be trusted to.
    /// </summary>
    internal bool TryAttachHeapCounters(ISuiteFramePumpHeapCounters counters, out string unavailable)
    {
        EnsureIdle("Heap counters cannot attach while a frame is being pumped.");
        return _state.HeapSampler.TryAttach(counters, out unavailable);
    }
#endif

    internal int ActionFailureCount => _state.ActionFailures.Count;

    internal ServiceActionFailure ActionFailureAt(int index)
    {
        EnsureIdle("Action-attribution failures cannot be read while a frame is being pumped.");
        return _state.ActionFailures.At(index);
    }

    internal void ClearActionFailures()
    {
        EnsureIdle("Action-attribution failures cannot be cleared while a frame is being pumped.");
        _state.ActionFailures.Clear();
    }

    /// <summary>
    /// Applies what the configuration slot says about the emergency stop, ahead of the frame.
    /// </summary>
    /// <remarks>
    /// The pump does this itself at the start of every frame. A host calls it earlier only when
    /// something it runs before the frame — observability deciding whether to attach, say — has to
    /// see the live state rather than last frame's.
    /// </remarks>
    public void ApplyConfiguredEmergencyStop() => _executor.ApplyConfiguredEmergencyStop();

    public void Dispose()
    {
        _state.AssertOwnerThread();
        if (_state.IsDisposed) return;
        if (_state.IsPumping)
            throw new InvalidOperationException(
                "The service-cycle frame pump cannot be disposed from a service callback.");
        _state.Journal.EnsureUnowned();
        _state.DisposeRegistry();
        _state.MarkDisposed();
    }

    internal SuiteFramePumpCumulativeSnapshot DiagnosticsSnapshot
    {
        get
        {
            EnsureDiagnosticsAvailable();
            var emergency = _state.Emergency.Snapshot;
            return _state.Observability.Snapshot(
                _state.Registry.CurrentLifecycle,
                in emergency,
                _state.Registry.LifecyclePositionTransitionCount);
        }
    }

    internal ServiceCycleRegistry DiagnosticsRegistry
    {
        get
        {
            EnsureDiagnosticsAvailable();
            return _state.Registry;
        }
    }

    internal MonotonicTimestamp DiagnosticsNow
    {
        get
        {
            EnsureDiagnosticsAvailable();
            return _state.Clock.Now;
        }
    }

    private void EnsureDiagnosticsAvailable() =>
        EnsureIdle("Frame-pump diagnostics cannot be read while a frame is being pumped.");

    private void EnsureIdle(string message) => _state.EnsureIdle(message);
}
