using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace;

public sealed class FullTraceRuntimeSessionTests
{
    private static readonly TimeSpan Deadline = ServiceCycleTestDeadline.Value;

    [Fact]
    public void IdleSessionOwnsNoStorageAndStartArmsUntilWorkerInitializationCompletes()
    {
        using var registry = Registry("trace.session.initialization");
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemorySessionStorage(blockInitialization: true);
        using var session = new FullTraceRuntimeSession(pump, 1);

        Assert.Equal(FullTraceRuntimeSessionState.Idle, session.Snapshot.State);
        Assert.False(storage.InitializeEntered.IsSet);

        session.Start(new FullTraceSessionId(601), new ServiceCycleTraceSessionId(602), storage);
        Assert.True(storage.InitializeEntered.Wait(Deadline));
        session.Tick();
        Assert.Equal(FullTraceRuntimeSessionState.Arming, session.Snapshot.State);

        storage.InitializeRelease.Set();
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);
    }

    [Fact]
    public void UserStopDetachesAndPublishesACompleteDiagnosticSession()
    {
        using var registry = Registry("trace.session.complete");
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemorySessionStorage();
        using var session = new FullTraceRuntimeSession(pump, 1);
        session.Start(new FullTraceSessionId(701), new ServiceCycleTraceSessionId(702), storage);
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);

        pump.PumpFrame(1);
        session.RequestStop();
        AdvanceTo(session, FullTraceRuntimeSessionState.Complete);

        var snapshot = session.Snapshot;
        var segment = FullTraceSegmentCodec.Decode(Assert.Single(storage.Segments));
        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.True(snapshot.ManifestCommitted);
        Assert.Equal(1, snapshot.SegmentCount);
        Assert.Equal(segment.Events.Length, snapshot.WrittenRecords);
        Assert.Contains(segment.Events, item => item.Kind == ServiceCycleSemanticEventKind.PumpCompleted);
        Assert.Equal(FullTraceCompleteness.Complete, manifest.Completeness);
        Assert.Equal(FullTraceTerminalReason.UserStopped, manifest.Reason);
    }

    [Fact]
    public void SegmentWriteFailureStopsOnlyTracingAndPublishesTheDurablePrefix()
    {
        using var registry = Registry("trace.session.write-failure");
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemorySessionStorage(failSegmentWrite: true);
        using var session = new FullTraceRuntimeSession(pump, 1);
        session.Start(new FullTraceSessionId(801), new ServiceCycleTraceSessionId(802), storage);
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);

        pump.PumpFrame(1);
        session.RequestStop();
        AdvanceTo(session, FullTraceRuntimeSessionState.Incomplete);

        Assert.True(pump.PumpFrame(2).Accepted);
        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(FullTraceTerminalReason.WriteFailed, manifest.Reason);
        Assert.Equal(0UL, manifest.WrittenRecords);
        Assert.Equal(1UL, manifest.FirstIncompleteTransportSequence);
    }

    /// <summary>
    /// A shutdown that reached the recorder mid-cycle ends the recording early and loses nothing, and
    /// the manifest says exactly that: the reason names the shutdown, the completeness answers for
    /// the records.
    /// </summary>
    /// <remarks>
    /// Completeness is about loss. Stamping a shutdown incomplete on the strength of the shutdown
    /// alone cost one 43-minute capture its standing: 486,377 accepted records, 486,377 durable, and
    /// a manifest naming 486,378 as the first record missing — a truncation that had not happened,
    /// which is indistinguishable from one that had until somebody reconciles the two counts by hand.
    /// </remarks>
    [Fact]
    public void ShutdownDuringACycleNamesTheShutdownWithoutClaimingLossAndReleasesPumpOwnership()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.session.shutdown") { ActionCount = 0 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        using var firstStorage = new MemorySessionStorage();
        using var session = new FullTraceRuntimeSession(pump, 1);
        session.Start(new FullTraceSessionId(901), new ServiceCycleTraceSessionId(902), firstStorage);
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);

        var frame = 1L;
        PumpUntil(pump, ref frame, report => report.CyclesStarted != 0, "capture admission");
        session.Dispose();

        Assert.True(session.ShutdownDrainFinished);
        Assert.Equal(FullTraceRuntimeSessionState.Complete, session.Snapshot.State);
        Assert.True(firstStorage.ManifestPublished.IsSet);
        var interrupted = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(firstStorage.Manifest));
        Assert.Equal(FullTraceCompleteness.Complete, interrupted.Completeness);
        Assert.Equal(FullTraceTerminalReason.RuntimeShutdown, interrupted.Reason);
        Assert.Equal(interrupted.AcceptedRecords, interrupted.WrittenRecords);
        Assert.Equal(0UL, interrupted.FirstIncompleteTransportSequence);

        definition.StartDecision = ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.NotReady,
            WakePolicy.AfterDecision(new MonotonicDuration(1)));
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        PumpUntil(pump, ref frame, report => report.ResponsesAcquired != 0, "response acquisition");
        PumpUntil(
            pump,
            ref frame,
            _ => registration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty,
            "cycle release");
        using var secondStorage = new MemorySessionStorage();
        using var next = new FullTraceRuntimeSession(pump, 1);
        next.Start(new FullTraceSessionId(903), new ServiceCycleTraceSessionId(904), secondStorage);
        AdvanceTo(next, FullTraceRuntimeSessionState.Recording);
        next.RequestStop();
        AdvanceTo(next, FullTraceRuntimeSessionState.Complete);
    }

    private static void AdvanceTo(
        FullTraceRuntimeSession session,
        FullTraceRuntimeSessionState expected)
    {
        Assert.True(
            SpinWait.SpinUntil(() =>
            {
                session.Tick();
                return session.Snapshot.State == expected;
            }, Deadline),
            $"Expected {expected}; observed {session.Snapshot.State}.");
    }

    private static SuiteFramePumpReport PumpUntil(
        SuiteFramePump pump,
        ref long frame,
        Func<SuiteFramePumpReport, bool> predicate,
        string transition)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var report = pump.PumpFrame(frame++);
            if (predicate(report)) return report;
        }
        throw new Xunit.Sdk.XunitException("Timed out waiting for " + transition + ".");
    }

    private static ServiceCycleRegistry Registry(string serviceId)
    {
        var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        registry.Register(
            new ExecutionServiceDefinition(serviceId)
            {
                StartDecision = ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.AfterDecision(new MonotonicDuration(1))),
            },
            new LifecycleGeneration(1));
        registry.Seal();
        return registry;
    }

    [Fact]
    public void ARecordingSessionStoresEachPublicationGenerationItSeesExactlyOnce()
    {
        using var registry = Registry("trace.session.stores");
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemorySessionStorage();
        using var session = new FullTraceRuntimeSession(pump, 1);
        session.Start(new FullTraceSessionId(901), new ServiceCycleTraceSessionId(902), storage);
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);

        pump.PumpFrame(1);
        session.Tick();
        pump.PumpFrame(2);
        session.Tick();

        Assert.Equal(
            new[] { "configuration-0000000000000001.oscv", "strategy-0000000000000001.oscv" },
            Ordered(storage.SideArtifacts.Keys));
        var configuration = Encoding.UTF8.GetString(storage.SideArtifacts["configuration-0000000000000001.oscv"]);
        Assert.StartsWith("OSCV 1 configuration 0000000000000001\n", configuration, StringComparison.Ordinal);
        Assert.Contains("General.Enabled = false", configuration, StringComparison.Ordinal);
        Assert.Equal(2, session.Snapshot.StoreCount);
        Assert.False(session.Snapshot.StoresLost);
    }

    /// <summary>
    /// A store that cannot be written costs a reader the settings behind a generation, not the
    /// events. The session finishes, and its completion record says the stores were lost — the
    /// failure used to live in a private flag nothing read, so the artifact reported itself whole
    /// while missing every file a decision's generation pointed at.
    /// </summary>
    [Fact]
    public void ALostPublicationStoreIsRecordedOnTheCompletedSessionAndStopsFurtherStoreWrites()
    {
        using var registry = Registry("trace.session.store-failure");
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemorySessionStorage(failSideArtifactWrite: true);
        using var session = new FullTraceRuntimeSession(pump, 1);
        session.Start(new FullTraceSessionId(1001), new ServiceCycleTraceSessionId(1002), storage);
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);

        pump.PumpFrame(1);
        session.Tick();
        pump.PumpFrame(2);
        session.Tick();

        Assert.True(session.Snapshot.StoresLost);
        Assert.Equal(0, session.Snapshot.StoreCount);
        Assert.Equal(1, storage.SideArtifactAttempts);

        session.RequestStop();
        AdvanceTo(session, FullTraceRuntimeSessionState.Complete);

        var snapshot = session.Snapshot;
        Assert.True(snapshot.ManifestCommitted);
        Assert.True(snapshot.StoresLost);
        Assert.Equal(1, storage.SideArtifactAttempts);
        Assert.Empty(storage.SideArtifacts);
    }

    private static string[] Ordered(IEnumerable<string> names)
    {
        var ordered = new List<string>(names);
        ordered.Sort(StringComparer.Ordinal);
        return ordered.ToArray();
    }

    private sealed class MemorySessionStorage : ISegmentSessionStorage, ISessionSideArtifactSink, IDisposable
    {
        private readonly bool _blockInitialization;
        private readonly bool _failSegmentWrite;
        private readonly bool _failSideArtifactWrite;

        internal MemorySessionStorage(
            bool blockInitialization = false,
            bool failSegmentWrite = false,
            bool failSideArtifactWrite = false)
        {
            _blockInitialization = blockInitialization;
            _failSegmentWrite = failSegmentWrite;
            _failSideArtifactWrite = failSideArtifactWrite;
        }

        internal ManualResetEventSlim InitializeEntered { get; } = new();
        internal ManualResetEventSlim InitializeRelease { get; } = new();
        internal ManualResetEventSlim ManifestPublished { get; } = new();
        internal List<byte[]> Segments { get; } = new();
        internal Dictionary<string, byte[]> SideArtifacts { get; } = new(StringComparer.Ordinal);
        internal int SideArtifactAttempts { get; private set; }
        internal byte[]? Manifest { get; private set; }

        public void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes)
        {
            SideArtifactAttempts++;
            if (_failSideArtifactWrite) throw new InvalidOperationException("Injected side-artifact failure.");
            SideArtifacts[name] = bytes.ToArray();
        }

        public void Initialize()
        {
            InitializeEntered.Set();
            if (_blockInitialization) InitializeRelease.Wait();
        }

        public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes)
        {
            if (_failSegmentWrite) throw new InvalidOperationException("Injected segment write failure.");
            Assert.Equal(Segments.Count, ordinal);
            Segments.Add(bytes.ToArray());
        }

        public void CommitManifest(ReadOnlySpan<byte> bytes)
        {
            Manifest = bytes.ToArray();
            ManifestPublished.Set();
        }

        public void Dispose()
        {
            InitializeEntered.Dispose();
            InitializeRelease.Dispose();
            ManifestPublished.Dispose();
        }
    }
}
