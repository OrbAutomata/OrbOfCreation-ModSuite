using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpRegisteredFeatureConfigurationTests
{
    [Theory]
    [InlineData("AutoLevels", "SpellCostSeconds", "2.5", "double")]
    [InlineData("AutoLevels", "DrainBufferSeconds", "120", "double")]
    [InlineData("QualityOfLife", "MutePassiveNotifications", "no", "bool")]
    public void RegisteredSettingRoundTripsThroughPublishedFullNarrowAndDescribedReads(
        string section, string key, string value, string type)
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });
        var schema = configuration.CreateGameMcpWritableSchema();
        var before = store.Current;
        var oldValue = GameMcpConfigurationSchema.SerializePublishedValue(before, section, key);
        var descriptor = Assert.Single(schema, item => item.Section == section && item.Key == key);
        Assert.Equal(type, descriptor.SettingType);

        Assert.Equal(AutomataConfigurationWrite.Committed,
            store.SetGameMcp(section, key, value, store.CurrentGeneration, out var reason, out _));
        Assert.Empty(reason);
        Assert.Equal(oldValue, GameMcpConfigurationSchema.SerializePublishedValue(before, section, key));
        var context = GameMcpTestHarness.Context(writable: schema, configuration: store.Current);
        Assert.Contains(section + "/" + key + ": " + value,
            GameMcpAcceptanceFixture.CallText("suite_configuration", context: context));
        Assert.Contains(section + "/" + key + ": " + value,
            GameMcpAcceptanceFixture.CallText("suite_configuration", new JObject { ["section"] = section }, context));
        var described = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            context, describe: true, section: section));
        Assert.Equal(value, (string?)described["settings"]!.Values<JObject>()
            .Single(item => (string?)item["setting"] == section + "/" + key)["value"]);
        file.Save();
        Assert.True(file.TryGetPersisted(section, key, out var persisted));
        Assert.Equal(value == "no" ? "false" : value, persisted);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    public void RegisteredNumericBudgetsRefuseInvalidValuesWithoutChangingCommittedIntent(string value)
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });
        var generation = store.CurrentGeneration;

        Assert.Equal(AutomataConfigurationWrite.Refused,
            store.SetGameMcp("AutoLevels", "SpellCostSeconds", value, generation, out var reason, out _));

        Assert.NotEmpty(reason);
        Assert.Equal(1d, store.Current.AutoLevels.SpellCostSeconds);
        Assert.Equal(generation, store.CurrentGeneration);
    }

    [Theory]
    [InlineData("auto_levels", "AutoLevels")]
    [InlineData("quality_of_life", "QualityOfLife")]
    public void RegisteredFeatureModeUsesTheOneAutomationBreakerAddress(string name, string section)
    {
        Assert.True(GameMcpAutomationFeatures.TryGet(name, out var feature));
        Assert.Equal(section, feature.Section);
        Assert.True(GameMcpAutomationFeatures.IsBreakerSetting(section, "Mode"));
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });
        Assert.False(feature.IsOn(store.Current));
        Assert.Equal(AutomataConfigurationWrite.Committed,
            store.SetGameMcp(feature.Section, feature.Key, feature.SerializedValue(true), store.CurrentGeneration,
                out var reason, out _));
        Assert.Empty(reason);
        Assert.True(feature.IsOn(store.Current));
        Assert.Equal("Active", GameMcpConfigurationSchema.SerializePublishedValue(store.Current, section, "Mode"));
    }
}
