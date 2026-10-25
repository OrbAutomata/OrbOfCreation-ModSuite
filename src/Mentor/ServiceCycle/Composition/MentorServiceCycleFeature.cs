using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbMentor;

internal sealed class MentorServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly MentorFeatureDependencies _dependencies;

    internal MentorServiceCycleFeature(MentorFeatureDependencies dependencies) =>
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public IAutomataServiceCycleFeatureRuntime Register(
        in AutomataServiceCycleFeatureContext context)
    {
        var adapters = MentorServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(
            adapters.Definition,
            ServiceActionDispatchPolicy.Single);
        return new MentorFeatureRuntime(
            _dependencies,
            adapters.Natives,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration);
    }

}

internal sealed class MentorFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly MentorFeatureDependencies _dependencies;
    private readonly MentorNativeAdapter _natives;
    private readonly ServiceRegistration<MentorCycleState, MentorCycleAction> _registration;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _cycleObserved;
    private bool _evaluationRefreshPending;
    private ServiceFault _runtimeFault;

    internal MentorFeatureRuntime(
        MentorFeatureDependencies dependencies,
        MentorNativeAdapter natives,
        ServiceRegistration<MentorCycleState, MentorCycleAction> registration,
        long lifecycle,
        ConfigGeneration configurationGeneration)
    {
        _dependencies = dependencies;
        _natives = natives;
        _registration = registration;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
    }

    public void ActivateDiagnostics() => Publish();

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        if (report.ResponsesAcquired != 0) _evaluationRefreshPending = true;
        var service = _evaluationRefreshPending || report.ActionsAttempted != 0
            ? ServiceCycleDiagnostics.ReadAvailableService(pump, MentorServicePolicies.ServiceId)
            : null;
        var changed = ServiceCycleDiagnostics.RefreshFault(service, ref _runtimeFault);
        if (_evaluationRefreshPending && service is { } observed &&
            ServiceCycleDiagnostics.HasCurrentProjection(in observed, _lifecycle, _configurationGeneration))
        {
            _evaluationRefreshPending = false;
            changed |= !_cycleObserved;
            _cycleObserved = true;
        }
        if (changed) Publish();
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _evaluationRefreshPending = false;
        _runtimeFault = default;
    }

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = nativeLifecycle;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _evaluationRefreshPending = false;
        _runtimeFault = default;
        _natives.InvalidateLifecycle();
        Publish();
    }

    public void DisposeDiagnostics()
    {
    }

    public void DisposeRegistration()
    {
        _registration.Dispose();
        _natives.Dispose();
    }

    private void Publish()
    {
        var health = ServiceCycleDiagnostics.ProjectHealth(
            _cycleObserved ? FeatureStatusState.Operational : FeatureStatusState.NotReady,
            _cycleObserved ? FeatureStatusReasonCode.None : FeatureStatusReasonCode.Initializing,
            _cycleObserved ? string.Empty : "Mentor is waiting for its first service cycle.",
            in _runtimeFault);
        _dependencies.FeatureStatus.ObserveRuntimeLifecycle(
            health.State,
            health.Reason,
            health.Summary,
            _lifecycle,
            _configurationGeneration);
    }
}
