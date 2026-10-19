using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceTargetRetryGateTests
{
    private static readonly ServiceFaultRecoveryPolicy Policy = new(
        new MonotonicDuration(10),
        new MonotonicDuration(80));

    private static readonly ServiceRetryTarget First = new(
        new Guid("22222222-2222-2222-2222-222222222222"),
        ServiceActionNativeTypeId.ConsumableSO);

    private static readonly ServiceRetryTarget Second = new(
        new Guid("33333333-3333-3333-3333-333333333333"),
        ServiceActionNativeTypeId.ConsumableSO);

    [Fact]
    public void EachFailureInARowDoublesTheWaitUpToThePolicyCap()
    {
        var gate = new ServiceTargetRetryGate(Policy);

        var waits = new long[6];
        for (var index = 0; index < waits.Length; index++)
            waits[index] = gate.Record(First, "no change", new MonotonicTimestamp(100)).Backoff.Ticks;

        Assert.Equal(new long[] { 10, 20, 40, 80, 80, 80 }, waits);
        Assert.Equal(6, gate.Snapshot.Latest.ConsecutiveFailures);
        Assert.Equal(new MonotonicTimestamp(180), gate.Snapshot.Latest.RetryDue);
    }

    [Fact]
    public void ACallerCapHoldsTheDoublingBelowThePolicyCap()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        var cap = new MonotonicDuration(30);

        var waits = new long[4];
        for (var index = 0; index < waits.Length; index++)
            waits[index] = gate.Record(First, "no change", default, maximumBackoff: cap).Backoff.Ticks;

        Assert.Equal(new long[] { 10, 20, 30, 30 }, waits);
    }

    [Fact]
    public void ACommitEndsOnlyThatTargetsEpisode()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        gate.Record(First, "first failed", new MonotonicTimestamp(100));
        gate.Record(Second, "second failed", new MonotonicTimestamp(100));
        var before = gate.Snapshot;

        gate.ClearOnCommit(First);

        Assert.False(gate.Snapshot.TryFind(First, out _));
        Assert.True(gate.Snapshot.TryFind(Second, out var remaining));
        Assert.Equal("second failed", remaining.Reason);
        Assert.Equal(2, before.Count);
        Assert.Equal(1, gate.Record(First, "first failed again", new MonotonicTimestamp(200)).ConsecutiveFailures);
    }

    [Fact]
    public void AFailureSeenInALaterWorldOutlastsCommitsAndEndsWhenTheTargetWorks()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        gate.Record(First, "cancelled", new MonotonicTimestamp(100), awaitsObservation: true);

        gate.ClearOnCommit(First);
        var second = gate.Record(First, "cancelled again", new MonotonicTimestamp(200), awaitsObservation: true);
        gate.Clear(First);

        Assert.Equal(2, second.ConsecutiveFailures);
        Assert.Equal(new MonotonicDuration(20), second.Backoff);
        Assert.Equal(0, gate.Snapshot.Count);
    }

    [Fact]
    public void PlanningWaitsUntilTheTargetIsDue()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        gate.Record(First, "no change", new MonotonicTimestamp(100));

        Assert.True(gate.Snapshot.IsWaiting(First, new MonotonicTimestamp(109)));
        Assert.False(gate.Snapshot.IsWaiting(First, new MonotonicTimestamp(110)));
        Assert.False(gate.Snapshot.IsWaiting(Second, new MonotonicTimestamp(100)));
    }

    [Fact]
    public void AWaitNeverOutlastsTheNextTargetThatIsStillToComeDue()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        gate.Record(First, "no change", new MonotonicTimestamp(100));
        gate.Record(Second, "no change", new MonotonicTimestamp(100));
        gate.Record(Second, "no change", new MonotonicTimestamp(100));
        var forever = new MonotonicTimestamp(long.MaxValue);

        Assert.Equal(new MonotonicTimestamp(110), gate.WakeNoLaterThanNextRetry(forever, new MonotonicTimestamp(100)));
        Assert.Equal(new MonotonicTimestamp(120), gate.WakeNoLaterThanNextRetry(forever, new MonotonicTimestamp(110)));
        Assert.Equal(forever, gate.WakeNoLaterThanNextRetry(forever, new MonotonicTimestamp(120)));
        Assert.Equal(new MonotonicTimestamp(105), gate.WakeNoLaterThanNextRetry(new MonotonicTimestamp(105), new MonotonicTimestamp(100)));
    }

    [Fact]
    public void AFaultedActionBacksItsTargetOffWithDoublingUntilItCommits()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.fault")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;
        TestWorldCollector.CollectedAtActivation(registry);

        var waits = new long[5];
        for (var failures = 1; failures <= waits.Length; failures++)
        {
            ServiceCyclePumpTestWait.PumpUntil(
                pump,
                ref frame,
                () => Failures(registration) == failures,
                clock,
                registry);
            var entry = registration.Runner.Snapshot.RetryGate.Latest;
            Assert.Equal(ExecutionServiceDefinition.Target, entry.Target);
            Assert.Equal(CommonActionResultCodes.AdapterFault.Name, entry.Reason);
            waits[failures - 1] = entry.Backoff.Ticks;
        }
        service.FaultAtIndex = -1;
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => registration.Runner.Snapshot.RetryGate.Count == 0,
            clock,
            registry);

        Assert.Equal(new long[] { 10, 20, 40, 80, 80 }, waits);
        Assert.Equal(5, service.LastContextRetryFailures);
    }

    [Fact]
    public void ANewLifecycleStartsWithNoTargetBackingOff()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.lifecycle")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;
        TestWorldCollector.CollectedAtActivation(registry);
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => Failures(registration) == 1, clock, registry);
        service.FaultAtIndex = -1;
        service.ActionCount = 0;
        var evaluations = service.EvaluationCount;
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => service.EvaluationCount > evaluations, clock, registry);
        var contextBeforeReplacement = service.LastContextRetryFailures;

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => registration.LifecycleSnapshot.ActiveLifecycle.Value == 2,
            clock,
            registry);
        evaluations = service.EvaluationCount;
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => service.EvaluationCount > evaluations, clock, registry);

        Assert.Equal(1, contextBeforeReplacement);
        Assert.Equal(0, registration.Runner.Snapshot.RetryGate.Count);
        Assert.Equal(0, service.LastContextRetryFailures);
    }

    [Fact]
    public void AFailureTheWorkerReportsBacksOffAndWakesTheServiceWhenDue()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.report");
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        var runner = registration.Runner;
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);

        service.EvaluationWake = WakePolicy.AfterDecision(new MonotonicDuration(1_000_000));
        service.ReportFailureOnNextEvaluations(1);
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);
        var reported = runner.Snapshot;
        var entry = reported.RetryGate.Latest;
        service.EvaluationWake = WakePolicy.Immediate;
        service.ActionCount = 1;
        clock.AdvanceTo(entry.RetryDue);
        ServiceRunnerTestWait.RunAndDrain(runner, clock, 1);
        var afterCommit = runner.Snapshot.RetryGate;
        service.ActionCount = 0;
        service.ReportVerifiedOnNextEvaluation();
        ServiceRunnerTestWait.RunZeroActionCycle(runner, clock);

        Assert.Equal("synthetic surprise", entry.Reason);
        Assert.True(entry.AwaitsObservation);
        Assert.Equal(new MonotonicTimestamp(1_010), entry.RetryDue);
        Assert.Equal(entry.RetryDue, reported.NextWakeDue);
        Assert.Equal(1, afterCommit.Count);
        Assert.Equal(1, service.LastContextRetryFailures);
        Assert.Equal(0, runner.Snapshot.RetryGate.Count);
    }

    private static int Failures(
        ServiceRegistration<ExecutionState, ExecutionAction> registration)
    {
        var gate = registration.Runner.Snapshot.RetryGate;
        return gate.Count == 0 ? 0 : gate.Latest.ConsecutiveFailures;
    }
}
