using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataFeatureStatusTests
{
    [Fact]
    public void AutomataRegistersConfigurationSeparatelyFromLifecycleReadiness()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        var added = 0;
        var changed = 0;
        registry.Transitioned += transition =>
        {
            if (transition.Kind == FeatureStatusTransitionKind.Added) added++;
            if (transition.Kind == FeatureStatusTransitionKind.Changed) changed++;
        };
        using var statuses = new AutomataFeatureStatuses(config.Current, 7, registry);

        Assert.Equal(FeatureStatusState.NotReady, statuses.AutoBuy.Current.State);
        Assert.True(statuses.AutoBuy.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoCast.Current.State);
        Assert.False(statuses.AutoCast.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoConcept.Current.State);
        Assert.Equal(FeatureStatusState.NotReady, statuses.SpellLevel.Current.State);
        Assert.True(statuses.SpellLevel.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoHarvest.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoItems.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoScribe.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Mentor.Current.State);
        Assert.All(registry.GetSnapshot(), status => Assert.Equal(7, status.LifecycleGeneration));
        Assert.Equal(8, added);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void AutomataReporterPublishesOnlyConditionTransitions()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, registry);
        var changes = 0;
        registry.Transitioned += transition =>
        {
            if (transition.Kind == FeatureStatusTransitionKind.Changed &&
                transition.Current?.Key.Equals(statuses.AutoBuy.Key) == true)
                changes++;
        };

        Assert.True(statuses.AutoBuy.ObserveOperational());
        for (var index = 0; index < 10_000; index++)
            Assert.False(statuses.AutoBuy.ObserveOperational());
        Assert.True(statuses.AutoBuy.Observe(
            true,
            FeatureStatusState.Degraded,
            FeatureStatusReasonCode.PartialCapabilityUnavailable,
            "Upgrade automation is unavailable."));
        for (var index = 0; index < 10_000; index++)
        {
            Assert.False(statuses.AutoBuy.Observe(
                true,
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "Equivalent wording does not create another transition."));
            Assert.False(statuses.AutoBuy.ObserveLifecycle(
                true,
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "Equivalent lifecycle observation does not create another transition.",
                1));
        }

        Assert.Equal(2, changes);
    }

    [Fact]
    public void LifecycleObservationNormalizesNoneReasonDespitePositiveSummaryText()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            1,
            new FeatureStatusRegistry());

        Assert.True(statuses.AutoBuy.ObserveLifecycle(
            configuredEnabled: true,
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            "Positive operational wording is not a reason.",
            lifecycleGeneration: 2));

        Assert.Equal(FeatureStatusState.Operational, statuses.AutoBuy.Current.State);
        Assert.Equal(FeatureStatusReasonCode.None, statuses.AutoBuy.Current.Reason.Code);
        Assert.Null(statuses.AutoBuy.Current.Reason.Summary);
        Assert.Equal(2, statuses.AutoBuy.Current.LifecycleGeneration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeHealthCannotRewriteConfiguredIntent(bool configuredEnabled)
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = configuredEnabled
            ? AutoCastOperationMode.Active
            : AutoCastOperationMode.Disabled;
        var registry = new FeatureStatusRegistry();
        var generation = new ConfigGeneration(1);
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            4,
            registry,
            generation);

        statuses.AutoCast.ObserveRuntimeLifecycle(
            configuredEnabled
                ? FeatureStatusState.ConfigurationDisabled
                : FeatureStatusState.Operational,
            FeatureStatusReasonCode.RuntimeFailure,
            "A runtime writer cannot change saved intent.",
            4,
            generation);

        Assert.Equal(configuredEnabled, statuses.AutoCast.Current.ConfiguredEnabled);
    }

    [Fact]
    public void AutoBuyWithNoSelectedPurchaseKindKeepsIntentOnAndRejectsRuntimeRepaint()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = false;
        config.AutoBuyUpgrades.Value = false;
        var generation = new ConfigGeneration(1);
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            4,
            new FeatureStatusRegistry(),
            generation);

        Assert.True(statuses.AutoBuy.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, statuses.AutoBuy.Current.State);
        Assert.Equal(
            FeatureStatusReasonCode.ConfigurationDisabled,
            statuses.AutoBuy.Current.Reason.Code);

        Assert.False(statuses.AutoBuy.ObserveRuntimeLifecycle(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            string.Empty,
            4,
            generation));

        Assert.True(statuses.AutoBuy.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, statuses.AutoBuy.Current.State);
        Assert.Equal(
            FeatureStatusReasonCode.ConfigurationDisabled,
            statuses.AutoBuy.Current.Reason.Code);
    }

    /// <summary>
    /// Auto Scribe writes the scrolls Auto Items reads, so with Scrolls off it waits on Auto Items.
    /// Whichever projection ran last, the reason names that switch, never Auto Buy or Automata.
    /// </summary>
    [Fact]
    public void AutoScribeWaitingOnScrollsSaysSoInEveryProjection()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.SetAutoScribeMode(AutoScribeOperationMode.Active);
        config.SetAutoItemsMode(AutoItemsOperationMode.Active);
        config.AutoItemsUseScrolls.Value = false;
        config.AutoItemsUseRelics.Value = true;
        const string reason = "Auto Scribe needs Auto Items running with Scrolls on.";
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, new FeatureStatusRegistry());

        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, statuses.AutoScribe.Current.Reason.Code);
        Assert.Equal(reason, statuses.AutoScribe.Current.Reason.Summary);

        statuses.ObserveLifecycleNotReady(config.Current, 2, statuses.AutoBuy.ConfigurationGeneration);
        Assert.Equal(reason, statuses.AutoScribe.Current.Reason.Summary);

        statuses.ObserveContractUnavailable(
            config.Current, 3, "Assembly contract mismatch.", statuses.AutoBuy.ConfigurationGeneration);
        Assert.Equal(reason, statuses.AutoScribe.Current.Reason.Summary);

        statuses.ObserveServiceCycleUnavailable(config.Current, statuses.AutoBuy.ConfigurationGeneration);
        Assert.Equal(reason, statuses.AutoScribe.Current.Reason.Summary);
    }

    [Fact]
    public void AssemblyContractFailurePreservesConfigurationDisabledPrecedence()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, registry);

        statuses.ObserveContractUnavailable(
            config.Current,
            1,
            "Assembly contract mismatch.",
            statuses.AutoBuy.ConfigurationGeneration);

        Assert.Equal(FeatureStatusState.ContractUnavailable, statuses.AutoBuy.Current.State);
        Assert.Equal(FeatureStatusState.ContractUnavailable, statuses.SpellLevel.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoCast.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoConcept.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoHarvest.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoItems.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoScribe.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Mentor.Current.State);
    }

    [Fact]
    public void GloballyDisabledAutomataAdvancesEveryFeatureOnLifecycleTransitionsThenStaysIdle()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.Enabled.Value = false;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 4, registry);
        var changes = 0;
        registry.Transitioned += transition =>
        {
            if (transition.Kind == FeatureStatusTransitionKind.Changed) changes++;
        };

        statuses.ObserveLifecycleNotReady(
            config.Current,
            9,
            statuses.AutoBuy.ConfigurationGeneration);

        Assert.All(registry.GetSnapshot(), status => Assert.Equal(9, status.LifecycleGeneration));
        Assert.Equal(8, changes);

        for (var index = 0; index < 10_000; index++)
            statuses.ObserveLifecycleNotReady(
                config.Current,
                9,
                statuses.AutoBuy.ConfigurationGeneration);

        Assert.Equal(8, changes);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, statuses.AutoBuy.Current.Reason.Code);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoCast.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoConcept.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, statuses.SpellLevel.Current.Reason.Code);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoHarvest.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoItems.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoScribe.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Mentor.Current.State);
    }

    [Fact]
    public void StableLifecycleProjectionReusesCachedConfigurationDisabledSummaries()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, registry);
        var autoCastSummary = statuses.AutoCast.ConfigurationDisabledSummary;
        var autoConceptSummary = statuses.AutoConcept.ConfigurationDisabledSummary;

        statuses.ObserveLifecycleNotReady(
            config.Current,
            2,
            statuses.AutoBuy.ConfigurationGeneration);
        Assert.Same(autoCastSummary, statuses.AutoCast.Current.Reason.Summary);
        Assert.Same(autoConceptSummary, statuses.AutoConcept.Current.Reason.Summary);

        for (var index = 0; index < 10_000; index++)
            statuses.ObserveLifecycleNotReady(
                config.Current,
                2,
                statuses.AutoBuy.ConfigurationGeneration);

        Assert.Same(autoCastSummary, statuses.AutoCast.Current.Reason.Summary);
        Assert.Same(autoConceptSummary, statuses.AutoConcept.Current.Reason.Summary);
    }

    [Fact]
    public void AggregateRegistrationRollsBackEarlierKeysWhenALaterKeyIsAlreadyOwned()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        var duplicateKey = new FeatureStatusKey(
            PluginIds.SuiteGuid,
            AutomataFeatureStatuses.AutoConceptFeatureId);
        using var existing = registry.Register(new FeatureStatusSnapshot(
            duplicateKey,
            "Existing Auto Concept",
            false,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Existing owner remains registered."),
            3));

        Assert.Throws<InvalidOperationException>(() =>
            new AutomataFeatureStatuses(config.Current, 3, registry));

        var snapshot = Assert.Single(registry.GetSnapshot());
        Assert.Equal(duplicateKey, snapshot.Key);
        Assert.Equal("Existing Auto Concept", snapshot.DisplayName);
        Assert.False(registry.TryGet(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoBuyFeatureId),
            out _));
        Assert.False(registry.TryGet(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoCastFeatureId),
            out _));
    }

    [Fact]
    public void ControlsKeepConfiguredIntentOnAcrossRuntimeHealthTransitions()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 3, registry);
        var changes = new AutomataConfigurationStore(config, (_, _) => { });
        var control = new AutoCastToggleControl(changes, () => statuses.AutoCast.Current);

        Assert.Equal(AutoCastToggleVisualState.On, control.State);
        Assert.True(control.Status.ConfiguredEnabled);

        statuses.AutoCast.Observe(
            true,
            FeatureStatusState.ContractUnavailable,
            FeatureStatusReasonCode.ContractUnavailable,
            "The native cast contract is unavailable.");

        Assert.Equal(AutoCastToggleVisualState.On, control.State);
        Assert.Equal(FeatureStatusState.ContractUnavailable, control.Status.State);
        Assert.Contains("Configured: Enabled", FeatureStatusPresenter.Format(control.Status));
        Assert.Contains("Runtime: Unavailable", FeatureStatusPresenter.Format(control.Status));

        statuses.AutoCast.ObserveOperational();
        Assert.Equal(AutoCastToggleVisualState.On, control.State);
    }

    [Fact]
    public void ControlDesiredStateComesOnlyFromSavedConfiguration()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 3, registry);
        var savedTransitions = 0;
        config.AutoCastMode.SettingChanged += (_, _) => savedTransitions++;
        var changes = new AutomataConfigurationStore(config, (_, _) => { });
        var control = new AutoCastToggleControl(changes, () => statuses.AutoCast.Current);

        Assert.True(control.Status.ConfiguredEnabled);
        Assert.Equal(AutoCastToggleVisualState.On, control.State);

        control.Toggle();

        Assert.Equal(1, savedTransitions);
        Assert.Equal(AutoCastOperationMode.Disabled, config.Current.AutoCast.Mode);
        Assert.True(control.Status.ConfiguredEnabled);
        Assert.Equal(AutoCastToggleVisualState.Off, control.State);
    }

    [Fact]
    public void ProductionWiredControlsPublishConfiguredIntentBeforeReturning()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 3, registry);
        var changes = new AutomataConfigurationStore(
            config,
            statuses.ObserveConfiguration);
        var autoBuy = new AutoBuyToggleControl(
            changes,
            () => AutoSpellLevelCapability.Locked,
            () => statuses.AutoBuy.Current,
            () => statuses.SpellLevel.Current);
        var autoCast = new AutoCastToggleControl(
            changes,
            () => statuses.AutoCast.Current);
        var autoConcept = new AutoConceptToggleControl(
            changes,
            () => statuses.AutoConcept.Current);

        Assert.Equal(AutoCastToggleVisualState.On, autoBuy.State);
        Assert.Equal(AutoCastToggleVisualState.Off, autoCast.State);
        Assert.Equal(AutoCastToggleVisualState.Off, autoConcept.State);

        autoBuy.Toggle();
        autoCast.Toggle();
        autoConcept.Toggle();

        Assert.Equal(AutoBuyOperationMode.Disabled, config.Current.AutoBuy.Mode);
        Assert.Equal(AutoCastOperationMode.Active, config.Current.AutoCast.Mode);
        Assert.Equal(AutoConceptOperationMode.Active, config.Current.AutoConcept.Mode);
        Assert.Equal(AutoCastToggleVisualState.Off, autoBuy.State);
        Assert.Equal(AutoCastToggleVisualState.On, autoCast.State);
        Assert.Equal(AutoCastToggleVisualState.On, autoConcept.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, autoBuy.Status.State);
        Assert.Equal(FeatureStatusState.NotReady, autoCast.Status.State);
        Assert.Equal(FeatureStatusState.NotReady, autoConcept.Status.State);
    }

    [Fact]
    public void OneClickPublishesOneSavedTransitionAndRejectsTheOlderStatusRepaint()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        config.TryTakeUnpublishedChange(out _);
        var registry = new FeatureStatusRegistry();
        var initialGeneration = new ConfigGeneration(1);
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            3,
            registry,
            initialGeneration);
        var savedTransitions = 0;
        config.AutoCastMode.SettingChanged += (_, _) => savedTransitions++;
        var publications = 0;
        var changes = new AutomataConfigurationStore(
            config,
            (snapshot, generation) =>
            {
                publications++;
                statuses.ObserveConfiguration(snapshot, generation);
            });
        var control = new AutoCastToggleControl(
            changes,
            () => statuses.AutoCast.Current);
        var renderedTransitions = new List<AutoCastToggleVisualState>();
        registry.Transitioned += transition =>
        {
            if (transition.Kind == FeatureStatusTransitionKind.Changed &&
                transition.Current?.Key.Equals(statuses.AutoCast.Key) == true)
                renderedTransitions.Add(control.State);
        };

        control.Toggle();

        Assert.Equal(1, savedTransitions);
        Assert.Equal(1, publications);
        Assert.Equal(AutoCastOperationMode.Disabled, config.Current.AutoCast.Mode);
        Assert.Equal(AutoCastToggleVisualState.Off, control.State);
        Assert.Equal(new[] { AutoCastToggleVisualState.Off }, renderedTransitions);

        Assert.False(statuses.AutoCast.ObserveLifecycle(
            true,
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            string.Empty,
            3,
            initialGeneration));

        Assert.Equal(AutoCastToggleVisualState.Off, control.State);
        Assert.Equal(new[] { AutoCastToggleVisualState.Off }, renderedTransitions);
    }

    [Fact]
    public void EveryFeatureStatusRejectsAConfiguredIntentFromAnOlderGeneration()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        var initialGeneration = new ConfigGeneration(1);
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            5,
            registry,
            initialGeneration);
        config.AutoBuyMode.Value = AutoBuyOperationMode.Disabled;
        config.AutoLevelSpells.Value = false;
        var currentGeneration = initialGeneration.Next();

        statuses.ObserveConfiguration(config.Current, currentGeneration);

        var reporters = new[]
        {
            statuses.AutoBuy,
            statuses.AutoCast,
            statuses.AutoConcept,
            statuses.SpellLevel,
            statuses.AutoHarvest,
            statuses.Mentor,
        };
        Assert.All(reporters, reporter => Assert.False(reporter.Current.ConfiguredEnabled));

        foreach (var reporter in reporters)
        {
            Assert.False(reporter.ObserveLifecycle(
                true,
                FeatureStatusState.Operational,
                FeatureStatusReasonCode.None,
                string.Empty,
                5,
                initialGeneration));
        }

        Assert.All(reporters, reporter =>
        {
            Assert.False(reporter.Current.ConfiguredEnabled);
            Assert.Equal(
                FeatureStatusState.ConfigurationDisabled,
                reporter.Current.State);
            Assert.Equal(currentGeneration, reporter.ConfigurationGeneration);
        });
    }

    [Fact]
    public void ConfigurationPublicationPreservesUnchangedRuntimeHealth()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 3, registry);
        statuses.AutoCast.Observe(
            true,
            FeatureStatusState.Faulted,
            FeatureStatusReasonCode.RuntimeFailure,
            "The worker is unavailable.");

        config.AutoCastFullCharge.Value = false;
        statuses.ObserveConfiguration(
            config.Current,
            statuses.AutoBuy.ConfigurationGeneration.Next());

        Assert.True(statuses.AutoCast.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.Faulted, statuses.AutoCast.Current.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, statuses.AutoCast.Current.Reason.Code);
    }

    [Fact]
    public void MissingServiceCycleTracksDisabledEnabledDisabledIntent()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 3, registry);

        statuses.ObserveServiceCycleUnavailable(
            config.Current,
            statuses.AutoBuy.ConfigurationGeneration);
        Assert.False(statuses.AutoCast.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoCast.Current.State);

        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        statuses.ObserveConfiguration(
            config.Current,
            statuses.AutoBuy.ConfigurationGeneration.Next());
        statuses.ObserveServiceCycleUnavailable(
            config.Current,
            statuses.AutoBuy.ConfigurationGeneration);
        Assert.True(statuses.AutoCast.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.Faulted, statuses.AutoCast.Current.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, statuses.AutoCast.Current.Reason.Code);

        config.AutoCastMode.Value = AutoCastOperationMode.Disabled;
        statuses.ObserveConfiguration(
            config.Current,
            statuses.AutoBuy.ConfigurationGeneration.Next());
        statuses.ObserveServiceCycleUnavailable(
            config.Current,
            statuses.AutoBuy.ConfigurationGeneration);
        Assert.False(statuses.AutoCast.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoCast.Current.State);
    }
}
