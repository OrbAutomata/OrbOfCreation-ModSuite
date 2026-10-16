using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCyclePumpTestWait;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpLifecycleConcurrencyTests
{
    [Fact]
    public void LifecycleReportCountsEveryPositionTransitionIncludingInternalCallbackReplacement()
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CaptureFirstThreadStarter();
        using var registry = new ServiceCycleRegistry(1, clock, false, starter);
        var definition = new LifecycleServiceDefinition("lifecycle.transition-report");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var retiringWorker = starter.FirstThread;
        definition.ShouldStartCallback = () =>
            pump.RequestLifecycleReplacement(new LifecycleGeneration(2));

        var replacementFrame = pump.PumpFrame(1);
        Assert.Equal((ulong)2, registration.LifecycleSnapshot.DesiredLifecycle.Value);
        Assert.Equal(3, registry.LifecyclePositionTransitionCount);
        Assert.Equal(2, replacementFrame.LifecyclePositionTransitions);
        Assert.True(retiringWorker.Join(TimeSpan.FromSeconds(2)));
        var releaseFrame = pump.PumpFrame(2);
        Assert.Equal(1, releaseFrame.LifecyclePositionTransitions);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void HugeLifecycleOrphanClearsReferenceSuffixOnWorkerBeforeExit()
    {
        var clock = new ThreadSafeTestClock(100);
        using var exit = new HoldWorkerExitObserver();
        using var registry = new ServiceCycleRegistry(1, clock, false, workerExitObserver: exit);
        var definition = new LifecycleServiceDefinition("lifecycle.huge-cleanup") { ActionCount = 100_000 };
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        PrepareBatch(pump, registration, ref frame);
        Assert.True(definition.IsPayloadAlive(1));
        var old = registration.Runner;

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.True(exit.WaitForCount(1));
        Assert.True(old.WorkerExitPrepared);
        Assert.Equal(ServiceHandoffPhase.Stopping, old.HandoffPhaseHint);
        Assert.True(CollectUntil(() => !definition.IsPayloadAlive(1)));
        exit.Release.Set();
    }

    [Fact]
    public void SiblingServiceContinuesWhileOtherServiceHasTwoStuckRetirees()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var stuckDefinition = new LifecycleServiceDefinition("lifecycle.sibling-stuck");
        var siblingDefinition = new LifecycleServiceDefinition("lifecycle.sibling-live");
        using var firstGate = stuckDefinition.BlockEvaluation(1);
        using var secondGate = stuckDefinition.BlockEvaluation(2);
        using var stuck = registry.Register(
            stuckDefinition, new LifecycleGeneration(1));
        using var sibling = registry.Register(
            siblingDefinition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        // The sibling changes the game, so it only keeps running while something reads the world:
        // these waits collect every frame the way the game's collection service does.
        var frame = 2L;
        PumpUntil(pump, ref frame, () => firstGate.Entered.IsSet, clock, registry);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        PumpUntil(pump, ref frame, () => secondGate.Entered.IsSet, clock, registry);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(3));
        Assert.Equal(2, stuck.LifecycleSnapshot.LivePositionCount);
        Assert.Throws<InvalidOperationException>(() => _ = stuck.Runner);

        PumpUntil(pump, ref frame, () => siblingDefinition.ExecutionCount(3) != 0, clock, registry);
        Assert.Equal(2, stuck.LifecycleSnapshot.LivePositionCount);
        Assert.True(siblingDefinition.EvaluationCount(3) > 0);
        firstGate.Release.Set();
        secondGate.Release.Set();
    }

    [Fact]
    public void SuspendedWorkerResourceReleaseCannotBlockLifecycleControlOrSiblingPump()
    {
        var clock = new ThreadSafeTestClock(100);
        using var exit = new HoldSelectedWorkerExitObserver("lifecycle.resource-suspended");
        using var registry = new ServiceCycleRegistry(2, clock, false, workerExitObserver: exit);
        var stuckDefinition = new LifecycleServiceDefinition("lifecycle.resource-suspended");
        var siblingDefinition = new LifecycleServiceDefinition("lifecycle.resource-sibling");
        using var stuck = registry.Register(
            stuckDefinition, new LifecycleGeneration(1));
        using var sibling = registry.Register(
            siblingDefinition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 2L;

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.True(exit.WaitForCount(1));
        PumpUntil(
            pump,
            ref frame,
            () => sibling.LifecycleSnapshot.LivePositionCount == 1,
            clock,
            registry);

        var control = Stopwatch.StartNew();
        pump.RequestLifecycleReplacement(new LifecycleGeneration(3));
        control.Stop();
        Assert.True(control.Elapsed < TimeSpan.FromMilliseconds(250));
        Assert.True(exit.WaitForCount(2));
        Assert.Equal(2, stuck.LifecycleSnapshot.LivePositionCount);

        var oneFrame = Stopwatch.StartNew();
        pump.PumpFrame(frame++);
        oneFrame.Stop();
        Assert.True(oneFrame.Elapsed < TimeSpan.FromMilliseconds(250));
        PumpUntil(pump, ref frame, () => siblingDefinition.ExecutionCount(3) != 0, clock, registry);
        Assert.Equal(2, stuck.LifecycleSnapshot.LivePositionCount);
        exit.Release.Set();
    }

    [Fact]
    public void LifecycleOutranksReentrantEmergencyAndFreshRunnerSleepsUntilClear()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.emergency") { ActionCount = 3 };
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        PrepareBatch(pump, registration, ref frame);
        definition.ActionCallback = () =>
        {
            pump.SetEmergencyStop(true);
            pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        };

        pump.PumpFrame(frame++);
        var fact = registration.LifecycleSnapshot.LatestTerminal;
        Assert.Equal(BatchTerminalDisposition.Orphaned, fact.Receipt.Disposition);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
        for (var index = 0; index < 5; index++) pump.PumpFrame(frame++);
        Assert.Equal(0, definition.EvaluationCount(2));
        pump.SetEmergencyStop(false);
        // The orphaned batch changed the game before it was cut short, so the fresh runner waits for
        // a reading that contains the change like any other cycle.
        TestWorldCollector.CollectedAt(registry, frame);
        PumpUntil(pump, ref frame, () => definition.EvaluationCount(2) != 0);
    }

    [Fact]
    public void TwoStuckRetireesKeepPausedPumpAllocationFreeAndDisposalPrompt()
    {
        var clock = new ThreadSafeTestClock(100);
        var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.stuck");
        using var firstGate = definition.BlockEvaluation(1);
        using var secondGate = definition.BlockEvaluation(2);
        var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        var pump = new SuiteFramePump(registry);
        var frame = 2L;
        PumpUntil(pump, ref frame, () => firstGate.Entered.IsSet, collector: registry);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        PumpUntil(pump, ref frame, () => secondGate.Entered.IsSet, collector: registry);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(3));

        for (var index = 0; index < 10; index++) pump.PumpFrame(frame++);

        Assert.Equal(0, AllocationProbe.MeasureRepeated(100, () => pump.PumpFrame(frame++)));

        var stopwatch = Stopwatch.StartNew();
        registration.Dispose();
        registry.Dispose();
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        firstGate.Release.Set();
        secondGate.Release.Set();
        pump.Dispose();
    }

    private static bool CollectUntil(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (deadline.Elapsed > TimeSpan.FromSeconds(3)) return false;
            Thread.Yield();
        }
        return true;
    }

    private sealed class CaptureFirstThreadStarter : IServiceCycleWorkerStarter
    {
        private Thread? _firstThread;

        internal Thread FirstThread => _firstThread ??
            throw new InvalidOperationException("No worker thread has been started.");

        public void Start(Thread thread)
        {
            _firstThread ??= thread;
            thread.Start();
        }
    }

    private sealed class HoldSelectedWorkerExitObserver : IServiceCycleWorkerExitObserver, IDisposable
    {
        private readonly string _workerNamePrefix;
        private int _enteredCount;

        internal HoldSelectedWorkerExitObserver(string serviceId) =>
            _workerNamePrefix = $"Orb.ServiceCycle.{serviceId}.lifecycle-";

        internal ManualResetEventSlim Release { get; } = new(false);

        public void OnWorkerExitPrepared()
        {
            if (Thread.CurrentThread.Name?.StartsWith(_workerNamePrefix, StringComparison.Ordinal) != true) return;
            Interlocked.Increment(ref _enteredCount);
            Release.Wait();
        }

        internal bool WaitForCount(int count) => SpinWait.SpinUntil(
            () => Volatile.Read(ref _enteredCount) >= count,
            ServiceCycleTestDeadline.Value);

        public void Dispose()
        {
            Release.Set();
            Release.Dispose();
        }
    }

}
