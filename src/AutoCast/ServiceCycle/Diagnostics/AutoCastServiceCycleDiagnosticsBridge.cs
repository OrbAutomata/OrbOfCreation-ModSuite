using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

/// <summary>
/// Keeps the Auto Cast feature-status line describing the running feature, which is what the toggle
/// button's tooltip reads.
/// </summary>
/// <remarks>
/// Ownership, the emergency stop and the manual pause are main-thread state, which is why the line
/// is written here rather than by the worker; the one worker fact it carries, a ritual hold, is read
/// from the worker's latest projection. The pause is read per frame
/// rather than latched, because it is the one blocking term that expires without anybody doing
/// anything, and a line that only updated when something happened would keep claiming a pause that
/// ran out while the player stood still.
/// </remarks>
internal sealed class AutoCastServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private readonly AutoCastManualPauseState _manualPause;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _manualPaused;
    private bool _cycleObserved;
    private AutoCastExclusion _heldFor;
    private Guid _heldSpellId;
    private ServiceCycleServiceDiagnosticsSnapshot[] _services =
        new ServiceCycleServiceDiagnosticsSnapshot[8];

    public AutoCastServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned,
        AutoCastManualPauseState manualPause,
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        _manualPause = manualPause;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        PublishFeatureStatus();
    }

    public void Observe(SuiteFramePump pump, in SuiteFramePumpReport report, bool owned)
    {
        var paused = _manualPause.IsPaused(pump.DiagnosticsNow);
        var conditionsChanged =
            _emergencyDisabled != pump.IsEmergencyStopEngaged ||
            _owned != owned ||
            _manualPaused != paused;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _owned = owned;
        _manualPaused = paused;
        if (report.ResponsesAcquired != 0)
        {
            ReadLatestHold(pump, out var heldFor, out var heldSpellId);
            conditionsChanged = conditionsChanged || !_cycleObserved ||
                _heldFor != heldFor || _heldSpellId != heldSpellId;
            _cycleObserved = true;
            _heldFor = heldFor;
            _heldSpellId = heldSpellId;
        }

        if (conditionsChanged) PublishFeatureStatus();
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _heldFor = AutoCastExclusion.None;
        _heldSpellId = Guid.Empty;
    }

    public void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        // Nothing survives a lifecycle boundary. A pause earned in the previous run of the game is
        // not a fact about this one, and the worker has not evaluated against the new world yet.
        _manualPause.Reset();
        _manualPaused = false;
        _cycleObserved = false;
        _heldFor = AutoCastExclusion.None;
        _heldSpellId = Guid.Empty;
        PublishFeatureStatus();
    }

    private void PublishFeatureStatus()
    {
        var health = AutoCastFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _manualPaused,
            _cycleObserved,
            _heldFor,
            _heldSpellId);
        _featureStatus.ObserveRuntimeLifecycle(
            health.State,
            health.Reason,
            health.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private void ReadLatestHold(SuiteFramePump pump, out AutoCastExclusion heldFor, out Guid heldSpellId)
    {
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        if (copy.RequiredCount > _services.Length)
        {
            _services = new ServiceCycleServiceDiagnosticsSnapshot[copy.RequiredCount];
            copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        }

        for (var index = 0; index < copy.WrittenCount; index++)
        {
            var service = _services[index];
            if (!service.ServiceId.Equals(AutoCastServicePolicies.ServiceId) ||
                !service.LatestProjection.IsPresent) continue;
            var projection = service.LatestProjection.Snapshot;
            heldSpellId = ServiceProjectionGuid.Read(in projection, AutoCastServiceProjection.HeldSpellKey);
            heldFor = AutoCastDecisionMetrics.HeldReason(
                heldSpellId, ReadInteger(in projection, AutoCastServiceProjection.ExcludedOutsideRitualKey));
            return;
        }

        heldFor = AutoCastExclusion.None;
        heldSpellId = Guid.Empty;
    }

    private static long ReadInteger(in ServiceStateProjectionSnapshot projection, int key)
    {
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == key && entry.Value.Kind == ServiceProjectionValueKind.Integer)
                return entry.Value.Integer;
        }
        return 0;
    }
}
