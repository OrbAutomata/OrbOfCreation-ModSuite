#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;
using System.Threading;
using BepInEx.Logging;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbAutomata.Runtime.ServiceCycle.Profile;

internal sealed class AutomataServiceCycleProfileController : IDisposable
{
    private static long _nextIdentity = DateTime.UtcNow.Ticks;

    private readonly ServiceCycleProfileProbe _probe = new();
    private readonly IMonotonicClock _clock;
    private readonly bool _traceActive;
    private readonly ManualLogSource _log;
    private readonly PerformanceProfileControlRegistration _control;
    private ServiceCycleProfileRuntimeSession? _session;
    private MonotonicTimestamp _startedAt;
    private TimeSpan _terminalDuration;
    private PerformanceProfileControlStatus _lastPublished = PerformanceProfileControlStatus.Idle;
    private string _artifactName = string.Empty;
    private bool _stopRequested;
    private bool _terminalDurationSet;
    private bool _terminalLogged;
    private bool _failed;
    private bool _disposed;

    private AutomataServiceCycleProfileController(
        IMonotonicClock clock,
        bool traceActive,
        ManualLogSource log,
        PerformanceProfileControlRegistration control)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _traceActive = traceActive;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    internal ServiceCycleProfileProbe Probe => _probe;

    internal static AutomataServiceCycleProfileController? TryCreate(
        IMonotonicClock clock,
        bool traceActive,
        ManualLogSource log,
        PerformanceProfileControlRegistry controls)
    {
        if (controls is null) throw new ArgumentNullException(nameof(controls));
        if (!controls.TryRegister(out var control) || control is null)
        {
            log.LogWarning("ServiceCycle performance-profile controls are already registered.");
            return null;
        }

        try
        {
            return new AutomataServiceCycleProfileController(clock, traceActive, log, control);
        }
        catch
        {
            control.Dispose();
            throw;
        }
    }

    internal void AfterPump()
    {
        if (_disposed || _failed) return;
        try
        {
            if (_control.TryTakeCommand(out var command)) Apply(command);
            if (_session is null) return;
            if (!_stopRequested && _probe.Fault != ServiceCycleProfileProbeFault.None)
                Stop(ServiceCycleProfileTerminalReason.ProbeFailed);
            PublishSession();
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            _failed = true;
            _log.LogWarning(
                $"ServiceCycle performance-profile control failed: {exception.GetBaseException().Message}");
        }
    }

