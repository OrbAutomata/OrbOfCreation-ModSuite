using System;
using BepInEx.Logging;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using System.Threading;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
#endif

namespace OrbAutomata;

internal sealed class AutomataServiceCycleObservability : IDisposable
{
    private readonly ManualLogSource _log;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataServiceCycleProfileController? _profile;
    private readonly ServiceCycleProfileProbe _profileProbe;
    private AutomataHeapCounters? _heapCounters;
#endif
    private AutomataFullTraceController? _fullTrace;
    private SuiteFramePump? _pump;
    private int _serviceCapacity;
    private ServiceCycleTraceRoster? _roster;
    private static long _nextSnapshotIdentity = DateTime.UtcNow.Ticks;
    private AutomataDecisionJournalController? _decisionJournal;
    private bool _attached;
    private bool _disposed;

    private AutomataServiceCycleObservability(
        ManualLogSource log
#if SERVICE_CYCLE_PROFILE
        , AutomataServiceCycleProfileController? profile
#endif
        )
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
#if SERVICE_CYCLE_PROFILE
        _profile = profile;
        _profileProbe = profile?.Probe ?? new ServiceCycleProfileProbe();
#endif
    }

    internal static AutomataServiceCycleObservability Create(
        IMonotonicClock clock,
        bool traceActive,
        ManualLogSource log)
    {
        if (clock is null) throw new ArgumentNullException(nameof(clock));
#if SERVICE_CYCLE_PROFILE
        var profile = AutomataServiceCycleProfileController.TryCreate(
            clock,
            traceActive,
            log,
            PerformanceProfileControlRegistry.Shared);
        return new AutomataServiceCycleObservability(log, profile);
#else
        return new AutomataServiceCycleObservability(log);
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceCycleProfileProbe ProfileProbe => _profileProbe;
#endif

    internal Func<bool>? PumpShutdown =>
        _decisionJournal is null ? null : _decisionJournal.DisposeWithPump;

    internal void Attach(
        SuiteFramePump pump,
        int serviceCapacity,
        ServiceCycleTraceRoster roster,
        in AutomataServiceCycleObservabilityOptions options)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleObservability));
        if (_attached) throw new InvalidOperationException("ServiceCycle observability is already attached.");
        if (pump is null) throw new ArgumentNullException(nameof(pump));

        SweepRunFolders();
        AutomataFullTraceController? fullTrace = null;
        try
        {
            var fullTraceOptions = options.FullTrace;
            if (fullTraceOptions.Enabled)
            {
                fullTrace = AutomataFullTraceController.Create(
                    pump,
                    serviceCapacity,
                    roster,
                    in fullTraceOptions,
                    _log);
#if SERVICE_CYCLE_PROFILE
                AttachHeapCounters(pump);
#endif
            }

            AutomataDecisionJournalController? decisionJournal = null;
            var journalOptions = options.DecisionJournal;
            if (journalOptions.Enabled)
            {
                decisionJournal = AutomataDecisionJournalController.TryCreate(
                    pump,
                    in journalOptions,
                    _log);
            }

            if (options.AutoStartDiagnosticSessions)
            {
                fullTrace?.StartAutomatically();
#if SERVICE_CYCLE_PROFILE
                _profile?.StartAutomatically();
#endif
            }

            _fullTrace = fullTrace;
            _decisionJournal = decisionJournal;
            _pump = pump;
            _serviceCapacity = serviceCapacity;
            _roster = roster;
            _attached = true;
        }
        catch
        {
            fullTrace?.Dispose();
            throw;
        }
    }

#if SERVICE_CYCLE_PROFILE
    /// <summary>
    /// Has the full trace sample the heap every pump, and says once what the capture will hold: a
    /// reader of a capture with no heap samples finds the reason here.
    /// </summary>
    private void AttachHeapCounters(SuiteFramePump pump)
    {
        var counters = AutomataHeapCounters.Create();
        if (!pump.TryAttachHeapCounters(counters, out var unavailable))
        {
            counters.Dispose();
            _log.LogAutomataError("Profiling full trace records no heap samples: " + unavailable + ".");
            return;
        }
        _heapCounters = counters;
        _log.LogAutomataInfo(
            "Profiling full trace samples the heap every pump; Unity's per-frame allocation counter is " +
            (counters.HasFrameAllocated ? "available." : "unavailable in this player."));
    }

#endif
    /// <summary>
    /// The journal's own cap governs the journal directory only. Run folders are the other half of
    /// the suite's disk use, and nothing else deletes them, so a launch prunes the oldest before it
    /// can add one more.
    /// </summary>
    private void SweepRunFolders()
    {
        var removed = AutomataTraceRunRoot.SweepRunFolders();
        if (removed == 0) return;
        _log.LogAutomataInfo(
            "ServiceCycle trace retention removed " + removed +
            " old capture folder(s); the newest " + AutomataTraceRunRoot.RetainedRunFolders +
            " are kept.");
    }

    internal void BeforePump()
    {
        _fullTrace?.BeforePump();
        _decisionJournal?.Tick();
    }

    internal AutomataDiagnosticsRuntimeEvidence CaptureDiagnostics()
    {
        if (_disposed || !_attached || _pump is null || _roster is null)
            return AutomataDiagnosticsRuntimeEvidence.Unavailable(
                "The automation runtime is not active, so recent event and journal evidence is unavailable.");

        HostTraceSnapshot? hostTrace = null;
        var unavailable = string.Empty;
        try
        {
            var source = _pump.SemanticTrace;
            if (source is null)
            {
                unavailable = "The recent-event buffer is unavailable.";
            }
            else
            {
                if (source.EmissionFaulted)
                    unavailable = "The recent-event buffer stopped recording when its trace threw: " +
                        source.EmissionFaultCause;
                hostTrace = HostTraceSnapshotWriter.Capture(
                    source,
                    new FullTraceSessionId(checked((ulong)Interlocked.Increment(ref _nextSnapshotIdentity))),
                    _serviceCapacity,
                    _roster);
            }
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            unavailable = GameActionFaultLog.Reason(
                "The recent-event buffer could not be captured.", exception, "recent-event capture").Text;
        }

        DecisionJournalStatus journal;
        try
        {
            journal = _decisionJournal?.Flush() ?? DecisionJournalStatus.Unavailable;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            journal = _decisionJournal?.Snapshot ?? DecisionJournalStatus.Unavailable;
            unavailable = Join(unavailable, GameActionFaultLog.Reason(
                "The decision journal could not be flushed.", exception, "decision-journal flush").Text);
        }

        return new AutomataDiagnosticsRuntimeEvidence(
            hostTrace,
            journal,
            AutomataDecisionJournalPathPolicy.DirectoryPath,
            unavailable);
    }

    internal void AfterPump()
    {
        _fullTrace?.AfterPump();
#if SERVICE_CYCLE_PROFILE
        _profile?.AfterPump();
#endif
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
#if SERVICE_CYCLE_PROFILE
        _profile?.Dispose();
#endif
        _fullTrace?.Dispose();
#if SERVICE_CYCLE_PROFILE
        _heapCounters?.Dispose();
#endif
    }

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : left + " " + right;
}
