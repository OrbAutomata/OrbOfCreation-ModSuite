using System.Threading;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

public sealed class AutoHarvestServiceCompositionTests
{
    [Fact]
    public void CommonPumpExecutesTheServiceThroughMainThreadPorts()
    {
        var ownerThread = Thread.CurrentThread.ManagedThreadId;
        var actions = new ActionPort(ownerThread);
        var definition = AutoHarvestService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAt(registry, 2, AutoHarvestTestWorlds.Harvestable());

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.Equal(1, actions.ExecutionCount);
        Assert.Equal(AutoHarvestPair.FruitTree, actions.LastPair);
    }

    private static SuiteRuntimeConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: false);

    private sealed class ActionPort : IAutoHarvestCycleActionPort
    {
        private readonly int _ownerThread;

        public ActionPort(int ownerThread) => _ownerThread = ownerThread;
        public int ExecutionCount { get; private set; }
        public AutoHarvestPair LastPair { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            ExecutionCount++;
            LastPair = action.Pair;
            var call = new NativeMutationCallOutcome(1, 1, 1);
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, call));
        }
    }
}
