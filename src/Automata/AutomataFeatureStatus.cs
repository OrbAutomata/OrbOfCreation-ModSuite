using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common;
using OrbMentor;

namespace OrbAutomata;

internal sealed class AutomataFeatureStatusReporter : IDisposable
{
    private readonly FeatureStatusRegistration _registration;
    private bool _emergencyStopActive;

    public AutomataFeatureStatusReporter(
        FeatureStatusRegistry registry,
        FeatureStatusSnapshot initialStatus,
        ConfigGeneration? configurationGeneration = null)
    {
        Key = initialStatus.Key;
        DisplayName = initialStatus.DisplayName;
        ConfigurationDisabledSummary = initialStatus.State == FeatureStatusState.ConfigurationDisabled
            ? initialStatus.Reason.Summary
            : DisplayName + " is disabled by configuration.";
        Current = initialStatus;
        ConfigurationGeneration =
            configurationGeneration ?? new ConfigGeneration(1);
        _registration = registry.Register(Current);
    }

    public FeatureStatusKey Key { get; }
    public string DisplayName { get; }
    public string ConfigurationDisabledSummary { get; }
    public FeatureStatusSnapshot Current { get; private set; }
    internal ConfigGeneration ConfigurationGeneration { get; private set; }

    public bool ObserveOperational() => Observe(
        true,
        FeatureStatusState.Operational,
        FeatureStatusReasonCode.None,
        string.Empty);

    public bool Observe(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary) =>
        Observe(
            configuredEnabled,
            state,
            reasonCode,
            summary,
            ConfigurationGeneration);

    internal bool Observe(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < ConfigurationGeneration.Value) return false;
        ConfigurationGeneration = configurationGeneration;
        ApplyEmergencyStop(configuredEnabled, ref state, ref reasonCode, ref summary);
        if (Current.ConfiguredEnabled == configuredEnabled &&
            Current.State == state &&
            Current.Reason.Code == reasonCode)
            return false;
        var status = new FeatureStatusSnapshot(
            Key,
            DisplayName,
            configuredEnabled,
            state,
            reasonCode == FeatureStatusReasonCode.None
                ? default
                : new FeatureStatusReason(reasonCode, summary),
            Current.LifecycleGeneration);
        var prior = Current;
        Current = status;
        if (_registration.Update(status)) return true;
        Current = prior;
        return false;
    }

    internal bool ObserveRuntime(
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        ConfigGeneration configurationGeneration)
    {
        if (!RuntimeHealthMayReplaceCurrent(state))
            return false;
        return Observe(
            true,
            state,
            reasonCode,
            summary,
            configurationGeneration);
    }

    public bool ObserveLifecycle(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        long lifecycleGeneration) =>
        ObserveLifecycle(
            configuredEnabled,
            state,
            reasonCode,
            summary,
            lifecycleGeneration,
            ConfigurationGeneration);

    internal bool ObserveLifecycle(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < ConfigurationGeneration.Value) return false;
        ConfigurationGeneration = configurationGeneration;
        ApplyEmergencyStop(configuredEnabled, ref state, ref reasonCode, ref summary);
        if (Current.ConfiguredEnabled == configuredEnabled &&
            Current.State == state &&
            Current.Reason.Code == reasonCode &&
            Current.LifecycleGeneration == lifecycleGeneration)
            return false;
        var status = new FeatureStatusSnapshot(
            Key,
            DisplayName,
            configuredEnabled,
            state,
            reasonCode == FeatureStatusReasonCode.None
                ? default
                : new FeatureStatusReason(reasonCode, summary),
            lifecycleGeneration);
        var prior = Current;
        Current = status;
        if (_registration.Update(status)) return true;
        Current = prior;
        return false;
    }

    internal bool ObserveRuntimeLifecycle(
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration)
    {
        if (!RuntimeHealthMayReplaceCurrent(state))
            return false;
        return ObserveLifecycle(
            true,
            state,
            reasonCode,
            summary,
            lifecycleGeneration,
            configurationGeneration);
    }

    private bool RuntimeHealthMayReplaceCurrent(FeatureStatusState state) =>
        Current.ConfiguredEnabled &&
        state != FeatureStatusState.ConfigurationDisabled &&
        !(Current.State == FeatureStatusState.TemporarilyBlocked &&
          Current.Reason.Code is FeatureStatusReasonCode.ParentFeatureDisabled
              or FeatureStatusReasonCode.ConfigurationDisabled);

    public void Dispose() => _registration.Dispose();

    internal void SetEmergencyStop(
        bool active,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < ConfigurationGeneration.Value) return;
        ConfigurationGeneration = configurationGeneration;
        if (_emergencyStopActive == active) return;
        _emergencyStopActive = active;
        if (active)
        {
            Observe(
                Current.ConfiguredEnabled,
                Current.State,
                Current.Reason.Code,
                Current.Reason.Summary,
                configurationGeneration);
        }
    }

    private void ApplyEmergencyStop(
        bool configuredEnabled,
        ref FeatureStatusState state,
        ref FeatureStatusReasonCode reasonCode,
        ref string summary)
    {
        if (!configuredEnabled || !_emergencyStopActive) return;
        state = FeatureStatusState.TemporarilyBlocked;
        reasonCode = FeatureStatusReasonCode.EmergencyDisabled;
        summary = "Suite emergency stop is active.";
    }
}

