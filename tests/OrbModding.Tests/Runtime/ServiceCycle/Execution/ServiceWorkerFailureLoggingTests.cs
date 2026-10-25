using System;
using System.Collections.Concurrent;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class ServiceWorkerFailureLoggingTests : IDisposable
{
    private readonly ConcurrentQueue<string> _entries = new();

    public ServiceWorkerFailureLoggingTests() => GameActionFaultLog.ConfigureLog(_entries.Enqueue);
    public void Dispose() => GameActionFaultLog.ConfigureLog(null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedWorkerFailureLogsOriginalCauseOnceAndRecoveryAllowsAnotherReport(bool projection)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.worker.failure-log");
        if (projection) definition.FailNextProjections(2);
        else definition.FailNextEvaluations(2);
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;

        RunFailure(runner, clock);
        var first = runner.Snapshot.Fault;
        var entry = Assert.Single(_entries);
        Assert.Contains(projection ? "synthetic projection fault" : "synthetic evaluation fault", entry);
        Assert.Contains(projection ? ".Project(" : ".Evaluate(", entry);
        Assert.Contains(definition.ServiceId.Value, entry);
        Assert.Contains("Reference MCP-", first.Reason);
        Assert.DoesNotContain("synthetic", first.Reason);

        clock.AdvanceTo(runner.Snapshot.NextWakeDue);
        RunFailure(runner, clock);
        Assert.Equal(2, runner.Snapshot.Fault.OccurrenceCount);
        Assert.Equal(first.Reason, runner.Snapshot.Fault.Reason);
        Assert.Single(_entries);

        clock.AdvanceTo(runner.Snapshot.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        Assert.False(runner.Snapshot.Fault.IsValid);
        Assert.Empty(runner.Snapshot.Fault.Reason);
        if (projection) definition.FailNextProjections(1);
        else definition.FailNextEvaluations(1);
        RunFailure(runner, clock);
        Assert.Equal(1, runner.Snapshot.Fault.OccurrenceCount);
        Assert.Equal(2, _entries.Count);
        Assert.NotEqual(first.Reason, runner.Snapshot.Fault.Reason);
    }

    [Fact]
    public void FailedStateRecreationKeepsBothOriginalEvaluationAndFactoryCausesInLog()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.worker.recreation-log");
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        definition.FailNextEvaluations(1);
        definition.FailNextStateFactories(1);

        RunFailure(runner, clock);

        Assert.Equal(ServiceFaultCategory.StateFactory, runner.Snapshot.Fault.Category);
        Assert.Equal(1, runner.Snapshot.Fault.OccurrenceCount);
        Assert.Contains(_entries, entry => entry.Contains("synthetic evaluation fault"));
        Assert.Contains(_entries, entry => entry.Contains("synthetic state factory fault"));
        Assert.Equal(2, _entries.Count);
        clock.AdvanceTo(runner.Snapshot.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        Assert.False(runner.Snapshot.Fault.IsValid);
    }

    [Fact]
    public void FailedStateReleaseLogsItsCauseWithoutPreventingRecreationOrShutdown()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.worker.release-log");
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        definition.FailNextEvaluations(1);
        definition.FailNextStateReleases(1);
        RunFailure(runner, clock);
        Assert.Contains(_entries, entry => entry.Contains("synthetic state release fault") && entry.Contains("state release"));
        Assert.Equal(2, definition.StateCreateCount);
        clock.AdvanceTo(runner.Snapshot.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        Assert.False(runner.Snapshot.Fault.IsValid);
        definition.FailNextStateReleases(1);
        registration.Dispose();
        Assert.True(runner.WaitForWorkerExit(ServiceCycleTestDeadline.Value));
        Assert.Equal(2, _entries.Count(entry => entry.Contains("synthetic state release fault")));
    }

    [Fact]
    public void InitialStateFactoryFailureLogsCauseAndRetriesNormally()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.worker.factory-log");
        definition.FailNextStateFactories(1);
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;
        RunFailure(runner, clock);
        Assert.Contains("synthetic state factory fault", Assert.Single(_entries));
        Assert.Equal(ServiceFaultCategory.StateFactory, runner.Snapshot.Fault.Category);
        clock.AdvanceTo(runner.Snapshot.NextWakeDue);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        Assert.False(runner.Snapshot.Fault.IsValid);
    }

    [Fact]
    public void LifecycleConstructionFailureLogsCauseOnceAndClearsOnReplacement()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("test.lifecycle.failure-log") { ActionCount = 0 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        definition.FailNextWorkerFactories(2);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        var first = registration.LifecycleSnapshot.ConstructionFault;
        Assert.Contains("synthetic worker-definition construction failure", Assert.Single(_entries));
        Assert.Contains("Reference MCP-", first.Reason);

        clock.AdvanceTo(registration.LifecycleSnapshot.ConstructionRetryDue);
        pump.PumpFrame(1);
        Assert.Equal(2, registration.LifecycleSnapshot.ConstructionFault.OccurrenceCount);
        Assert.Equal(first.Reason, registration.LifecycleSnapshot.ConstructionFault.Reason);
        Assert.Single(_entries);
        clock.AdvanceTo(registration.LifecycleSnapshot.ConstructionRetryDue);
        pump.PumpFrame(2);
        Assert.False(registration.LifecycleSnapshot.ConstructionFault.IsValid);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
    }

    private static void RunFailure(ServiceRunner<ExecutionState, ExecutionAction> runner, ThreadSafeTestClock clock)
    {
        ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        ServiceRunnerTestWait.ForHandback(runner);
        Assert.True(runner.Snapshot.Fault.IsValid);
    }
}
