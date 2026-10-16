using System;
using System.Runtime.CompilerServices;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceLifetimeShutdownTests
{
    [Fact]
    public void SignalOnlyShutdownNeverWaitsForBlockedEvaluation()
    {
        var clock = new ThreadSafeTestClock(100);
        var registry = new ServiceCycleRegistry(1, clock);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition("test.execution.shutdown")
        {
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));

        HeldGate.AssertReturnsWhileHeld(registration.Dispose, release.Set);
        Assert.True(runner.Snapshot.Handoff.StopRequested);
        Assert.NotEqual(ServiceHandoffPhase.Stopped, runner.Snapshot.Handoff.Phase);

        release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => runner.Snapshot.Handoff.Phase == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
        Assert.Equal(1, definition.StateReleaseCount);
        registry.Dispose();
    }

    [Fact]
    public void StoppedHandoffDoesNotRetainPinnedConfiguration()
    {
        var clock = new ThreadSafeTestClock(100);
        var registry = new ServiceCycleRegistry(1, clock);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition("test.execution.shutdown-config")
        {
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        var registration = RegisterPinningConfiguration(registry, definition, out var reference);
        var runner = registration.Runner;

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));
        registration.Dispose();
        release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => runner.ProbeHandoff().Phase == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));

        // Superseding the snapshot is what makes the question askable: the suite's publication holds
        // whatever it published last, so only a snapshot nothing publishes any more can show whether
        // the stopped handoff is still pinning it. The registry stays alive on purpose — the handoff
        // is the only suspect left.
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(92));
        ForceCollection(reference);
        Assert.False(reference.IsAlive);
        GC.KeepAlive(runner);
        registry.Dispose();
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ServiceRegistration<ExecutionState, ExecutionAction>
        RegisterPinningConfiguration(
            ServiceCycleRegistry registry,
            ExecutionServiceDefinition definition,
            out WeakReference reference)
    {
        var pinned = TestSuiteConfiguration.WithSetting(91);
        reference = new WeakReference(pinned);
        registry.Configuration.Publish(pinned);
        return registry.Register(definition, new LifecycleGeneration(1));
    }
}
