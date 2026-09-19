using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceWorkerFaultTransactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryFactoryContentionIsTransientAndPreservesOriginalFault(
        bool projectionFault)
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var actionsAppended = new ManualResetEventSlim(false);
        using var actionsRelease = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition(
            $"test.execution.recovery-contention.{projectionFault}");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        definition.ActionsAppended = actionsAppended;
        definition.ActionsRelease = actionsRelease;
        if (projectionFault) definition.FailNextProjections(1);
        else definition.FailNextEvaluations(1);

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(actionsAppended.Wait(ServiceCycleTestDeadline.Value));
        var ledger = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.WorkerDefinition, out var blocker));
        actionsRelease.Set();
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var contended = runner.Snapshot;
        Assert.False(contended.Fault.IsValid);
        Assert.Equal(1, contended.WorkerStateConstructionContentionCount);
        Assert.Equal(1, definition.StateCreateCount);
        Assert.Equal(1, definition.StateReleaseCount);
        Assert.False(runner.TryStartCycle(clock.Now).Queued);

        ledger.EndFactory(blocker);
        clock.AdvanceTo(contended.NextWakeDue);
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var preserved = runner.Snapshot;
        Assert.True(preserved.Fault.IsValid);
        Assert.Equal(
            projectionFault ? ServiceFaultCategory.StateProjection : ServiceFaultCategory.Evaluation,
            preserved.Fault.Category);
        Assert.Equal(1, preserved.Fault.OccurrenceCount);
        Assert.Equal(1, preserved.WorkerStateConstructionContentionCount);
        Assert.Equal(2, definition.StateCreateCount);

        definition.ActionsAppended = null;
        definition.ActionsRelease = null;
        clock.AdvanceTo(preserved.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        Assert.False(runner.Snapshot.Fault.IsValid);
        var stopping = Stopwatch.StartNew();
        registration.Dispose();
        Assert.True(SpinWait.SpinUntil(
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
        stopping.Stop();
        Assert.True(stopping.Elapsed < TimeSpan.FromMilliseconds(250));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefinitiveStateFactoryFailureSupersedesPendingRecoveryFault(
        bool projectionFault)
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var actionsAppended = new ManualResetEventSlim(false);
        using var actionsRelease = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition(
            $"test.execution.recovery-contention-superseded.{projectionFault}");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        definition.ActionsAppended = actionsAppended;
        definition.ActionsRelease = actionsRelease;
        if (projectionFault) definition.FailNextProjections(1);
        else definition.FailNextEvaluations(1);

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(actionsAppended.Wait(ServiceCycleTestDeadline.Value));
        var ledger = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.WorkerDefinition, out var blocker));
        definition.FailNextStateFactories(1);
        actionsRelease.Set();
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var contended = runner.Snapshot;
        Assert.False(contended.Fault.IsValid);
        Assert.Equal(1, contended.WorkerStateConstructionContentionCount);
        Assert.Equal(2, definition.EvaluationCount);

        ledger.EndFactory(blocker);
        clock.AdvanceTo(contended.NextWakeDue);
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var stateFactoryFault = runner.Snapshot;
        Assert.Equal(ServiceFaultCategory.StateFactory, stateFactoryFault.Fault.Category);
        Assert.Equal(1, stateFactoryFault.Fault.OccurrenceCount);
        Assert.Equal(1, stateFactoryFault.WorkerStateConstructionContentionCount);
        Assert.Equal(2, definition.StateCreateCount);
        Assert.Equal(2, definition.EvaluationCount);

        definition.ActionsAppended = null;
        definition.ActionsRelease = null;
        clock.AdvanceTo(stateFactoryFault.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        var recovered = runner.Snapshot;
        Assert.False(recovered.Fault.IsValid);
        Assert.Equal(1, recovered.WorkerStateConstructionContentionCount);
        Assert.Equal(3, definition.StateCreateCount);
        Assert.Equal(3, definition.EvaluationCount);
    }

    [Fact]
    public void OwnerSnapshotPublishesOnlyCompletedWorkerMetrics()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var appended = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition("test.execution.metrics.coherent")
        {
            ActionCount = 500,
            ActionsAppended = appended,
            ActionsRelease = release,
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(appended.Wait(ServiceCycleTestDeadline.Value));
        var whileWorkerOwns = runner.Snapshot;
        Assert.Equal(ServiceHandoffPhase.Evaluating, whileWorkerOwns.Handoff.Phase);
        Assert.Equal(0, whileWorkerOwns.ActionCount);
        Assert.Equal(0, whileWorkerOwns.ActionCapacity);

        release.Set();
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.Equal(500, runner.Snapshot.ActionCount);
        Assert.True(runner.Snapshot.ActionCapacity >= 500);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvaluationAndProjectionFaultsAreFailureAtomicAndRecover(bool projectionFault)
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition($"test.execution.fault.{projectionFault}");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        var firstProjection = runner.Snapshot.Projection;
        Assert.Equal(1L, firstProjection.Snapshot.GetEntry(0).Value.Integer);

        definition.PartialActionCountBeforeFault = 20;
        if (projectionFault) definition.FailNextProjections(1);
        else definition.FailNextEvaluations(1);
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var faulted = runner.Snapshot;
        Assert.True(faulted.Fault.IsValid);
        Assert.Equal(
            projectionFault ? ServiceFaultCategory.StateProjection : ServiceFaultCategory.Evaluation,
            faulted.Fault.Category);
        Assert.Equal(firstProjection.Context.Publication, faulted.Projection.Context.Publication);
        Assert.Equal(0, faulted.ActionCount);
        Assert.True(definition.StateReleaseCount >= 1);
        Assert.True(definition.StateCreateCount >= 2);

        var evaluationCount = definition.EvaluationCount;
        Assert.False(runner.TryStartCycle(new MonotonicTimestamp(faulted.NextWakeDue.Ticks - 1)).Queued);
        Assert.Equal(evaluationCount, definition.EvaluationCount);
        clock.AdvanceTo(faulted.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        Assert.Equal(1L, runner.Snapshot.Projection.Snapshot.GetEntry(0).Value.Integer);
    }

    [Fact]
    public void FailedPartialEvaluationStillPublishesRetainedGrowthMetrics()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.failed-growth")
        {
            PartialActionCountBeforeFault = 500,
        };
        definition.FailNextEvaluations(1);
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var snapshot = runner.Snapshot;
        Assert.Equal(0, snapshot.ActionCount);
        Assert.Equal(0, snapshot.ActionCursor);
        Assert.True(snapshot.ActionCapacity >= 500);
        Assert.True(snapshot.ActionHighWater >= 500);
        Assert.True(snapshot.ActionGrowthAllocations > 0);
        Assert.Equal(snapshot.ActionCapacity, snapshot.RetainedActionSlots);
    }

    [Fact]
    public void PersistentActionFaultsBackOffAndDebounce()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.action-fault")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.True(runner.TryExecuteOne(clock.Now).BatchTerminal);
        ServiceRunnerTestWait.ForCleanup(runner);
        var first = runner.Snapshot;
        Assert.Equal(ServiceFaultCategory.ActionExecution, first.Fault.Category);
        Assert.Equal(1, first.Fault.OccurrenceCount);
        var handoffBeforeSleepScans = first.Handoff;
        var unexpectedlyQueued = false;
        var sleepScanAllocations = AllocationProbe.MeasureRepeated(
            10_000,
            () => unexpectedlyQueued |=
                runner.TryStartCycle(new MonotonicTimestamp(first.NextWakeDue.Ticks - 1)).Queued);

        Assert.False(unexpectedlyQueued);
        Assert.Equal(0, sleepScanAllocations);
        Assert.Equal(handoffBeforeSleepScans.TransitionCount, runner.Snapshot.Handoff.TransitionCount);
        Assert.Equal(1, definition.ActionExecutionCount);

        clock.AdvanceTo(first.NextWakeDue);
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.True(runner.TryExecuteOne(clock.Now).BatchTerminal);
        var second = runner.Snapshot;
        Assert.Equal(2, second.Fault.OccurrenceCount);
        Assert.True(second.NextWakeDue > first.NextWakeDue);
        Assert.Equal(2, definition.ActionExecutionCount);
    }

    [Fact]
    public void StateFactoryFaultsAreDebouncedAndCannotSpin()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.factory");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        definition.FailNextStateFactories(2);
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var first = runner.Snapshot;
        Assert.Equal(ServiceFaultCategory.StateFactory, first.Fault.Category);
        var createsAfterFirst = definition.StateCreateCount;

        for (var index = 0; index < 100; index++)
            Assert.False(runner.TryStartCycle(new MonotonicTimestamp(first.NextWakeDue.Ticks - 1)).Queued);
        Assert.Equal(createsAfterFirst, definition.StateCreateCount);

        clock.AdvanceTo(first.NextWakeDue);
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var second = runner.Snapshot;
        Assert.Equal(ServiceFaultCategory.StateFactory, second.Fault.Category);
        Assert.True(second.NextWakeDue > first.NextWakeDue);
        var createsAfterSecond = definition.StateCreateCount;
        Assert.False(runner.TryStartCycle(new MonotonicTimestamp(second.NextWakeDue.Ticks - 1)).Queued);
        Assert.Equal(createsAfterSecond, definition.StateCreateCount);

        clock.AdvanceTo(second.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        Assert.False(runner.Snapshot.Fault.IsValid);
    }
}