internal sealed class AutomataFeatureStatuses : IDisposable
{
    internal const string AutoBuyFeatureId = "AutoBuy";
    internal const string AutoCastFeatureId = "AutoCast";
    internal const string AutoConceptFeatureId = "AutoConcept";
    internal const string SpellLevelFeatureId = "SpellLevel";
    internal const string AutoHarvestFeatureId = "AutoHarvest";
    internal const string AutoItemsFeatureId = "AutoItems";
    internal const string AutoScribeFeatureId = "AutoScribe";
    internal const string MentorFeatureId = "Mentor";

    public AutomataFeatureStatuses(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        FeatureStatusRegistry? registry = null,
        ConfigGeneration? configurationGeneration = null)
    {
        var initialGeneration = configurationGeneration ?? new ConfigGeneration(1);
        var target = registry ?? FeatureStatusRegistry.Shared;
        AutomataFeatureStatusReporter? autoBuy = null;
        AutomataFeatureStatusReporter? autoCast = null;
        AutomataFeatureStatusReporter? autoConcept = null;
        AutomataFeatureStatusReporter? spellLevel = null;
        AutomataFeatureStatusReporter? autoHarvest = null;
        AutomataFeatureStatusReporter? autoItems = null;
        AutomataFeatureStatusReporter? autoScribe = null;
        AutomataFeatureStatusReporter? mentor = null;
        try
        {
            autoBuy = CreateInitialReporter(
                target, AutoBuyFeatureId, "Auto Buy", config.General.Enabled,
                config.AutoBuy.Mode == AutoBuyOperationMode.Active, true, lifecycleGeneration, initialGeneration);
            autoCast = CreateInitialReporter(
                target, AutoCastFeatureId, "Auto Cast", config.General.Enabled,
                config.AutoCast.Mode == AutoCastOperationMode.Active, true, lifecycleGeneration, initialGeneration);
            autoConcept = CreateInitialReporter(
                target, AutoConceptFeatureId, "Auto Concept", config.General.Enabled,
                config.AutoConcept.Mode == AutoConceptOperationMode.Active, true, lifecycleGeneration, initialGeneration);
            spellLevel = CreateInitialReporter(
                target, SpellLevelFeatureId, "Spell Leveling", config.General.Enabled,
                config.AutoBuy.AutoLevelSpells,
                config.AutoBuy.Mode == AutoBuyOperationMode.Active,
                lifecycleGeneration,
                initialGeneration);
            autoHarvest = CreateInitialReporter(
                target, AutoHarvestFeatureId, "Auto Harvest", config.General.Enabled,
                IsAutoHarvestConfigured(config), true, lifecycleGeneration, initialGeneration);
            autoItems = CreateInitialReporter(
                target, AutoItemsFeatureId, "Auto Items", config.General.Enabled,
                IsAutoItemsConfigured(config), true, lifecycleGeneration, initialGeneration);
            autoScribe = CreateInitialReporter(
                target, AutoScribeFeatureId, "Auto Scribe", config.General.Enabled,
                IsAutoScribeConfigured(config), IsAutoItemsScrollsConfigured(config),
                lifecycleGeneration, initialGeneration);
            mentor = CreateInitialReporter(
                target, MentorFeatureId, "Orb Mentor", config.General.Enabled,
                config.Mentor.Mode == MentorOperationMode.Active, true, lifecycleGeneration, initialGeneration);
        }
        catch
        {
            mentor?.Dispose();
            autoScribe?.Dispose();
            autoItems?.Dispose();
            autoHarvest?.Dispose();
            spellLevel?.Dispose();
            autoConcept?.Dispose();
            autoCast?.Dispose();
            autoBuy?.Dispose();
            throw;
        }
        AutoBuy = autoBuy!;
        AutoCast = autoCast!;
        AutoConcept = autoConcept!;
        SpellLevel = spellLevel!;
        AutoHarvest = autoHarvest!;
        AutoItems = autoItems!;
        AutoScribe = autoScribe!;
        Mentor = mentor!;
        ObserveConfiguration(config, initialGeneration);
    }

