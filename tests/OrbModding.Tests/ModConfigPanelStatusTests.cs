using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigPanelStatusTests
{
    [Fact]
    public void DashboardRefreshesChangedDetailsWithoutConditionTransitionsOrIdleRebuilds()
    {
        var features = new FeatureStatusRegistry();
        var key = new FeatureStatusKey("plugin.test", "feature.test");
        FeatureStatusSnapshot Blocked(string summary) => new(
            key, "Test feature", true, FeatureStatusState.TemporarilyBlocked,
            new FeatureStatusReason(FeatureStatusReasonCode.NativeBusy, summary), 1);
        using var registration = features.Register(Blocked("Waiting for wood."));
        var transitions = 0;
        features.Transitioned += _ => transitions++;

        var file = new ConfigFile();
        var config = BepInExAutomataConfiguration.Bind(file);
        var mentor = MentorConfig.Bind(file);
        config.AttachMentor(mentor);
        var store = new AutomataConfigurationStore(config, (_, _) => { });
        using var commandStatuses = new AutomataFeatureStatuses(store.Current, 1, new FeatureStatusRegistry());
        var commands = new ModConfigFeatureCommands(
            AutomationFeatureControlRegistry.Create(store, commandStatuses, new SpellLevelCapabilityState(), mentor),
            new EmergencyStopControl(store, _ => { }));
        var sources = new ModConfigRuntimeSources(
            new ConfigurationSchemaStatusRegistry(),
            features,
            new RuntimeDiagnosticsRegistry(),
            new ServiceActionTimelineRegistry(),
            new ServiceCyclePumpTimingRegistry(),
            new DiagnosticsBundleRegistry(),
            new AutomataDifferentialVerificationControl(_ => { }),
            new DecisionJournalStatusRegistry(),
            new DeferredStrategistBulletinSource(() => null, () => "Runtime not active."));
        var root = new GameObject("PanelParent", typeof(RectTransform));
        var template = new GameObject("LabelTemplate").AddComponent<TextMeshProUGUI>();
        var visuals = new NativeFeatureRailVisualPrimitives(
            new Sprite(), new Sprite(), new Sprite(), new Sprite(),
            new Sprite(), new Sprite(), new Sprite(), new Sprite());
        try
        {
            using var panel = ModConfigPanel.Create(
                root.transform, template, new ConfigCatalogSnapshot(Array.Empty<ModConfigDescriptor>()),
                new ManualLogSource(), sources, commands, visuals);
            panel.SetActive(true);
            var details = Labels(panel.Root.transform).Where(label =>
                label.text.Contains("Waiting for wood.", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, details.Length); // Health grid and detailed plugin card.
            var originalDashboard = Dashboard(panel);

            Assert.False(registration.Update(Blocked("Waiting for stone.")));
            Assert.All(details, label => Assert.Contains("Waiting for wood.", label.text));
            panel.RefreshRuntimeDashboardIfNeeded();

            Assert.All(details, label =>
            {
                Assert.Contains("Waiting for stone.", label.text);
                Assert.DoesNotContain("Waiting for wood.", label.text);
            });
            Assert.Equal(0, transitions);
            Assert.NotSame(originalDashboard, Dashboard(panel));
            var refreshedDashboard = Dashboard(panel);

            Assert.False(registration.Update(Blocked("Waiting for stone.")));
            panel.RefreshRuntimeDashboardIfNeeded();
            panel.RefreshRuntimeDashboardIfNeeded();

            Assert.Same(refreshedDashboard, Dashboard(panel));
            Assert.Equal(0, transitions);
        }
        finally
        {
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(template.gameObject);
        }
    }

    [Fact]
    public void ShellRestoresLaterPluginSectionsAndStrategistWithIndependentNavigationScroll()
    {
        var configurationFile = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(configurationFile);
        var mentor = MentorConfig.Bind(configurationFile);
        configuration.AttachMentor(mentor);
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });
        using var statuses = new AutomataFeatureStatuses(store.Current, 1, new FeatureStatusRegistry());
        var commands = new ModConfigFeatureCommands(
            AutomationFeatureControlRegistry.Create(store, statuses, new SpellLevelCapabilityState(), mentor),
            new EmergencyStopControl(store, _ => { }));
        var sources = new ModConfigRuntimeSources(new ConfigurationSchemaStatusRegistry(),
            new FeatureStatusRegistry(), new RuntimeDiagnosticsRegistry(), new ServiceActionTimelineRegistry(),
            new ServiceCyclePumpTimingRegistry(), new DiagnosticsBundleRegistry(),
            new AutomataDifferentialVerificationControl(_ => { }), new DecisionJournalStatusRegistry(),
            new DeferredStrategistBulletinSource(() => null, () => "Runtime not active."));
        var first = new ConfigFile();
        first.Bind("First", "Value", 1, "First value");
        var later = new ConfigFile();
        later.Bind("Earlier", "Value", 2, "Earlier value");
        for (var index = 0; index < 12; index++) later.Bind("Later", "Value" + index, index, "Later value");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("a.plugin", "A", "1", first),
            new ConfigPluginSource("z.plugin", "Z", "1", later),
        });
        var root = new GameObject("PanelParent", typeof(RectTransform));
        var template = new GameObject("Template", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        var visuals = new NativeFeatureRailVisualPrimitives(new Sprite(), new Sprite(), new Sprite(), new Sprite(),
            new Sprite(), new Sprite(), new Sprite(), new Sprite());
        try
        {
            using var panel = ModConfigPanel.Create(root.transform, template, catalog, new ManualLogSource(), sources, commands, visuals);
            panel.SetActive(true);
            var navigation = Child(Child(panel.Root.transform, "ModTabs"), "NavigationViewport").GetComponent<ScrollRect>()!;
            var detail = Child(panel.Root.transform, "SettingsViewport").GetComponent<ScrollRect>()!;
            Assert.NotSame(navigation, detail);
            Assert.NotSame(navigation.content, detail.content);
            Assert.NotNull(navigation.viewport!.GetComponent<RectMask2D>());
            Assert.NotNull(detail.viewport!.GetComponent<RectMask2D>());
            var bookmark = new ModConfigNavigationBookmark("z.plugin", "Later", 42f, RailScrollOffset: 17f);

            panel.RestoreNavigation(bookmark);

            Assert.Equal(bookmark, panel.CaptureNavigation());
            var strategist = ModConfigNavigationBookmark.Strategist with { ScrollOffset = 13f, RailScrollOffset = 8f };
            panel.RestoreNavigation(strategist);
            Assert.Equal(strategist, panel.CaptureNavigation());
            Assert.Contains(Labels(panel.Root.transform), label => label.text == "Orb Of Creation ModSuite · Strategist");
            panel.RestoreNavigation(bookmark);
            Assert.Equal(bookmark, panel.CaptureNavigation());
        }
        finally
        {
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(template.gameObject);
        }
    }

    private static Transform Child(Transform parent, string name) =>
        Enumerable.Range(0, parent.childCount).Select(parent.GetChild).Single(child => child.gameObject.name == name);

    private static object Dashboard(ModConfigPanel panel) =>
        typeof(ModConfigPanel).GetField("_dashboard", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(panel)!;

    private static IEnumerable<TextMeshProUGUI> Labels(Transform root)
    {
        if (root.GetComponent<TextMeshProUGUI>() is { } label) yield return label;
        for (var index = 0; index < root.childCount; index++)
        {
            foreach (var childLabel in Labels(root.GetChild(index))) yield return childLabel;
        }
    }
}
