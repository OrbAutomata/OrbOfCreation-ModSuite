using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpState
{
    private readonly int _ownerThreadId;

    internal SuiteFramePumpState(
        ServiceCycleRegistry registry,
        ServiceCycleSemanticRecorder? semanticRecorder,
        ServiceActionTimelineRegistry? actionTimeline
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        if (semanticRecorder is { Enabled: true } &&
            semanticRecorder.ServiceCapacity != registry.OrdinalCount)
        {
            throw new ArgumentException(
                "The semantic recorder service capacity must equal the registry ordinal count.",
                nameof(semanticRecorder));
        }
#if SERVICE_CYCLE_PROFILE
        if (profileProbe is null) throw new ArgumentNullException(nameof(profileProbe));
#endif
        var serviceCapacity = registry.ClaimPump();
        Clock = registry.Clock;
        Transitioned = new bool[serviceCapacity];
        Traces = new SuiteFramePumpTraceSession(
            registry,
            serviceCapacity,
            semanticRecorder);
        Journal = new SuiteFramePumpJournalSession(registry, serviceCapacity, actionTimeline);
        EvidenceScanner = new SuiteFramePumpEvidenceScanner(registry, serviceCapacity);
        ServiceFailures = new ServiceFailureBuffer();
#if SERVICE_CYCLE_PROFILE
        EvidenceProfiler = new SuiteFramePumpEvidenceProfiler(profileProbe);
        EvidenceEmitter = new SuiteFramePumpEvidenceEmitter(
            Traces,
            Journal,
            ServiceFailures,
            EvidenceProfiler);
#else
        EvidenceEmitter = new SuiteFramePumpEvidenceEmitter(
            Traces,
            Journal,
            ServiceFailures);
#endif
    }

    internal ServiceCycleRegistry Registry { get; }
    internal IMonotonicClock Clock { get; }
    internal bool[] Transitioned { get; }
    internal SuiteEmergencyStopState Emergency { get; } = new();
    internal SuiteFramePumpTraceSession Traces { get; }
    internal SuiteFramePumpJournalSession Journal { get; }
    internal SuiteFramePumpEvidenceScanner EvidenceScanner { get; }
    internal ServiceFailureBuffer ServiceFailures { get; }
    internal SuiteFramePumpEvidenceEmitter EvidenceEmitter { get; }
#if SERVICE_CYCLE_PROFILE
    internal SuiteFramePumpEvidenceProfiler EvidenceProfiler { get; }
    internal SuiteFramePumpHeapSampler HeapSampler { get; } = new();
#endif
    internal int NextStartOrdinal { get; set; }
    internal bool IsPumping { get; private set; }
    internal bool IsInsideServiceCallback { get; private set; }
    internal bool IsDisposed { get; private set; }
    internal bool HasAcceptedFrame { get; private set; }
    internal long LastAcceptedFrameIdentity { get; private set; }

    internal void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "The service-cycle frame pump must remain on its owning main thread.");
    }

    internal void EnsureAvailable()
    {
        AssertOwnerThread();
        if (IsDisposed) throw new ObjectDisposedException(nameof(SuiteFramePump));
    }

    internal void EnsureIdle(string message)
    {
        EnsureAvailable();
        if (IsPumping) throw new InvalidOperationException(message);
    }

    internal void BeginPump() => IsPumping = true;

    internal void RecordAcceptedFrame(long frameIdentity)
    {
        HasAcceptedFrame = true;
        LastAcceptedFrameIdentity = frameIdentity;
    }

    internal void PrepareFrame()
    {
        Emergency.BeginFrame();
        ServiceFailures.BeginFrame();
        Array.Clear(Transitioned, 0, Transitioned.Length);
    }

    internal void EndFrame()
    {
        IsInsideServiceCallback = false;
        IsPumping = false;
        Emergency.EndFrame();
    }

    internal void EnterServiceCallback()
    {
        Registry.EnterPumpCallback();
        IsInsideServiceCallback = true;
    }

    internal void ExitServiceCallback()
    {
        IsInsideServiceCallback = false;
        Registry.ExitPumpCallback();
    }

    internal void DisposeRegistry()
    {
        try { Journal.Dispose(Clock.Now); }
        finally { Registry.Dispose(); }
    }

    internal void MarkDisposed() => IsDisposed = true;
}
