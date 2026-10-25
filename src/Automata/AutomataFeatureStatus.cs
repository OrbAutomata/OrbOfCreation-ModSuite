using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common;

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

    public bool ObserveOperational(string detail = "") => Observe(
        true,
        FeatureStatusState.Operational,
        FeatureStatusReasonCode.None,
        string.Empty,
        detail);

    public bool Observe(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        string detail = "") =>
        Observe(
            configuredEnabled,
            state,
            reasonCode,
            summary,
            ConfigurationGeneration,
            detail);

    internal bool Observe(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        ConfigGeneration configurationGeneration,
        string detail = "") =>
        ObserveLifecycle(
            configuredEnabled,
            state,
            reasonCode,
            summary,
            Current.LifecycleGeneration,
            configurationGeneration,
            detail);

    public bool ObserveLifecycle(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        long lifecycleGeneration,
        string detail = "") =>
        ObserveLifecycle(
            configuredEnabled,
            state,
            reasonCode,
            summary,
            lifecycleGeneration,
            ConfigurationGeneration,
            detail);

    internal bool ObserveLifecycle(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration,
        string detail = "")
    {
        if (configurationGeneration.Value < ConfigurationGeneration.Value) return false;
        ConfigurationGeneration = configurationGeneration;
        ApplyEmergencyStop(configuredEnabled, ref state, ref reasonCode, ref summary);
        var status = new FeatureStatusSnapshot(
            Key,
            DisplayName,
            configuredEnabled,
            state,
            reasonCode == FeatureStatusReasonCode.None
                ? default
                : new FeatureStatusReason(reasonCode, summary),
            lifecycleGeneration,
            configuredEnabled && !_emergencyStopActive ? detail : string.Empty);
        Current = status;
        return _registration.Update(status);
    }

    internal bool ObserveRuntimeLifecycle(
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration,
        string detail = "")
    {
        if (!RuntimeHealthMayReplaceCurrent(state))
            return false;
        return ObserveLifecycle(
            true,
            state,
            reasonCode,
            summary,
            lifecycleGeneration,
            configurationGeneration,
            detail);
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
    private readonly Dictionary<string, AutomataFeatureStatusReporter> _byId = new(StringComparer.Ordinal);
    private readonly AutomataFeatureStatusReporter[] _all;
    private ConfigGeneration _configurationGeneration;

    internal AutomataFeatureStatusReporter Get(string featureId) => _byId[featureId];

    public AutomataFeatureStatuses(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        FeatureStatusRegistry? registry = null,
        ConfigGeneration? configurationGeneration = null)
    {
        var initialGeneration = configurationGeneration ?? new ConfigGeneration(1);
        _configurationGeneration = initialGeneration;
        var target = registry ?? FeatureStatusRegistry.Shared;
        var reporters = new AutomataFeatureStatusReporter?[AutomataFeatureCatalog.All.Count];
        try
        {
            for (var index = 0; index < AutomataFeatureCatalog.All.Count; index++)
            {
                reporters[index] = CreateInitialReporter(
                    target, AutomataFeatureCatalog.All[index], config,
                    lifecycleGeneration, initialGeneration);
            }
        }
        catch
        {
            for (var index = reporters.Length - 1; index >= 0; index--) reporters[index]?.Dispose();
            throw;
        }
        _all = reporters!;
        foreach (var reporter in _all) _byId.Add(reporter.Key.FeatureId, reporter);
        ObserveConfiguration(config, initialGeneration);
    }

    internal void ObserveConfiguration(
        SuiteRuntimeConfiguration config,
        ConfigGeneration configurationGeneration)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        foreach (var reporter in _all)
            reporter.SetEmergencyStop(config.Safety.EmergencyDisable, configurationGeneration);
        foreach (var reporter in _all)
            ObserveConfiguredIntent(reporter, config, configurationGeneration);
    }

    internal void ObserveContractUnavailable(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        string summary,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
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
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
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
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
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
        var blockedBy = BlockedBy(featureId, config);
        var constraint = blockedBy ?? ConfiguredConstraint(featureId, config);
        if (constraint is null) return false;
        reporter.ObserveLifecycle(
            true,
            FeatureStatusState.TemporarilyBlocked,
            blockedBy is not null ? FeatureStatusReasonCode.ParentFeatureDisabled :
                FeatureStatusReasonCode.ConfigurationDisabled,
            constraint,
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
        if (!reporter.Current.ConfiguredEnabled ||
            reporter.Current.Reason.Code is FeatureStatusReasonCode.ParentFeatureDisabled
                or FeatureStatusReasonCode.EmergencyDisabled
                or FeatureStatusReasonCode.ConfigurationDisabled)
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
                configurationGeneration,
                reporter.Current.Detail);
    }

    private static AutomataFeatureContribution Contribution(string featureId) =>
        AutomataFeatureCatalog.All.First(feature => feature.Id == featureId);

    private static bool IsConfigured(string featureId, SuiteRuntimeConfiguration config) =>
        Contribution(featureId).IsConfigured(config);

    private static string? BlockedBy(string featureId, SuiteRuntimeConfiguration config) =>
        !config.General.Enabled ? "Automata is disabled by configuration." :
            Contribution(featureId).BlockedBy(config);

    private static string? ConfiguredConstraint(string featureId, SuiteRuntimeConfiguration config) =>
        Contribution(featureId).ConfiguredConstraint(config);

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
        AutomataFeatureContribution feature,
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        ConfigGeneration configurationGeneration)
    {
        var featureId = feature.Id;
        var displayName = feature.DisplayName;
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
