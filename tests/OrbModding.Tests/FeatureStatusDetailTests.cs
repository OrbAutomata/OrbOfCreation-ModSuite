using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests;

public sealed class FeatureStatusDetailTests
{
    [Fact]
    public void OperationalDetailsRefreshTheSnapshotWithoutChangingTheHealthCondition()
    {
        var key = new FeatureStatusKey("plugin", "feature");
        FeatureStatusSnapshot Snapshot(string detail) => new(key, "Feature", true,
            FeatureStatusState.Operational, lifecycleGeneration: 1, detail: detail);
        var initial = Snapshot("Dial one: budget 100.\nDial two: budget 200.");
        var replacement = Snapshot("Dial one: budget 150.\nDial two: budget 250.");
        Assert.NotEqual(initial, replacement);
        Assert.Equal(initial.ConditionKey, replacement.ConditionKey);
        Assert.Equal(initial.GetHashCode(), Snapshot(initial.Detail).GetHashCode());
        var registry = new FeatureStatusRegistry();
        using var registration = registry.Register(initial);
        var conditions = 0;
        var snapshots = 0;
        registry.Transitioned += _ => conditions++;
        registry.SnapshotChanged += () => snapshots++;

        Assert.False(registration.Update(replacement));
        Assert.False(registration.Update(replacement));

        Assert.Equal(0, conditions);
        Assert.Equal(1, snapshots);
        var current = Assert.Single(registry.GetSnapshot());
        Assert.Equal(replacement.Detail, current.Detail);
        Assert.True(current.Reason.IsEmpty);
        Assert.Equal(FeatureRuntimePresentationState.Operational, FeatureStatusPresenter.Present(current).RuntimeState);
        Assert.Contains("Details: Dial one", FeatureStatusPresenter.Format(current));
        Assert.DoesNotContain("Reason:", FeatureStatusPresenter.Format(current));
    }

    [Fact]
    public void ReporterForwardsRuntimeDetailsAndRejectsStaleConfigurationPublications()
    {
        var registry = new FeatureStatusRegistry();
        using var reporter = new AutomataFeatureStatusReporter(registry,
            new FeatureStatusSnapshot(new FeatureStatusKey("plugin", "feature"), "Feature", true,
                FeatureStatusState.Operational, lifecycleGeneration: 2), new ConfigGeneration(3));
        Assert.False(reporter.ObserveRuntimeLifecycle(FeatureStatusState.Operational, FeatureStatusReasonCode.None,
            string.Empty, 2, new ConfigGeneration(3), detail: "Current evaluation."));
        Assert.Equal("Current evaluation.", reporter.Current.Detail);
        Assert.False(reporter.ObserveRuntimeLifecycle(FeatureStatusState.Operational, FeatureStatusReasonCode.None,
            string.Empty, 2, new ConfigGeneration(2), detail: "Obsolete evaluation."));
        Assert.Equal("Current evaluation.", reporter.Current.Detail);
        reporter.ObserveLifecycle(true, FeatureStatusState.NotReady, FeatureStatusReasonCode.GameplayNotReady,
            "A new save is loading.", 3, new ConfigGeneration(3));
        Assert.Empty(reporter.Current.Detail);
        reporter.ObserveOperational(detail: "New save evaluation.");
        reporter.SetEmergencyStop(true, new ConfigGeneration(4));
        Assert.Empty(reporter.Current.Detail);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, reporter.Current.Reason.Code);
    }

    [Fact]
    public void DetailTooltipsPreserveAllAuthoredRowsAndKeepEachNativeNodeOnOneLine()
    {
        var detail = "Dial one: " + string.Join(" ", Enumerable.Repeat("first-budget", 14)) + "\n" +
            "Dial two: second-budget.\nDial three: third-budget.\nDial four: last-budget.";
        var status = new FeatureStatusSnapshot(new FeatureStatusKey("plugin", "feature"), "Feature", true,
            FeatureStatusState.Operational, detail: detail);
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, status, UnityEngine.Color.white, 42);
        var text = string.Join("\n", nodes.Select(node => node.Text));
        Assert.Contains("Dial one:", text);
        Assert.Contains("Dial two:", text);
        Assert.Contains("Dial three:", text);
        Assert.Contains("Dial four:", text);
        Assert.Contains("last-budget.", text);
        Assert.DoesNotContain("...", text);
        Assert.All(nodes, node =>
        {
            Assert.DoesNotContain('\n', node.Text);
            Assert.DoesNotContain('\r', node.Text);
            Assert.True(node.Text.Length <= 42);
        });
        Assert.Equal("Configured: Enabled\nRuntime: Operational", FeatureStatusPresenter.Format(
            new FeatureStatusSnapshot(status.Key, "Feature", true, FeatureStatusState.Operational)));
    }
}
