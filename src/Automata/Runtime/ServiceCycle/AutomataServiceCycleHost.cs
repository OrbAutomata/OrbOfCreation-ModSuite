using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Observation.WorldCollection;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutomataServiceCycleHost : IDisposable
{
    private readonly Func<long> _readFrameIdentity;
    private readonly IServiceCyclePumpTimingSink? _pumpTiming;
    private readonly Action<string>? _attributionFailureLog;
    private readonly SuiteFramePump _pump;
    private readonly int _serviceCapacity;
    private readonly ServiceCycleTraceRoster _roster;
    private AutomataServiceCycleObservability? _observability;
    private Func<bool>? _disposeClaimedPump;
    private bool _disposed;

#if SERVICE_CYCLE_PROFILE
    internal AutomataServiceCycleHost(
        ServiceCycleRegistry registry,
        Func<long> readFrameIdentity,
        IServiceCyclePumpTimingSink? pumpTiming,
        ServiceCycleSemanticRecorder? semanticTrace,
        ServiceCycleProfileProbe profileProbe,
        Action<string>? attributionFailureLog = null)
        : this(
            registry, readFrameIdentity, pumpTiming, semanticTrace, null, profileProbe,
            attributionFailureLog) { }

    internal AutomataServiceCycleHost(
        ServiceCycleRegistry registry,
        Func<long> readFrameIdentity,
        IServiceCyclePumpTimingSink? pumpTiming,
        ServiceCycleSemanticRecorder? semanticTrace,
        ServiceActionOutcomeWindowRegistry? actionOutcomes,
        ServiceCycleProfileProbe profileProbe,
        Action<string>? attributionFailureLog = null)
#else
    internal AutomataServiceCycleHost(
        ServiceCycleRegistry registry,
        Func<long> readFrameIdentity,
        IServiceCyclePumpTimingSink? pumpTiming,
        ServiceCycleSemanticRecorder? semanticTrace,
        ServiceActionOutcomeWindowRegistry? actionOutcomes = null,
        Action<string>? attributionFailureLog = null)
#endif
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        _readFrameIdentity = readFrameIdentity ??
            throw new ArgumentNullException(nameof(readFrameIdentity));
        _pumpTiming = pumpTiming;
        _attributionFailureLog = attributionFailureLog;
        _serviceCapacity = registry.OrdinalCount;
        // Read before the seal, while every registration is present and nothing can add another: this
        // is the one moment the suite knows its whole roster.
        _roster = AutomataServiceCycleTraceRoster.Build(
            registry,
            WorldCollectionSpanRegistry.Shared.Categories);
        registry.Seal();
        _pump = new SuiteFramePump(
            registry,
            semanticTrace,
            actionOutcomes
#if SERVICE_CYCLE_PROFILE
            , profileProbe
#endif
            );
    }

    internal SuiteFramePump Pump => _pump;
    internal LifecycleGeneration CurrentLifecycle => _pump.CurrentLifecycle;
    internal bool EmergencyStopEngaged => _pump.IsEmergencyStopEngaged;

    internal SuiteFramePumpReport Tick()
    {
        ThrowIfDisposed();
        // Ahead of observability: the decision journal decides whether to attach from the live
        // emergency state, so a suite that starts stopped must be stopped before it looks.
        _pump.ApplyConfiguredEmergencyStop();
        _observability?.BeforePump();
        var report = _pump.PumpFrame(_readFrameIdentity());
        LogAttributionFailures();
        _pumpTiming?.Observe(in report);
        _observability?.AfterPump();
        return report;
    }

    private void LogAttributionFailures()
    {
        try
        {
            if (_attributionFailureLog is null) return;
            for (var index = 0; index < _pump.AttributionFailureCount; index++)
            {
                var failure = _pump.AttributionFailureAt(index);
                var service = _roster[failure.ServiceOrdinal].MachineId;
                _attributionFailureLog(
                    $"ServiceCycle action attribution failed for service {service}; " +
                    $"the action executed and the journal marked attribution failed: {failure.Reason}");
            }
        }
        finally { _pump.ClearAttributionFailures(); }
    }

    internal void AttachObservability(
        AutomataServiceCycleObservability observability,
        in AutomataServiceCycleObservabilityOptions options)
    {
        ThrowIfDisposed();
        if (observability is null) throw new ArgumentNullException(nameof(observability));
        if (_observability is not null)
            throw new InvalidOperationException("ServiceCycle observability is already attached.");
        if (_disposeClaimedPump is not null)
            throw new InvalidOperationException("The ServiceCycle pump already has a shutdown owner.");

        observability.Attach(_pump, _serviceCapacity, _roster, in options);
        var pumpShutdown = observability.PumpShutdown;
        if (pumpShutdown is not null) ClaimPumpShutdown(pumpShutdown);
        _observability = observability;
    }

    internal void SetEmergencyStop(bool engaged, EmergencyStopReason reason)
    {
        ThrowIfDisposed();
        _pump.SetEmergencyStop(engaged, reason);
    }

    internal AutomataDiagnosticsRuntimeEvidence CaptureDiagnostics() =>
        _observability?.CaptureDiagnostics() ??
        AutomataDiagnosticsRuntimeEvidence.Unavailable(
            "The automation runtime has no diagnostics source.");

    internal bool TryReplaceLifecycle(long nativeGeneration)
    {
        ThrowIfDisposed();
        var lifecycle = ToLifecycle(nativeGeneration);
        return lifecycle.Value > _pump.CurrentLifecycle.Value &&
            _pump.RequestLifecycleReplacement(lifecycle);
    }

    internal void ClaimPumpShutdown(Func<bool> disposeClaimedPump)
    {
        ThrowIfDisposed();
        if (disposeClaimedPump is null)
            throw new ArgumentNullException(nameof(disposeClaimedPump));
        if (_disposeClaimedPump is not null)
            throw new InvalidOperationException("The ServiceCycle pump already has a shutdown owner.");
        _disposeClaimedPump = disposeClaimedPump;
    }

    public void Dispose() => Shutdown();

    internal void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        try { _observability?.Dispose(); }
        finally
        {
            if (_disposeClaimedPump?.Invoke() != true) _pump.Dispose();
        }
    }

    internal static LifecycleGeneration ToLifecycle(long generation)
    {
        if (generation <= 0)
            throw new InvalidOperationException(
                "The Automata ServiceCycle host requires a positive native lifecycle generation.");
        return new LifecycleGeneration((ulong)generation);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleHost));
    }
}
