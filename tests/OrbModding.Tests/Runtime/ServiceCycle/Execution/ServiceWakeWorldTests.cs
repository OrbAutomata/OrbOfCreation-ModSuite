using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceWakeWorldTests
{
    [Fact]
    public void PublishingWorldWakesAServiceWaitingForTheNextWorld()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.world-wakes-result")
        {
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        Assert.False(runner.TryStartCycleNonBlocking(clock.Now).Queued);

        registry.WorldPublication.Publish(new GameWorldState(), new WorldGeneration(2));

        var awakened = ServiceRunnerTestWait.StartCycle(runner, clock.Now);

        Assert.True(awakened.Queued);
        Assert.Equal(new WorldGeneration(2), awakened.Cycle.World);
    }

    [Fact]
    public void PublishingWorldDoesNotCutATimedWaitShort()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.world-keeps-timed-wait")
        {
            EvaluationWake = WakePolicy.AfterDecision(new MonotonicDuration(1000)),
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        registry.WorldPublication.Publish(new GameWorldState(), new WorldGeneration(2));

        Assert.False(runner.TryStartCycleNonBlocking(clock.Now).Queued);
        Assert.Equal(1100, runner.Snapshot.NextWakeDue.Ticks);

        clock.AdvanceTo(new MonotonicTimestamp(1100));

        Assert.True(ServiceRunnerTestWait.StartCycle(runner, clock.Now).Queued);
    }

    [Fact]
    public void WorldPublishedDuringEvaluationInvalidatesItsResultingWait()
    {
        var clock = new ThreadSafeTestClock(100);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.world-wakes-inflight-result")
        {
            EvaluationEntered = entered,
            EvaluationRelease = release,
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));
        registry.WorldPublication.Publish(new GameWorldState(), new WorldGeneration(2));
        release.Set();
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        ServiceRunnerTestWait.ForHandback(runner);

        var awakened = ServiceRunnerTestWait.StartCycle(runner, clock.Now);

        Assert.True(awakened.Queued);
        Assert.Equal(new WorldGeneration(2), awakened.Cycle.World);
    }

    [Fact]
    public void PublishingWorldDoesNotBypassEvaluationFaultBackoff()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.world-keeps-fault-backoff");
        definition.FailNextEvaluations(1);
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        ServiceRunnerTestWait.ForHandback(runner);
        var due = runner.Snapshot.NextWakeDue;

        registry.WorldPublication.Publish(new GameWorldState(), new WorldGeneration(2));

        Assert.False(runner.TryStartCycleNonBlocking(clock.Now).Queued);
        Assert.Equal(due, runner.Snapshot.NextWakeDue);
    }

    [Fact]
    public void PublishingWorldDoesNotWakeTheSourceThatPublishesIt()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("test.source.world-keeps-own-cadence")
        {
            DefaultWakePolicy = WakePolicy.AfterBatch(new MonotonicDuration(1000)),
        };
        using var registration = registry.RegisterSource(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        registry.WorldPublication.Publish(new GameWorldState(), new WorldGeneration(2));

        Assert.False(runner.TryStartCycleNonBlocking(clock.Now).Queued);
        Assert.Equal(1100, runner.Snapshot.NextWakeDue.Ticks);
    }
}
