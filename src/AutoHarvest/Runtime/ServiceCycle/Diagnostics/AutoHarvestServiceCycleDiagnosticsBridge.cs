using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoHarvestServiceCycleDiagnosticsBridge : IDisposable
{
    internal const string ImplementationName = "ServiceCycle";

    private readonly AutomataFeatureStatusReporter _featureStatus;
    private readonly AutoHarvestRuntimeDiagnosticsPublisher? _runtime;
    private readonly ServiceRetryEpisodeNarrator _retries;
    private AutoHarvestPairHealth _fruit;
    private AutoHarvestPairHealth _treasure;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _ownsActionFamily;
    private bool _projectionRefreshPending;

    public AutoHarvestServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool ownsActionFamily,
        RuntimeDiagnosticsRegistry? runtimeDiagnostics,
        AutomataFeatureStatusReporter featureStatus,
        Action<string>? warn = null)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        _retries = new ServiceRetryEpisodeNarrator(
            "Auto Harvest",
            warn ?? AutoHarvestFeatureDependencies.LogWarning,
            AutoHarvestServicePolicies.Describe);
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _ownsActionFamily = ownsActionFamily;
        InitialHealth(featureStatus?.Current.ConfiguredEnabled == true, out _fruit, out _treasure);
        if (runtimeDiagnostics is not null)
        {
            _runtime = new AutoHarvestRuntimeDiagnosticsPublisher(
                lifecycle,
                _fruit,
                _treasure,
                ImplementationName,
                runtimeDiagnostics);
        }
        PublishFeatureStatus();
    }

    public void Observe(
        SuiteFramePump pump,
        in SuiteFramePumpReport report,
        bool ownsActionFamily)
    {
        var conditionsChanged =
            _emergencyDisabled != pump.IsEmergencyStopEngaged ||
            _ownsActionFamily != ownsActionFamily;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _ownsActionFamily = ownsActionFamily;
        var service = report.ResponsesAcquired != 0 || report.ActionsAttempted != 0
            ? ReadService(pump)
            : null;
        conditionsChanged |= _retries.Observe(service?.RetryGate ?? _retries.Current, pump.DiagnosticsNow);
        if (report.ResponsesAcquired != 0) _projectionRefreshPending = true;
        if (!_projectionRefreshPending)
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        service ??= ReadService(pump);
        if (service is not { LatestProjection.IsPresent: true })
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        _projectionRefreshPending = false;
        var projection = service.Value.LatestProjection.Snapshot;
        if (!AutoHarvestServiceProjection.TryReadFruitHealth(in projection, out var fruit) ||
            !AutoHarvestServiceProjection.TryReadTreasureHealth(in projection, out var treasure))
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        if (SameHealth(_fruit, fruit) && SameHealth(_treasure, treasure))
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        _fruit = fruit;
        _treasure = treasure;
        _runtime?.PublishState(_lifecycle, _fruit, _treasure);
        PublishFeatureStatus();
    }

    public void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool ownsActionFamily)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _ownsActionFamily = ownsActionFamily;
        _projectionRefreshPending = false;
        _retries.InvalidateLifecycle();
        InitialHealth(_featureStatus.Current.ConfiguredEnabled, out _fruit, out _treasure);
        _runtime?.PublishState(lifecycle, _fruit, _treasure);
        PublishFeatureStatus();
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _projectionRefreshPending = false;
        InitialHealth(_featureStatus.Current.ConfiguredEnabled, out _fruit, out _treasure);
        _runtime?.PublishState(_lifecycle, _fruit, _treasure);
    }

    public void Dispose() => _runtime?.Dispose();

    private static ServiceCycleServiceDiagnosticsSnapshot? ReadService(SuiteFramePump pump) =>
        ServiceCycleDiagnostics.ReadAvailableService(pump, AutoHarvestServicePolicies.ServiceId);

    private void PublishFeatureStatus()
    {
        var health = AutoHarvestFeatureStatusProjector.Project(
            _fruit,
            _treasure,
            _retries.TryGetBackingOff(out var retry) ? retry : null);
        if (health.State != FeatureStatusState.ConfigurationDisabled && _emergencyDisabled)
        {
            _featureStatus.ObserveRuntimeLifecycle(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Emergency disable blocks Auto Harvest.",
                _lifecycle,
                _configurationGeneration);
            return;
        }
        if (health.State != FeatureStatusState.ConfigurationDisabled && !_ownsActionFamily)
        {
            _featureStatus.ObserveRuntimeLifecycle(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Harvest action-family ownership is unavailable.",
                _lifecycle,
                _configurationGeneration);
            return;
        }
        _featureStatus.ObserveRuntimeLifecycle(
            health.State,
            health.Reason,
            health.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private static void InitialHealth(
        bool configuredEnabled,
        out AutoHarvestPairHealth fruit,
        out AutoHarvestPairHealth treasure)
    {
        fruit = configuredEnabled
            ? AutoHarvestPairHealth.NotObserved(AutoHarvestPair.FruitTree)
            : AutoHarvestPairHealth.NotSelected(AutoHarvestPair.FruitTree);
        treasure = configuredEnabled
            ? AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree)
            : AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree);
    }

    private static bool SameHealth(
        in AutoHarvestPairHealth left,
        in AutoHarvestPairHealth right) =>
        left.Pair == right.Pair &&
        left.Selected == right.Selected &&
        left.Kind == right.Kind &&
        left.FeatureScoped == right.FeatureScoped;
}
