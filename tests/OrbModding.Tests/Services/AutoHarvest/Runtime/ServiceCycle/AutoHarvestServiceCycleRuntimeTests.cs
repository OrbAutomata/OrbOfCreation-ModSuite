using System;
using System.Threading;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

public sealed class AutoHarvestServiceCycleRuntimeTests
{
    private static AutomataServiceCycleRuntime CreateRuntime(
        SuiteRuntimeConfiguration configuration,
        Func<long> readFrameIdentity,
        Func<long> readLifecycleEpoch,
        TypedRegistryResolver resolver,
        RuntimeDiagnosticsRegistry? runtimeDiagnostics = null,
        AutomataServiceCycleObservabilityOptions observability = default)
    {
        var hostDependencies = new AutomataServiceCycleHostDependencies(
            readFrameIdentity,
            readLifecycleEpoch,
            new ServiceActionOutcomeWindowRegistry(),
            observability: observability);
        var feature = new AutoHarvestServiceCycleFeature(
            new AutoHarvestFeatureDependencies(
                resolver,
                ownsActionFamily: () => true,
                tryCaptureMutationPermit: () => true,
                runtimeDiagnostics: runtimeDiagnostics,
                featureStatus: FeatureStatus()));
        return AutomataServiceCycleComposition.Create(
            configuration,
            new ConfigGeneration(1),
            hostDependencies,
            new IAutomataServiceCycleFeature[] { feature },
            new ManualLogSource());
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

    [Fact]
    public void ProductionRuntimePublishesConfigurationAndLifecycleWithoutDisabledNativeReads()
    {
        var configuration = new MutableConfiguration();
        var frame = 0L;
        var lifecycle = 7L;
        var nativeRegistryReads = 0;
        var resolver = new TypedRegistryResolver(
            () => lifecycle,
            () =>
            {
                nativeRegistryReads++;
                return TypedRegistrySourceSnapshot.NotReady("not needed while disabled");
            },
            _ => null);
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        using var runtime = CreateRuntime(
            configuration.Snapshot(),
            () => ++frame,
            () => lifecycle,
            resolver,
            runtimeDiagnostics: runtimeDiagnostics);

        Assert.Equal(
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            Assert.Single(runtimeDiagnostics.GetSnapshot()).Implementation);
        Assert.Equal(new ConfigGeneration(1), runtime.CurrentConfigurationGeneration);

        runtime.Tick(0);
        configuration.EmergencyDisabled = true;
        runtime.Tick(0);

        runtime.PublishSavedConfiguration(
            configuration.Snapshot(),
            new ConfigGeneration(1).Next());
        Assert.Equal(new ConfigGeneration(2), runtime.CurrentConfigurationGeneration);
        runtime.Tick(0);
        Assert.True(runtime.EmergencyStopEngaged);
        Assert.Equal(3, frame);
        Assert.Equal(0, nativeRegistryReads);

        lifecycle = 8;
        runtime.InvalidateLifecycle();
        Assert.Equal(new LifecycleGeneration(8), runtime.CurrentLifecycle);

        configuration.EmergencyDisabled = false;
        runtime.PublishSavedConfiguration(
            configuration.Snapshot(),
            new ConfigGeneration(3));
        runtime.Tick(0);
        Assert.False(runtime.EmergencyStopEngaged);

        runtime.Dispose();
        Assert.Empty(runtimeDiagnostics.GetSnapshot());
    }

    [Fact]
    public void ConfigurationGenerationCannotSkipOrDoublePublish()
    {
        var lifecycle = 7L;
        using var runtime = CreateRuntime(
            new MutableConfiguration().Snapshot(),
            () => 1,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("disabled"),
                _ => null));
        var snapshot = new MutableConfiguration().Snapshot();

        runtime.PublishSavedConfiguration(snapshot, new ConfigGeneration(1));
        Assert.Equal(new ConfigGeneration(1), runtime.CurrentConfigurationGeneration);

        Assert.Throws<InvalidOperationException>(() =>
            runtime.PublishSavedConfiguration(snapshot, new ConfigGeneration(3)));
        Assert.Equal(new ConfigGeneration(1), runtime.CurrentConfigurationGeneration);

        runtime.PublishSavedConfiguration(snapshot, new ConfigGeneration(2));
        Assert.Equal(new ConfigGeneration(2), runtime.CurrentConfigurationGeneration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ProductionConstructionRejectsInvalidNativeLifecycle(long lifecycle)
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateRuntime(
                new MutableConfiguration().Snapshot(),
                () => 1,
                () => lifecycle,
                new TypedRegistryResolver(
                    () => lifecycle,
                    () => TypedRegistrySourceSnapshot.NotReady("unused"),
                    _ => null)));
    }

