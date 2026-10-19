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

    private static readonly ServiceActionResultCode Code = CommonActionResultCodes.AdapterFault;

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
            waits[index] = gate.Record(First, Code, "no change", new MonotonicTimestamp(100)).Backoff.Ticks;

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
            waits[index] = gate.Record(First, Code, "no change", default, maximumBackoff: cap).Backoff.Ticks;

        Assert.Equal(new long[] { 10, 20, 30, 30 }, waits);
    }

    [Fact]
    public void ACallerFirstWaitStartsTheDoublingAndMayNotUndercutThePolicyOrOutgrowItsCap()
    {
        var gate = new ServiceTargetRetryGate(Policy);

        var waits = new long[4];
        for (var index = 0; index < waits.Length; index++)
        {
            waits[index] = gate.Record(
                First, Code, "paid, no work", default,
                maximumBackoff: new MonotonicDuration(100),
                initialBackoff: new MonotonicDuration(30)).Backoff.Ticks;
        }

        Assert.Equal(new long[] { 30, 60, 100, 100 }, waits);
        Assert.False(gate.Allows(new MonotonicDuration(5), null));
        Assert.False(gate.Allows(new MonotonicDuration(90), null));
        Assert.False(gate.Allows(new MonotonicDuration(120), new MonotonicDuration(100)));
        Assert.True(gate.Allows(new MonotonicDuration(120), new MonotonicDuration(120)));
    }

    [Fact]
    public void AReporterCapMayExceedThePolicyCapButNotUndercutTheFirstWait()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        var store = new ReusableActionStore<ExecutionAction>();
        var buffer = new ServiceRetryReportBuffer(Policy);
        var writer = new ServiceActionWriter<ExecutionAction>(store, buffer);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceActionWriter<ExecutionAction>(store, buffer)
                .ReportFailure(First, Code, "too eager", new MonotonicDuration(5)));
        var waits = new long[6];
        for (var index = 0; index < waits.Length; index++)
        {
            buffer.Reset();
            writer.ReportFailure(First, Code, "slow target", new MonotonicDuration(200));
            gate.Apply(buffer.ToArrayOrNull(), new MonotonicTimestamp(100));
            waits[index] = gate.Snapshot.Latest.Backoff.Ticks;
        }

        Assert.Equal(new long[] { 10, 20, 40, 80, 160, 200 }, waits);
    }

    [Fact]
    public void ACommitEndsOnlyThatTargetsEpisode()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        gate.Record(First, Code, "first failed", new MonotonicTimestamp(100));
        gate.Record(Second, Code, "second failed", new MonotonicTimestamp(100));
        var before = gate.Snapshot;

        gate.ClearOnCommit(First);

        Assert.False(gate.Snapshot.TryFind(First, out _));
        Assert.True(gate.Snapshot.TryFind(Second, out var remaining));
        Assert.Equal("second failed", remaining.Reason);
        Assert.Equal(2, before.Count);
        Assert.Equal(1, gate.Record(First, Code, "first failed again", new MonotonicTimestamp(200)).ConsecutiveFailures);
    }

    [Fact]
    public void AFailureSeenInALaterWorldOutlastsCommitsAndEndsWhenTheTargetWorks()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        gate.Record(First, Code, "cancelled", new MonotonicTimestamp(100), awaitsObservation: true);

        gate.ClearOnCommit(First);
        var second = gate.Record(First, Code, "cancelled again", new MonotonicTimestamp(200), awaitsObservation: true);
        gate.Clear(First);

        Assert.Equal(2, second.ConsecutiveFailures);
        Assert.Equal(new MonotonicDuration(20), second.Backoff);
        Assert.Equal(0, gate.Snapshot.Count);
    }

    [Fact]
    public void PlanningWaitsUntilTheTargetIsDue()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        gate.Record(First, Code, "no change", new MonotonicTimestamp(100));

        Assert.True(gate.Snapshot.IsWaiting(First, new MonotonicTimestamp(109)));
        Assert.False(gate.Snapshot.IsWaiting(First, new MonotonicTimestamp(110)));
        Assert.False(gate.Snapshot.IsWaiting(Second, new MonotonicTimestamp(100)));
    }

    [Fact]
    public void AWaitNeverOutlastsTheNextTargetThatIsStillToComeDue()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        gate.Record(First, Code, "no change", new MonotonicTimestamp(100));
        gate.Record(Second, Code, "no change", new MonotonicTimestamp(100));
        gate.Record(Second, Code, "no change", new MonotonicTimestamp(100));
        var forever = new MonotonicTimestamp(long.MaxValue);

        Assert.Equal(new MonotonicTimestamp(110), gate.WakeNoLaterThanNextRetry(forever, new MonotonicTimestamp(100)));
        Assert.Equal(new MonotonicTimestamp(120), gate.WakeNoLaterThanNextRetry(forever, new MonotonicTimestamp(110)));
        Assert.Equal(forever, gate.WakeNoLaterThanNextRetry(forever, new MonotonicTimestamp(120)));
        Assert.Equal(new MonotonicTimestamp(105), gate.WakeNoLaterThanNextRetry(new MonotonicTimestamp(105), new MonotonicTimestamp(100)));
    }

    [Fact]
    public void AQuietFirstFailureIsAnnouncedOnlyOnceAFresherWorldFailsItAgain()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        var planned = new WorldGeneration(4);

        var first = gate.Record(First, Code, "gone", default, quietFirstFailure: true, plannedFrom: planned);
        var sameWorld = gate.Record(First, Code, "gone", default, quietFirstFailure: true, plannedFrom: planned);
        var announced = gate.TryGetLatestAnnouncedOrDefault();
        var fresher = gate.Record(First, Code, "gone", default, quietFirstFailure: true, plannedFrom: planned.Next());
        var later = gate.Record(First, Code, "gone", default, quietFirstFailure: true, plannedFrom: planned.Next().Next());

        Assert.True(first.IsQuiet);
        Assert.True(sameWorld.IsQuiet);
        Assert.Null(announced);
        Assert.False(fresher.IsQuiet);
        Assert.False(later.IsQuiet);
        Assert.Equal(3, fresher.ConsecutiveFailures);
        Assert.Equal(fresher.Target, gate.TryGetLatestAnnouncedOrDefault()?.Target);
    }

    [Fact]
    public void TheNarratorAnnouncesEachEpisodeOnceWhenItStartsAndOnceWhenItEnds()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        var warnings = new System.Collections.Generic.List<string>();
        var narrator = new OrbModding.Common.ServiceRetryEpisodeNarrator("Feature", warnings.Add);

        gate.Record(Second, Code, "first try gone", default, quietFirstFailure: true, plannedFrom: new WorldGeneration(1));
        narrator.Observe(gate.Snapshot, default);
        gate.Clear(Second);
        narrator.Observe(gate.Snapshot, default);
        for (var failures = 0; failures < 3; failures++)
        {
            gate.Record(First, Code, "no change", default);
            narrator.Observe(gate.Snapshot, default);
        }
        gate.ClearOnCommit(First);
        narrator.Observe(gate.Snapshot, default);

        Assert.Equal(2, warnings.Count);
        Assert.Contains("Feature backs off", warnings[0]);
        Assert.Contains("after 1 failed attempt and tries again in", warnings[0]);
        Assert.EndsWith(": no change", warnings[0]);
        Assert.Contains("Feature stops backing off", warnings[1]);
        Assert.EndsWith("after 3 failed attempts in a row.", warnings[1]);
        Assert.DoesNotContain(warnings, line => line.Contains(Second.Id.ToString("D")));
    }

    [Fact]
    public void AnAnnouncedTargetIsBackingOffOnlyUntilItsRetryIsDue()
    {
        var gate = new ServiceTargetRetryGate(Policy);
        var narrator = new OrbModding.Common.ServiceRetryEpisodeNarrator("Feature", static _ => { });
        var entry = gate.Record(First, Code, "no change", new MonotonicTimestamp(100));

        var recorded = narrator.Observe(gate.Snapshot, new MonotonicTimestamp(100));
        var waiting = narrator.TryGetBackingOff(out _);
        var stillWaiting = narrator.Observe(gate.Snapshot, new MonotonicTimestamp(109));
        var due = narrator.Observe(gate.Snapshot, entry.RetryDue);

        Assert.True(recorded);
        Assert.True(waiting);
        Assert.False(stillWaiting);
        Assert.True(due);
        Assert.False(narrator.TryGetBackingOff(out _));
        Assert.True(gate.Snapshot.TryFind(First, out _));
        Assert.Null(gate.TryGetLatestAnnouncedOrDefault(entry.RetryDue));
    }

    [Fact]
    public void ARefusalThatBacksOffItsTargetRetriesItWhenDueUnderTheCapItCarries()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.refusal")
        {
            ActionCount = 1,
            RejectAtIndex = 0,
            RefusalsBackOff = true,
            RefusalBackoffCap = new MonotonicDuration(20),
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;
        TestWorldCollector.CollectedAtActivation(registry);

        var waits = new long[3];
        for (var failures = 1; failures <= waits.Length; failures++)
        {
            ServiceCyclePumpTestWait.PumpUntil(
                pump, ref frame, () => Failures(registration) == failures, clock, registry);
            waits[failures - 1] = registration.Runner.Snapshot.RetryGate.Latest.Backoff.Ticks;
        }
        var entry = registration.Runner.Snapshot.RetryGate.Latest;
        service.RejectAtIndex = -1;
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => registration.Runner.Snapshot.RetryGate.Count == 0, clock, registry);

        Assert.Equal(new long[] { 10, 20, 20 }, waits);
        Assert.Equal(CommonActionResultCodes.PolicyRejected, entry.Code);
        Assert.Equal(CommonActionResultCodes.PolicyRejected.Name, entry.Reason);
        Assert.False(entry.IsQuiet);
    }

    [Fact]
    public void AMarkedRefusalDoublesPastThePolicyMaximumUpToTheCapItCarries()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.refusal.long")
        {
            ActionCount = 1,
            RejectAtIndex = 0,
            RefusalsBackOff = true,
            RefusalBackoffCap = new MonotonicDuration(200),
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;
        TestWorldCollector.CollectedAtActivation(registry);

        var waits = new long[7];
        for (var failures = 1; failures <= waits.Length; failures++)
        {
            ServiceCyclePumpTestWait.PumpUntil(
                pump, ref frame, () => Failures(registration) == failures, clock, registry);
            waits[failures - 1] = registration.Runner.Snapshot.RetryGate.Latest.Backoff.Ticks;
        }

        Assert.Equal(80, service.FaultRecoveryPolicy.MaximumBackoff.Ticks);
        Assert.Equal(new long[] { 10, 20, 40, 80, 160, 200, 200 }, waits);
    }

    [Fact]
    public void ASkipThatBacksOffItsTargetStillCompletesTheBatch()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.skip")
        {
            ActionCount = 1,
            SkipWithoutNativeAtIndex = 0,
            RefusalsBackOff = true,
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;
        TestWorldCollector.CollectedAtActivation(registry);

        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => Failures(registration) == 1, clock, registry);
        var entry = registration.Runner.Snapshot.RetryGate.Latest;

        Assert.Equal(CommonActionResultCodes.Skipped, entry.Code);
        Assert.Equal(new MonotonicDuration(10), entry.Backoff);
        Assert.Equal(BatchTerminalDisposition.Completed, registration.Runner.Snapshot.PreviousReceipt.Disposition);
    }

    [Fact]
    public void ARefusalCappedBelowTheFirstWaitFaultsItsAction()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.cap")
        {
            ActionCount = 1,
            RejectAtIndex = 0,
            RefusalsBackOff = true,
            RefusalBackoffCap = new MonotonicDuration(5),
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;
        TestWorldCollector.CollectedAtActivation(registry);

        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => Failures(registration) == 1, clock, registry);
        var entry = registration.Runner.Snapshot.RetryGate.Latest;

        Assert.Equal(CommonActionResultCodes.AdapterFault, entry.Code);
        Assert.Contains("below its initial fault backoff", entry.Reason);
    }

    [Fact]
    public void AMarkedFaultBacksItsTargetOffFromTheFirstWaitItCarries()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.fault.marked")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
            FaultsBackOff = true,
            RefusalInitialBackoff = new MonotonicDuration(30),
            RefusalBackoffCap = new MonotonicDuration(200),
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;
        TestWorldCollector.CollectedAtActivation(registry);

        var waits = new long[4];
        for (var failures = 1; failures <= waits.Length; failures++)
        {
            ServiceCyclePumpTestWait.PumpUntil(
                pump, ref frame, () => Failures(registration) == failures, clock, registry);
            waits[failures - 1] = registration.Runner.Snapshot.RetryGate.Latest.Backoff.Ticks;
        }

        Assert.Equal(new long[] { 30, 60, 120, 200 }, waits);
        Assert.Equal(CommonActionResultCodes.AdapterFault.Name, registration.Runner.Snapshot.RetryGate.Latest.Reason);
    }

    [Fact]
    public void AFirstWaitBelowThePolicysFaultsItsAction()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("retry.first")
        {
            ActionCount = 1,
            RejectAtIndex = 0,
            RefusalsBackOff = true,
            RefusalInitialBackoff = new MonotonicDuration(5),
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;
        TestWorldCollector.CollectedAtActivation(registry);

        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => Failures(registration) == 1, clock, registry);
        var entry = registration.Runner.Snapshot.RetryGate.Latest;

        Assert.Equal(CommonActionResultCodes.AdapterFault, entry.Code);
        Assert.Contains("below its initial fault backoff", entry.Reason);
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
        service.EndRetryOnNextEvaluation();
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

internal static class ServiceTargetRetryGateTestExtensions
{
    internal static ServiceRetryEntry? TryGetLatestAnnouncedOrDefault(
        this ServiceTargetRetryGate gate,
        MonotonicTimestamp now = default) =>
        gate.Snapshot.TryGetLatestAnnounced(now, out var entry) ? entry : null;
}
