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
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
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
        if (report.ResponsesAcquired != 0) _projectionRefreshPending = true;
        if (!_projectionRefreshPending)
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        if (!TryReadLatestProjection(pump, out var projection))
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        _projectionRefreshPending = false;
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

    /// <summary>
    /// The latest projection Auto Harvest's own service published, if it has published one and the
    /// read is fresh.
    /// </summary>
    private static bool TryReadLatestProjection(
        SuiteFramePump pump,
        out ServiceStateProjectionSnapshot projection)
    {
        if (ServiceCycleDiagnostics.ReadAvailableService(pump, AutoHarvestServicePolicies.ServiceId) is
            { LatestProjection.IsPresent: true } service)
        {
            projection = service.LatestProjection.Snapshot;
            return true;
        }
        projection = default;
        return false;
    }

    private void PublishFeatureStatus()
    {
        var health = AutoHarvestFeatureStatusProjector.Project(_fruit, _treasure);
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
