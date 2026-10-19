using System;
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
    public void AReadFindsTheNamedServiceAndHasNothingForAReleasedOrUnknownOne()
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
        var secondId = new ServiceId("diagnostics.order.b");
        var beforeRelease = AvailableServiceDiagnostics.Await(pump, secondId);

        second.Dispose();
        var thirdRead = AvailableServiceDiagnostics.Await(pump, new ServiceId("diagnostics.order.c"));

        Assert.Equal(1, beforeRelease.Ordinal);
        Assert.Equal(secondId, beforeRelease.ServiceId);
        Assert.Null(ServiceCycleDiagnostics.ReadAvailableService(pump, secondId));
        Assert.Null(ServiceCycleDiagnostics.ReadAvailableService(pump, new ServiceId("diagnostics.order.missing")));
        Assert.Equal(2, thirdRead.Ordinal);
        Assert.Equal("diagnostics.order.c", thirdRead.ServiceId.Value);
        Assert.NotEqual(beforeRelease.RegistrationInstance, thirdRead.RegistrationInstance);
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
            var frame = TestWorldCollector.ActivationFrame + 1;
            ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
            Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));
            registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));

            var snapshot = AvailableServiceDiagnostics.Await(pump, new ServiceId("diagnostics.context"));

            Assert.Equal(ServiceCycleOperationalPhase.Evaluating, snapshot.Phase);
            Assert.Equal(ServiceCycleHandoffDiagnosticsPhase.Evaluating, snapshot.Handoff.Phase);
            Assert.True(snapshot.Context.HasCurrentCycle);
            Assert.Equal((ulong)1, snapshot.Context.CurrentCycle.Config.Value);
            Assert.Equal((ulong)1, snapshot.Context.CurrentCycle.Strategy.Value);
            Assert.Equal((ulong)2, snapshot.Context.LatestConfiguration.Value);
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
    public void AReadUnderHandoffContentionHasNothingWithoutBlockingAndNoReadAllocatesAfterWarmup()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new ExecutionServiceDefinition("diagnostics.contention"),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var id = new ServiceId("diagnostics.contention");
        AvailableServiceDiagnostics.Await(pump, id);
        using var contention = new HandoffGateContention(registration.Runner);
        contention.Acquire();
        var contended = HeldGate.AssertReturnsWhileHeld(
            () => ServiceCycleDiagnostics.ReadAvailableService(pump, id),
            contention.Release);
        var contendedMissing = 0;
        var contendedAllocated = AllocationProbe.MeasureRepeated(1_000, () =>
        {
            if (ServiceCycleDiagnostics.ReadAvailableService(pump, id) is null) contendedMissing++;
        });
        contention.Release();
        AvailableServiceDiagnostics.Await(pump, id);

        var available = 0;
        var allocated = AllocationProbe.MeasureRepeated(1_000, () =>
        {
            if (ServiceCycleDiagnostics.ReadAvailableService(pump, id) is not null) available++;
        });

        Assert.Null(contended);
        Assert.Equal(2_000, contendedMissing);
        Assert.Equal(0, contendedAllocated);
        Assert.Equal(2_000, available);
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
        var frame = TestWorldCollector.ActivationFrame + 1;
        ServiceRunnerTestWait.PumpToResponse(pump, registration.Runner, ref frame);
        Assert.Equal(1, pump.PumpFrame(frame).ActionsAttempted);
        clock.Advance(new MonotonicDuration(5));

        var snapshot = AvailableServiceDiagnostics.Await(pump, new ServiceId("diagnostics.batch"));

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
    public void ALifecycleStormHasNoReadUntilACurrentRunnerCarriesTheLatestTerminal()
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
        var id = new ServiceId("diagnostics.lifecycle");
        var frame = 2L;

        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => firstGate.Entered.IsSet, collector: registry);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => secondGate.Entered.IsSet, collector: registry);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(3)));
        var duringStorm = ServiceCycleDiagnostics.ReadAvailableService(pump, id);
        firstGate.Release.Set();
        secondGate.Release.Set();
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => ServiceCycleDiagnostics.ReadAvailableService(pump, id) is not null,
            collector: registry);
        var settled = AvailableServiceDiagnostics.Await(pump, id);

        Assert.Null(duringStorm);
        Assert.Equal((ulong)3, settled.Lifecycle.DesiredLifecycle.Value);
        Assert.True(settled.Lifecycle.LatestTerminal.IsPresent);
        Assert.Equal(settled.Lifecycle.LatestTerminal.Sequence, settled.Lifecycle.LatestTerminalSequence);
        Assert.True(settled.Lifecycle.LatestTerminalSequence >= 2);
    }

    [Fact]
    public void ADuplicateFrameIsRejectedAndTheEmergencyEpisodeKeepsItsReason()
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

        Assert.True(accepted.Accepted);
        Assert.False(rejected.Accepted);
        Assert.Equal(1, pump.LastAcceptedFrameIdentity);
        Assert.True(pump.IsEmergencyStopEngaged);
        Assert.Equal(1, pump.ActiveEmergency.Episode.Value);
        Assert.Equal(1, pump.LatestEmergency.Episode.Value);
        Assert.Equal(EmergencyStopReason.SafetyInterlock, pump.LatestEmergency.Reason);

        pump.SetEmergencyStop(false);
        Assert.False(pump.IsEmergencyStopEngaged);
        Assert.False(pump.ActiveEmergency.IsValid);
        Assert.Equal(1, pump.LatestEmergency.Episode.Value);
        Assert.Equal(EmergencyStopReason.SafetyInterlock, pump.LatestEmergency.Reason);
        Assert.Equal(2, pump.EmergencyTransition.Value);
    }

    [Fact]
    public void PublicDiagnosticsTypesAreImmutableFixedSnapshotsWithoutNativeLeakage()
    {
        var diagnosticsTypes = typeof(ServiceCycleDiagnostics).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace == typeof(ServiceCycleDiagnostics).Namespace)
            .ToArray();
        var forbiddenPrefixes = new[] { "UnityEngine", "BepInEx", "HarmonyLib" };

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
