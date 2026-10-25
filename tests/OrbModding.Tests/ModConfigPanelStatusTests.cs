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
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using TMPro;
using UnityEngine;
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
            new DecisionJournalStatusRegistry());
        var root = new GameObject("PanelParent", typeof(RectTransform));
        var template = new GameObject("LabelTemplate").AddComponent<TextMeshProUGUI>();
        var prototype = new GameObject("NativeRailPrototype", typeof(RectTransform));
        var visuals = new NativeFeatureRailVisualPrimitives(
            prototype.AddComponent<Behaviour>(),
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
            UnityEngine.Object.Destroy(prototype);
        }
    }

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
