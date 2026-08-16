using System;
using BepInEx.Logging;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Observation.WorldCollection;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbAutomata;

/// <summary>The automatic detailed-trace companion to a profiling session.</summary>
internal sealed class AutomataFullTraceController : IDisposable
{
    private readonly FullTraceRuntimeSession _session;
    private readonly IAutomataFullTraceSessionSource _sessions;
    private readonly ManualLogSource _log;
    private string _artifactName = string.Empty;
    private bool _started;
    private bool _startFailed;
    private bool _terminalReported;
    private bool _disposed;

    private AutomataFullTraceController(
        SuiteFramePump pump,
        int serviceCapacity,
        ServiceCycleTraceRoster roster,
        IAutomataFullTraceSessionSource sessions,
        ManualLogSource log)
    {
        _session = new FullTraceRuntimeSession(
            pump,
            serviceCapacity,
            roster,
            WorldCollectionSpanRegistry.Shared);
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal static AutomataFullTraceController Create(
        SuiteFramePump pump,
        int serviceCapacity,
        ServiceCycleTraceRoster roster,
        in AutomataFullTraceOptions options,
        ManualLogSource log)
    {
        if (!options.Enabled || options.Sessions is null)
            throw new ArgumentException("Enabled profiling full-trace options are required.", nameof(options));
        return new AutomataFullTraceController(
            pump,
            serviceCapacity,
            roster,
            options.Sessions,
            log);
    }

    internal FullTraceRuntimeSessionSnapshot Snapshot => _session.Snapshot;

    internal void BeforePump() => Tick();

    internal void AfterPump() => Tick();

    internal void StartAutomatically()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataFullTraceController));
        if (_started || _startFailed) return;
        try
        {
            var spec = _sessions.Create();
            _artifactName = spec.ArtifactName;
            _session.Start(spec.Session, spec.SemanticSession, spec.Storage);
            _started = true;
            _log.LogAutomataInfo(
                "Profiling full trace " + _artifactName + " started at " +
                AutomataFullTracePathPolicy.FormatRelativeArtifactPath(_artifactName) + ".");
            Tick();
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            _startFailed = true;
            _log.LogAutomataError(
                "Profiling full trace could not start; profiling and gameplay continue: " +
                Describe(exception));
        }
    }

    /// <summary>
    /// Closes the session and says how it ended, behind the drain rather than in front of it.
    /// </summary>
    /// <remarks>
    /// The completeness line is reported from <see cref="Tick"/>, and shutdown is the one boundary no
    /// tick follows. This used to announce a manifest that "publishes behind this line" — a promise
    /// the writer kept only when it won a race against process exit, which is why a 43-minute capture
    /// ended without one word about itself anywhere in the log. The session now waits, bounded, for
    /// its own writer, so the same terminal line every other ending gets is available here too. A
    /// drain that outlives its bound is its own line: it is the one ending whose manifest may never
    /// arrive.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
        if (!_started || _startFailed || _terminalReported) return;
        if (!_session.ShutdownDrainFinished)
        {
            var pending = _session.Snapshot;
            _terminalReported = true;
            _log.LogAutomataError(
                "Profiling full trace did not finish its shutdown drain: " + _artifactName +
                " | bound=" + BufferedSegmentShutdown.DrainBound.TotalSeconds + "s" +
                " | records=" + pending.WrittenRecords + "/" + pending.AcceptedRecords +
                " | segments=" + pending.SegmentCount +
                "; read it as interrupted unless its manifest is present.");
            return;
        }
        Report(_session.Snapshot);
    }

    private void Tick()
    {
        if (_disposed || !_started || _startFailed) return;
        _session.Tick();
        Report(_session.Snapshot);
    }

    private void Report(in FullTraceRuntimeSessionSnapshot snapshot)
    {
        if (_terminalReported || snapshot.State is not (
                FullTraceRuntimeSessionState.Complete or FullTraceRuntimeSessionState.Incomplete))
            return;
        _terminalReported = true;
        if (snapshot.State == FullTraceRuntimeSessionState.Incomplete)
        {
            _log.LogAutomataError(
                "Profiling full trace ended incomplete: " + _artifactName +
                " | reason=" + snapshot.FaultReason +
                " | records=" + snapshot.WrittenRecords + "/" + snapshot.AcceptedRecords +
                " | first missing sequence=" + snapshot.FirstIncompleteSequence + ".");
            return;
        }
        _log.LogAutomataInfo(
            "Profiling full trace ended complete: " + _artifactName +
            " | reason=" + snapshot.TerminalReason +
            " | records=" + snapshot.WrittenRecords +
            " | segments=" + snapshot.SegmentCount + ".");
    }

    private static string Describe(Exception exception)
    {
        var message = exception.GetBaseException().Message?.Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }
}
