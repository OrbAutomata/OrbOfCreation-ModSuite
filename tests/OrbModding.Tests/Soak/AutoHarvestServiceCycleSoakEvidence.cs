using System;
using System.Diagnostics;
using System.Threading;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;
using Xunit;

namespace OrbModding.Tests.Soak;

public sealed class AutoHarvestServiceCycleSoakEvidence
{
    private const int CycleCount = 5_000;
    private static readonly MonotonicDuration Step =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));

    [Fact]
    public void SameInstanceSurvivesConfigurationAndLifecycleChurn()
    {
        var clock = new ThreadSafeTestClock(100);
        var actions = new CommittingActions();
        var definition = AutoHarvestService.Define(actions);
        var world = AutoHarvestTestWorlds.Harvestable();
        using var registry = new ServiceCycleRegistry(1, clock);
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var deadline = new ServiceCycleTestDeadline.Budget();
        var frame = 0L;
        var lifecycle = 2UL;
        var maximumLivePositions = 1;
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(lifecycle)));
        maximumLivePositions = Math.Max(
            maximumLivePositions,
            registration.Slot.LifecycleSnapshot.LivePositionCount);

        for (var cycle = 1; cycle <= CycleCount; cycle++)
        {
            RunCycle(pump, registry, world, registration, clock, ref frame, deadline);
            if (cycle == CycleCount / 3)
                PublishConfiguration(registry);
            if (cycle == CycleCount * 2 / 3)
                PublishConfiguration(registry);
            if (cycle % 500 != 0 || cycle == CycleCount) continue;

            lifecycle++;
            Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(lifecycle)));
            maximumLivePositions = Math.Max(
                maximumLivePositions,
                registration.Slot.LifecycleSnapshot.LivePositionCount);
        }

        Assert.Equal(CycleCount / 2, actions.FruitCount);
        Assert.Equal(CycleCount / 2, actions.TreasureCount);
        Assert.Equal(4UL, registry.Configuration.ReadLatest().Generation.Value);
        Assert.InRange(maximumLivePositions, 1, 2);
        Assert.Equal(1, registration.Slot.LifecycleSnapshot.LivePositionCount);
        Assert.Equal(lifecycle, registration.Runner.Lifecycle.Value);
    }

    private static void RunCycle(
        SuiteFramePump pump,
        ServiceCycleRegistry registry,
        GameWorldState world,
        ServiceRegistration<
            AutoHarvestCycleState,
            AutoHarvestCycleAction> registration,
        ThreadSafeTestClock clock,
        ref long frame,
        ServiceCycleTestDeadline.Budget deadline)
    {
        clock.Advance(Step);
        TestWorldCollector.CollectedAt(registry, frame + 2, world);
        Assert.Equal(1, pump.PumpFrame(++frame).CyclesStarted);
        Assert.True(registration.WaitForResponseReady(deadline.Remaining));
        Assert.Equal(1, pump.PumpFrame(++frame).ResponsesAcquired);
        Assert.Equal(1, pump.PumpFrame(++frame).ActionsAttempted);
        pump.PumpFrame(++frame);
        deadline.For(() => !registration.Runner.Snapshot.Handoff.CleanupPending, "Auto Harvest worker cleanup");
    }

    private static void PublishConfiguration(ServiceCycleRegistry registry) =>
        registry.ConfigurationPublication.Publish(Configuration());

    private static SuiteRuntimeConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: true);

    private sealed class CommittingActions : IAutoHarvestCycleActionPort
    {
        public int FruitCount { get; private set; }
        public int TreasureCount { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            if (action.Pair == AutoHarvestPair.FruitTree) FruitCount++;
            else TreasureCount++;
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
        }
    }
}
