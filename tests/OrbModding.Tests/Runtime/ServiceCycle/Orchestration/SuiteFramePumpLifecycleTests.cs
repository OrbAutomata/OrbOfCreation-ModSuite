using OrbModding.TestSupport;
using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCyclePumpTestWait;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpLifecycleTests
{
    [Fact]
    public void RegistryCentralizesInitialLifecycleAndPublisherSurvivesReplacement()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, new LifecycleGeneration(7), clock);
        var definition = new LifecycleServiceDefinition("lifecycle.registry");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(7));
        var publisher = registry.Configuration;

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new LifecycleServiceDefinition("lifecycle.mismatch"),
            new LifecycleGeneration(8)));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(8)));
        Assert.False(pump.RequestLifecycleReplacement(new LifecycleGeneration(8)));
        Assert.False(pump.RequestLifecycleReplacement(new LifecycleGeneration(6)));
        Assert.Same(publisher, registry.Configuration);
        publisher.Publish(TestSuiteConfiguration.WithSetting(2));
        Assert.Equal((ulong)8, registration.Runner.Lifecycle.Value);
        Assert.Equal(2, TestSuiteConfiguration.SettingOf(publisher.ReadLatest().Snapshot));
    }

    [Fact]
    public void ReentrantShouldStartAndCaptureReplacementPreventOldRequestPublication()
    {
        VerifyReentrantStartReplacement();
    }

    [Fact]
    public void ReplacementOfCapturedDeferredPublicationDoesNotBlockOrPublishOldRequest()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.capture-deferred");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var oldRunner = registration.Runner;
        using var contention = new HandoffGateContention(oldRunner);
        definition.ShouldStartCallback = contention.Acquire;

        var captured = pump.PumpFrame(1);
        Assert.Equal(1, captured.CyclesStarted);
        Assert.Equal(ServiceHandoffPhase.Empty, oldRunner.HandoffPhaseHint);
        HeldGate.AssertReturnsWhileHeld(
            () => pump.RequestLifecycleReplacement(new LifecycleGeneration(2)),
            contention.Release);
        Assert.False(registration.LifecycleSnapshot.LatestTerminal.HasPublishedCycle);
        contention.Release();
        Assert.True(SpinWait.SpinUntil(
            () => oldRunner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
        Assert.Equal(0, definition.EvaluationCount(1));
    }

    [Fact]
    public void RequestReadyIsTerminalizedBeforeDeferredWorkerCanEvaluate()
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new DeferFirstThreadStart();
        using var registry = new ServiceCycleRegistry(
            1, clock, measureWorkerAllocations: false, workerStarter: starter);
        var definition = new LifecycleServiceDefinition("lifecycle.request-ready");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var oldRunner = registration.Runner;

        pump.PumpFrame(1);
        Assert.Equal(ServiceHandoffPhase.RequestReady, oldRunner.HandoffPhaseHint);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        var fact = registration.LifecycleSnapshot.LatestTerminal;
        Assert.True(fact.HasPublishedCycle);
        Assert.False(fact.HasReceipt);
        starter.StartDeferred();
        Assert.True(SpinWait.SpinUntil(
            () => oldRunner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
        Assert.Equal(0, definition.EvaluationCount(1));
    }

    [Fact]
    public void ReplacementDuringEvaluationDiscardsLateResponseAndPublishesOnlyFreshState()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.evaluation");
        using var gate = definition.BlockEvaluation(1);
        using var freshGate = definition.BlockEvaluation(2);
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 2L;
        PumpUntil(pump, ref frame, () => gate.Entered.IsSet, collector: registry);

        var oldRunner = registration.Runner;
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.Equal(ServiceRunnerPositionState.Retiring, registration.LifecycleSnapshot.Position0.State);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
        gate.Release.Set();
        ServiceRunnerTestWait.ForPhase(oldRunner, ServiceHandoffPhase.Stopped);

        PumpUntil(pump, ref frame, () => freshGate.Entered.IsSet, collector: registry);
        freshGate.Release.Set();
        ServiceRunnerTestWait.ForResponse(registration);
        PumpUntil(pump, ref frame, () => registration.Runner.Snapshot.Projection.IsPresent, collector: registry);
        Assert.Equal((ulong)2, registration.Runner.Snapshot.Projection.Context.Cycle.Lifecycle.Value);
        Assert.Equal(0, definition.ExecutionCount(1));
        Assert.Equal((ulong)1, registration.LifecycleSnapshot.LatestTerminal.RetiredLifecycle.Value);
    }

    [Fact]
    public void ResponseReadyReplacementNeverInstallsOldProjectionOrActions()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.response") { ActionCount = 3 };
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        WaitForResponse(pump, registration, ref frame);

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        var fact = registration.LifecycleSnapshot.LatestTerminal;
        Assert.True(fact.HasPublishedCycle);
        Assert.False(fact.HasReceipt);
        Assert.Equal(0, definition.ExecutionCount(1));
        Assert.False(registration.Runner.Snapshot.Projection.IsPresent);
    }

    [Fact]
    public void ReentrantActionReplacementOrphansExactCommittedPrefixAndStopsOldScan()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.action") { ActionCount = 4 };
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        PrepareBatch(pump, registration, ref frame);
        definition.ActionCallback = () => pump.RequestLifecycleReplacement(new LifecycleGeneration(2));

        var actionFrame = pump.PumpFrame(frame++);
        var fact = registration.LifecycleSnapshot.LatestTerminal;
        Assert.Equal(1, actionFrame.ActionsAttempted);
        Assert.True(fact.HasReceipt);
        Assert.Equal(BatchTerminalDisposition.Orphaned, fact.Receipt.Disposition);
        Assert.Equal(4, fact.Receipt.ActionCount);
        Assert.Equal(1, fact.Receipt.CommittedCount);
        Assert.Equal(3, fact.Receipt.UntouchedSuffixCount);
        Assert.Equal(1, fact.Receipt.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(1, definition.ExecutionCount(1));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("rejected")]
    [InlineData("faulted")]
    public void AlreadyEnteredTerminalActionWinsAndLifecycleDoesNotDoubleTerminal(string outcome)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.terminal." + outcome)
        {
            ActionCount = outcome == "completed" ? 1 : 3,
            RejectAtIndex = outcome == "rejected" ? 0 : -1,
            FaultAtIndex = outcome == "faulted" ? 0 : -1,
        };
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        PrepareBatch(pump, registration, ref frame);
        var oldRunner = registration.Runner;
        definition.ActionCallback = () => pump.RequestLifecycleReplacement(new LifecycleGeneration(2));

        pump.PumpFrame(frame);
        var receipt = oldRunner.Snapshot.PreviousReceipt;
        var expected = outcome switch
        {
            "completed" => BatchTerminalDisposition.Completed,
            "rejected" => BatchTerminalDisposition.Rejected,
            _ => BatchTerminalDisposition.Faulted,
        };
        Assert.Equal(expected, receipt.Disposition);
        Assert.False(registration.LifecycleSnapshot.LatestTerminal.HasReceipt);
        Assert.Equal(outcome == "rejected" ? 0 : 1, receipt.NativeCallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void LifecycleStormUsesExactlyTwoPositionsAndReusesFirstStoppedForNewestOnly()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.storm");
        using var firstGate = definition.BlockEvaluation(1);
        using var secondGate = definition.BlockEvaluation(2);
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 2L;
        PumpUntil(pump, ref frame, () => firstGate.Entered.IsSet, collector: registry);
        var firstRunner = registration.Runner;

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        PumpUntil(pump, ref frame, () => secondGate.Entered.IsSet, collector: registry);
        var secondRunner = registration.Runner;
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(3)));
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(4)));
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(5)));
        var paused = registration.LifecycleSnapshot;
        Assert.Equal(2, paused.LivePositionCount);
        Assert.Equal(ServiceRunnerPositionState.Retiring, paused.Position0.State);
        Assert.Equal(ServiceRunnerPositionState.Retiring, paused.Position1.State);
        Assert.Equal((ulong)5, paused.DesiredLifecycle.Value);
        Assert.Throws<InvalidOperationException>(() => _ = registration.Runner);
        Assert.Equal(2, definition.WorkerDefinitionCreateCount);

        firstGate.Release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => firstRunner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
        pump.PumpFrame(frame++);
        Assert.Equal((ulong)5, registration.Runner.Lifecycle.Value);
        Assert.Equal(3, definition.WorkerDefinitionCreateCount);
        Assert.Equal(0, definition.EvaluationCount(3));
        Assert.Equal(0, definition.EvaluationCount(4));

        secondGate.Release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => secondRunner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void HundredThousandActionLifecycleOrphanTerminatesSuffixOnOwnerWithoutExecutingAnAction()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.huge") { ActionCount = 100_000 };
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        PrepareBatch(pump, registration, ref frame);

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        var receipt = registration.LifecycleSnapshot.LatestTerminal.Receipt;
        Assert.Equal(BatchTerminalDisposition.Orphaned, receipt.Disposition);
        Assert.Equal(100_000, receipt.ActionCount);
        Assert.Equal(100_000, receipt.UntouchedSuffixCount);
        Assert.Equal(0, receipt.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(0, definition.ActionExecutionCount);
    }

    [Fact]
    public void StoppedAndPositionReuseWaitForActualWorkerExit()
    {
        var clock = new ThreadSafeTestClock(100);
        using var exit = new HoldWorkerExitObserver();
        var starter = new CountingThreadStarter();
        using var registry = new ServiceCycleRegistry(
            1, clock, false, starter, exit);
        var definition = new LifecycleServiceDefinition("lifecycle.actual-exit");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var first = registration.Runner;

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        var second = registration.Runner;
        pump.RequestLifecycleReplacement(new LifecycleGeneration(3));
        Assert.True(exit.WaitForCount(2));
        Assert.True(first.WorkerExitPrepared);
        Assert.True(first.WorkerWakeDisposed);
        Assert.True(second.WorkerExitPrepared);
        Assert.True(second.WorkerWakeDisposed);
        Assert.NotEqual(ServiceHandoffPhase.Stopped, first.HandoffPhaseHint);
        Assert.NotEqual(ServiceHandoffPhase.Stopped, second.HandoffPhaseHint);
        Assert.Equal(2, registration.LifecycleSnapshot.LivePositionCount);

        for (var frame = 1L; frame <= 5; frame++) pump.PumpFrame(frame);
        Assert.Equal(2, starter.AttemptCount);
        Assert.Throws<InvalidOperationException>(() => _ = registration.Runner);

        exit.Release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => first.HandoffPhaseHint == ServiceHandoffPhase.Stopped &&
                  second.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
        pump.PumpFrame(6);
        Assert.Equal(3, starter.AttemptCount);
        Assert.Equal((ulong)3, registration.Runner.Lifecycle.Value);
    }

    [Fact]
    public void ZeroActionResponseReceiptRemainsCompletedAcrossReplacement()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.zero-terminal") { ActionCount = 0 };
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        WaitForResponse(pump, registration, ref frame);

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        var fact = registration.LifecycleSnapshot.LatestTerminal;
        Assert.True(fact.HasReceipt);
        Assert.Equal(BatchTerminalDisposition.Completed, fact.Receipt.Disposition);
        Assert.Equal(0, fact.Receipt.ActionCount);
        Assert.Equal((ulong)1, fact.Receipt.Cycle.Lifecycle.Value);
    }

    private static void VerifyReentrantStartReplacement()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.should-start");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        definition.ShouldStartCallback = () => pump.RequestLifecycleReplacement(new LifecycleGeneration(2));

        var frame = 1L;
        PumpUntil(pump, ref frame, () =>
            registration.LifecycleSnapshot.DesiredLifecycle.Value == 2,
            clock);
        PumpUntil(pump, ref frame, () =>
        {
            var snapshot = registration.LifecycleSnapshot;
            return (snapshot.Position0.State == ServiceRunnerPositionState.Current &&
                    snapshot.Position0.Lifecycle.Value == 2) ||
                   (snapshot.Position1.State == ServiceRunnerPositionState.Current &&
                    snapshot.Position1.Lifecycle.Value == 2);
        }, clock);

        Assert.Equal(0, definition.EvaluationCount(1));
        Assert.False(registration.LifecycleSnapshot.LatestTerminal.HasPublishedCycle);
    }

    private sealed class DeferFirstThreadStart : IServiceCycleWorkerStarter
    {
        private Thread? _deferred;
        private int _attempts;

        public void Start(Thread thread)
        {
            _attempts++;
            if (_attempts == 1)
            {
                _deferred = thread;
                return;
            }
            thread.Start();
        }

        internal void StartDeferred()
        {
            var thread = _deferred ?? throw new InvalidOperationException("No worker start is deferred.");
            _deferred = null;
            thread.Start();
        }
    }
}