    [Fact]
    public void FailedConstructionAfterJournalStartupReleasesJournalAndPumpOwnership()
    {
        var configuration = new MutableConfiguration();
        const long lifecycle = 7;
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        using var existingPublisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            lifecycle,
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.FruitTree),
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree),
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            runtimeDiagnostics);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var journalSource = new JournalSource(storage);
        var status = new DecisionJournalStatusRegistry();
        Assert.Throws<InvalidOperationException>(() =>
            CreateRuntime(
                configuration.Snapshot(),
                () => 1,
                () => lifecycle,
                new TypedRegistryResolver(
                    () => lifecycle,
                    () => TypedRegistrySourceSnapshot.NotReady("unused"),
                    _ => null),
                runtimeDiagnostics: runtimeDiagnostics,
                observability: new AutomataServiceCycleObservabilityOptions(
                    default,
                    new AutomataDecisionJournalOptions(
                        status,
                        journalSource,
                        "journal"))));

        Assert.Equal(1, journalSource.CreateCount);
        Assert.Equal(DecisionJournalStatusState.Unavailable, status.Status.State);
        Assert.Single(runtimeDiagnostics.GetSnapshot());
    }

    [Fact]
    public void ProductionHostCompositionOwnsTheDecisionJournal()
    {
        var configuration = new MutableConfiguration();
        var frame = 0L;
        const long lifecycle = 7;
        using var storage = new DecisionJournalRuntimeTestStorage();
        var status = new DecisionJournalStatusRegistry();
        using var runtime = CreateRuntime(
            configuration.Snapshot(),
            () => ++frame,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("disabled"),
                _ => null),
            observability: new AutomataServiceCycleObservabilityOptions(
                default,
                new AutomataDecisionJournalOptions(
                    status,
                    new JournalSource(storage),
                    "journal")));

        Assert.True(SpinWait.SpinUntil(() =>
        {
            runtime.Tick(0);
            return status.Status.State == DecisionJournalStatusState.Recording;
        }, ServiceCycleTestDeadline.Value));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            runtime.Tick(0);
            return status.Status.WrittenRecords > 0;
        }, ServiceCycleTestDeadline.Value));

        Assert.Equal("journal", status.Status.ArtifactName);
        Assert.NotEmpty(storage.ReadRecords());
        runtime.Dispose();
        Assert.Equal(DecisionJournalStatusState.Unavailable, status.Status.State);
    }

    [Fact]
    public void ProductionJournalWaitsForTheLiveEmergencyStateBeforeAttaching()
    {
        var configuration = new MutableConfiguration { EmergencyDisabled = true };
        var frame = 0L;
        const long lifecycle = 7;
        using var storage = new DecisionJournalRuntimeTestStorage(blockReconcile: true);
        var status = new DecisionJournalStatusRegistry();
        using var runtime = CreateRuntime(
            configuration.Snapshot(),
            () => ++frame,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("disabled"),
                _ => null),
            observability: new AutomataServiceCycleObservabilityOptions(
                default,
                new AutomataDecisionJournalOptions(
                    status,
                    new JournalSource(storage),
                    "journal")));
        Assert.True(storage.ReconcileEntered.Wait(ServiceCycleTestDeadline.Value));
        storage.ReconcileRelease.Set();
        Assert.True(storage.ReconcileCompleted.Wait(ServiceCycleTestDeadline.Value));

        runtime.Tick(0);

        Assert.Equal(DecisionJournalStatusState.Arming, status.Status.State);
        Assert.Empty(storage.ReadRecords());

        configuration.EmergencyDisabled = false;
        runtime.PublishSavedConfiguration(
            configuration.Snapshot(),
            new ConfigGeneration(1).Next());
        Assert.True(SpinWait.SpinUntil(() =>
        {
            runtime.Tick(0);
            return status.Status.State == DecisionJournalStatusState.Recording;
        }, ServiceCycleTestDeadline.Value));
        runtime.Dispose();
    }

    private sealed class MutableConfiguration
    {
        public bool MasterEnabled { get; set; }
        public bool EmergencyDisabled { get; set; }
        public bool ActiveMode { get; set; }
        public bool FruitSelected { get; set; } = true;
        public bool TreasureSelected { get; set; } = true;
        public SuiteRuntimeConfiguration Snapshot() => AutoHarvestConfigurationFactory.Create(
            MasterEnabled,
            EmergencyDisabled,
            ActiveMode,
            FruitSelected,
            TreasureSelected);
    }

    private sealed class JournalSource : IAutomataDecisionJournalSource
    {
        private readonly DecisionJournalRuntimeTestStorage _storage;

        internal JournalSource(DecisionJournalRuntimeTestStorage storage) => _storage = storage;

        internal int CreateCount { get; private set; }

        public AutomataDecisionJournalSpec Create()
        {
            CreateCount++;
            return new AutomataDecisionJournalSpec(
                _storage,
                new DecisionJournalRunId(1),
                maximumCommittedSegments: 4,
                blockCount: 3,
                new MonotonicDuration(1));
        }
    }
}
