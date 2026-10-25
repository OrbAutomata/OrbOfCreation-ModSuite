using System.Collections.Generic;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests;

public sealed class EmergencyStopControlTests
{
    [Fact]
    public void StopAndResumeAreImmediatePlainToggles()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var events = new List<string>();
        var publications = new List<(SuiteRuntimeConfiguration Configuration, ConfigGeneration Generation)>();
        var store = new AutomataConfigurationStore(
            config,
            (configuration, generation) =>
            {
                events.Add("published");
                publications.Add((configuration, generation));
            });
        var control = new EmergencyStopControl(
            store,
            _ => events.Add("changed"));

        control.Activate();

        Assert.True(store.Current.Safety.EmergencyDisable);
        Assert.Equal("STOPPED", control.Label);
        Assert.Equal(new[] { "changed", "published" }, events);
        Assert.Single(publications);
        Assert.True(publications[0].Configuration.Safety.EmergencyDisable);
        Assert.Equal(new ConfigGeneration(2), publications[0].Generation);

        control.Activate();

        Assert.False(store.Current.Safety.EmergencyDisable);
        Assert.Equal("STOP ALL", control.Label);
        Assert.Equal(
            new[] { "changed", "published", "changed", "published" },
            events);
        Assert.Equal(2, publications.Count);
        Assert.False(publications[1].Configuration.Safety.EmergencyDisable);
        Assert.Equal(new ConfigGeneration(3), publications[1].Generation);
    }

    [Fact]
    public void EmergencyProjectionOverridesHealthyNotReadyAndFaultedRuntimeStates()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var registry = new FeatureStatusRegistry();
        var initialGeneration = new ConfigGeneration(1);
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            3,
            registry,
            initialGeneration);
        var store = new AutomataConfigurationStore(
            config,
            statuses.ObserveConfiguration);
        var control = new EmergencyStopControl(
            store,
            _ => { });

        statuses.Get(AutoCastFeatureContribution.FeatureId).ObserveOperational();
        control.Activate();
        AssertStopped(statuses.Get(AutoCastFeatureContribution.FeatureId).Current);

        statuses.ObserveLifecycleNotReady(
            store.Current,
            4,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        AssertStopped(statuses.Get(AutoCastFeatureContribution.FeatureId).Current);

        statuses.ObserveServiceCycleUnavailable(
            store.Current,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        AssertStopped(statuses.Get(AutoCastFeatureContribution.FeatureId).Current);
    }

    private static void AssertStopped(FeatureStatusSnapshot status)
    {
        Assert.True(status.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, status.Reason.Code);
    }
}
