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
        ConfigGeneration configurationGeneration) =>
        ObserveLifecycle(
            configuredEnabled,
            state,
            reasonCode,
            summary,
            Current.LifecycleGeneration,
            configurationGeneration);

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

    private static readonly (string Id, string DisplayName)[] Features =
    {
        (AutoBuyFeatureId, "Auto Buy"),
        (AutoCastFeatureId, "Auto Cast"),
        (AutoConceptFeatureId, "Auto Concept"),
        (SpellLevelFeatureId, "Spell Leveling"),
        (AutoHarvestFeatureId, "Auto Harvest"),
        (AutoItemsFeatureId, "Auto Items"),
        (AutoScribeFeatureId, "Auto Scribe"),
        (MentorFeatureId, "Orb Mentor"),
    };

    private readonly AutomataFeatureStatusReporter[] _all;

    public AutomataFeatureStatuses(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        FeatureStatusRegistry? registry = null,
        ConfigGeneration? configurationGeneration = null)
    {
        var initialGeneration = configurationGeneration ?? new ConfigGeneration(1);
        var target = registry ?? FeatureStatusRegistry.Shared;
        var reporters = new AutomataFeatureStatusReporter?[Features.Length];
        try
        {
            for (var index = 0; index < Features.Length; index++)
            {
                reporters[index] = CreateInitialReporter(
                    target, Features[index].Id, Features[index].DisplayName, config,
                    lifecycleGeneration, initialGeneration);
            }
        }
        catch
        {
            for (var index = reporters.Length - 1; index >= 0; index--) reporters[index]?.Dispose();
            throw;
        }
        _all = reporters!;
        AutoBuy = _all[0];
        AutoCast = _all[1];
        AutoConcept = _all[2];
        SpellLevel = _all[3];
        AutoHarvest = _all[4];
        AutoItems = _all[5];
        AutoScribe = _all[6];
        Mentor = _all[7];
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
        foreach (var reporter in _all)
            reporter.SetEmergencyStop(config.Safety.EmergencyDisable, configurationGeneration);
        foreach (var reporter in _all)
            ObserveConfiguredIntent(reporter, config, configurationGeneration);
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

    internal void ObserveContractUnavailable(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        string summary,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < AutoBuy.ConfigurationGeneration.Value) return;
        ObserveConfiguration(config, configurationGeneration);
        foreach (var reporter in _all)
        {
            if (ObserveOffOrBlocked(reporter, config, lifecycleGeneration)) continue;
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable,
                summary,
                lifecycleGeneration);
        }
    }

    internal void ObserveServiceCycleUnavailable(
        SuiteRuntimeConfiguration config,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < AutoBuy.ConfigurationGeneration.Value) return;
        ObserveConfiguration(config, configurationGeneration);
        foreach (var reporter in _all)
        {
            var lifecycleGeneration = reporter.Current.LifecycleGeneration;
            if (ObserveOffOrBlocked(reporter, config, lifecycleGeneration)) continue;
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "The shared Automata ServiceCycle host is unavailable.",
                lifecycleGeneration);
        }
    }

    internal void ObserveLifecycleNotReady(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < AutoBuy.ConfigurationGeneration.Value) return;
        ObserveConfiguration(config, configurationGeneration);
        foreach (var reporter in _all)
        {
            if (ObserveOffOrBlocked(reporter, config, lifecycleGeneration)) continue;
            ObserveGameplayNotReady(reporter, lifecycleGeneration);
        }
    }

    /// <summary>
    /// Shows a feature switched off, or switched on and waiting on a switch above it, and says
    /// whether it did. Every projection asks this first, so the reasons cannot drift apart.
    /// </summary>
    private static bool ObserveOffOrBlocked(
        AutomataFeatureStatusReporter reporter,
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        ConfigGeneration? configurationGeneration = null)
    {
        var featureId = reporter.Key.FeatureId;
        if (!IsConfigured(featureId, config))
        {
            ObserveConfigurationDisabled(reporter, lifecycleGeneration, configurationGeneration);
            return true;
        }
        if (BlockedBy(featureId, config) is not { } blockedBy) return false;
        reporter.ObserveLifecycle(
            true,
            FeatureStatusState.TemporarilyBlocked,
            FeatureStatusReasonCode.ParentFeatureDisabled,
            blockedBy,
            lifecycleGeneration,
            configurationGeneration ?? reporter.ConfigurationGeneration);
        return true;
    }

    private static void ObserveConfiguredIntent(
        AutomataFeatureStatusReporter reporter,
        SuiteRuntimeConfiguration config,
        ConfigGeneration configurationGeneration)
    {
        var lifecycleGeneration = reporter.Current.LifecycleGeneration;
        if (ObserveOffOrBlocked(reporter, config, lifecycleGeneration, configurationGeneration)) return;
        if (ConfiguredConstraint(reporter.Key.FeatureId, config) is { } configuredConstraint)
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

    private static bool IsConfigured(string featureId, SuiteRuntimeConfiguration config) => featureId switch
    {
        AutoBuyFeatureId => config.AutoBuy.Mode == AutoBuyOperationMode.Active,
        AutoCastFeatureId => config.AutoCast.Mode == AutoCastOperationMode.Active,
        AutoConceptFeatureId => config.AutoConcept.Mode == AutoConceptOperationMode.Active,
        SpellLevelFeatureId => config.AutoBuy.AutoLevelSpells,
        AutoHarvestFeatureId => config.AutoHarvest.Mode == AutoHarvestOperationMode.Active &&
            (config.AutoHarvest.CollectFruitTrees || config.AutoHarvest.CollectTreasureTrees),
        AutoItemsFeatureId => config.AutoItems.Mode == AutoItemsOperationMode.Active &&
            AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems),
        AutoScribeFeatureId => config.AutoScribe.Mode == AutoScribeOperationMode.Active,
        MentorFeatureId => config.Mentor.Mode == MentorOperationMode.Active,
        _ => throw new ArgumentOutOfRangeException(nameof(featureId), featureId, null),
    };

    /// <summary>The switch above a switched-on feature that holds it, or null when none does.</summary>
    private static string? BlockedBy(string featureId, SuiteRuntimeConfiguration config)
    {
        if (!config.General.Enabled) return "Automata is disabled by configuration.";
        return featureId switch
        {
            SpellLevelFeatureId when config.AutoBuy.Mode != AutoBuyOperationMode.Active =>
                "Auto Buy is disabled by configuration.",
            AutoScribeFeatureId when config.AutoItems.Mode != AutoItemsOperationMode.Active ||
                                     !config.AutoItems.UseScrolls =>
                "Auto Scribe needs Auto Items running with Scrolls on.",
            _ => null,
        };
    }

    private static string? ConfiguredConstraint(string featureId, SuiteRuntimeConfiguration config) =>
        featureId switch
        {
            AutoBuyFeatureId when !config.AutoBuy.IncludeStructures && !config.AutoBuy.IncludeUpgrades =>
                "Auto Buy has neither structures nor upgrades selected to buy.",
            AutoItemsFeatureId when !AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems) =>
                "Auto Items has neither Scrolls nor Relics selected.",
            AutoScribeFeatureId => AutoScribeRoleSelection.ConfigurationProblem(config.AutoScribe.Roles),
            _ => null,
        };

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
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration)
    {
        var key = new FeatureStatusKey(PluginIds.SuiteGuid, featureId);
        var status = !IsConfigured(featureId, config)
            ? new FeatureStatusSnapshot(
                key,
                displayName,
                false,
                FeatureStatusState.ConfigurationDisabled,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.ConfigurationDisabled,
                    $"{displayName} is disabled by configuration."),
                lifecycleGeneration)
            : BlockedBy(featureId, config) is { } blockedBy
                ? new FeatureStatusSnapshot(
                    key,
                    displayName,
                    true,
                    FeatureStatusState.TemporarilyBlocked,
                    new FeatureStatusReason(FeatureStatusReasonCode.ParentFeatureDisabled, blockedBy),
                    lifecycleGeneration)
                : new FeatureStatusSnapshot(
                    key,
                    displayName,
                    true,
                    FeatureStatusState.NotReady,
                    new FeatureStatusReason(
                        FeatureStatusReasonCode.GameplayNotReady,
                        "Gameplay lifecycle is not ready."),
                    lifecycleGeneration);
        return new AutomataFeatureStatusReporter(registry, status, configurationGeneration);
    }

    public void Dispose()
    {
        for (var index = _all.Length - 1; index >= 0; index--) _all[index].Dispose();
    }
}

internal static class AutomataFeatureStatusVisuals
{
    public static bool IsEmergencyStopped(in FeatureStatusSnapshot status) =>
        status.ConfiguredEnabled &&
        status.Reason.Code == FeatureStatusReasonCode.EmergencyDisabled;
}
