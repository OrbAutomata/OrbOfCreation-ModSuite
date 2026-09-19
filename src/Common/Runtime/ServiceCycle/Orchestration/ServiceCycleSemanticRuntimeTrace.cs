using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>
/// No-throw owner-thread boundary between runtime facts and focused semantic translators. The boundary
/// permanently isolates observability after its first fault so tracing cannot alter gameplay progression.
/// </summary>
internal sealed class ServiceCycleSemanticRuntimeTrace : IServiceCycleAttemptObserver
{
    private readonly ServiceCycleSemanticRecorder _recorder;
    private readonly ServiceCycleSemanticTraceSource _source;
    private readonly ServiceCycleSemanticPublicationEvents _publications;
    private readonly ServiceCycleSemanticLifecycleEvents _lifecycle;
    private readonly ServiceCycleSemanticExecutionEvents _execution;
    private bool _faulted;

    internal ServiceCycleSemanticRuntimeTrace(
        ServiceCycleSemanticRecorder recorder,
        int registryOrdinalCount)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        if (registryOrdinalCount <= 0) throw new ArgumentOutOfRangeException(nameof(registryOrdinalCount));
        if (recorder.ServiceCapacity != registryOrdinalCount)
            throw new ArgumentException(
                "The semantic recorder service capacity must equal the registry ordinal count.",
                nameof(recorder));

        _source = new ServiceCycleSemanticTraceSource(recorder);
        var state = new ServiceCycleSemanticTraceState(registryOrdinalCount);
        _publications = new ServiceCycleSemanticPublicationEvents(recorder);
        _execution = new ServiceCycleSemanticExecutionEvents(recorder, _publications, state);
        _lifecycle = new ServiceCycleSemanticLifecycleEvents(recorder, _execution, state);
    }

    internal ServiceCycleSemanticTraceSource Source => _source;
    internal bool IsFaulted => _faulted;

    internal void Bind(
        int ordinal,
        ServiceId service,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        LifecycleGeneration lifecycle,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt)
    {
        if (_faulted) return;
        try
        {
            _publications.Bind(ordinal, service, configuration, strategy, observedAt);
            _lifecycle.Bind(ordinal, lifecycle, lifecycleSemanticVersion, observedAt);
        }
        catch { Fault(); }
    }

    internal void ObservePublications(
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        if (_faulted) return;
        try { _publications.Observe(configuration, strategy, observedAt); }
        catch { Fault(); }
    }

    internal void LifecycleRequested(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt)
    {
        if (_faulted) return;
        try { _lifecycle.Requested(ordinal, lifecycle, observedAt); }
        catch { Fault(); }
    }

    internal void ObserveLifecycle(
        int ordinal,
        in ServiceLifecycleSlotSnapshot snapshot,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt)
    {
        if (_faulted) return;
        try { _lifecycle.Observe(ordinal, in snapshot, lifecycleSemanticVersion, observedAt); }
        catch { Fault(); }
    }

    internal bool NeedsLifecycleObservation(int ordinal, long lifecycleSemanticVersion) =>
        !_faulted && _lifecycle.NeedsObservation(ordinal, lifecycleSemanticVersion);

    internal void EmergencyEntered(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        if (_faulted) return;
        try { _recorder.EmergencyEntered(in emergency, observedAt); }
        catch { Fault(); }
    }

    internal void EmergencyCleared(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        if (_faulted) return;
        try { _recorder.EmergencyCleared(in emergency, observedAt); }
        catch { Fault(); }
    }

    internal void EmergencyAppliedToService(int ordinal, in EmergencyStopContext emergency)
    {
        if (_faulted) return;
        try { _recorder.RetainEmergencyForService(ordinal, in emergency); }
        catch { Fault(); }
    }

    /// <summary>
    /// Records one category of one world-collection pass, through the same no-throw boundary every
    /// other fact crosses. Collection runs on the main thread inside the capture the game is read by,
    /// so a span that threw would fault that capture; here it costs the recording and nothing else.
    /// </summary>
    internal void WorldCategoryCollected(
        int category,
        int sampled,
        int passCategories,
        ulong lifecycle,
        long frameIdentity,
        MonotonicTimestamp observedAt,
        MonotonicDuration elapsed)
    {
        if (_faulted) return;
        try
        {
            _recorder.WorldCategoryCollected(
                category,
                sampled,
                passCategories,
                lifecycle,
                frameIdentity,
                observedAt,
                elapsed);
        }
        catch { Fault(); }
    }

    internal void EnterFrame(long frameIdentity)
    {
        if (_faulted) return;
        try { _recorder.EnterFrame(frameIdentity); }
        catch { Fault(); }
    }

    internal void LeaveFrame()
    {
        if (_faulted) return;
        try { _recorder.LeaveFrame(); }
        catch { Fault(); }
    }

    internal void PumpCompleted(in SuiteFramePumpReport report, MonotonicTimestamp observedAt)
    {
        if (_faulted) return;
        try { _recorder.PumpCompleted(in report, observedAt); }
        catch { Fault(); }
    }

    internal void StartAttemptObserved(int ordinal, in ServiceCycleStartAttempt attempt)
    {
        if (_faulted) return;
        try { _execution.StartAttemptObserved(ordinal, in attempt); }
        catch { Fault(); }
    }

    public void StartAttempted(
        int ordinal,
        in ServiceCycleStartContext context,
        MonotonicTimestamp observedAt)
    {
        if (_faulted) return;
        try { _recorder.StartAttempted(ordinal, in context, observedAt); }
        catch { Fault(); }
    }

    public void StartReady(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (_faulted) return;
        try { _recorder.StartReady(ordinal, in context, in decision, observedAt, duration); }
        catch { Fault(); }
    }

    internal void ResponseAcquired(int ordinal, in ServiceResponseAcquisition acquisition)
    {
        if (_faulted) return;
        try { _execution.ResponseAcquired(ordinal, in acquisition); }
        catch { Fault(); }
    }

    internal void ActionDispatched(int ordinal, in ServiceActionDispatch dispatch)
    {
        if (_faulted) return;
        try { _execution.ActionDispatched(ordinal, in dispatch); }
        catch { Fault(); }
    }

    public void CaptureStarted(int ordinal, in ServiceCaptureContext context)
    {
        if (_faulted) return;
        try { _execution.CaptureStarted(ordinal, in context); }
        catch { Fault(); }
    }

    public void ActionAttempted(int ordinal, in ServiceActionContext context)
    {
        if (_faulted) return;
        try { _execution.ActionAttempted(ordinal, in context); }
        catch { Fault(); }
    }

    internal void EmergencyRejected(int ordinal, in BatchReceipt receipt)
    {
        if (_faulted) return;
        try { _execution.EmergencyRejected(ordinal, in receipt); }
        catch { Fault(); }
    }

    private void Fault()
    {
        _faulted = true;
        _source.RecordEmissionFault();
    }
}
