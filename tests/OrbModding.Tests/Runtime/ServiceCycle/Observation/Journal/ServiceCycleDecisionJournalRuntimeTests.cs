using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class ServiceCycleDecisionJournalRuntimeTests
{
    private static readonly TimeSpan Deadline = ServiceCycleTestDeadline.Value;

    [Fact]
    public void ConstructorPreflightRejectsInvalidPumpsBeforeStartingStorage()
    {
        using (var emptyRegistry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100)))
        {
            emptyRegistry.Seal();
            using var emptyPump = new SuiteFramePump(emptyRegistry);
            using var emptyStorage = new DecisionJournalRuntimeTestStorage();
            Assert.Throws<InvalidOperationException>(() => Runtime(emptyPump, emptyStorage));
            Assert.False(emptyStorage.ReconcileEntered.IsSet);
        }

        using var disposedRegistry = WaitingRegistry(
            "journal.runtime.disposed-preflight",
            new ThreadSafeTestClock(100));
        using var disposedPump = new SuiteFramePump(disposedRegistry);
        disposedPump.Dispose();
        using var disposedStorage = new DecisionJournalRuntimeTestStorage();
        Assert.Throws<ObjectDisposedException>(() => Runtime(disposedPump, disposedStorage));
        Assert.False(disposedStorage.ReconcileEntered.IsSet);
    }

    [Fact]
    public void PumpOwnershipRejectsReplacementUntilTheOldWriterIsTerminal()
    {
        using var registry = WaitingRegistry("journal.runtime.ownership", new ThreadSafeTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var firstStorage = new DecisionJournalRuntimeTestStorage(blockReconcile: true);
        var first = Runtime(pump, firstStorage);
        using var firstTeardown = new JournalTeardown(first);
        ServiceCycleTestDeadline.ForSignal(firstStorage.ReconcileEntered, "the first journal reconcile");

        using var rejectedStorage = new DecisionJournalRuntimeTestStorage();
        Assert.Throws<InvalidOperationException>(() => Runtime(pump, rejectedStorage));
        Assert.False(rejectedStorage.ReconcileEntered.IsSet);
        Assert.Throws<InvalidOperationException>(() => pump.Dispose());
        Assert.False(pump.IsDisposed);

        firstStorage.ReconcileRelease.Set();
        AdvanceTo(first, DecisionJournalRuntimeState.Recording);
        first.RequestStop();
        AdvanceTo(first, DecisionJournalRuntimeState.Stopped);

        using var replacementStorage = new DecisionJournalRuntimeTestStorage();
        var replacement = Runtime(pump, replacementStorage);
        using var replacementTeardown = new JournalTeardown(replacement);
        AdvanceTo(replacement, DecisionJournalRuntimeState.Recording);
        replacement.RequestStop();
        AdvanceTo(replacement, DecisionJournalRuntimeState.Stopped);
    }

    [Fact]
    public void InitializationAndEmergencyBoundaryArmWithoutInventingTransitions()
    {
        using var registry = WaitingRegistry("journal.runtime.arming", new ThreadSafeTestClock(100));
        using var pump = new SuiteFramePump(registry);
        pump.SetEmergencyStop(true);
        using var storage = new DecisionJournalRuntimeTestStorage(blockReconcile: true);
        var runtime = Runtime(pump, storage);
        using var teardown = new JournalTeardown(runtime);
        var ownerThread = Environment.CurrentManagedThreadId;

        ServiceCycleTestDeadline.ForSignal(storage.ReconcileEntered, "the journal reconcile");
        Assert.NotEqual(ownerThread, storage.ReconcileThreadId);
        runtime.Tick();
        Assert.Equal(DecisionJournalRuntimeState.Initializing, runtime.Snapshot.State);

        storage.ReconcileRelease.Set();
        AdvanceTo(runtime, DecisionJournalRuntimeState.Arming);
        Assert.False(runtime.Snapshot.Attached);

        pump.SetEmergencyStop(false);
        AdvanceTo(runtime, DecisionJournalRuntimeState.Recording);
        Assert.True(runtime.Snapshot.Attached);

        runtime.RequestStop();
        AdvanceTo(runtime, DecisionJournalRuntimeState.Stopped);
        Assert.Empty(storage.Segments);
    }

    [Fact]
    public void CompletedActionCycleWritesOneAttributedOutcome()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("journal.runtime.completed")
        {
            ActionCount = 1,
            EvaluationWake = WakePolicy.AfterBatch(new MonotonicDuration(17)),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var runtime = Runtime(pump, storage);
        using var teardown = new JournalTeardown(runtime);
        AdvanceTo(runtime, DecisionJournalRuntimeState.Recording);

        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        Assert.Equal(1, pump.PumpFrame(frame).ActionsAttempted);
        runtime.RequestStop();
        AdvanceTo(runtime, DecisionJournalRuntimeState.Stopped);

        var action = Assert.Single(
            storage.ReadRecords(),
            item => item.Kind == DecisionJournalRecordKind.Action);
        Assert.Equal(ServiceActionDisposition.Committed, action.ActionOutcome.Disposition);
        Assert.Equal(CommonActionResultCodes.Committed.Value, action.ActionOutcome.Code);
        Assert.Equal(ServiceActionNativeTypeId.StructureSO, action.Attribution.NativeType);
        Assert.NotEqual(Guid.Empty, action.Attribution.CandidateId);
    }

    [Fact]
    public void AttributionFailureExecutesAndJournalsDistinctStatusWithLoggedReason()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("journal.runtime.attribution-failure")
        {
            ActionCount = 2,
            ThrowOnDescribeAction = true,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1),
            ServiceActionDispatchPolicy.Bounded(2));
        var messages = new List<string>();
        var frameIdentity = 3L;
        using var host = new AutomataServiceCycleHost(
            registry,
            () => frameIdentity,
            pumpTiming: null,
            semanticTrace: null,
            actionOutcomes: null,
            serviceFailureLog: messages.Add);
        var pump = host.Pump;
        TestWorldCollector.CollectedAtActivation(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var runtime = Runtime(pump, storage);
        using var teardown = new JournalTeardown(runtime);
        AdvanceTo(runtime, DecisionJournalRuntimeState.Recording);

        Assert.Equal(frameIdentity, ServiceRunnerTestWait.PrepareBatch(pump, registration));
        Assert.Equal(2, host.Tick().ActionsAttempted);
        Assert.Equal(2, definition.ActionExecutionCount);
        runtime.RequestStop();
        AdvanceTo(runtime, DecisionJournalRuntimeState.Stopped);

        var actions = storage.ReadRecords()
            .Where(item => item.Kind == DecisionJournalRecordKind.Action)
            .ToArray();
        Assert.Equal(2, actions.Length);
        Assert.All(actions, action =>
        {
            Assert.Equal(ServiceActionDisposition.Committed, action.ActionOutcome.Disposition);
            Assert.Equal(ServiceActionRouteStatus.AttributionFailed, action.Attribution.RouteStatus);
        });
        Assert.Equal(2, messages.Count);
        Assert.Equal(messages[0], messages[1]);
        Assert.Equal(
            $"ServiceCycle action attribution failed for service {definition.ServiceId.Value}; " +
            "the action executed and the journal marked attribution failed: attribution exploded",
            messages[0]);
    }

    /// <summary>
    /// A service whose action throws stops its batch with an adapter fault, and the log says what
    /// threw. The fault code alone told whoever read the status that something failed, never what.
    /// </summary>
    [Fact]
    public void AThrowingActionFaultsItsBatchAndLogsWhatThrew()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("journal.runtime.execution-failure")
        {
            ActionCount = 2,
            ThrowOnExecute = new InvalidOperationException("adapter exploded"),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1),
            ServiceActionDispatchPolicy.Bounded(2));
        var messages = new List<string>();
        var frameIdentity = 3L;
        using var host = new AutomataServiceCycleHost(
            registry,
            () => frameIdentity,
            pumpTiming: null,
            semanticTrace: null,
            actionOutcomes: null,
            serviceFailureLog: messages.Add);
        TestWorldCollector.CollectedAtActivation(registry);

        Assert.Equal(frameIdentity, ServiceRunnerTestWait.PrepareBatch(host.Pump, registration));
        Assert.Equal(1, host.Tick().ActionsAttempted);

        Assert.Equal(
            $"ServiceCycle action for service {definition.ServiceId.Value} threw, so its batch " +
            "stopped with an adapter fault: adapter exploded",
            Assert.Single(messages));
    }

    /// <summary>
    /// Running out of memory is not an action fault the suite can retry past. It leaves the action
    /// boundary the way it leaves every other boundary that checks for it.
    /// </summary>
    [Fact]
    public void AProcessFatalActionFailureIsNotContained()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("journal.runtime.execution-fatal")
        {
            ActionCount = 1,
            ThrowOnExecute = new OutOfMemoryException(),
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);

        Assert.Throws<OutOfMemoryException>(() => pump.PumpFrame(frame));
    }

    [Fact]
    public void BetweenFrameEmergencyWritesTransitionAndRejectedTerminal()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("journal.runtime.emergency") { ActionCount = 2 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var runtime = Runtime(pump, storage);
        using var teardown = new JournalTeardown(runtime);
        AdvanceTo(runtime, DecisionJournalRuntimeState.Recording);

        _ = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        pump.SetEmergencyStop(true, EmergencyStopReason.SafetyInterlock);
        runtime.RequestStop();
        AdvanceTo(runtime, DecisionJournalRuntimeState.Stopped);

        var records = storage.ReadRecords();
        var transition = Assert.Single(
            records,
            item => item.Kind == DecisionJournalRecordKind.EmergencyEntered);
        var decision = Assert.Single(
            records,
            item => item.Kind == DecisionJournalRecordKind.DecisionSpan);
        Assert.Equal((int)EmergencyStopReason.SafetyInterlock, transition.TransitionCode);
        Assert.Equal(DecisionJournalDecisionOutcomeKind.Batch, decision.DecisionOutcomeKind);
        Assert.Equal(CommonActionResultCodes.EmergencyStop.Value, decision.DecisionOutcomeCode);
        Assert.Equal(0, definition.ActionExecutionCount);
    }

    [Fact]
    public void CommitFailureDetachesJournalAndLeavesPumpOperational()
    {
        using var registry = WaitingRegistry("journal.runtime.failure", new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage(failCommit: true);
        var runtime = Runtime(pump, storage, checkpointTicks: 1);
        using var teardown = new JournalTeardown(runtime);
        AdvanceTo(runtime, DecisionJournalRuntimeState.Recording);

        Assert.True(pump.PumpFrame(1).Accepted);
        ServiceCycleTestDeadline.ForSignal(storage.CommitEntered, "the failing journal commit");
        AdvanceTo(runtime, DecisionJournalRuntimeState.Faulted);

        Assert.False(runtime.Snapshot.Attached);
        Assert.True(pump.PumpFrame(2).Accepted);
    }

    [Fact]
    public void DisposeReturnsWhileStorageCommitIsBlockedAndReleasesTerminalOwnership()
    {
        using var registry = WaitingRegistry("journal.runtime.nonblocking-stop", new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage(blockCommit: true);
        var runtime = Runtime(pump, storage, checkpointTicks: 1);
        using var teardown = new JournalTeardown(runtime);
        AdvanceTo(runtime, DecisionJournalRuntimeState.Recording);

        Assert.True(pump.PumpFrame(1).Accepted);
        ServiceCycleTestDeadline.ForSignal(storage.CommitEntered, "the blocked journal commit");

        using var stopReturned = new ManualResetEventSlim();
        var watchdogReleasedStorage = 0;
        var watchdog = new Thread(() =>
        {
            if (stopReturned.Wait(Deadline)) return;
            Interlocked.Exchange(ref watchdogReleasedStorage, 1);
            storage.CommitRelease.Set();
        })
        {
            IsBackground = true,
            Name = "Decision journal stop watchdog",
        };
        watchdog.Start();
        runtime.Dispose();
        stopReturned.Set();
        Assert.True(watchdog.Join(Deadline), "The stop watchdog never finished.");
        Assert.Equal(0, Volatile.Read(ref watchdogReleasedStorage));
        Assert.False(storage.CommitRelease.IsSet);
        Assert.Equal(DecisionJournalRuntimeState.Stopping, runtime.Snapshot.State);

        storage.CommitRelease.Set();
        AdvanceTo(runtime, DecisionJournalRuntimeState.Stopped);

        using var replacementStorage = new DecisionJournalRuntimeTestStorage();
        var replacement = Runtime(pump, replacementStorage);
        using var replacementTeardown = new JournalTeardown(replacement);
        AdvanceTo(replacement, DecisionJournalRuntimeState.Recording);
        replacement.RequestStop();
        AdvanceTo(replacement, DecisionJournalRuntimeState.Stopped);
    }

    [Fact]
    public void OwnedPumpShutdownDoesNotWaitForTheJournalWriter()
    {
        using var registry = WaitingRegistry("journal.runtime.pump-shutdown", new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage(blockCommit: true);
        var runtime = Runtime(pump, storage, checkpointTicks: 1);
        using var teardown = new JournalTeardown(runtime);
        AdvanceTo(runtime, DecisionJournalRuntimeState.Recording);

        Assert.True(pump.PumpFrame(1).Accepted);
        ServiceCycleTestDeadline.ForSignal(storage.CommitEntered, "the blocked journal commit");

        using var shutdownReturned = new ManualResetEventSlim();
        var watchdogReleasedStorage = 0;
        var watchdog = new Thread(() =>
        {
            if (shutdownReturned.Wait(Deadline)) return;
            Interlocked.Exchange(ref watchdogReleasedStorage, 1);
            storage.CommitRelease.Set();
        })
        {
            IsBackground = true,
            Name = "Decision journal pump-shutdown watchdog",
        };
        watchdog.Start();
        runtime.DisposeWithPump();
        shutdownReturned.Set();

        Assert.True(watchdog.Join(Deadline), "The pump-shutdown watchdog never finished.");
        Assert.Equal(0, Volatile.Read(ref watchdogReleasedStorage));
        Assert.True(pump.IsDisposed);
        Assert.False(storage.CommitRelease.IsSet);
        Assert.Equal(DecisionJournalRuntimeState.Stopping, runtime.Snapshot.State);

        storage.CommitRelease.Set();
        AdvanceTo(runtime, DecisionJournalRuntimeState.Stopped);
    }

    /// <summary>
    /// Tears the pump down through the journal runtime that owns it, whatever the body did.
    /// </summary>
    /// <remarks>
    /// A runtime claims the pump for as long as it is not terminal, and <c>Dispose</c> only requests
    /// the stop — it does not release the claim, because the background writer may still be inside a
    /// commit. So the pump's own <c>using</c> is a second owner: a body that failed before the
    /// runtime reached a terminal state left that dispose to trip the ownership guard from the
    /// unwind, and the guard's exception replaced the assertion that actually failed. Declared after
    /// the pump, this runs first and hands the pump back through its owner; the pump's own dispose
    /// then finds nothing left to do. Where a test claims the pump twice, each claim gets its own
    /// teardown and the newest one unwinds first.
    /// </remarks>
    private readonly struct JournalTeardown : IDisposable
    {
        private readonly ServiceCycleDecisionJournalRuntime _runtime;

        internal JournalTeardown(ServiceCycleDecisionJournalRuntime runtime) => _runtime = runtime;

        public void Dispose() => _runtime.DisposeWithPump();
    }

    private static ServiceCycleDecisionJournalRuntime Runtime(
        SuiteFramePump pump,
        DecisionJournalRuntimeTestStorage storage,
        long checkpointTicks = 1_000) => new(
            pump,
            storage,
            new DecisionJournalRunId(1),
            maximumCommittedSegments: 4,
            blockCount: 3,
            new MonotonicDuration(checkpointTicks));

    private static ServiceCycleRegistry WaitingRegistry(string id, IMonotonicClock clock)
    {
        var registry = new ServiceCycleRegistry(1, clock);
        registry.Register(
            new ExecutionServiceDefinition(id)
            {
                StartDecision = ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.AfterDecision(new MonotonicDuration(5))),
            },
            new LifecycleGeneration(1));
        registry.Seal();
        return registry;
    }

    private static void AdvanceTo(
        ServiceCycleDecisionJournalRuntime runtime,
        DecisionJournalRuntimeState expected)
    {
        var observed = runtime.Snapshot;
        Assert.True(
            SpinWait.SpinUntil(() =>
            {
                runtime.Tick();
                observed = runtime.Snapshot;
                return observed.State == expected;
            }, Deadline),
            $"Expected {expected}; observed {observed.State}, attached {observed.Attached}, " +
            $"transport {observed.Transport.Status}.");
    }
}
