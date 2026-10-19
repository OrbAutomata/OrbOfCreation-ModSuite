using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoItemsServiceCycleDiagnosticsBridge
{
    private readonly AutoItemsFeatureDependencies _dependencies;
    private readonly AutoItemsConsumableUseGameAction _gameAction;
    private readonly AutoItemsActionHealth _health;
    private readonly ServiceRetryEpisodeNarrator _retries;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _cycleObserved;
    private AutoItemsDecisionKind _decisionKind;
    private Guid _pausedTemporaryItem;
    private AutoItemsTemporaryPauseCause _temporaryPauseCause;
    private long _publishedHealthRevision = -1;
    private ServiceCycleServiceDiagnosticsSnapshot[] _services =
        new ServiceCycleServiceDiagnosticsSnapshot[8];

    internal AutoItemsServiceCycleDiagnosticsBridge(
        AutoItemsFeatureDependencies dependencies,
        AutoItemsConsumableUseGameAction gameAction,
        AutoItemsActionHealth health,
        long lifecycle,
        ConfigGeneration configurationGeneration)
    {
        _dependencies = dependencies;
        _gameAction = gameAction;
        _health = health;
        _retries = new ServiceRetryEpisodeNarrator("Auto Items", dependencies.Warn);
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        Publish();
    }

    internal void Observe(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        var changed = _emergencyDisabled != pump.IsEmergencyStopEngaged;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        if (report.ResponsesAcquired == 0 && report.ActionsAttempted == 0)
        {
            changed |= _retries.Observe(_retries.Current, pump.DiagnosticsNow);
            PublishIfChanged(changed);
            return;
        }
        var service = ReadService(pump);
        changed |= _retries.Observe(service?.RetryGate ?? _retries.Current, pump.DiagnosticsNow);
        if (report.ResponsesAcquired != 0 &&
            service.HasValue &&
            TryReadLatestDecision(
                service.Value,
                out var decisionKind,
                out var pausedTemporaryItem,
                out var temporaryPauseCause))
        {
            if (_pausedTemporaryItem != pausedTemporaryItem ||
                _temporaryPauseCause != temporaryPauseCause)
                WarnPauseTransition(pausedTemporaryItem, temporaryPauseCause);
            changed = changed ||
                !_cycleObserved ||
                _decisionKind != decisionKind;
            _cycleObserved = true;
            _decisionKind = decisionKind;
            _pausedTemporaryItem = pausedTemporaryItem;
            _temporaryPauseCause = temporaryPauseCause;
        }
        PublishIfChanged(changed);
    }

    private void PublishIfChanged(bool changed)
    {
        if (_publishedHealthRevision != _health.Revision)
        {
            _publishedHealthRevision = _health.Revision;
            changed = true;
        }
        if (changed) Publish();
    }

    internal void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _decisionKind = AutoItemsDecisionKind.Disabled;
    }

    internal void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _decisionKind = AutoItemsDecisionKind.Disabled;
        _pausedTemporaryItem = Guid.Empty;
        _temporaryPauseCause = AutoItemsTemporaryPauseCause.None;
        _health.InvalidateLifecycle();
        _publishedHealthRevision = _health.Revision;
        _retries.InvalidateLifecycle();
        Publish();
    }

    private void Publish()
    {
        var status = AutoItemsFeatureStatusProjector.Project(
            _emergencyDisabled,
            _dependencies.OwnsActionFamily(),
            _dependencies.ReadOwnershipFailure(),
            _gameAction.BindingsAvailable,
            _gameAction.BindingFailure,
            _cycleObserved,
            _decisionKind,
            _health,
            _retries.Current,
            _retries.ObservedAt);
        _dependencies.FeatureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private void WarnPauseTransition(Guid pausedItem, AutoItemsTemporaryPauseCause cause)
    {
        if (_pausedTemporaryItem != Guid.Empty && _pausedTemporaryItem != pausedItem)
            _dependencies.Warn(
                $"Auto Items resumed temporary item {EntityIdentityFormatter.Format(_pausedTemporaryItem)}: " +
                "no usage of it remains.");
        if (cause == AutoItemsTemporaryPauseCause.None) return;
        if (cause != AutoItemsTemporaryPauseCause.MultipleUsages)
            throw new ArgumentOutOfRangeException(nameof(cause), cause, null);
        _dependencies.Warn(
            $"Auto Items paused temporary item {EntityIdentityFormatter.Format(pausedItem)} " +
            "because more than one native usage appeared and Auto Items creates only one; " +
            "it resumes once no usage of it remains.");
    }

    private ServiceCycleServiceDiagnosticsSnapshot? ReadService(SuiteFramePump pump)
    {
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        if (copy.RequiredCount > _services.Length)
        {
            _services = new ServiceCycleServiceDiagnosticsSnapshot[copy.RequiredCount];
            copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        }
        for (var index = 0; index < copy.WrittenCount; index++)
            if (_services[index].ServiceId.Equals(AutoItemsServicePolicies.ServiceId))
                return _services[index];
        return null;
    }

    private static bool TryReadLatestDecision(
        in ServiceCycleServiceDiagnosticsSnapshot service,
        out AutoItemsDecisionKind decisionKind,
        out Guid pausedTemporaryItem,
        out AutoItemsTemporaryPauseCause temporaryPauseCause)
    {
        if (service.LatestProjection.IsPresent)
        {
            var projection = service.LatestProjection.Snapshot;
            return AutoItemsServiceProjection.TryReadDecision(
                in projection,
                out decisionKind,
                out pausedTemporaryItem,
                out temporaryPauseCause);
        }
        decisionKind = AutoItemsDecisionKind.Disabled;
        pausedTemporaryItem = Guid.Empty;
        temporaryPauseCause = AutoItemsTemporaryPauseCause.None;
        return false;
    }
}
