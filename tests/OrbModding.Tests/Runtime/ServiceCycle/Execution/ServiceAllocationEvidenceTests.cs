using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

[Trait("Category", "PerformanceSimulation")]
public sealed class ServiceAllocationEvidenceTests
{
    [Fact]
    public void WarmedIdleAppendDrainAndReceiptPathsAllocateNothingOnTheirOwningThreads()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock, measureWorkerAllocations: true);
        var definition = new ExecutionServiceDefinition("test.execution.alloc") { ActionCount = 512 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunAndDrain(runner, clock, 512);
        definition.MeasureAppendAllocations = true;

        // A drained batch is spent, so each pass takes a freshly published one; arming happens
        // outside the measured window and never counts against the drain.
        void ArmFullBatch()
        {
            var measuredBefore = runner.Snapshot.MeasuredWorkerCycleCount;
            Assert.True(runner.TryStartCycle(clock.Now).Queued);
            ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
            Assert.True(SpinWait.SpinUntil(
                () => runner.Snapshot.MeasuredWorkerCycleCount > measuredBefore,
                ServiceCycleTestDeadline.Value));
            Assert.True(runner.TryAcquireResponse());
        }

        var drainAllocated = AllocationProbe.MeasureRepeated(
            512,
            () => runner.TryExecuteOne(clock.Now),
            prepare: ArmFullBatch);

        Assert.Equal(0, drainAllocated);
        Assert.Equal(0, definition.LastAppendAllocatedBytes);
        Assert.Equal(0, runner.Snapshot.WorkerCycleAllocatedBytes);

        var idleAllocated = AllocationProbe.MeasureRepeated(
            10_000,
            () => runner.TryAcquireResponse());

        Assert.Equal(0, idleAllocated);
    }
}