    internal void StartAutomatically()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleProfileController));
        if (_failed) return;
        Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            var session = _session;
            if (session is not null && !_stopRequested)
                Stop(ServiceCycleProfileTerminalReason.RuntimeShutdown);
            session?.Dispose();
            if (session is not null) ReportShutdown(session);
        }
        finally
        {
            _control.Dispose();
        }
    }

    /// <summary>
    /// Says whether the session that ended at shutdown is readable, once its writer has been waited
    /// for.
    /// </summary>
    /// <remarks>
    /// A profiling session writes everything it has during its stop, and the stop line above promises
    /// only that the writer started. The manifest is what makes the session readable, so the one word
    /// worth saying at shutdown is whether it exists.
    /// </remarks>
    private void ReportShutdown(ServiceCycleProfileRuntimeSession session)
    {
        if (_terminalLogged) return;
        _terminalLogged = true;
        if (session.ManifestCommitted)
        {
            _log.LogInfo(
                $"ServiceCycle performance profile {_artifactName} is durable at shutdown " +
                $"({session.Snapshot.WrittenRecords} records, {session.Snapshot.BytesWritten} bytes); " +
                "files are under " + AutomataTraceRunRoot.FormatRelativePath("profile") + "/.");
            return;
        }
        _log.LogWarning(
            $"ServiceCycle performance profile {_artifactName} published no manifest within " +
            $"{BufferedSegmentShutdown.DrainBound.TotalSeconds}s of shutdown; its session directory " +
            "must be read as interrupted.");
    }

    private void Apply(PerformanceProfileCommand command)
    {
        if (command == PerformanceProfileCommand.Stop)
        {
            Stop(ServiceCycleProfileTerminalReason.UserStopped);
            return;
        }
        if (command != PerformanceProfileCommand.Start)
            throw new InvalidOperationException("The performance-profile command is invalid.");
        Start();
    }

    private void Start()
    {
        _session?.Dispose();
        _session = null;
        _artifactName = string.Empty;
        _terminalDuration = TimeSpan.Zero;
        _terminalDurationSet = false;
        _terminalLogged = false;
        _stopRequested = false;
        _startedAt = _clock.Now;

        try
        {
            var sessionId = new ServiceCycleProfileSessionId(
                checked((ulong)Interlocked.Increment(ref _nextIdentity)));
            var root = AutomataTraceRunRoot.Child("profile");
            var artifactName = "session-" +
                sessionId.Value.ToString("x16", CultureInfo.InvariantCulture);
            var storage = new AtomicSegmentSessionStorage(
                root,
                artifactName,
                ".osps",
                "manifest.ospm");
            _artifactName = storage.ArtifactName;
            _session = new ServiceCycleProfileRuntimeSession(
                storage,
                sessionId,
                _clock,
                typeof(AutomataServiceCycleProfileController).Assembly.ManifestModule.ModuleVersionId,
                _traceActive,
                _probe);
            Publish(Status(PerformanceProfileControlState.Recording, PerformanceProfileResult.None));
            _log.LogInfo($"ServiceCycle performance profile {_artifactName} started.");
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            _session?.Dispose();
            _session = null;
            Publish(Status(
                PerformanceProfileControlState.Faulted,
                PerformanceProfileResult.InitializationFailed));
            _log.LogWarning(
                $"ServiceCycle performance profile could not start: {exception.GetBaseException().Message}");
        }
    }

    private void Stop(ServiceCycleProfileTerminalReason reason)
    {
        if (_session is null || _stopRequested) return;
        _stopRequested = true;
        _session.Stop(reason);
        Publish(Status(PerformanceProfileControlState.Stopping, PerformanceProfileResult.None));
        _log.LogInfo(
            $"ServiceCycle performance profile {_artifactName} stop requested; " +
            "the writer is finishing in the background.");
    }

    private void PublishSession()
    {
        var session = _session ?? throw new InvalidOperationException("No performance-profile session is active.");
        var snapshot = session.Snapshot;
        var state = snapshot.State switch
        {
            ServiceCycleProfileSinkState.Initializing or ServiceCycleProfileSinkState.Running =>
                _stopRequested ? PerformanceProfileControlState.Stopping : PerformanceProfileControlState.Recording,
            ServiceCycleProfileSinkState.Stopping => PerformanceProfileControlState.Stopping,
            ServiceCycleProfileSinkState.Stopped when
                session.ManifestCommitted && session.TerminalReason == ServiceCycleProfileTerminalReason.UserStopped =>
                PerformanceProfileControlState.Complete,
            ServiceCycleProfileSinkState.Stopped or ServiceCycleProfileSinkState.Faulted =>
                PerformanceProfileControlState.Faulted,
            _ => throw new InvalidOperationException("The performance-profile sink state is invalid."),
        };
        var result = state switch
        {
            PerformanceProfileControlState.Complete => PerformanceProfileResult.UserStopped,
            PerformanceProfileControlState.Faulted => MapResult(session),
            _ => PerformanceProfileResult.None,
        };
        Publish(Status(state, result));

        if (_terminalLogged || state is not (PerformanceProfileControlState.Complete or PerformanceProfileControlState.Faulted))
            return;
        _terminalLogged = true;
        if (state == PerformanceProfileControlState.Complete)
        {
            _log.LogInfo(
                $"ServiceCycle performance profile {_artifactName} is durable " +
                $"({snapshot.WrittenRecords} records, {snapshot.BytesWritten} bytes); files are under " +
                AutomataTraceRunRoot.FormatRelativePath("profile") + "/.");
        }
        else
        {
            _log.LogWarning($"ServiceCycle performance profile {_artifactName} ended with {result}.");
        }
    }

    private PerformanceProfileControlStatus Status(
        PerformanceProfileControlState state,
        PerformanceProfileResult result)
    {
        var snapshot = _session?.Snapshot;
        var terminal = state is PerformanceProfileControlState.Complete or PerformanceProfileControlState.Faulted;
        return new PerformanceProfileControlStatus(
            state,
            Elapsed(terminal),
            snapshot?.WrittenRecords ?? 0,
            snapshot?.BytesWritten ?? 0,
            result,
            _artifactName);
    }

    private TimeSpan Elapsed(bool terminal)
    {
        if (_terminalDurationSet) return _terminalDuration;
        var elapsed = (_clock.Now - _startedAt).ToTimeSpan();
        if (terminal)
        {
            _terminalDuration = elapsed;
            _terminalDurationSet = true;
            return elapsed;
        }
        return TimeSpan.FromTicks(
            elapsed.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
    }

    private void Publish(PerformanceProfileControlStatus status)
    {
        if (status == _lastPublished) return;
        _lastPublished = status;
        _control.Publish(status);
    }

    private static PerformanceProfileResult MapResult(ServiceCycleProfileRuntimeSession session)
    {
        if (!session.ManifestCommitted) return PerformanceProfileResult.WriteFailed;
        return session.TerminalReason switch
        {
            ServiceCycleProfileTerminalReason.RuntimeShutdown => PerformanceProfileResult.RuntimeShutdown,
            ServiceCycleProfileTerminalReason.BufferExhausted => PerformanceProfileResult.BufferExhausted,
            ServiceCycleProfileTerminalReason.SequenceExhausted => PerformanceProfileResult.SequenceExhausted,
            ServiceCycleProfileTerminalReason.WriteFailed => PerformanceProfileResult.WriteFailed,
            ServiceCycleProfileTerminalReason.ProbeFailed => PerformanceProfileResult.ProbeFailed,
            _ => throw new InvalidOperationException("The performance profile has no failure result."),
        };
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is StackOverflowException or OutOfMemoryException or AccessViolationException;
}
#endif
