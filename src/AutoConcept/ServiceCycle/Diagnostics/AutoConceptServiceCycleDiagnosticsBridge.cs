using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoConceptServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
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
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
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
        if (report.ResponsesAcquired != 0 &&
            TryReadLatestDecision(pump, out var idle))
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
            _idle);
        _featureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private static bool TryReadLatestDecision(
        SuiteFramePump pump,
        out AutoConceptIdle idle)
    {
        idle = default;
        if (ServiceCycleDiagnostics.ReadAvailableService(pump, AutoConceptServicePolicies.ServiceId) is not
            { LatestProjection.IsPresent: true } service)
            return false;
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
