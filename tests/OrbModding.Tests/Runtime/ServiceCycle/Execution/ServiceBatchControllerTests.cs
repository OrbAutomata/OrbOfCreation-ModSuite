using System;
using System.Runtime.CompilerServices;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceBatchControllerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(500)]
    public void FiniteBatchesAreNeverTruncatedAndStatePersists(int actionCount)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition($"test.execution.batch.{actionCount}")
        {
            ActionCount = actionCount,
        };
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(3));
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        Assert.Equal(actionCount, runner.Snapshot.PreviousReceipt.IsPresent ? 0 : runner.Snapshot.ActionCount);

        for (var index = 0; index < actionCount; index++)
        {
            var dispatch = runner.TryExecuteOneNonBlocking(clock.Now);
            Assert.True(dispatch.Attempted);
            Assert.Equal(index == actionCount - 1, dispatch.BatchTerminal);
        }

        ServiceRunnerTestWait.ForHandback(runner);
        Assert.Equal(ServiceHandoffPhase.Empty, runner.Snapshot.Handoff.Phase);
        Assert.Equal(actionCount, runner.Snapshot.PreviousReceipt.ActionCount);
        Assert.Equal(actionCount, runner.Snapshot.PreviousReceipt.CommittedCount);
        if (actionCount == 0) Assert.Equal(5, runner.Snapshot.Handoff.TransitionCount);
        Assert.Equal(3, definition.LastEvaluatedSetting);
        if (actionCount != 0) Assert.Equal(3, definition.LastExecutedSetting);

        ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        Assert.Equal(2L, runner.Snapshot.Projection.Snapshot.GetEntry(0).Value.Integer);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void HundredThousandActionRejectedSuffixIsClearedByWorkerNotUnity()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.huge")
        {
            ActionCount = 100_000,
            RejectAtIndex = 0,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        Assert.Equal(100_000, runner.Snapshot.ActionCount);
        Assert.True(runner.Snapshot.ActionCapacity >= 100_000);
        Assert.True(runner.Snapshot.ActionGrowthAllocations > 0);

        var dispatch = runner.TryExecuteOneNonBlocking(clock.Now);
        Assert.True(dispatch.BatchTerminal);
        Assert.Equal(BatchTerminalDisposition.Rejected, dispatch.Receipt.Disposition);
        Assert.Equal(99_999, dispatch.Receipt.UntouchedSuffixCount);
        Assert.Equal(0, runner.Snapshot.Handoff.CleanupRequestCount);
        ServiceRunnerTestWait.ForHandback(runner);
        Assert.Equal(1, runner.Snapshot.Handoff.CleanupRequestCount);

        ServiceRunnerTestWait.ForCleanup(runner);
        var snapshot = runner.Snapshot;
        Assert.Equal(0, snapshot.ActionCount);
        Assert.Equal(1, snapshot.Handoff.CleanupAcknowledgementCount);
        Assert.Equal(snapshot.WorkerThreadId, snapshot.LastCleanupThreadId);
    }

    [Fact]
    public void BatchEvidenceUsesLongTotalsWithoutRetryingCommittedActions()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.long-evidence")
        {
            ActionCount = 2,
            CommittedNativeOutcome = new NativeMutationCallOutcome(int.MaxValue, int.MaxValue, int.MaxValue),
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunAndDrain(runner, clock, 2);

        Assert.Equal(2, definition.ActionExecutionCount);
        Assert.Equal(2L * int.MaxValue, runner.Snapshot.PreviousReceipt.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(2L * int.MaxValue, runner.Snapshot.PreviousReceipt.NativeCallOutcome.MutationAttempts);
        Assert.Equal(2L * int.MaxValue, runner.Snapshot.PreviousReceipt.NativeCallOutcome.MutationsCommitted);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public void SkippedActionAdvancesAndLaterActionStillCommits(
        bool beforeNative,
        long expectedMutationAttempts)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.skipped")
        {
            ActionCount = 3,
            SkipAtIndex = beforeNative ? -1 : 1,
            SkipWithoutNativeAtIndex = beforeNative ? 1 : -1,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunAndDrain(runner, clock, 3);

        var receipt = runner.Snapshot.PreviousReceipt;
        Assert.Equal(BatchTerminalDisposition.Completed, receipt.Disposition);
        Assert.Equal(3, receipt.ActionCount);
        Assert.Equal(2, receipt.CommittedCount);
        Assert.Equal(1, receipt.SkippedCount);
        Assert.Equal(beforeNative ? 1 : 0, receipt.PreNativeSkippedCount);
        Assert.Equal(3, definition.ActionExecutionCount);
        Assert.Equal(expectedMutationAttempts, receipt.NativeCallOutcome.MutationAttempts);
        Assert.Equal(2, receipt.NativeCallOutcome.MutationsCommitted);
    }

    [Fact]
    public void RejectedReferenceSuffixIsCollectibleAndCapacityIsReused()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.reference-cleanup")
        {
            ActionCount = 100_000,
            RejectAtIndex = 0,
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;
        var reference = SetUniquePayload(definition);

        ServiceRunnerTestWait.RunRejectedBatch(runner, clock);
        definition.Payload = null;
        ServiceRunnerTestWait.ForCleanup(runner);
        var first = runner.Snapshot;
        ForceCollection(reference);
        Assert.False(reference.IsAlive);

        definition.Payload = new ActionPayload(22);
        ServiceRunnerTestWait.RunRejectedBatch(runner, clock);
        ServiceRunnerTestWait.ForCleanup(runner);
        var second = runner.Snapshot;
        Assert.Equal(first.ActionCapacity, second.ActionCapacity);
        Assert.Equal(first.ActionGrowthAllocations, second.ActionGrowthAllocations);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SetUniquePayload(ExecutionServiceDefinition definition)
    {
        var payload = new ActionPayload(11);
        definition.Payload = payload;
        return new WeakReference(payload);
    }

    private static void ForceCollection(WeakReference reference)
    {
        for (var attempt = 0; attempt < 8 && reference.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

}
