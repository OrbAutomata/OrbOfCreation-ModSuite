using OrbMentor;
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

        Assert.Equal(FeatureStatusState.NotReady, statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.State);
        Assert.True(statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.State);
        Assert.False(statuses.Get(AutoCastFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoConceptFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.NotReady, statuses.Get(SpellLevelFeatureContribution.FeatureId).Current.State);
        Assert.True(statuses.Get(SpellLevelFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoHarvestFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoItemsFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoScribeFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(MentorFeatureContribution.FeatureId).Current.State);
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
                transition.Current?.Key.Equals(statuses.Get(AutoBuyFeatureContribution.FeatureId).Key) == true)
                changes++;
        };

        Assert.True(statuses.Get(AutoBuyFeatureContribution.FeatureId).ObserveOperational());
        for (var index = 0; index < 10_000; index++)
            Assert.False(statuses.Get(AutoBuyFeatureContribution.FeatureId).ObserveOperational());
        Assert.True(statuses.Get(AutoBuyFeatureContribution.FeatureId).Observe(
            true,
            FeatureStatusState.Degraded,
            FeatureStatusReasonCode.PartialCapabilityUnavailable,
            "Upgrade automation is unavailable."));
        for (var index = 0; index < 10_000; index++)
        {
            Assert.False(statuses.Get(AutoBuyFeatureContribution.FeatureId).Observe(
                true,
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "Equivalent wording does not create another transition."));
            Assert.False(statuses.Get(AutoBuyFeatureContribution.FeatureId).ObserveLifecycle(
                true,
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "Equivalent lifecycle observation does not create another transition.",
                1));
        }

        Assert.Equal(2, changes);
        var reporter = statuses.Get(AutoBuyFeatureContribution.FeatureId);
        Assert.Equal("Equivalent lifecycle observation does not create another transition.", reporter.Current.Reason.Summary);
        Assert.True(registry.TryGet(reporter.Key, out var refreshed));
        Assert.Equal(reporter.Current, refreshed);
    }

    [Fact]
    public void LifecycleObservationNormalizesNoneReasonDespitePositiveSummaryText()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            1,
            new FeatureStatusRegistry());

        Assert.True(statuses.Get(AutoBuyFeatureContribution.FeatureId).ObserveLifecycle(
            configuredEnabled: true,
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            "Positive operational wording is not a reason.",
            lifecycleGeneration: 2));

        Assert.Equal(FeatureStatusState.Operational, statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusReasonCode.None, statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.Reason.Code);
        Assert.Null(statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.Reason.Summary);
        Assert.Equal(2, statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.LifecycleGeneration);
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

        statuses.Get(AutoCastFeatureContribution.FeatureId).ObserveRuntimeLifecycle(
            configuredEnabled
                ? FeatureStatusState.ConfigurationDisabled
                : FeatureStatusState.Operational,
            FeatureStatusReasonCode.RuntimeFailure,
            "A runtime writer cannot change saved intent.",
            4,
            generation);

        Assert.Equal(configuredEnabled, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.ConfiguredEnabled);
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

        Assert.True(statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.State);
        Assert.Equal(
            FeatureStatusReasonCode.ConfigurationDisabled,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.Reason.Code);

        Assert.False(statuses.Get(AutoBuyFeatureContribution.FeatureId).ObserveRuntimeLifecycle(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            string.Empty,
            4,
            generation));

        Assert.True(statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.State);
        Assert.Equal(
            FeatureStatusReasonCode.ConfigurationDisabled,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.Reason.Code);
    }

    [Fact]
    public void ItemsWithNoEligibleFamiliesKeepsConfiguredIntentAcrossProjectionsAndRecovers()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        config.AutoItemsUseScrolls.Value = false;
        config.AutoItemsUseRelics.Value = false;
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, new FeatureStatusRegistry());
        var reporter = statuses.Get(AutoItemsFeatureContribution.FeatureId);
        var generation = reporter.ConfigurationGeneration;

        AssertConfiguredConstraint(reporter, "temporary items");
        statuses.ObserveLifecycleNotReady(config.Current, 2, generation);
        AssertConfiguredConstraint(reporter, "temporary items");
        statuses.ObserveContractUnavailable(config.Current, 2, "Native contract unavailable.", generation);
        AssertConfiguredConstraint(reporter, "temporary items");
        statuses.ObserveServiceCycleUnavailable(config.Current, generation);
        AssertConfiguredConstraint(reporter, "temporary items");
        Assert.False(reporter.ObserveRuntimeLifecycle(FeatureStatusState.Operational,
            FeatureStatusReasonCode.None, string.Empty, 2, generation));

        config.AutoItemsUseScrolls.Value = true;
        statuses.ObserveConfiguration(config.Current, generation.Next());
        Assert.True(reporter.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.NotReady, reporter.Current.State);
        Assert.Equal(FeatureStatusReasonCode.GameplayNotReady, reporter.Current.Reason.Code);
    }

    [Fact]
    public void HarvestWithNoSelectedFamiliesKeepsConfiguredIntentAcrossProjectionsAndRecovers()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoHarvestMode.Value = AutoHarvestOperationMode.Active;
        config.AutoHarvestFruitTrees.Value = false;
        config.AutoHarvestTreasureTrees.Value = false;
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, new FeatureStatusRegistry());
        var reporter = statuses.Get(AutoHarvestFeatureContribution.FeatureId);
        var generation = reporter.ConfigurationGeneration;

        AssertConfiguredConstraint(reporter, "fruit trees nor treasure trees");
        statuses.ObserveLifecycleNotReady(config.Current, 2, generation);
        AssertConfiguredConstraint(reporter, "fruit trees nor treasure trees");
        statuses.ObserveContractUnavailable(config.Current, 2, "Native contract unavailable.", generation);
        AssertConfiguredConstraint(reporter, "fruit trees nor treasure trees");
        statuses.ObserveServiceCycleUnavailable(config.Current, generation);
        AssertConfiguredConstraint(reporter, "fruit trees nor treasure trees");

        config.AutoHarvestFruitTrees.Value = true;
        statuses.ObserveConfiguration(config.Current, generation.Next());
        Assert.True(reporter.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.NotReady, reporter.Current.State);
    }

    private static void AssertConfiguredConstraint(AutomataFeatureStatusReporter reporter, string reason)
    {
        Assert.True(reporter.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, reporter.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ConfigurationDisabled, reporter.Current.Reason.Code);
        Assert.Contains(reason, reporter.Current.Reason.Summary);
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

        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, statuses.Get(AutoScribeFeatureContribution.FeatureId).Current.Reason.Code);
        Assert.Equal(reason, statuses.Get(AutoScribeFeatureContribution.FeatureId).Current.Reason.Summary);

        statuses.ObserveLifecycleNotReady(config.Current, 2, statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        Assert.Equal(reason, statuses.Get(AutoScribeFeatureContribution.FeatureId).Current.Reason.Summary);

        statuses.ObserveContractUnavailable(
            config.Current, 3, "Assembly contract mismatch.", statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        Assert.Equal(reason, statuses.Get(AutoScribeFeatureContribution.FeatureId).Current.Reason.Summary);

        statuses.ObserveServiceCycleUnavailable(config.Current, statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        Assert.Equal(reason, statuses.Get(AutoScribeFeatureContribution.FeatureId).Current.Reason.Summary);
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
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);

        Assert.Equal(FeatureStatusState.ContractUnavailable, statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ContractUnavailable, statuses.Get(SpellLevelFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoConceptFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoHarvestFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoItemsFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoScribeFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(MentorFeatureContribution.FeatureId).Current.State);
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
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);

        Assert.All(registry.GetSnapshot(), status => Assert.Equal(9, status.LifecycleGeneration));
        Assert.Equal(8, changes);

        for (var index = 0; index < 10_000; index++)
            statuses.ObserveLifecycleNotReady(
                config.Current,
                9,
                statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);

        Assert.Equal(8, changes);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, statuses.Get(AutoBuyFeatureContribution.FeatureId).Current.Reason.Code);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoConceptFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, statuses.Get(SpellLevelFeatureContribution.FeatureId).Current.Reason.Code);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoHarvestFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoItemsFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoScribeFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(MentorFeatureContribution.FeatureId).Current.State);
    }

    [Fact]
    public void StableLifecycleProjectionReusesCachedConfigurationDisabledSummaries()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, registry);
        var autoCastSummary = statuses.Get(AutoCastFeatureContribution.FeatureId).ConfigurationDisabledSummary;
        var autoConceptSummary = statuses.Get(AutoConceptFeatureContribution.FeatureId).ConfigurationDisabledSummary;

        statuses.ObserveLifecycleNotReady(
            config.Current,
            2,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        Assert.Same(autoCastSummary, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.Reason.Summary);
        Assert.Same(autoConceptSummary, statuses.Get(AutoConceptFeatureContribution.FeatureId).Current.Reason.Summary);

        for (var index = 0; index < 10_000; index++)
            statuses.ObserveLifecycleNotReady(
                config.Current,
                2,
                statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);

        Assert.Same(autoCastSummary, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.Reason.Summary);
        Assert.Same(autoConceptSummary, statuses.Get(AutoConceptFeatureContribution.FeatureId).Current.Reason.Summary);
    }

    [Fact]
    public void AggregateRegistrationRollsBackEarlierKeysWhenALaterKeyIsAlreadyOwned()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        var duplicateKey = new FeatureStatusKey(
            PluginIds.SuiteGuid,
            AutoConceptFeatureContribution.FeatureId);
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
            new FeatureStatusKey(PluginIds.SuiteGuid, AutoBuyFeatureContribution.FeatureId),
            out _));
        Assert.False(registry.TryGet(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutoCastFeatureContribution.FeatureId),
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
        var control = new AutoCastToggleControl(changes, () => statuses.Get(AutoCastFeatureContribution.FeatureId).Current);

        Assert.True(control.IsOn);
        Assert.True(control.Status.ConfiguredEnabled);

        statuses.Get(AutoCastFeatureContribution.FeatureId).Observe(
            true,
            FeatureStatusState.ContractUnavailable,
            FeatureStatusReasonCode.ContractUnavailable,
            "The native cast contract is unavailable.");

        Assert.True(control.IsOn);
        Assert.Equal(FeatureStatusState.ContractUnavailable, control.Status.State);
        Assert.Contains("Configured: Enabled", FeatureStatusPresenter.Format(control.Status));
        Assert.Contains("Runtime: Unavailable", FeatureStatusPresenter.Format(control.Status));

        statuses.Get(AutoCastFeatureContribution.FeatureId).ObserveOperational();
        Assert.True(control.IsOn);
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
        var control = new AutoCastToggleControl(changes, () => statuses.Get(AutoCastFeatureContribution.FeatureId).Current);

        Assert.True(control.Status.ConfiguredEnabled);
        Assert.True(control.IsOn);

        control.Toggle();

        Assert.Equal(1, savedTransitions);
        Assert.Equal(AutoCastOperationMode.Disabled, config.Current.AutoCast.Mode);
        Assert.True(control.Status.ConfiguredEnabled);
        Assert.False(control.IsOn);
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
            () => statuses.Get(AutoBuyFeatureContribution.FeatureId).Current,
            () => statuses.Get(SpellLevelFeatureContribution.FeatureId).Current);
        var autoCast = new AutoCastToggleControl(
            changes,
            () => statuses.Get(AutoCastFeatureContribution.FeatureId).Current);
        var autoConcept = new AutoConceptToggleControl(
            changes,
            () => statuses.Get(AutoConceptFeatureContribution.FeatureId).Current);

        Assert.True(autoBuy.IsOn);
        Assert.False(autoCast.IsOn);
        Assert.False(autoConcept.IsOn);

        autoBuy.Toggle();
        autoCast.Toggle();
        autoConcept.Toggle();

        Assert.Equal(AutoBuyOperationMode.Disabled, config.Current.AutoBuy.Mode);
        Assert.Equal(AutoCastOperationMode.Active, config.Current.AutoCast.Mode);
        Assert.Equal(AutoConceptOperationMode.Active, config.Current.AutoConcept.Mode);
        Assert.False(autoBuy.IsOn);
        Assert.True(autoCast.IsOn);
        Assert.True(autoConcept.IsOn);
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
            () => statuses.Get(AutoCastFeatureContribution.FeatureId).Current);
        var renderedTransitions = new List<bool>();
        registry.Transitioned += transition =>
        {
            if (transition.Kind == FeatureStatusTransitionKind.Changed &&
                transition.Current?.Key.Equals(statuses.Get(AutoCastFeatureContribution.FeatureId).Key) == true)
                renderedTransitions.Add(control.IsOn);
        };

        control.Toggle();

        Assert.Equal(1, savedTransitions);
        Assert.Equal(1, publications);
        Assert.Equal(AutoCastOperationMode.Disabled, config.Current.AutoCast.Mode);
        Assert.False(control.IsOn);
        Assert.Equal(new[] { false }, renderedTransitions);

        Assert.False(statuses.Get(AutoCastFeatureContribution.FeatureId).ObserveLifecycle(
            true,
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            string.Empty,
            3,
            initialGeneration));

        Assert.False(control.IsOn);
        Assert.Equal(new[] { false }, renderedTransitions);
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
            statuses.Get(AutoBuyFeatureContribution.FeatureId),
            statuses.Get(AutoCastFeatureContribution.FeatureId),
            statuses.Get(AutoConceptFeatureContribution.FeatureId),
            statuses.Get(SpellLevelFeatureContribution.FeatureId),
            statuses.Get(AutoHarvestFeatureContribution.FeatureId),
            statuses.Get(MentorFeatureContribution.FeatureId),
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
        statuses.Get(AutoCastFeatureContribution.FeatureId).Observe(
            true,
            FeatureStatusState.Faulted,
            FeatureStatusReasonCode.RuntimeFailure,
            "The worker is unavailable.");

        config.AutoCastFullCharge.Value = false;
        statuses.ObserveConfiguration(
            config.Current,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration.Next());

        Assert.True(statuses.Get(AutoCastFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.Faulted, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.Reason.Code);
    }

    [Fact]
    public void MissingServiceCycleTracksDisabledEnabledDisabledIntent()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 3, registry);

        statuses.ObserveServiceCycleUnavailable(
            config.Current,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        Assert.False(statuses.Get(AutoCastFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.State);

        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        statuses.ObserveConfiguration(
            config.Current,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration.Next());
        statuses.ObserveServiceCycleUnavailable(
            config.Current,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        Assert.True(statuses.Get(AutoCastFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.Faulted, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.Reason.Code);

        config.AutoCastMode.Value = AutoCastOperationMode.Disabled;
        statuses.ObserveConfiguration(
            config.Current,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration.Next());
        statuses.ObserveServiceCycleUnavailable(
            config.Current,
            statuses.Get(AutoBuyFeatureContribution.FeatureId).ConfigurationGeneration);
        Assert.False(statuses.Get(AutoCastFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.Get(AutoCastFeatureContribution.FeatureId).Current.State);
    }
}