    public AutomataFeatureStatusReporter AutoBuy { get; }
    public AutomataFeatureStatusReporter AutoCast { get; }
    public AutomataFeatureStatusReporter AutoConcept { get; }
    public AutomataFeatureStatusReporter SpellLevel { get; }
    public AutomataFeatureStatusReporter AutoHarvest { get; }
    public AutomataFeatureStatusReporter AutoItems { get; }
    public AutomataFeatureStatusReporter AutoScribe { get; }
    public AutomataFeatureStatusReporter Mentor { get; }

    internal void ObserveConfiguration(
        SuiteRuntimeConfiguration config,
        ConfigGeneration configurationGeneration)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        SetEmergencyStop(config.Safety.EmergencyDisable, configurationGeneration);
        ObserveConfiguredIntent(
            AutoBuy,
            config.General.Enabled,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            parentEnabled: true,
            configurationGeneration,
            config.AutoBuy.IncludeStructures || config.AutoBuy.IncludeUpgrades
                ? null
                : "Auto Buy has neither structures nor upgrades selected to buy.");
        ObserveConfiguredIntent(
            AutoCast,
            config.General.Enabled,
            config.AutoCast.Mode == AutoCastOperationMode.Active,
            parentEnabled: true,
            configurationGeneration);
        ObserveConfiguredIntent(
            AutoConcept,
            config.General.Enabled,
            config.AutoConcept.Mode == AutoConceptOperationMode.Active,
            parentEnabled: true,
            configurationGeneration);
        ObserveConfiguredIntent(
            SpellLevel,
            config.General.Enabled,
            config.AutoBuy.AutoLevelSpells,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            configurationGeneration);
        ObserveConfiguredIntent(
            AutoHarvest,
            config.General.Enabled,
            IsAutoHarvestConfigured(config),
            parentEnabled: true,
            configurationGeneration);
        ObserveConfiguredIntent(
            AutoItems,
            config.General.Enabled,
            IsAutoItemsConfigured(config),
            parentEnabled: true,
            configurationGeneration,
            AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems)
                ? null
                : "Auto Items has neither Scrolls nor Relics selected.");
        ObserveConfiguredIntent(
            AutoScribe,
            config.General.Enabled,
            IsAutoScribeConfigured(config),
            IsAutoItemsScrollsConfigured(config),
            configurationGeneration,
            AutoScribeRoleSelection.ConfigurationProblem(config.AutoScribe.Roles));
        ObserveConfiguredIntent(
            Mentor,
            config.General.Enabled,
            config.Mentor.Mode == MentorOperationMode.Active,
            parentEnabled: true,
            configurationGeneration);
    }

    internal bool ObserveAutoBuyInvariantStandDown(
        string summary,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < AutoBuy.ConfigurationGeneration.Value)
            return false;
        return AutoBuy.Observe(
            false,
            FeatureStatusState.ConfigurationDisabled,
            FeatureStatusReasonCode.InvariantViolation,
            summary,
            configurationGeneration);
    }

    private void SetEmergencyStop(
        bool active,
        ConfigGeneration configurationGeneration)
    {
        AutoBuy.SetEmergencyStop(active, configurationGeneration);
        AutoCast.SetEmergencyStop(active, configurationGeneration);
        AutoConcept.SetEmergencyStop(active, configurationGeneration);
        SpellLevel.SetEmergencyStop(active, configurationGeneration);
        AutoHarvest.SetEmergencyStop(active, configurationGeneration);
        AutoItems.SetEmergencyStop(active, configurationGeneration);
        AutoScribe.SetEmergencyStop(active, configurationGeneration);
        Mentor.SetEmergencyStop(active, configurationGeneration);
    }

    private void ProjectContractUnavailable(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        string summary)
    {
        ObserveContractFeature(AutoBuy, config.General.Enabled,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active, true, lifecycleGeneration, summary);
        ObserveContractFeature(AutoCast, config.General.Enabled,
            config.AutoCast.Mode == AutoCastOperationMode.Active, true, lifecycleGeneration, summary);
        ObserveContractFeature(AutoConcept, config.General.Enabled,
            config.AutoConcept.Mode == AutoConceptOperationMode.Active, true, lifecycleGeneration, summary);
        ObserveContractFeature(SpellLevel, config.General.Enabled,
            config.AutoBuy.AutoLevelSpells,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            lifecycleGeneration, summary);
        ObserveContractFeature(AutoHarvest, config.General.Enabled,
            IsAutoHarvestConfigured(config), true, lifecycleGeneration, summary);
        ObserveContractFeature(AutoItems, config.General.Enabled,
            IsAutoItemsConfigured(config), true, lifecycleGeneration, summary);
        ObserveContractFeature(AutoScribe, config.General.Enabled,
            IsAutoScribeConfigured(config), IsAutoItemsScrollsConfigured(config),
            lifecycleGeneration, summary);
        ObserveContractFeature(Mentor, config.General.Enabled,
            config.Mentor.Mode == MentorOperationMode.Active, true, lifecycleGeneration, summary);
    }

    internal void ObserveContractUnavailable(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        string summary,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < AutoBuy.ConfigurationGeneration.Value) return;
        ObserveConfiguration(config, configurationGeneration);
        ProjectContractUnavailable(config, lifecycleGeneration, summary);
    }

    private void ProjectServiceCycleUnavailable(SuiteRuntimeConfiguration config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        const string summary = "The shared Automata ServiceCycle host is unavailable.";
        ObserveUnavailableFeature(
            AutoBuy,
            config.General.Enabled,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            parentEnabled: true,
            summary);
        ObserveUnavailableFeature(
            AutoCast,
            config.General.Enabled,
            config.AutoCast.Mode == AutoCastOperationMode.Active,
            parentEnabled: true,
            summary);
        ObserveUnavailableFeature(
            AutoConcept,
            config.General.Enabled,
            config.AutoConcept.Mode == AutoConceptOperationMode.Active,
            parentEnabled: true,
            summary);
        ObserveUnavailableFeature(
            SpellLevel,
            config.General.Enabled,
            config.AutoBuy.AutoLevelSpells,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            summary);
        ObserveUnavailableFeature(
            AutoHarvest,
            config.General.Enabled,
            IsAutoHarvestConfigured(config),
            parentEnabled: true,
            summary);
        ObserveUnavailableFeature(
            AutoItems,
            config.General.Enabled,
            IsAutoItemsConfigured(config),
            parentEnabled: true,
            summary);
        ObserveUnavailableFeature(
            AutoScribe,
            config.General.Enabled,
            IsAutoScribeConfigured(config),
            IsAutoItemsScrollsConfigured(config),
            summary);
        ObserveUnavailableFeature(
            Mentor,
            config.General.Enabled,
            config.Mentor.Mode == MentorOperationMode.Active,
            parentEnabled: true,
            summary);
    }

    internal void ObserveServiceCycleUnavailable(
        SuiteRuntimeConfiguration config,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < AutoBuy.ConfigurationGeneration.Value) return;
        ObserveConfiguration(config, configurationGeneration);
        ProjectServiceCycleUnavailable(config);
    }

    private void ProjectLifecycleNotReady(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration)
    {
        ObserveLifecycleFeature(
            AutoBuy,
            config.General.Enabled,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoCast,
            config.General.Enabled,
            config.AutoCast.Mode == AutoCastOperationMode.Active,
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoConcept,
            config.General.Enabled,
            config.AutoConcept.Mode == AutoConceptOperationMode.Active,
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoHarvest,
            config.General.Enabled,
            IsAutoHarvestConfigured(config),
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoItems,
            config.General.Enabled,
            IsAutoItemsConfigured(config),
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoScribe,
            config.General.Enabled && IsAutoItemsScrollsConfigured(config),
            IsAutoScribeConfigured(config),
            lifecycleGeneration);
        ObserveLifecycleFeature(
            Mentor,
            config.General.Enabled,
            config.Mentor.Mode == MentorOperationMode.Active,
            lifecycleGeneration);

        if (!config.AutoBuy.AutoLevelSpells)
        {
            ObserveConfigurationDisabled(SpellLevel, lifecycleGeneration);
        }
        else if (!config.General.Enabled || config.AutoBuy.Mode == AutoBuyOperationMode.Disabled)
        {
            SpellLevel.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                !config.General.Enabled
                    ? "Automata is disabled by configuration."
                    : "Auto Buy is disabled by configuration.",
                lifecycleGeneration);
        }
        else
        {
            ObserveGameplayNotReady(SpellLevel, lifecycleGeneration);
        }
    }

    internal void ObserveLifecycleNotReady(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < AutoBuy.ConfigurationGeneration.Value) return;
        ObserveConfiguration(config, configurationGeneration);
        ProjectLifecycleNotReady(config, lifecycleGeneration);
    }

    private static void ObserveLifecycleFeature(
        AutomataFeatureStatusReporter reporter,
        bool pluginEnabled,
        bool featureEnabled,
        long lifecycleGeneration)
    {
        if (!featureEnabled)
        {
            ObserveConfigurationDisabled(reporter, lifecycleGeneration);
            return;
        }
        if (!pluginEnabled)
        {
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Automata is disabled by configuration.",
                lifecycleGeneration);
            return;
        }
        ObserveGameplayNotReady(reporter, lifecycleGeneration);
    }

    private static void ObserveConfiguredIntent(
        AutomataFeatureStatusReporter reporter,
        bool pluginEnabled,
        bool featureEnabled,
        bool parentEnabled,
        ConfigGeneration configurationGeneration,
        string? configuredConstraint = null)
    {
        var lifecycleGeneration = reporter.Current.LifecycleGeneration;
        if (!featureEnabled)
        {
            ObserveConfigurationDisabled(
                reporter,
                lifecycleGeneration,
                configurationGeneration);
            return;
        }
        if (!pluginEnabled || !parentEnabled)
        {
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                !pluginEnabled
                    ? "Automata is disabled by configuration."
                    : "Auto Buy is disabled by configuration.",
                lifecycleGeneration,
                configurationGeneration);
            return;
        }
        if (configuredConstraint is not null)
        {
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ConfigurationDisabled,
                configuredConstraint,
                lifecycleGeneration,
                configurationGeneration);
            return;
        }
        if (!reporter.Current.ConfiguredEnabled ||
            reporter.Current.Reason.Code is FeatureStatusReasonCode.ParentFeatureDisabled
                or FeatureStatusReasonCode.EmergencyDisabled)
            ObserveGameplayNotReady(
                reporter,
                lifecycleGeneration,
                configurationGeneration);
        else
            // Even when the rendered status is unchanged, advance the rejection floor.
            reporter.ObserveLifecycle(
                reporter.Current.ConfiguredEnabled,
                reporter.Current.State,
                reporter.Current.Reason.Code,
                reporter.Current.Reason.Summary,
                reporter.Current.LifecycleGeneration,
                configurationGeneration);
    }

    private static void ObserveContractFeature(
        AutomataFeatureStatusReporter reporter,
        bool pluginEnabled,
        bool featureEnabled,
        bool parentEnabled,
        long lifecycleGeneration,
        string summary)
    {
        if (!featureEnabled)
        {
            ObserveConfigurationDisabled(reporter, lifecycleGeneration);
            return;
        }
        if (!pluginEnabled || !parentEnabled)
        {
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                !pluginEnabled
                    ? "Automata is disabled by configuration."
                    : "The parent automation feature is disabled by configuration.",
                lifecycleGeneration);
            return;
        }
        reporter.ObserveLifecycle(
            true,
            FeatureStatusState.ContractUnavailable,
            FeatureStatusReasonCode.ContractUnavailable,
            summary,
            lifecycleGeneration);
    }

    private static void ObserveUnavailableFeature(
        AutomataFeatureStatusReporter reporter,
        bool pluginEnabled,
        bool featureEnabled,
        bool parentEnabled,
        string summary)
    {
        var lifecycleGeneration = reporter.Current.LifecycleGeneration;
        if (!featureEnabled)
        {
            ObserveConfigurationDisabled(reporter, lifecycleGeneration);
            return;
        }
        if (!pluginEnabled || !parentEnabled)
        {
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                !pluginEnabled
                    ? "Automata is disabled by configuration."
                    : "The parent automation feature is disabled by configuration.",
                lifecycleGeneration);
            return;
        }
        reporter.ObserveLifecycle(
            true,
            FeatureStatusState.Faulted,
            FeatureStatusReasonCode.RuntimeFailure,
            summary,
            lifecycleGeneration);
    }

    private static bool IsAutoHarvestConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoHarvest.Mode == AutoHarvestOperationMode.Active &&
        (config.AutoHarvest.CollectFruitTrees || config.AutoHarvest.CollectTreasureTrees);

    private static bool IsAutoItemsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoItems.Mode == AutoItemsOperationMode.Active &&
        AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems);

    private static bool IsAutoItemsScrollsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoItems.Mode == AutoItemsOperationMode.Active &&
        config.AutoItems.UseScrolls;

    private static bool IsAutoScribeConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoScribe.Mode == AutoScribeOperationMode.Active;

    private static void ObserveConfigurationDisabled(
        AutomataFeatureStatusReporter reporter,
        long lifecycleGeneration,
        ConfigGeneration? configurationGeneration = null) =>
        reporter.ObserveLifecycle(
            false,
            FeatureStatusState.ConfigurationDisabled,
            FeatureStatusReasonCode.ConfigurationDisabled,
            reporter.ConfigurationDisabledSummary,
            lifecycleGeneration,
            configurationGeneration ?? reporter.ConfigurationGeneration);

    private static void ObserveGameplayNotReady(
        AutomataFeatureStatusReporter reporter,
        long lifecycleGeneration,
        ConfigGeneration? configurationGeneration = null) =>
        reporter.ObserveLifecycle(
            true,
            FeatureStatusState.NotReady,
            FeatureStatusReasonCode.GameplayNotReady,
            "Gameplay lifecycle is not ready.",
            lifecycleGeneration,
            configurationGeneration ?? reporter.ConfigurationGeneration);

    private static AutomataFeatureStatusReporter CreateInitialReporter(
        FeatureStatusRegistry registry,
        string featureId,
        string displayName,
        bool pluginEnabled,
        bool featureEnabled,
        bool parentEnabled,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration)
    {
        var key = new FeatureStatusKey(PluginIds.SuiteGuid, featureId);
        FeatureStatusSnapshot status;
        if (!featureEnabled)
        {
            status = new FeatureStatusSnapshot(
                key,
                displayName,
                false,
                FeatureStatusState.ConfigurationDisabled,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.ConfigurationDisabled,
                    $"{displayName} is disabled by configuration."),
                lifecycleGeneration);
        }
        else if (!pluginEnabled || !parentEnabled)
        {
            status = new FeatureStatusSnapshot(
                key,
                displayName,
                true,
                FeatureStatusState.TemporarilyBlocked,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.ParentFeatureDisabled,
                    !pluginEnabled
                        ? "Automata is disabled by configuration."
                        : "Auto Buy is disabled by configuration."),
                lifecycleGeneration);
        }
        else
        {
            status = new FeatureStatusSnapshot(
                key,
                displayName,
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "Gameplay lifecycle is not ready."),
                lifecycleGeneration);
        }
        return new AutomataFeatureStatusReporter(registry, status, configurationGeneration);
    }

    public void Dispose()
    {
        Mentor.Dispose();
        AutoScribe.Dispose();
        AutoItems.Dispose();
        AutoHarvest.Dispose();
        SpellLevel.Dispose();
        AutoConcept.Dispose();
        AutoCast.Dispose();
        AutoBuy.Dispose();
    }
}

internal static class AutomataFeatureStatusVisuals
{
    public static bool IsEmergencyStopped(in FeatureStatusSnapshot status) =>
        status.ConfiguredEnabled &&
        status.Reason.Code == FeatureStatusReasonCode.EmergencyDisabled;
}
