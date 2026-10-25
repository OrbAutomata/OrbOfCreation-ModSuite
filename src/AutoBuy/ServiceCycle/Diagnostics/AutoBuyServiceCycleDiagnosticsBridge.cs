using System;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

/// <summary>
/// Keeps the Auto Buy feature-status line describing the running feature: what the player configured,
/// what the suite is allowed to do, and whether the service has actually evaluated yet.
/// </summary>
/// <remarks>
/// The decision journal records every cycle, but nothing in the UI reads it. The button, its tooltip,
/// and the Mod Config health row all read the feature status registry, so a mode changed mid-session
/// is invisible until something republishes here.
/// </remarks>
internal sealed class AutoBuyServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private readonly AutoBuyInvariantLatch _latch;
    private readonly Action<string> _warn;
    private readonly Action<string> _info;
    private readonly ServiceRetryEpisodeNarrator _retries;
    private int _publishedLatchRevision;
    private ServiceFault _runtimeFault;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private AutoBuyCandidateKinds _owned;
    private bool _cycleObserved;
    private bool _evaluationRefreshPending;
    private AutoBuyDecisionBlockReason _decisionBlock;
    private AutoBuyDecisionBlockReason _narratedDecisionBlock;

    public AutoBuyServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        AutoBuyCandidateKinds owned,
        AutomataFeatureStatusReporter featureStatus,
        AutoBuyInvariantLatch latch,
        Action<string> warn,
        Action<string>? info = null)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        _latch = latch ?? throw new ArgumentNullException(nameof(latch));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _info = info ?? (static _ => { });
        _retries = new ServiceRetryEpisodeNarrator("Auto Buy", warn);
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        PublishFeatureStatus();
    }

    public void Observe(
        SuiteFramePump pump,
        in SuiteFramePumpReport report,
        AutoBuyCandidateKinds owned)
    {
        var conditionsChanged =
            _emergencyDisabled != pump.IsEmergencyStopEngaged ||
            _owned != owned ||
            _publishedLatchRevision != _latch.Revision;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _owned = owned;
        if (report.ResponsesAcquired != 0) _evaluationRefreshPending = true;
        var service = report.ResponsesAcquired != 0 || report.ActionsAttempted != 0
            ? ServiceCycleDiagnostics.ReadAvailableService(pump, AutoBuyServicePolicies.ServiceId)
            : null;
        if (_evaluationRefreshPending)
            service ??= ServiceCycleDiagnostics.ReadAvailableService(pump, AutoBuyServicePolicies.ServiceId);
        conditionsChanged |= _retries.Observe(service?.RetryGate ?? _retries.Current, pump.DiagnosticsNow);
        conditionsChanged |= ServiceCycleDiagnostics.RefreshFault(service, ref _runtimeFault);
        if (_evaluationRefreshPending && service is { } observed &&
            ServiceCycleDiagnostics.HasCurrentProjection(in observed, _lifecycle, _configurationGeneration))
        {
            var projection = observed.LatestProjection;
            _evaluationRefreshPending = false;
            var wasObserved = _cycleObserved;
            var priorBlock = _decisionBlock;
            _cycleObserved = true;
            var snapshot = projection.Snapshot;
            _decisionBlock = TotalRelationBlock(in snapshot);
            if (!wasObserved || priorBlock != _decisionBlock)
            {
                PublishFeatureStatus();
                return;
            }
        }
        if (conditionsChanged) PublishFeatureStatus();
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _runtimeFault = default;
        _evaluationRefreshPending = false;
        _decisionBlock = AutoBuyDecisionBlockReason.None;
    }

    public void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        AutoBuyCandidateKinds owned)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        // A lifecycle boundary retires the worker state the previous generation evaluated against, so
        // the feature is waiting on a first evaluation again.
        _cycleObserved = false;
        _runtimeFault = default;
        _evaluationRefreshPending = false;
        _decisionBlock = AutoBuyDecisionBlockReason.None;
        _narratedDecisionBlock = AutoBuyDecisionBlockReason.None;
        _retries.InvalidateLifecycle();
        PublishFeatureStatus();
    }

    private void PublishFeatureStatus()
    {
        _publishedLatchRevision = _latch.Revision;
        var health = AutoBuyFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _cycleObserved,
            _decisionBlock,
            _latch.Summary,
            _retries.Current,
            _retries.ObservedAt);
        var runtimeStatus = ServiceCycleDiagnostics.ProjectHealth(health.State, health.Reason, health.Summary, in _runtimeFault);
        _featureStatus.ObserveRuntimeLifecycle(
            runtimeStatus.State,
            runtimeStatus.Reason,
            runtimeStatus.Summary,
            _lifecycle,
            _configurationGeneration);
        NarrateDecisionBlock(health);
    }

    internal static AutoBuyDecisionBlockReason TotalRelationBlock(
        in ServiceStateProjectionSnapshot projection)
    {
        var captured = ReadInteger(in projection, AutoBuyServiceProjection.CapturedCandidatesKey);
        if (captured <= 0 || ReadInteger(in projection, AutoBuyServiceProjection.EligibleCandidatesKey) != 0)
            return AutoBuyDecisionBlockReason.None;
        if (ReadInteger(in projection, AutoBuyServiceProjection.ExcludedOwningViewUnavailableKey) == captured)
            return AutoBuyDecisionBlockReason.OwningViewUnavailable;
        if (ReadInteger(in projection, AutoBuyServiceProjection.ExcludedOwningViewRelationMissingKey) == captured)
            return AutoBuyDecisionBlockReason.OwningViewRelationMissing;
        if (ReadInteger(in projection, AutoBuyServiceProjection.ExcludedOwningViewRelationUnreadableKey) == captured)
            return AutoBuyDecisionBlockReason.OwningViewRelationUnreadable;
        if (ReadInteger(in projection, AutoBuyServiceProjection.ExcludedOwningViewRelationContradictoryKey) == captured)
            return AutoBuyDecisionBlockReason.OwningViewRelationContradictory;
        var relationExclusions =
            ReadInteger(in projection, AutoBuyServiceProjection.ExcludedOwningViewUnavailableKey) +
            ReadInteger(in projection, AutoBuyServiceProjection.ExcludedOwningViewRelationMissingKey) +
            ReadInteger(in projection, AutoBuyServiceProjection.ExcludedOwningViewRelationUnreadableKey) +
            ReadInteger(in projection, AutoBuyServiceProjection.ExcludedOwningViewRelationContradictoryKey);
        if (relationExclusions == captured)
            return AutoBuyDecisionBlockReason.MixedPurchaseViewRelations;
        return AutoBuyDecisionBlockReason.None;
    }

    private static long ReadInteger(in ServiceStateProjectionSnapshot projection, int key)
    {
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == key && entry.Value.Kind == ServiceProjectionValueKind.Integer)
                return entry.Value.Integer;
        }
        return -1;
    }

    private void NarrateDecisionBlock(in AutoBuyFeatureStatus health)
    {
        if (_decisionBlock == _narratedDecisionBlock) return;
        if (_decisionBlock == AutoBuyDecisionBlockReason.None)
        {
            if (_narratedDecisionBlock != AutoBuyDecisionBlockReason.None)
                _info("Auto Buy recovered from its prior total purchase-view block.");
        }
        else
        {
            _warn("Auto Buy is temporarily blocked: " + health.Summary);
        }
        _narratedDecisionBlock = _decisionBlock;
    }
}
