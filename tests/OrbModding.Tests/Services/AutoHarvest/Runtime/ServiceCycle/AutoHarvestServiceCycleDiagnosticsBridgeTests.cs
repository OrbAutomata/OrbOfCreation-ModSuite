using System.Linq;
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

public sealed class AutoHarvestServiceCycleDiagnosticsBridgeTests
{
    [Fact]
    public void ProjectionPublishesServiceCyclePairHealthWithoutLegacyCycleIdentity()
    {
        var configuration = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: true,
            treasureSelected: false);
        var definition = AutoHarvestService.Define(new CommittingActions());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(configuration);
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAt(registry, 2, AutoHarvestTestWorlds.Harvestable());
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        using var featureStatus = FeatureStatus();
        using var bridge = new AutoHarvestServiceCycleDiagnosticsBridge(
            1,
            new ConfigGeneration(1),
            ownsActionFamily: true,
            runtimeDiagnostics: runtimeDiagnostics,
            featureStatus);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        var response = pump.PumpFrame(2);
        using (var contention = new HandoffGateContention(registration.Runner))
        {
            contention.Acquire();
            try { bridge.Observe(pump, in response, ownsActionFamily: true); }
            finally { contention.Release(); }
        }

        var waiting = Assert.Single(runtimeDiagnostics.GetSnapshot());
        Assert.NotEqual(
            FeatureStatusState.Operational,
            waiting.Capabilities.Single(value =>
                value.CapabilityId == AutoHarvestRuntimeDiagnosticsPublisher.FruitCapabilityId).State);

        var quiet = pump.PumpFrame(3);
        Assert.Equal(0, quiet.ResponsesAcquired);
        bridge.Observe(pump, in quiet, ownsActionFamily: true);

        var snapshot = Assert.Single(runtimeDiagnostics.GetSnapshot());
        Assert.Equal(AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName, snapshot.Implementation);
        Assert.Equal(
            FeatureStatusState.Operational,
            snapshot.Capabilities.Single(value =>
                value.CapabilityId == AutoHarvestRuntimeDiagnosticsPublisher.FruitCapabilityId).State);
        Assert.Equal(
            FeatureStatusState.ConfigurationDisabled,
            snapshot.Capabilities.Single(value =>
                value.CapabilityId == AutoHarvestRuntimeDiagnosticsPublisher.TreasureCapabilityId).State);
    }

    private sealed class CommittingActions : IAutoHarvestCycleActionPort
    {
        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
    }

    private static AutomataFeatureStatusReporter FeatureStatus() =>
        new(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(
                    PluginIds.SuiteGuid,
                    AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.RegistryNotReady,
                    "waiting"),
                lifecycleGeneration: 1));

    /// <summary>
    /// The host registers world collection first, so Auto Harvest is never ordinal zero in the game.
    /// Reading one copied slot at index zero found the wrong service — and, because a one-slot copy of
    /// a three-service host is never complete, found nothing at all — so the health line sat on its
    /// seeded value all session and told players a running feature was waiting for native evidence.
    /// </summary>
    [Fact]
    public void PairHealthComesFromAutoHarvestsOwnServiceAndNotWhicheverRegisteredFirst()
    {
        var configuration = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: true,
            treasureSelected: false);
        using var registry = new ServiceCycleRegistry(3, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(configuration);
        using var collection = registry.Register(
            new SyntheticServiceDefinition("orbautomata.world-collection"));
        using var harvest = registry.Register(AutoHarvestService.Define(new CommittingActions()));
        using var purchases = registry.Register(
            new SyntheticServiceDefinition("orbautomata.auto-buy"));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAt(registry, 2, AutoHarvestTestWorlds.Harvestable());
        var featureRegistry = new FeatureStatusRegistry();
        using var featureStatus = new AutomataFeatureStatusReporter(
            featureRegistry,
            new FeatureStatusSnapshot(
                new FeatureStatusKey(
                    PluginIds.SuiteGuid,
                    AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycleGeneration: 1));
        using var bridge = new AutoHarvestServiceCycleDiagnosticsBridge(
            1,
            new ConfigGeneration(1),
            ownsActionFamily: true,
            runtimeDiagnostics: null,
            featureStatus);

        Assert.NotEqual(0, harvest.Ordinal);

        ServiceRunnerTestWait.ForWorkerReady(harvest);
        pump.PumpFrame(1);
        Assert.True(harvest.Runner.Snapshot.HasInFlightCycle);
        ServiceRunnerTestWait.ForResponse(harvest);
        var response = pump.PumpFrame(2);
        bridge.Observe(pump, in response, ownsActionFamily: true);
        var quiet = pump.PumpFrame(3);
        bridge.Observe(pump, in quiet, ownsActionFamily: true);

        Assert.Equal(FeatureStatusState.Operational, featureStatus.Current.State);
    }

    [Fact]
    public void EmergencyAndOwnershipConditionsOverrideProjectedPairHealth()
    {
        var configuration = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: true,
            treasureSelected: false);
        var definition = AutoHarvestService.Define(new CommittingActions());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(configuration);
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAt(registry, 2, AutoHarvestTestWorlds.Harvestable());
        var featureRegistry = new FeatureStatusRegistry();
        using var featureStatus = new AutomataFeatureStatusReporter(
            featureRegistry,
            new FeatureStatusSnapshot(
                new FeatureStatusKey(
                    PluginIds.SuiteGuid,
                    AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycleGeneration: 1));
        using var bridge = new AutoHarvestServiceCycleDiagnosticsBridge(
            1,
            new ConfigGeneration(1),
            ownsActionFamily: true,
            runtimeDiagnostics: null,
            featureStatus);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        var response = pump.PumpFrame(2);
        bridge.Observe(pump, in response, ownsActionFamily: true);
        Assert.Equal(FeatureStatusState.Operational, featureStatus.Current.State);

        pump.SetEmergencyStop(true);
        bridge.Observe(pump, default, ownsActionFamily: true);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, featureStatus.Current.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, featureStatus.Current.Reason.Code);

        pump.SetEmergencyStop(false);
        bridge.Observe(pump, default, ownsActionFamily: false);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, featureStatus.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, featureStatus.Current.Reason.Code);
    }
}
