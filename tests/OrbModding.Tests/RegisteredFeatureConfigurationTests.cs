using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using Xunit;

namespace OrbModding.Tests;

public sealed class RegisteredFeatureConfigurationTests
{
    [Fact]
    public void FeatureOwnedEntriesPublishTypedImmutableDefaultsAndEdits()
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        var pinned = configuration.Current;
        Assert.Equal(AutoLevelsOperationMode.Disabled, pinned.AutoLevels.Mode);
        Assert.Equal(1d, pinned.AutoLevels.SpellCostSeconds);
        Assert.Equal(300d, pinned.AutoLevels.DrainBufferSeconds);
        Assert.Equal(QualityOfLifeOperationMode.Disabled, pinned.QualityOfLife.Mode);
        Assert.True(pinned.QualityOfLife.MutePassiveNotifications);

        Entry(file, "AutoLevels", "SpellCostSeconds").SetSerializedValue("2.5");
        Entry(file, "QualityOfLife", "MutePassiveNotifications").SetSerializedValue("false");

        Assert.Equal(2.5d, configuration.Current.AutoLevels.SpellCostSeconds);
        Assert.False(configuration.Current.QualityOfLife.MutePassiveNotifications);
        Assert.Equal(1d, pinned.AutoLevels.SpellCostSeconds);
        Assert.True(pinned.QualityOfLife.MutePassiveNotifications);
        file.Save();
        Assert.True(file.TryGetPersisted("AutoLevels", "SpellCostSeconds", out var saved));
        Assert.Equal("2.5", saved);
        Assert.True(file.TryGetPersisted("QualityOfLife", "MutePassiveNotifications", out saved));
        Assert.Equal("false", saved);
    }

    [Theory]
    [InlineData("AutoLevels")]
    [InlineData("QualityOfLife")]
    public void RegisteredQuickControlUsesCommittedIntentAndPublishesOnce(string featureId)
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        var mentor = MentorConfig.Bind(file);
        configuration.AttachMentor(mentor);
        using var statuses = new AutomataFeatureStatuses(configuration.Current, 1, new FeatureStatusRegistry());
        var published = new List<SuiteRuntimeConfiguration>();
        var store = new AutomataConfigurationStore(configuration, (snapshot, generation) =>
        {
            published.Add(snapshot);
            statuses.ObserveConfiguration(snapshot, generation);
        });
        var feature = AutomataFeatureCatalog.All.Single(candidate => candidate.Id == featureId);
        var controls = AutomationFeatureControlRegistry.Create(store, statuses, new SpellLevelCapabilityState(), mentor);
        Assert.True(controls.TryGet(feature.PageLabel!, out var control));
        Entry(file, feature.ConfigurationSection!, "Mode").SetSerializedValue("Active");
        Assert.False(feature.IsConfigured(store.Current));

        control.Toggle();

        Assert.True(feature.IsConfigured(store.Current));
        Assert.True(control.Status.ConfiguredEnabled);
        Assert.Same(store.Current, Assert.Single(published));
        Assert.False(store.TryPublishPending());
        store.SetFeatureMode(featureId, false);
        Assert.False(feature.IsConfigured(store.Current));
        Assert.Equal(2, published.Count);
    }

    [Fact]
    public void RegisteredSettingsUseTheSharedStagedApplyAndImmediateModeSurfaces()
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });
        store.SetFeatureMode("AutoLevels", true);
        store.SetFeatureMode("QualityOfLife", true);
        var catalog = ConfigCatalog.Build(new[] { new ConfigPluginSource(PluginIds.SuiteGuid, "Suite", "test", file) });
        var settings = catalog.Mods.Single().Sections.SelectMany(section => section.Settings).ToArray();
        var levels = settings.Single(setting => setting.SourceSection == "AutoLevels" && setting.Key == "SpellCostSeconds");
        var mute = settings.Single(setting => setting.SourceSection == "QualityOfLife" && setting.Key == "MutePassiveNotifications");
        foreach (var mode in settings.Where(setting => setting.Key == "Mode" &&
            setting.SourceSection is "AutoLevels" or "QualityOfLife"))
            Assert.True(ModSettingsPage.IsImmediateCommandSetting(mode));
        Assert.False(ModSettingsPage.IsImmediateCommandSetting(levels));
        Assert.Single(levels.Dependencies);
        var session = new ConfigEditSession(catalog);
        session.Get(levels).Stage("3.5");
        session.Get(mute).Stage("false");
        Assert.Equal(1d, store.Current.AutoLevels.SpellCostSeconds);
        Assert.True(store.Current.QualityOfLife.MutePassiveNotifications);

        Assert.True(session.Apply(out var error), error);
        Assert.True(store.TryPublishPending());

        Assert.Equal(3.5d, store.Current.AutoLevels.SpellCostSeconds);
        Assert.False(store.Current.QualityOfLife.MutePassiveNotifications);
        Assert.False(store.TryPublishPending());
    }

    private static ConfigEntryBase Entry(ConfigFile file, string section, string key) =>
        file.Single(entry => entry.Key.Section == section && entry.Key.Key == key).Value;
}
