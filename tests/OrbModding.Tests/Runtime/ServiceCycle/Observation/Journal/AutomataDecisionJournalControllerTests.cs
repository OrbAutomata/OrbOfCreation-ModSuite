using System;
using System.Threading;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;
using OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class AutomataDecisionJournalControllerTests
{
    private static readonly TimeSpan Deadline = ServiceCycleTestDeadline.Value;

    /// <remarks>
    /// The writer publishes a segment's records, then its bytes, then the segment itself, as separate
    /// counters, so only the segment count says the others already include it.
    /// </remarks>
    [Fact]
    public void ControllerRecordsAndPublishesTransportStatus()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var status = new DecisionJournalStatusRegistry();
        var source = new Source(storage);
        var options = Options(status, source);
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(
                pump,
                in options,
                new ManualLogSource()));
        using var teardown = new JournalTeardown(controller);
        AdvanceTo(controller, status, DecisionJournalStatusState.Recording);

        Assert.True(pump.PumpFrame(1).Accepted);
        ServiceCycleTestDeadline.For(
            () =>
            {
                controller.Tick();
                return status.Status.WrittenSegments > 0;
            },
            "a durable journal segment");

        Assert.Equal("journal", status.Status.ArtifactName);
        Assert.True(status.Status.AcceptedRecords > 0);
        Assert.True(status.Status.WrittenRecords > 0);
        Assert.True(status.Status.BytesWritten > 0);
        Assert.NotEmpty(storage.ReadRecords());
        Assert.Equal(1, source.CreateCount);

        Assert.True(controller.DisposeWithPump());
        Assert.True(pump.IsDisposed);
        Assert.Equal(DecisionJournalStatusState.Unavailable, status.Status.State);
    }

    [Fact]
    public void SynchronousJournalFailureLeavesThePumpOperational()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        var status = new DecisionJournalStatusRegistry();
        var source = new Source();
        var options = Options(status, source);
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(
                pump,
                in options,
                new ManualLogSource()));

        Assert.Equal(DecisionJournalStatusState.Faulted, status.Status.State);
        Assert.Equal(DecisionJournalStatusResult.InitializationFailed, status.Status.Result);
        Assert.True(pump.PumpFrame(1).Accepted);
        Assert.Equal(1, source.CreateCount);
        Assert.False(controller.DisposeWithPump());
        pump.Dispose();
    }

    [Fact]
    public void ExistingProducerPreventsASecondStorageClaim()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var status = new DecisionJournalStatusRegistry();
        using var existing = status.Register();
        var source = new Source(storage);
        var options = Options(status, source);

        var controller = AutomataDecisionJournalController.TryCreate(
            pump,
            in options,
            new ManualLogSource());

        Assert.Null(controller);
        Assert.Equal(0, source.CreateCount);
        Assert.True(pump.PumpFrame(1).Accepted);
    }

    [Fact]
    public void BackgroundWriteFailureDetachesOnlyTheJournal()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage(failCommit: true);
        var status = new DecisionJournalStatusRegistry();
        var options = Options(status, new Source(storage));
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(
                pump,
                in options,
                new ManualLogSource()));
        using var teardown = new JournalTeardown(controller);
        AdvanceTo(controller, status, DecisionJournalStatusState.Recording);

        Assert.True(pump.PumpFrame(1).Accepted);
        ServiceCycleTestDeadline.ForSignal(storage.CommitEntered, "a journal storage commit");
        AdvanceTo(controller, status, DecisionJournalStatusState.Faulted);

        Assert.Equal(DecisionJournalStatusResult.WriteFailed, status.Status.Result);
        Assert.True(pump.PumpFrame(2).Accepted);
        Assert.True(controller.DisposeWithPump());
    }

    /// <summary>
    /// A journal that died writing says what the storage said. "Write failed" alone left a full
    /// disk and a revoked permission looking the same on the status page.
    /// </summary>
    [Fact]
    public void ABackgroundWriteFailureNamesWhatTheStorageSaid()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage(failCommit: true);
        var status = new DecisionJournalStatusRegistry();
        var options = Options(status, new Source(storage));
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(
                pump,
                in options,
                new ManualLogSource()));
        using var teardown = new JournalTeardown(controller);
        AdvanceTo(controller, status, DecisionJournalStatusState.Recording);

        Assert.True(pump.PumpFrame(1).Accepted);
        AdvanceTo(controller, status, DecisionJournalStatusState.Faulted);

        Assert.Equal("DecisionJournalWriter", status.Status.FaultSite);
        Assert.Equal("Injected journal commit failure.", status.Status.FaultMessage);
    }

    /// <summary>
    /// A stopped journal reports the observation it died in and what it disagreed with.
    /// </summary>
    /// <remarks>
    /// The live incident reported nothing but "stopped after ProducerFailed", which is what every
    /// contained producer failure says. Without the site and the guard message the desync that
    /// killed it cannot be found from the log the player can actually send.
    /// </remarks>
    [Fact]
    public void AProducerFaultIsLoggedWithTheObservationAndTheGuardItViolated()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var status = new DecisionJournalStatusRegistry();
        var options = Options(status, new Source(storage));
        var log = new ManualLogSource();
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(pump, in options, log));
        using var teardown = new JournalTeardown(controller);
        var faulted = DecisionJournalObserverTestData.FaultedOnMismatchedResponse();
        var transport = Metrics(
            BufferedSegmentStatus.Faulted,
            BufferedSegmentFaultReason.ProducerFailed,
            firstIncompleteSequence: 2);
        var consumer = new DecisionJournalConsumerMetrics(
            retainedSegments: 1,
            startupPrunedSegments: 0,
            incompatibleSegmentsPruned: 0,
            staleTemporaryFilesRemoved: 0,
            evictedSegments: 0,
            DecisionJournalConsumerFaultReason.None);

        controller.Publish(new DecisionJournalRuntimeSnapshot(
            DecisionJournalRuntimeState.Faulted,
            attached: false,
            in transport,
            in consumer,
            faulted.FaultException,
            faulted.FaultSite));

        Assert.Equal("ResponseAcquired", status.Status.FaultSite);
        Assert.Equal(
            "Journal facts do not match the pending service cycle.",
            status.Status.FaultMessage);
        Assert.Contains(
            "stopped after ProducerFailed at ResponseAcquired: " +
            "Journal facts do not match the pending service cycle.",
            Assert.IsType<string>(Assert.Single(log.Entries)));
        Assert.True(controller.DisposeWithPump());
    }

    /// <summary>
    /// Discarding a store the journal could not continue is said once, loudly, and stays on screen.
    /// </summary>
    [Fact]
    public void AbandonedIncompatibleSegmentsAreReportedOnceWhenRecordingStarts()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage(
            recovery: new TraceSegmentStorageRecovery(0, 0, 0, 0, incompatibleSegmentsPruned: 6));
        var status = new DecisionJournalStatusRegistry();
        var options = Options(status, new Source(storage));
        var log = new ManualLogSource();
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(pump, in options, log));
        using var teardown = new JournalTeardown(controller);

        AdvanceTo(controller, status, DecisionJournalStatusState.Recording);
        controller.Tick();

        Assert.Equal(6, status.Status.IncompatibleSegmentsPruned);
        Assert.Equal(2, log.Entries.Count);
        Assert.Contains(
            "discarded 6 incompatible segments it could not continue from at " +
            "BepInEx/config/OrbOfCreation-ModSuite/trace/journal.",
            Assert.IsType<string>(log.Entries[1]));
        Assert.Same(DecisionJournalSegmentHeaderProbe.Instance, storage.Probe);
        Assert.True(controller.DisposeWithPump());
    }

    [Fact]
    public void ConsumerFailureIsPublishedOnlyAfterTransportFaults()
    {
        var runningTransport = Metrics(
            BufferedSegmentStatus.Running,
            BufferedSegmentFaultReason.None,
            firstIncompleteSequence: 0);
        var consumer = new DecisionJournalConsumerMetrics(
            retainedSegments: 1,
            startupPrunedSegments: 0,
            incompatibleSegmentsPruned: 0,
            staleTemporaryFilesRemoved: 0,
            evictedSegments: 0,
            DecisionJournalConsumerFaultReason.RetentionFailed);
        var pending = new DecisionJournalRuntimeSnapshot(
            DecisionJournalRuntimeState.Recording,
            attached: true,
            in runningTransport,
            in consumer);

        var pendingStatus = AutomataDecisionJournalController.MapStatus(in pending, "journal");

        Assert.Equal(DecisionJournalStatusState.Recording, pendingStatus.State);
        Assert.Equal(DecisionJournalStatusResult.None, pendingStatus.Result);
        Assert.Equal(0, pendingStatus.FirstIncompleteSequence);

        var faultingTransport = Metrics(
            BufferedSegmentStatus.Faulting,
            BufferedSegmentFaultReason.ProducerFailed,
            firstIncompleteSequence: 2);
        var faulting = new DecisionJournalRuntimeSnapshot(
            DecisionJournalRuntimeState.Stopping,
            attached: false,
            in faultingTransport,
            in consumer);

        var faultingStatus = AutomataDecisionJournalController.MapStatus(in faulting, "journal");

        Assert.Equal(DecisionJournalStatusState.Stopping, faultingStatus.State);
        Assert.Equal(DecisionJournalStatusResult.RetentionFailed, faultingStatus.Result);
        Assert.Equal(2, faultingStatus.FirstIncompleteSequence);
    }

    /// <summary>
    /// The always-on journal writes outside the per-launch run folder.
    /// </summary>
    /// <remarks>
    /// One directory is what the rolling segment cap and the restart reconciliation both govern.
    /// Inside a per-launch folder every launch received a fresh budget, so the cap capped nothing and
    /// reconciliation had no earlier segments to reconcile.
    /// </remarks>
    [Fact]
    public void TheJournalPathIsStableAcrossLaunchesAndNeverIncludesTheMachineRoot()
    {
        Assert.Equal(
            "BepInEx/config/OrbOfCreation-ModSuite/trace/journal",
            AutomataDecisionJournalPathPolicy.FormatRelativeArtifactPath("journal"));
        Assert.Throws<ArgumentException>(() =>
            AutomataDecisionJournalPathPolicy.FormatRelativeArtifactPath("private/journal"));
        Assert.Equal(10, AutomataDecisionJournalPathPolicy.LiveCandidateBlockCount);
        Assert.Equal(6_476, AutomataDecisionJournalPathPolicy.LiveCandidateMaximumCommittedSegments);
    }

    /// <summary>
    /// The retained journal fits inside its fixed 64 MiB envelope.
    /// </summary>
    /// <remarks>
    /// Asserted against the codec's own segment size rather than a copied number, so a format change
    /// that grows a segment fails here instead of quietly spending the budget it was tuned for.
    /// </remarks>
    [Fact]
    public void TheRetainedJournalCannotExceedTheSuitesOnDiskBudget()
    {
        var segmentBytes = DecisionJournalSegmentCodec.GetEncodedLength(
            DecisionJournalSegmentCodec.MaximumRecords);

        var retainedBytes = (long)segmentBytes *
            AutomataDecisionJournalPathPolicy.LiveCandidateMaximumCommittedSegments;
        var maximumTransitionBytes = retainedBytes + segmentBytes;

        Assert.Equal(10_360, segmentBytes);
        Assert.Equal(67_091_360L, retainedBytes);
        Assert.Equal(67_101_720L, maximumTransitionBytes);
        Assert.True(
            maximumTransitionBytes <= AutomataDecisionJournalPathPolicy.LiveCandidateMaximumBytes,
            "The retained journal plus one atomic-commit temporary must fit its 64 MiB envelope.");
    }

    [Fact]
    public void ProductionPathPolicyCreatesTheLiveCandidateExactlyOnce()
    {
        var options = AutomataDecisionJournalPathPolicy.Create(new DecisionJournalStatusRegistry());

        var spec = Assert.IsAssignableFrom<IAutomataDecisionJournalSource>(options.Source).Create();

        Assert.IsType<OrbModding.Common.Runtime.Tracing.FileTraceSegmentStorage>(spec.Storage);
        Assert.True(spec.Run.IsValid);
        Assert.Equal(10, spec.BlockCount);
        Assert.Equal(6_476, spec.MaximumCommittedSegments);
        Assert.Equal(
            MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1)),
            spec.CheckpointInterval);
        Assert.Throws<InvalidOperationException>(() => options.Source!.Create());
    }

    private static AutomataDecisionJournalOptions Options(
        DecisionJournalStatusRegistry status,
        IAutomataDecisionJournalSource source) => new(status, source, "journal");

    private static BufferedSegmentMetrics Metrics(
        BufferedSegmentStatus state,
        BufferedSegmentFaultReason fault,
        long firstIncompleteSequence) => new(
        state,
        fault,
        acceptedRecords: 1,
        writtenRecords: 1,
        discardedRecords: 0,
        bytesWritten: 512,
        sealedBlocks: 1,
        writtenBlocks: 1,
        discardedBlocks: 0,
        pendingBlocks: 0,
        peakPendingBlocks: 1,
        firstIncompleteSequence);

    private static void AdvanceTo(
        AutomataDecisionJournalController controller,
        DecisionJournalStatusRegistry status,
        DecisionJournalStatusState expected)
    {
        Assert.True(
            SpinWait.SpinUntil(() =>
            {
                controller.Tick();
                return status.Status.State == expected;
            }, Deadline),
            $"Expected {expected}; observed {status.Status.State}.");
    }

    private static ServiceCycleRegistry Registry(IMonotonicClock clock)
    {
        var registry = new ServiceCycleRegistry(1, clock);
        registry.Register(
            new ExecutionServiceDefinition("auto-harvest.decision-journal")
            {
                StartDecision = ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.AfterDecision(new MonotonicDuration(1))),
            },
            new LifecycleGeneration(1));
        registry.Seal();
        return registry;
    }

    /// <summary>
    /// Tears the pump down through the journal that owns it, whatever the body did.
    /// </summary>
    /// <remarks>
    /// A controller with a runtime claims the pump, so the pump's own <c>using</c> is a second
    /// owner. A body that failed before reaching <c>DisposeWithPump</c> left that dispose to find
    /// the journal still owning the pump; it threw from the unwind and replaced the assertion that
    /// actually failed with the ownership guard, which is how a starved background writer came to
    /// be reported as a disposal defect. Declared after the pump, this runs first.
    /// </remarks>
    private readonly struct JournalTeardown : IDisposable
    {
        private readonly AutomataDecisionJournalController _controller;

        internal JournalTeardown(AutomataDecisionJournalController controller) =>
            _controller = controller;

        public void Dispose() => _controller.DisposeWithPump();
    }

    private sealed class Source : IAutomataDecisionJournalSource
    {
        private readonly DecisionJournalRuntimeTestStorage? _storage;

        internal Source(DecisionJournalRuntimeTestStorage? storage = null) => _storage = storage;

        internal int CreateCount { get; private set; }

        public AutomataDecisionJournalSpec Create()
        {
            CreateCount++;
            if (_storage is null) throw new InvalidOperationException("Injected journal source failure.");
            return new AutomataDecisionJournalSpec(
                _storage,
                new DecisionJournalRunId(1),
                maximumCommittedSegments: 4,
                blockCount: 3,
                new MonotonicDuration(1));
        }
    }
}
