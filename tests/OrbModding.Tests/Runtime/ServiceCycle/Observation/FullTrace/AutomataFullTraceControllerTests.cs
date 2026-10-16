using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace;

public sealed class AutomataFullTraceControllerTests
{
    private static readonly TimeSpan Deadline = ServiceCycleTestDeadline.Value;
    private static readonly ServiceCycleTraceRoster TestRoster = new(new[]
    {
        new ServiceCycleTraceRosterEntry(
            ServiceCycleTraceRoster.ServiceKind,
            1,
            "orbautomata.auto-harvest",
            "Auto Harvest"),
    });

    [Fact]
    public void AutomaticProfilingTraceWritesRosterAndShutdownManifest()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemoryStorage();
        var sessions = new SessionSource(storage);
        var options = new AutomataFullTraceOptions(sessions);
        var controller = AutomataFullTraceController.Create(
            pump, 1, TestRoster, in options, new ManualLogSource());

        controller.StartAutomatically();
        AdvanceTo(controller, FullTraceRuntimeSessionState.Recording);
        Assert.Equal(1, sessions.CreateCount);
        Assert.True(storage.SideArtifacts.TryGetValue(TraceRosterFormat.FileName, out var written));
        var roster = TraceRosterFormat.Decode(Encoding.UTF8.GetString(written!));
        Assert.Equal(1, roster.Count);
        Assert.Equal("Auto Harvest", roster[0].DisplayName);

        pump.PumpFrame(1);
        controller.AfterPump();
        controller.Dispose();

