using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

/// <summary>
/// Application composition for an automation whose one GameAction supplies native capability
/// health and whose worker projects its feature-owned decision. Native lifetime remains owned by
/// LifecycleGameActions; this wrapper owns the registration and the shared health observation.
/// </summary>
internal sealed class AutomationServiceFeatureRuntime<TState, TAction> : IAutomataServiceCycleFeatureRuntime
{
    private readonly ServiceRegistration<TState, TAction> _registration;
    private readonly ServiceId _serviceId;
    private readonly ILifecycleGameAction _gameAction;
    private readonly AutomataFeatureStatusReporter _status;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Func<string> _ownershipFailure;
    private readonly Func<ServiceStateProjectionSnapshot,
        (FeatureStatusState State, FeatureStatusReasonCode Reason, string Summary)> _projectHealth;
    private readonly ServiceRetryEpisodeNarrator _retries;
    private readonly Func<ServiceStateProjectionSnapshot, string>? _describeDetail;
    private long _lifecycle;
    private ConfigGeneration _configuration;
    private ServiceFault _fault;
    private ServiceStateProjectionSnapshot _projection;
    private bool _hasProjection;
    private bool _evaluationRefreshPending;
    private bool _emergency;
    private bool _owned;
    private bool _bindingsAvailable;
    private string _bindingFailure;
    private bool _diagnosticsActive;

    internal AutomationServiceFeatureRuntime(ServiceRegistration<TState, TAction> registration,
        ServiceId serviceId, ILifecycleGameAction gameAction, AutomataFeatureStatusReporter status,
        Func<bool> ownsActionFamily, Func<string> ownershipFailure,
        Func<ServiceStateProjectionSnapshot,
            (FeatureStatusState State, FeatureStatusReasonCode Reason, string Summary)> projectHealth,
        long lifecycle, ConfigGeneration configuration, Action<string> warn,
        Func<ServiceStateProjectionSnapshot, string>? describeDetail = null)
    {
        _registration = registration;
        _serviceId = serviceId;
        _gameAction = gameAction;
        _status = status;
        _ownsActionFamily = ownsActionFamily;
        _ownershipFailure = ownershipFailure;
        _projectHealth = projectHealth;
        _describeDetail = describeDetail;
        _lifecycle = lifecycle;
        _configuration = configuration;
        _retries = new ServiceRetryEpisodeNarrator(status.DisplayName, warn);
        _owned = AutomationCycleGuard.Owns(ownsActionFamily);
        _bindingsAvailable = gameAction.BindingsAvailable;
        _bindingFailure = gameAction.BindingFailure;
    }

    public void ActivateDiagnostics()
    {
        _diagnosticsActive = true;
        Publish();
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        if (!_diagnosticsActive) return;
        var owned = AutomationCycleGuard.Owns(_ownsActionFamily);
        var changed = _emergency != pump.IsEmergencyStopEngaged || _owned != owned ||
            _bindingsAvailable != _gameAction.BindingsAvailable ||
            !string.Equals(_bindingFailure, _gameAction.BindingFailure, StringComparison.Ordinal);
        _emergency = pump.IsEmergencyStopEngaged;
        _owned = owned;
        _bindingsAvailable = _gameAction.BindingsAvailable;
        _bindingFailure = _gameAction.BindingFailure;
        if (report.ResponsesAcquired != 0) _evaluationRefreshPending = true;
        // Start and state-construction faults can precede any response or action. Take one
        // nonblocking diagnostics read each frame; only a pending response admits a projection.
        // A contended read keeps that response pending for a later quiet frame.
        var service = ServiceCycleDiagnostics.ReadAvailableService(pump, _serviceId);
        changed |= _retries.Observe(service?.RetryGate ?? _retries.Current, pump.DiagnosticsNow);
        var lifecycle = _registration.LifecycleSnapshot;
        var constructionFault = lifecycle.ConstructionFault;
        changed |= service is null && lifecycle.DesiredLifecycle.Value == (ulong)_lifecycle && constructionFault.IsValid
            ? ServiceCycleDiagnostics.RefreshFault(in constructionFault, ref _fault)
            : ServiceCycleDiagnostics.RefreshFault(service, ref _fault);
        if (_evaluationRefreshPending && service is { } current &&
            ServiceCycleDiagnostics.HasCurrentProjection(in current, _lifecycle, _configuration))
        {
            _evaluationRefreshPending = false;
            _projection = current.LatestProjection.Snapshot;
            _hasProjection = true;
            changed = true;
        }
        if (changed) Publish();
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configuration.Value) return;
        _configuration = configurationGeneration;
        ClearProjection();
        if (_diagnosticsActive) Publish();
    }

    public void ObserveLifecycle(long nativeLifecycle, ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configuration.Value) return;
        _lifecycle = nativeLifecycle;
        _configuration = configurationGeneration;
        ClearProjection();
        _retries.InvalidateLifecycle();
        if (_diagnosticsActive) Publish();
    }

    public void DisposeDiagnostics() => _diagnosticsActive = false;
    public void DisposeRegistration() => _registration.Dispose();

    private void ClearProjection()
    {
        _hasProjection = false;
        _evaluationRefreshPending = false;
        _projection = default;
        _fault = default;
    }

    private void Publish()
    {
        var health = Project();
        health = ServiceCycleDiagnostics.ProjectHealth(health.State, health.Reason, health.Summary, in _fault);
        var detail = _hasProjection && _describeDetail is not null ? _describeDetail(_projection) : string.Empty;
        _status.ObserveRuntimeLifecycle(health.State, health.Reason, health.Summary, _lifecycle, _configuration,
            detail: detail);
    }

    private (FeatureStatusState State, FeatureStatusReasonCode Reason, string Summary) Project()
    {
        if (_emergency)
            return (FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.EmergencyDisabled,
                "Suite emergency stop is active.");
        if (!_owned)
        {
            var reason = _ownershipFailure();
            return (FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.ActionFamilyConflict,
                string.IsNullOrWhiteSpace(reason) ? _status.DisplayName + " does not own its action family." : reason);
        }
        if (!_gameAction.BindingsAvailable)
            return (FeatureStatusState.ContractUnavailable, FeatureStatusReasonCode.ContractUnavailable,
                _gameAction.BindingFailure);
        // A due retry remains an unresolved failure until it commits or the worker ends the episode.
        if (_retries.Current.Count != 0)
        {
            var retry = _retries.Current.Latest;
            return (FeatureStatusState.Degraded, FeatureStatusReasonCode.NativeMutationFailed,
                "Retrying " + EntityIdentityFormatter.Format(retry.Target.Id) + ": " + retry.Reason);
        }
        if (!_hasProjection)
            return (FeatureStatusState.NotReady, FeatureStatusReasonCode.GameplayNotReady,
                _status.DisplayName + " is waiting for its first current world evaluation.");
        return _projectHealth(_projection);
    }
}
