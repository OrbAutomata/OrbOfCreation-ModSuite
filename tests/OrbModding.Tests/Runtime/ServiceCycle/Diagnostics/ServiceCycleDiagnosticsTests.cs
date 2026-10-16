using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Diagnostics;

public sealed class ServiceCycleDiagnosticsTests
{
    [Fact]
    public void CallerBufferReportsExactCapacityOrderAndUnavailableSlots()
    {
        using var registry = new ServiceCycleRegistry(3);
        using var first = registry.Register(
            new ExecutionServiceDefinition("diagnostics.order.a"),
            new LifecycleGeneration(1));
        var second = registry.Register(
            new ExecutionServiceDefinition("diagnostics.order.b"),
            new LifecycleGeneration(1));
        using var third = registry.Register(
            new ExecutionServiceDefinition("diagnostics.order.c"),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var beforeDisposal = new ServiceCycleServiceDiagnosticsSnapshot[3];
        ServiceCycleDiagnostics.CopyServices(pump, beforeDisposal);
        var secondInstance = beforeDisposal[1].RegistrationInstance;

        second.Dispose();
        var completeBuffer = new ServiceCycleServiceDiagnosticsSnapshot[3];
        var complete = ServiceCycleDiagnostics.CopyServices(pump, completeBuffer);

        Assert.Equal(3, complete.RequiredCount);
        Assert.Equal(3, complete.WrittenCount);
        Assert.Equal(1, complete.UnavailableCount);
        Assert.True(complete.IsComplete);
        Assert.Equal(new[] { 0, 1, 2 }, completeBuffer.Select(snapshot => snapshot.Ordinal));
        Assert.Equal(
            new[] { "diagnostics.order.a", "diagnostics.order.b", "diagnostics.order.c" },
            completeBuffer.Select(snapshot => snapshot.ServiceId.Value));
        Assert.Equal(ServiceCycleDiagnosticsAvailability.Disposed, completeBuffer[1].Availability);
        Assert.Equal(ServiceCycleOperationalPhase.Disposed, completeBuffer[1].Phase);
        Assert.True(completeBuffer[1].Lifecycle.IsHistorical);
        Assert.Equal(secondInstance, completeBuffer[1].RegistrationInstance);
        Assert.Equal(ServiceRunnerPositionState.Retiring, completeBuffer[1].Lifecycle.Position0.State);

        var shortBuffer = new ServiceCycleServiceDiagnosticsSnapshot[1];
        var shortCopy = ServiceCycleDiagnostics.CopyServices(pump, shortBuffer);
        Assert.Equal(3, shortCopy.RequiredCount);
        Assert.Equal(1, shortCopy.WrittenCount);
        Assert.Equal(1, shortCopy.UnavailableCount);
        Assert.False(shortCopy.IsComplete);
        Assert.Equal(0, shortBuffer[0].Ordinal);

        var empty = ServiceCycleDiagnostics.CopyServices(
            pump,
            Span<ServiceCycleServiceDiagnosticsSnapshot>.Empty);
        Assert.Equal(3, empty.RequiredCount);
        Assert.Equal(0, empty.WrittenCount);
        Assert.Equal(1, empty.UnavailableCount);
    }

    [Fact]
    public void EvaluatingSnapshotKeepsPinnedContextSeparateFromLatestConfiguration()
    {
        var clock = new ThreadSafeTestClock(100);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var registry = new ServiceCycleRegistry(1, clock, measureWorkerAllocations: true);
        var definition = new ExecutionServiceDefinition("diagnostics.context")
        {
            ActionCount = 1,
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        try
        {
            Assert.True(registration.Runner.TryStartCycle(clock.Now).Queued);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));

            var buffer = new ServiceCycleServiceDiagnosticsSnapshot[1];
            var copy = ServiceCycleDiagnostics.CopyServices(pump, buffer);
            var snapshot = buffer[0];

            Assert.True(copy.IsComplete);
            Assert.Equal(ServiceCycleDiagnosticsAvailability.Available, snapshot.Availability);
            Assert.Equal(ServiceCycleOperationalPhase.Evaluating, snapshot.Phase);
            Assert.Equal(ServiceCycleHandoffDiagnosticsPhase.Evaluating, snapshot.Handoff.Phase);
            Assert.True(snapshot.Context.HasCurrentCycle);
            Assert.Equal((ulong)1, snapshot.Context.CurrentCycle.Config.Value);
            Assert.Equal((ulong)1, snapshot.Context.CurrentCycle.Strategy.Value);
            Assert.Equal((ulong)2, snapshot.Context.LatestConfiguration.Value);
            Assert.Equal(
                ServiceCycleDiagnosticsValueAvailability.Available,
                snapshot.Context.LatestConfigurationAvailability);
            Assert.Equal(
                ServiceCycleDiagnosticsValueAvailability.Available,
                snapshot.Context.LatestStrategyAvailability);
            Assert.Equal(1UL, snapshot.Context.LatestStrategy.Value);
            Assert.True(snapshot.Worker.IsBackground);
            Assert.True(snapshot.Worker.ThreadId > 0);
            Assert.Equal(
                ServiceCycleStorageDiagnosticsAvailability.LastPublished,
                snapshot.Storage.Availability);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void ProjectionIsNonBlockingUnderHandoffContentionAndAllocatesNothingAfterWarmup()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new ExecutionServiceDefinition("diagnostics.contention"),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var buffer = new ServiceCycleServiceDiagnosticsSnapshot[1];
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));
        registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(2));
        using var contention = new HandoffGateContention(registration.Runner);
        contention.Acquire();
        var stopwatch = Stopwatch.StartNew();
        var contended = ServiceCycleDiagnostics.CopyServices(pump, buffer);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, contended.UnavailableCount);
        Assert.Equal(ServiceCycleDiagnosticsAvailability.HandoffContended, buffer[0].Availability);
        Assert.Equal(ServiceCycleOperationalPhase.Unavailable, buffer[0].Phase);
        Assert.Equal(ServiceRunnerPositionState.Current, buffer[0].Lifecycle.Position0.State);
        Assert.Equal((ulong)2, buffer[0].Context.LatestConfiguration.Value);
        Assert.Equal(
            ServiceCycleDiagnosticsValueAvailability.Available,
            buffer[0].Context.LatestConfigurationAvailability);
        Assert.Equal(2UL, buffer[0].Context.LatestStrategy.Value);
        Assert.Equal(
            ServiceCycleDiagnosticsValueAvailability.Available,
            buffer[0].Context.LatestStrategyAvailability);
        var contendedObserved = 0;
        var contendedAllocated = AllocationProbe.MeasureRepeated(
            1_000,
            () => contendedObserved += ServiceCycleDiagnostics.CopyServices(pump, buffer).UnavailableCount);
        Assert.Equal(2_000, contendedObserved);
        Assert.Equal(0, contendedAllocated);
        contention.Release();

        var observed = 0;
        var allocated = AllocationProbe.MeasureRepeated(1_000, () =>
        {
            observed += ServiceCycleDiagnostics.CopyServices(pump, buffer).WrittenCount;
            observed += (int)ServiceCycleDiagnostics.ReadPump(pump).AcceptedFrameCount;
        });

        Assert.Equal(2_000, observed);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ActiveBatchProjectionCarriesExactBatchProjectionAndWorkerEvidence()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock, measureWorkerAllocations: true);
        var definition = new ExecutionServiceDefinition("diagnostics.batch") { ActionCount = 2 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        Assert.True(registration.Runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(registration.Runner.TryAcquireResponse());
        Assert.True(registration.Runner.TryExecuteOne(clock.Now).Attempted);
        clock.Advance(new MonotonicDuration(5));

        var buffer = new ServiceCycleServiceDiagnosticsSnapshot[1];
        ServiceCycleDiagnostics.CopyServices(pump, buffer);
        var snapshot = buffer[0];

        Assert.Equal(ServiceCycleOperationalPhase.DrainingBatch, snapshot.Phase);
        Assert.Equal(ServiceCycleHandoffDiagnosticsPhase.MainOwnedBatch, snapshot.Handoff.Phase);
        Assert.True(snapshot.ActiveBatch.IsPresent);
        Assert.Equal(snapshot.Context.CurrentCycle, snapshot.ActiveBatch.Cycle);
        Assert.Equal(snapshot.Context.CurrentBatch, snapshot.ActiveBatch.Batch);
        Assert.Equal(2, snapshot.ActiveBatch.ActionCount);
        Assert.Equal(1, snapshot.ActiveBatch.ActionCursor);
        Assert.Equal(1, snapshot.ActiveBatch.CommittedCount);
        Assert.Equal(1, snapshot.ActiveBatch.NativeOutcome.NativeCallsAttempted);
        Assert.Equal(5, snapshot.ActiveBatch.Age.Ticks);
        Assert.True(snapshot.Timing.HasResponseAge);
        Assert.Equal(5, snapshot.Timing.ResponseAge.Ticks);
        Assert.True(snapshot.LatestProjection.IsPresent);
        Assert.True(snapshot.Worker.MeasuredCycleCount > 0);
        Assert.True(snapshot.Handoff.TransitionCount > 0);
    }

    [Fact]
    public void LifecycleStormProjectsRunnerlessStateAndLatestTerminalSequenceTruthfully()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("diagnostics.lifecycle");
        using var firstGate = definition.BlockEvaluation(1);
        using var secondGate = definition.BlockEvaluation(2);
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 2L;

        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => firstGate.Entered.IsSet, collector: registry);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => secondGate.Entered.IsSet, collector: registry);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(3)));

        var buffer = new ServiceCycleServiceDiagnosticsSnapshot[1];
        var copy = ServiceCycleDiagnostics.CopyServices(pump, buffer);
        var snapshot = buffer[0];

        Assert.Equal(1, copy.UnavailableCount);
        Assert.Equal(ServiceCycleDiagnosticsAvailability.NoCurrentRunner, snapshot.Availability);
        Assert.Equal(ServiceCycleOperationalPhase.Orphaned, snapshot.Phase);
        Assert.Equal(ServiceRunnerPositionState.Retiring, snapshot.Lifecycle.Position0.State);
        Assert.Equal(ServiceRunnerPositionState.Retiring, snapshot.Lifecycle.Position1.State);
        Assert.Equal(
            ServiceCycleStorageDiagnosticsAvailability.LastPublished,
            snapshot.Lifecycle.Position0.Storage.Availability);
        Assert.Equal(
            ServiceCycleStorageDiagnosticsAvailability.LastPublished,
            snapshot.Lifecycle.Position1.Storage.Availability);
        Assert.Equal((ulong)3, snapshot.Lifecycle.DesiredLifecycle.Value);
        Assert.True(snapshot.Lifecycle.LatestTerminal.IsPresent);
        Assert.Equal(snapshot.Lifecycle.LatestTerminal.Sequence, snapshot.Lifecycle.LatestTerminalSequence);
        Assert.Equal(2, snapshot.Lifecycle.LatestTerminalSequence);

        firstGate.Release.Set();
        secondGate.Release.Set();
    }

    [Fact]
    public void PumpSnapshotRetainsCumulativeReportsAndEmergencyEpisodeReason()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var registration = registry.Register(
            new ExecutionServiceDefinition("diagnostics.pump"),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var accepted = pump.PumpFrame(1);
        var rejected = pump.PumpFrame(1);
        pump.SetEmergencyStop(true, EmergencyStopReason.SafetyInterlock);
        var engaged = ServiceCycleDiagnostics.ReadPump(pump);

        Assert.True(accepted.Accepted);
        Assert.False(rejected.Accepted);
        Assert.Equal(1, engaged.AcceptedFrameCount);
        Assert.Equal(1, engaged.RejectedFrameCount);
        Assert.True(engaged.HasLastReport);
        Assert.False(engaged.LastReport.Accepted);
        Assert.Equal(accepted.ResponsesAcquired, engaged.ResponsesAcquired);
        Assert.Equal(accepted.ActionsAttempted, engaged.ActionsAttempted);
        Assert.Equal(accepted.CapturesAttempted, engaged.CapturesAttempted);
        Assert.Equal(accepted.TotalDuration, engaged.TotalDuration);
        Assert.True(engaged.EmergencyStopEngaged);
        Assert.Equal(1, engaged.ActiveEmergency.Episode.Value);
        Assert.Equal(1, engaged.LatestEmergency.Episode.Value);
        Assert.Equal(EmergencyStopReason.SafetyInterlock, engaged.LatestEmergency.Reason);

        pump.SetEmergencyStop(false);
        var disengaged = ServiceCycleDiagnostics.ReadPump(pump);
        Assert.False(disengaged.EmergencyStopEngaged);
        Assert.False(disengaged.ActiveEmergency.IsValid);
        Assert.Equal(1, disengaged.LatestEmergency.Episode.Value);
        Assert.Equal(EmergencyStopReason.SafetyInterlock, disengaged.LatestEmergency.Reason);
        Assert.Equal(2, disengaged.EmergencyTransition.Value);
    }

    [Fact]
    public void PublicDiagnosticsTypesAreImmutableFixedSnapshotsWithoutNativeLeakage()
    {
        var diagnosticsTypes = typeof(ServiceCycleDiagnostics).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace == typeof(ServiceCycleDiagnostics).Namespace)
            .ToArray();
        var forbiddenPrefixes = new[] { "UnityEngine", "BepInEx", "HarmonyLib" };
        Assert.DoesNotContain("Disabled", Enum.GetNames(typeof(ServiceCycleOperationalPhase)));

        foreach (var type in diagnosticsTypes.Where(type => type.IsValueType && !type.IsEnum))
        {
            Assert.True(type.IsSealed);
            Assert.All(
                type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                field => Assert.True(field.IsInitOnly, type.FullName + "." + field.Name));
            Assert.All(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), property =>
            {
                Assert.Null(property.SetMethod);
                Assert.DoesNotContain(forbiddenPrefixes, prefix =>
                    (property.PropertyType.Namespace ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal));
                Assert.False(property.PropertyType.IsArray);
            });
        }
    }

}
