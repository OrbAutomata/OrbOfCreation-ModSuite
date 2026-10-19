using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceProjectionKeyFaultTests
{
    [Fact]
    public void DuplicateProjectionKeyFaultsTheWholePublicationAndRecovers()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.projection.duplicate-key");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        var accepted = runner.Snapshot.Projection;

        definition.ActionCount = 3;
        definition.DuplicateProjectionKey = true;
        ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        ServiceRunnerTestWait.ForHandback(runner);
        var faulted = runner.Snapshot;

        Assert.Equal(ServiceFaultCategory.StateProjection, faulted.Fault.Category);
        Assert.Equal(accepted.Context.Publication, faulted.Projection.Context.Publication);
        Assert.Equal(0, faulted.ActionCount);
        Assert.Equal(1, definition.StateReleaseCount);
        Assert.Equal(2, definition.StateCreateCount);

        definition.ActionCount = 0;
        definition.DuplicateProjectionKey = false;
        clock.AdvanceTo(faulted.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);

        Assert.False(runner.Snapshot.Fault.IsValid);
        Assert.NotEqual(accepted.Context.Publication, runner.Snapshot.Projection.Context.Publication);
    }
}
