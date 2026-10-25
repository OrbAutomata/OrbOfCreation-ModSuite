using System;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpFeatureStatusDetailTests
{
    [Fact]
    public void HealthyDetailsKeepTheirOperationalGroupAndEveryThinkingRow()
    {
        const string detail = "Casting Output: next cost 250; budget 500.\n" +
            "Casting Reserve: next drain 3/s; resource buffer 300s.\n" +
            "Concept speed: Knowledge budget 800.\nAlchemy speed: <color=green>mana-buffer-300</color>.";
        var feature = new FeatureStatusSnapshot(new FeatureStatusKey(PluginIds.SuiteGuid, "AutoLevels"),
            "Auto Levels", true, FeatureStatusState.Operational, lifecycleGeneration: 9, detail: detail);

        var text = GameMcpHealthProjection.ProjectGameMcpHealthText(GameMcpTestHarness.Context(features: new[] { feature }));

        Assert.Contains("\nfeatures operational: auto_levels\n", text, StringComparison.Ordinal);
        Assert.Contains("\nauto_levels details:\nCasting Output: next cost 250; budget 500.\n", text, StringComparison.Ordinal);
        Assert.Contains("Casting Reserve: next drain 3/s; resource buffer 300s.", text, StringComparison.Ordinal);
        Assert.Contains("Concept speed: Knowledge budget 800.\nAlchemy speed: mana-buffer-300.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("auto_levels reason:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<color", text, StringComparison.Ordinal);
        Assert.DoesNotContain("temporarily_blocked", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDetailsLeaveTheExistingHealthTextByteIdentical()
    {
        var key = new FeatureStatusKey(PluginIds.SuiteGuid, "AutoLevels");
        var original = new FeatureStatusSnapshot(key, "Auto Levels", true, FeatureStatusState.Operational, lifecycleGeneration: 9);
        var explicitEmpty = new FeatureStatusSnapshot(key, "Auto Levels", true, FeatureStatusState.Operational,
            lifecycleGeneration: 9, detail: "   ");
        var before = GameMcpHealthProjection.ProjectGameMcpHealthText(GameMcpTestHarness.Context(features: new[] { original }));
        var after = GameMcpHealthProjection.ProjectGameMcpHealthText(GameMcpTestHarness.Context(features: new[] { explicitEmpty }));
        Assert.Equal(before, after);
        Assert.DoesNotContain("details:", after, StringComparison.Ordinal);
    }
}
