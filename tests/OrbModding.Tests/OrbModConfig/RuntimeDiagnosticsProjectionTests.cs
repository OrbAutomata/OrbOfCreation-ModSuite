using System;
using System.Linq;
using System.Threading;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class RuntimeDiagnosticsProjectionTests
{
    [Fact]
    public void TopNavigationStartsWithSyntheticRuntimeAndKeepsPluginIndexesExact()
    {
        var catalog = new ConfigCatalogSnapshot(new[]
        {
            Mod("plugin.a", "Orb Automata"),
            Mod("plugin.m", "Orb Mentor"),
        });

        var pages = ModConfigTopNavigation.Build(catalog, attentionCount: 2);

        Assert.Equal(ModConfigTopPageKind.Runtime, pages[0].Kind);
        Assert.Equal("Runtime (2)", pages[0].Label);
        Assert.Equal(-1, pages[0].PluginIndex);
        Assert.Equal(ModConfigTopPageKind.Strategist, pages[1].Kind);
        Assert.Equal(-1, pages[1].PluginIndex);
        Assert.Equal("Orb Automata · Main", pages[2].Label);
        Assert.Equal(0, pages[2].PluginIndex);
        Assert.Equal(0, pages[2].SectionIndex);
        Assert.Equal("Orb Mentor · Main", pages[3].Label);
        Assert.Equal(1, pages[3].PluginIndex);
        Assert.Equal(0, pages[3].SectionIndex);
    }

    [Fact]
    public void FeatureHealthGridPutsFailuresAndAttentionBeforeHealthyFeatures()
    {
        var dashboard = new RuntimeDiagnosticsDashboard(new[]
        {
            new RuntimeDiagnosticsCard(
                "suite",
                "Suite",
                "1",
                "Schema ready",
                new[]
                {
                    Feature("suite", "healthy", FeatureStatusState.Operational),
                    Feature("suite", "failure", FeatureStatusState.Faulted),
                    Feature("suite", "attention", FeatureStatusState.Degraded),
                },
                Array.Empty<RuntimeServiceDiagnosticsSnapshot>(),
                RuntimeDiagnosticsSeverity.Healthy),
        });

        var items = RuntimeFeatureHealthProjection.Build(dashboard);

        Assert.Equal(
            new[] { "failure", "attention", "healthy" },
            items.Select(item => item.Status.Key.FeatureId));
        Assert.Equal(
            new[]
            {
                RuntimeDiagnosticsSeverity.Failure,
                RuntimeDiagnosticsSeverity.Attention,
                RuntimeDiagnosticsSeverity.Healthy,
            },
            items.Select(item => item.Severity));
    }

    [Fact]
    public void ProjectionUsesExactGuidJoinsAndRetainsRuntimeOnlyPublishers()
    {
        var catalog = new ConfigCatalogSnapshot(
            Array.Empty<ModConfigDescriptor>(),
            new[]
            {
                new LoadedPluginDescriptor("plugin.one", "One", "1.0"),
                new LoadedPluginDescriptor("plugin.two", "Two", "2.0"),
            });
        var schemas = new ConfigurationSchemaStatusRegistry();
        var features = new FeatureStatusRegistry();
        var runtime = new RuntimeDiagnosticsRegistry();
        using var exactFeature = features.Register(Feature(
            "plugin.one",
            "AutoHarvest",
            FeatureStatusState.Operational));
        using var prefixTrap = features.Register(Feature(
            "plugin.one.extra",
            "Other",
            FeatureStatusState.Degraded));
        using var runtimeOnly = runtime.Register(Service("plugin.runtime"));

        var dashboard = RuntimeDiagnosticsProjection.Build(catalog, schemas, features, runtime);

        Assert.Equal(4, dashboard.Cards.Count);
        var one = dashboard.Cards.Single(card => card.PluginGuid == "plugin.one");
        Assert.Single(one.FeatureStatuses);
        Assert.Equal("AutoHarvest", one.FeatureStatuses[0].Key.FeatureId);
        var fallback = dashboard.Cards.Single(card => card.PluginGuid == "plugin.runtime");
        Assert.Equal("plugin.runtime", fallback.DisplayName);
        Assert.Single(fallback.RuntimeServices);
        Assert.Empty(dashboard.Cards.Single(card => card.PluginGuid == "plugin.two").FeatureStatuses);
    }

    [Fact]
    public void FailureAndAttentionCardsSortBeforeWaitingAndHealthyCards()
    {
        var catalog = new ConfigCatalogSnapshot(
            Array.Empty<ModConfigDescriptor>(),
            new[]
            {
                new LoadedPluginDescriptor("healthy", "Healthy", "1"),
                new LoadedPluginDescriptor("waiting", "Waiting", "1"),
                new LoadedPluginDescriptor("degraded", "Degraded", "1"),
                new LoadedPluginDescriptor("failed", "Failed", "1"),
            });
        var schemas = new ConfigurationSchemaStatusRegistry();
        schemas.Publish(new ConfigurationSchemaStatus(
            "failed",
            ConfigurationSchemaState.Failed,
            0,
            1,
            saved: false,
            loaded: false,
            "Migration failed.",
            backupCreated: false));
        var features = new FeatureStatusRegistry();
        using var waiting = features.Register(Feature(
            "waiting",
            "Harvest",
            FeatureStatusState.NotReady));
        using var degraded = features.Register(Feature(
            "degraded",
            "Harvest",
            FeatureStatusState.Degraded));
        var runtime = new RuntimeDiagnosticsRegistry();

        var dashboard = RuntimeDiagnosticsProjection.Build(catalog, schemas, features, runtime);

        Assert.Equal(new[] { "failed", "degraded", "waiting", "healthy" },
            dashboard.Cards.Select(card => card.PluginGuid));
        Assert.Equal(2, dashboard.AttentionCount);
    }

    [Fact]
    public void RuntimeCapabilitiesParticipateInSeverityWithoutInventingFeatureHealth()
    {
        var catalog = new ConfigCatalogSnapshot(
            Array.Empty<ModConfigDescriptor>(),
            new[] { new LoadedPluginDescriptor("plugin", "Plugin", "1") });
        var runtime = new RuntimeDiagnosticsRegistry();
        using var registration = runtime.Register(new RuntimeServiceDiagnosticsSnapshot(
            new FeatureStatusKey("plugin", "AutoHarvest"),
            "Auto Harvest",
            "DeterministicKernel",
            1,
            new[]
            {
                new RuntimeCapabilityDiagnostics(
                    "Fruit",
                    "Fruit trees",
                    true,
                    FeatureStatusState.ContractUnavailable,
                    new FeatureStatusReason(
                        FeatureStatusReasonCode.ContractUnavailable,
                        "Contract unavailable.")),
            }));

        var dashboard = RuntimeDiagnosticsProjection.Build(
            catalog,
            new ConfigurationSchemaStatusRegistry(),
            new FeatureStatusRegistry(),
            runtime);

        Assert.Equal(RuntimeDiagnosticsSeverity.Failure, Assert.Single(dashboard.Cards).Severity);
        Assert.Empty(dashboard.Cards[0].FeatureStatuses);
    }

    [Fact]
    public void DirtyLatchCoalescesWorkerTransitionsForOneMainThreadRefresh()
    {
        var latch = new RuntimeDiagnosticsDirtyLatch();
        var worker = new Thread(() =>
        {
            latch.MarkDirty();
            latch.MarkDirty();
        });

        worker.Start();
        worker.Join();

        Assert.True(latch.IsDirty);
        Assert.True(latch.TryConsume());
        Assert.False(latch.TryConsume());
    }

    [Fact]
    public void CardTextNamesRuntimeImplementationAndCapabilities()
    {
        var card = new RuntimeDiagnosticsCard(
            "plugin",
            "Plugin",
            "1",
            "Schema current.",
            Array.Empty<FeatureStatusSnapshot>(),
            new[] { Service("plugin") },
            RuntimeDiagnosticsSeverity.Healthy);

        var text = RuntimeDiagnosticsCardText.Body(card);
        Assert.Contains("Auto Harvest runtime: ServiceCycle", text);
        Assert.Contains("Fruit trees: Operational", text);
    }

    [Fact]
    public void SettingsNavigationStatePreservesScrollAndClampsRemovedSection()
    {
        var remembered = new ModSettingsNavigationState(sectionIndex: 3, scrollOffset: 125f);

        var restored = remembered.ClampTo(sectionCount: 2);

        Assert.Equal(1, restored.SectionIndex);
        Assert.Equal(125f, restored.ScrollOffset);
    }

    [Fact]
    public void DashboardAppliesCapabilityTransitionInPlaceWithoutRebuildingCatalogCards()
    {
        var card = new RuntimeDiagnosticsCard(
            "plugin",
            "Plugin",
            "1",
            "Schema current.",
            Array.Empty<FeatureStatusSnapshot>(),
            new[] { Service("plugin") },
            RuntimeDiagnosticsSeverity.Healthy);
        var dashboard = new RuntimeDiagnosticsDashboard(new[] { card });
        var current = Service("plugin", state: FeatureStatusState.Locked);
        var transition = new RuntimeDiagnosticsTransition(
            RuntimeDiagnosticsTransitionKind.Changed,
            card.RuntimeServices[0],
            current,
            revision: 2);

        Assert.True(dashboard.TryApplyChangedRuntime(transition, out var attentionChanged));

        Assert.False(attentionChanged);
        Assert.Same(card, dashboard.Cards[0]);
        Assert.Equal(FeatureStatusState.Locked, dashboard.Cards[0].RuntimeServices[0].Capabilities[0].State);
        Assert.Equal(2, card.Revision);
    }

    [Fact]
    public void RuntimeTransitionQueueIsBoundedAndSignalsRecoveryRebuild()
    {
        var queue = new RuntimeDiagnosticsTransitionQueue(capacity: 2);
        var snapshot = Service("plugin");
        var transition = new RuntimeDiagnosticsTransition(
            RuntimeDiagnosticsTransitionKind.Changed,
            snapshot,
            Service("plugin", state: FeatureStatusState.Locked),
            revision: 1);

        queue.Enqueue(transition);
        queue.Enqueue(transition);
        queue.Enqueue(transition);

        Assert.Equal(2, queue.Count);
        Assert.True(queue.ConsumeOverflow());
        Assert.False(queue.ConsumeOverflow());
        Assert.True(queue.TryDequeue(out _));
        Assert.True(queue.TryDequeue(out _));
        Assert.False(queue.TryDequeue(out _));
    }

    private static ModConfigDescriptor Mod(string guid, string name) =>
        new(
            guid,
            name,
            "1",
            new[] { new ConfigSectionDescriptor("Main", Array.Empty<ConfigSettingDescriptor>()) });

    private static FeatureStatusSnapshot Feature(
        string pluginId,
        string featureId,
        FeatureStatusState state)
    {
        var reason = state switch
        {
            FeatureStatusState.Operational => default,
            FeatureStatusState.Degraded => new FeatureStatusReason(
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "Partial capability unavailable."),
            _ => new FeatureStatusReason(
                FeatureStatusReasonCode.GameplayNotReady,
                "Not ready."),
        };
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(pluginId, featureId),
            featureId,
            configuredEnabled: true,
            state,
            reason,
            lifecycleGeneration: 1);
    }

    private static RuntimeServiceDiagnosticsSnapshot Service(
        string pluginId,
        string implementation = "ServiceCycle",
        FeatureStatusState state = FeatureStatusState.Operational) => new(
        new FeatureStatusKey(pluginId, "AutoHarvest"),
        "Auto Harvest",
        implementation,
        1,
        new[]
        {
            new RuntimeCapabilityDiagnostics(
                "Fruit",
                "Fruit trees",
                true,
                state,
                state == FeatureStatusState.Operational
                    ? default
                    : new FeatureStatusReason(FeatureStatusReasonCode.ProgressionLocked, "Locked.")),
        });
}
