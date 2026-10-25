using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoCastTests
{
    [Fact]
    public void FreshConfigUsesZeroResourceThreshold()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.Equal(0.0f, config.AutoCastStartResourcePercent.Value);
    }

    [Fact]
    public void FreshConfigFullChargesChargedSpellsByDefault()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.True(config.AutoCastFullCharge.Value);
    }

    [Fact]
    public void FreshConfigKeepsAttackSpellsForRitualBattlesByDefault()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.True(config.AutoCastRitualAware.Value);
        Assert.True(BepInExAutomataConfigurationReader.Read(config).AutoCast.RitualAware);
    }

    [Fact]
    public void ExistingResourceThresholdIsPreserved()
    {
        var file = new ConfigFile();
        file.Bind("AutoCast", "StartResourcePercent", 80.0f, "existing").Value = 37.0f;

        var config = BepInExAutomataConfiguration.Bind(file);

        Assert.Equal(37.0f, config.AutoCastStartResourcePercent.Value);
    }

    [Fact]
    public void ToggleSwitchesBetweenDisabledAndActive()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var toggle = CreateToggle(config);

        Assert.False(toggle.IsOn);
        toggle.Toggle();
        Assert.Equal(AutoCastOperationMode.Active, config.AutoCastMode.Value);
        Assert.True(toggle.IsOn);

        toggle.Toggle();
        Assert.False(toggle.IsOn);
        toggle.Toggle();
        Assert.True(toggle.IsOn);
    }

    [Fact]
    public void EmergencyDisableKeepsConfiguredIntentVisuallyOn()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var toggle = CreateToggle(config);

        Assert.True(toggle.IsOn);
        config.EmergencyDisable.Value = true;
        Assert.True(toggle.IsOn);
    }

    [Fact]
    public void OneClickChangesSavedModeOnceAndOwnsTheRenderedGraphic()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var savedTransitions = 0;
        var publications = 0;
        config.AutoCastMode.SettingChanged += (_, _) => savedTransitions++;
        var changes = new AutomataConfigurationStore(config, (_, _) => publications++);
        var control = new AutoCastToggleControl(changes, () => CreateStatus(config));
        var inheritedGraphic = new Image();
        var button = new Button { targetGraphic = inheritedGraphic };

        ConfiguredIntentButtonVisualOwnership.Claim(button);
        control.Toggle();

        Assert.Equal(1, savedTransitions);
        Assert.Equal(1, publications);
        Assert.Equal(AutoCastOperationMode.Disabled, config.Current.AutoCast.Mode);
        Assert.False(control.IsOn);
        Assert.Null(button.targetGraphic);
    }

    private static AutoCastToggleControl CreateToggle(BepInExAutomataConfiguration configuration)
    {
        var changes = new AutomataConfigurationStore(configuration, (_, _) => { });
        return new AutoCastToggleControl(changes, () => CreateStatus(configuration));
    }

    private static FeatureStatusSnapshot CreateStatus(BepInExAutomataConfiguration configuration)
    {
        var enabled = configuration.Current.AutoCast.Mode == AutoCastOperationMode.Active;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutoCastFeatureContribution.FeatureId),
            "Auto Cast",
            enabled,
            enabled ? FeatureStatusState.Operational : FeatureStatusState.ConfigurationDisabled);
    }
}
