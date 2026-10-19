using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

/// <summary>
/// Projects Auto Concept's service evidence onto its feature status. A Concept action backing off on
/// the service's retry gate is narrated here and keeps the feature Degraded, naming the Concept, what
/// the service was doing with it and why, until its episode ends.
/// </summary>
internal sealed class AutoConceptServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private readonly ServiceRetryEpisodeNarrator _retries;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _cycleObserved;
    private AutoConceptIdle _idle;

    internal AutoConceptServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned,
        AutomataFeatureStatusReporter featureStatus,
        Action<string> warn)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        _retries = new ServiceRetryEpisodeNarrator(
            AutoConceptNativeAdapter.Feature, warn, AutoConceptServicePolicies.Describe);
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        Publish();
    }

    internal void Observe(SuiteFramePump pump, in SuiteFramePumpReport report, bool owned)
    {
        var changed = _emergencyDisabled != pump.IsEmergencyStopEngaged || _owned != owned;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _owned = owned;
        var service = report.ResponsesAcquired != 0 || report.ActionsAttempted != 0
            ? ServiceCycleDiagnostics.ReadAvailableService(pump, AutoConceptServicePolicies.ServiceId)
            : null;
        changed |= _retries.Observe(service?.RetryGate ?? _retries.Current, pump.DiagnosticsNow);
        if (report.ResponsesAcquired != 0 && service is { } available &&
            TryReadLatestDecision(in available, out var idle))
        {
            changed = changed || !_cycleObserved || !_idle.Equals(idle);
            _cycleObserved = true;
            _idle = idle;
        }
        if (changed) Publish();
    }

    internal void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _idle = default;
    }

    internal void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        _retries.InvalidateLifecycle();
        _cycleObserved = false;
        _idle = default;
        Publish();
    }

    private void Publish()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _cycleObserved,
            _idle,
            _retries.TryGetBackingOff(out var retry) ? retry : null);
        _featureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private static bool TryReadLatestDecision(
        in ServiceCycleServiceDiagnosticsSnapshot service,
        out AutoConceptIdle idle)
    {
        idle = default;
        if (!service.LatestProjection.IsPresent) return false;
        var projection = service.LatestProjection.Snapshot;
        var reason = ReadInteger(in projection, AutoConceptServiceProjection.IdleReasonKey);
        var move = ReadInteger(in projection, AutoConceptServiceProjection.IdleMoveKey);
        if (reason is not (>= 0 and <= int.MaxValue) || move is not (>= 0 and <= int.MaxValue) ||
            !Enum.IsDefined(typeof(AutoConceptIdleReason), (int)reason.Value) ||
            !Enum.IsDefined(typeof(AutoConceptIdleMove), (int)move.Value))
            return false;
        idle = new AutoConceptIdle(
            (AutoConceptIdleReason)reason.Value,
            (AutoConceptIdleMove)move.Value,
            ServiceProjectionGuid.Read(in projection, AutoConceptServiceProjection.IdleRecipeKey),
            ServiceProjectionGuid.Read(in projection, AutoConceptServiceProjection.IdleDisplacedKey),
            ServiceProjectionGuid.Read(in projection, AutoConceptServiceProjection.IdleResourceKey));
        return true;
    }

    private static long? ReadInteger(in ServiceStateProjectionSnapshot projection, int key)
    {
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == key && entry.Value.Kind == ServiceProjectionValueKind.Integer)
                return entry.Value.Integer;
        }
        return null;
    }

}
