using System;
using OrbModding;
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
/// <para>
/// A spell backing off on the service's retry gate is narrated here and keeps the feature Degraded,
/// naming the spell, what the service was doing with it and why, until its episode ends.
/// </para>
/// </remarks>
internal sealed class AutoCastServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private readonly AutoCastManualPauseState _manualPause;
    private readonly ServiceRetryEpisodeNarrator _retries = new(
        AutoCastNativeAdapter.Feature,
        static message => Plugin.Log?.LogAutomataWarning(message),
        AutoCastServicePolicies.Describe);
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _manualPaused;
    private bool _cycleObserved;
    private AutoCastExclusion _heldFor;
    private Guid _heldSpellId;

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
        var service = report.ResponsesAcquired != 0 || report.ActionsAttempted != 0
            ? ServiceCycleDiagnostics.ReadAvailableService(pump, AutoCastServicePolicies.ServiceId)
            : null;
        conditionsChanged |= _retries.Observe(service?.RetryGate ?? _retries.Current, pump.DiagnosticsNow);
        if (report.ResponsesAcquired != 0 && service is { } available)
        {
            ReadLatestHold(in available, out var heldFor, out var heldSpellId);
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
        _retries.InvalidateLifecycle();
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
            _heldSpellId,
            _retries.TryGetBackingOff(out var retry) ? retry : null);
        _featureStatus.ObserveRuntimeLifecycle(
            health.State,
            health.Reason,
            health.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private static void ReadLatestHold(
        in ServiceCycleServiceDiagnosticsSnapshot service,
        out AutoCastExclusion heldFor,
        out Guid heldSpellId)
    {
        heldFor = AutoCastExclusion.None;
        heldSpellId = Guid.Empty;
        if (!service.LatestProjection.IsPresent) return;
        var projection = service.LatestProjection.Snapshot;
        heldSpellId = ServiceProjectionGuid.Read(in projection, AutoCastServiceProjection.HeldSpellKey);
        heldFor = AutoCastDecisionMetrics.HeldReason(
            heldSpellId, ReadInteger(in projection, AutoCastServiceProjection.ExcludedOutsideRitualKey));
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