        Assert.True(storage.ManifestCommitted.IsSet);
        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(FullTraceTerminalReason.RuntimeShutdown, manifest.Reason);
        Assert.True(manifest.WrittenRecords > 0);
    }

    [Fact]
    public void AutomaticTraceCapturesTheSameFrameEmergencyTransition()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemoryStorage();
        var options = new AutomataFullTraceOptions(new SessionSource(storage));
        var controller = AutomataFullTraceController.Create(
            pump, 1, TestRoster, in options, new ManualLogSource());
        controller.StartAutomatically();
        AdvanceTo(controller, FullTraceRuntimeSessionState.Recording);

        controller.BeforePump();
        pump.SetEmergencyStop(true);
        pump.PumpFrame(1);
        controller.AfterPump();
        controller.Dispose();

        Assert.True(storage.ManifestCommitted.IsSet);
        var segment = FullTraceSegmentCodec.Decode(Assert.Single(storage.Segments));
        Assert.Contains(segment.Events, item =>
            item.Kind == ServiceCycleSemanticEventKind.EmergencyEntered);
    }

    /// <summary>
    /// A 43-minute capture said nothing about itself anywhere in the log: no start, no stop, and the
    /// completeness line is reported from a tick that shutdown has none of. Correlating that trace to
    /// the log it belongs beside took two independent clock anchors and an mtime. The closing line is
    /// the same terminal line every other ending gets, because the session now waits for its writer
    /// instead of announcing a manifest it only hoped would follow.
    /// </summary>
    [Fact]
    public void TheSessionNamesItselfWhenItStartsAndWhenItCloses()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemoryStorage();
        var log = new ManualLogSource();
        var options = new AutomataFullTraceOptions(new SessionSource(storage));
        var controller = AutomataFullTraceController.Create(pump, 1, TestRoster, in options, log);

        controller.StartAutomatically();
        AdvanceTo(controller, FullTraceRuntimeSessionState.Recording);
        pump.PumpFrame(1);
        controller.AfterPump();
        controller.Dispose();

        var lines = new List<string>();
        foreach (var entry in log.Entries) lines.Add(entry?.ToString() ?? string.Empty);
        Assert.Contains(
            lines,
            line => line.Contains("Profiling full trace session-0000000000000065 started", StringComparison.Ordinal));
        Assert.Contains(
            lines,
            line => line.Contains(
                "Profiling full trace ended complete: session-0000000000000065 | reason=RuntimeShutdown",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// A drain that outlives its bound is said out loud, and nothing is written to cover for it.
    /// </summary>
    /// <remarks>
    /// The bound keeps a wedged writer from holding the host's quit, so the ending it cannot reach
    /// has to be reported rather than assumed. The session directory is left without a manifest,
    /// which is what an interrupted capture has always looked like — the line below is what stops a
    /// reader having to notice the absence on their own.
    /// </remarks>
    [Fact]
    public void ADrainThatOutlivesItsBoundIsReportedAndPublishesNoManifest()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemoryStorage(blockSegments: true);
        var log = new ManualLogSource();
        var options = new AutomataFullTraceOptions(new SessionSource(storage));
        var controller = AutomataFullTraceController.Create(pump, 1, TestRoster, in options, log);

        controller.StartAutomatically();
        AdvanceTo(controller, FullTraceRuntimeSessionState.Recording);
        pump.PumpFrame(1);
        controller.AfterPump();
        controller.Dispose();

        Assert.Null(storage.Manifest);
        var lines = new List<string>();
        foreach (var entry in log.Entries) lines.Add(entry?.ToString() ?? string.Empty);
        Assert.Contains(
            lines,
            line => line.Contains(
                "Profiling full trace did not finish its shutdown drain: session-0000000000000065",
                StringComparison.Ordinal));
        Assert.Contains(
            lines,
            line => line.Contains(
                "read it as interrupted unless its manifest is present",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ProfilingTraceStartFailureIsContainedAndNeverRetried()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemoryStorage();
        var sessions = new SessionSource(storage, failFirst: true);
        var options = new AutomataFullTraceOptions(sessions);
        using var controller = AutomataFullTraceController.Create(
            pump, 1, TestRoster, in options, new ManualLogSource());

        controller.StartAutomatically();
        controller.StartAutomatically();

        Assert.Equal(1, sessions.CreateCount);
        Assert.True(pump.PumpFrame(1).Accepted);
        Assert.Null(storage.Manifest);
    }

    [Fact]
    public void RelativeArtifactPathNeverIncludesTheMachineRoot()
    {
        Assert.Equal(
            "BepInEx/config/OrbOfCreation-ModSuite/trace/" + AutomataTraceRunRoot.RunName +
                "/full/session-0000000000000001",
            AutomataFullTracePathPolicy.FormatRelativeArtifactPath("session-0000000000000001"));
        Assert.Throws<ArgumentException>(() =>
            AutomataFullTracePathPolicy.FormatRelativeArtifactPath("private/session"));
    }

    private static void AdvanceTo(
        AutomataFullTraceController controller,
        FullTraceRuntimeSessionState expected)
    {
        Assert.True(
            SpinWait.SpinUntil(() =>
            {
                controller.BeforePump();
                controller.AfterPump();
                return controller.Snapshot.State == expected;
            }, Deadline),
            $"Expected {expected}; observed {controller.Snapshot.State}.");
    }

    private static ServiceCycleRegistry Registry(IMonotonicClock clock)
    {
        var registry = new ServiceCycleRegistry(1, clock);
        registry.Register(
            new ExecutionServiceDefinition("auto-harvest.full-trace")
            {
                StartDecision = ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.AfterDecision(new MonotonicDuration(1))),
            },
            new LifecycleGeneration(1));
        registry.Seal();
        return registry;
    }

    private sealed class SessionSource : IAutomataFullTraceSessionSource
    {
        private readonly ISegmentSessionStorage _storage;
        private readonly bool _failFirst;

        internal SessionSource(ISegmentSessionStorage storage, bool failFirst = false)
        {
            _storage = storage;
            _failFirst = failFirst;
        }

        internal int CreateCount { get; private set; }

        public AutomataFullTraceSessionSpec Create()
        {
            CreateCount++;
            if (_failFirst && CreateCount == 1)
                throw new InvalidOperationException("Injected session creation failure.");
            var session = new FullTraceSessionId((ulong)(100 + CreateCount));
            return new AutomataFullTraceSessionSpec(
                session,
                new ServiceCycleTraceSessionId((ulong)(200 + CreateCount)),
                _storage,
                "session-" + session.Value.ToString("x16"));
        }
    }

    private sealed class MemoryStorage : ISegmentSessionStorage, ISessionSideArtifactSink, IDisposable
    {
        private readonly bool _blockSegments;

        internal MemoryStorage(bool blockSegments = false) => _blockSegments = blockSegments;

        internal ManualResetEventSlim ManifestCommitted { get; } = new();
        internal ManualResetEventSlim ReleaseSegments { get; } = new();
        internal List<byte[]> Segments { get; } = new();
        internal byte[]? Manifest { get; private set; }
        internal Dictionary<string, byte[]> SideArtifacts { get; } = new(StringComparer.Ordinal);

        public void Initialize() { }

        public void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes) =>
            SideArtifacts[name] = bytes.ToArray();

        public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes)
        {
            if (_blockSegments) ReleaseSegments.Wait();
            Assert.Equal(Segments.Count, ordinal);
            Segments.Add(bytes.ToArray());
        }

        public void CommitManifest(ReadOnlySpan<byte> bytes)
        {
            Manifest = bytes.ToArray();
            ManifestCommitted.Set();
        }

        public void Dispose()
        {
            ReleaseSegments.Set();
            ManifestCommitted.Dispose();
        }
    }
}
