using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpStrategistConfigurationTests
{
    [Fact]
    public void GoalPreferenceRoundTripsThroughPublishedReadsWritesSchemaAndPersistence()
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });
        var schema = configuration.CreateGameMcpWritableSchema();
        var before = configuration.Current;
        var descriptor = Assert.Single(schema,
            setting => setting.Section == "AutoBuy" && setting.Key == "FollowStrategistGoal");
        Assert.Equal("bool", descriptor.SettingType);
        Assert.Equal("one of: yes, no", descriptor.Constraint.Domain);
        Assert.Equal("no", GameMcpConfigurationSchema.SerializePublishedValue(
            before, "AutoBuy", "FollowStrategistGoal"));

        Assert.Equal(AutomataConfigurationWrite.Refused,
            store.SetGameMcp("AutoBuy", "FollowStrategistGoal", "maybe", store.CurrentGeneration,
                out _, out _));
        Assert.False(configuration.Current.AutoBuy.FollowStrategistGoal);
        Assert.Equal(AutomataConfigurationWrite.Committed,
            store.SetGameMcp("AutoBuy", "FollowStrategistGoal", "yes", store.CurrentGeneration,
                out var reason, out _));
        Assert.Empty(reason);
        Assert.True(configuration.Current.AutoBuy.FollowStrategistGoal);
        var context = GameMcpTestHarness.Context(writable: schema, configuration: configuration.Current);
        Assert.Contains("AutoBuy/FollowStrategistGoal: yes",
            GameMcpAcceptanceFixture.CallText("suite_configuration", context: context));
        Assert.Contains("AutoBuy/FollowStrategistGoal: yes",
            GameMcpAcceptanceFixture.CallText("suite_configuration",
                new JObject { ["section"] = "AutoBuy" }, context));
        var described = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            context, describe: true, section: "AutoBuy"));
        Assert.Equal("yes", (string?)described["settings"]!.Values<JObject>()
            .Single(setting => (string?)setting["setting"] == "AutoBuy/FollowStrategistGoal")["value"]);
        Assert.Equal("no", GameMcpConfigurationSchema.SerializePublishedValue(
            before, "AutoBuy", "FollowStrategistGoal"));
        file.Save();
        Assert.True(file.TryGetPersisted("AutoBuy", "FollowStrategistGoal", out var stored));
        Assert.Equal("true", stored);

        Assert.Equal(AutomataConfigurationWrite.Committed,
            store.SetGameMcp("AutoBuy", "FollowStrategistGoal", "no", store.CurrentGeneration,
                out _, out _));
        Assert.False(configuration.Current.AutoBuy.FollowStrategistGoal);
        Assert.Equal("no", GameMcpConfigurationSchema.SerializePublishedValue(
            configuration.Current, "AutoBuy", "FollowStrategistGoal"));
        file.Save();
        Assert.True(file.TryGetPersisted("AutoBuy", "FollowStrategistGoal", out stored));
        Assert.Equal("false", stored);
    }
}
