using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoConceptServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);

    private readonly AutoConceptFeatureDependencies _dependencies;

    public AutoConceptServiceCycleFeature(AutoConceptFeatureDependencies dependencies) =>
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoConceptServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(adapters.Definition, ActionDispatchPolicy);
        return new AutoConceptFeatureRuntime(
            _dependencies,
            adapters.Natives,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration
#if SERVICE_CYCLE_PROFILE
            , adapters.Actions
#endif
            );
    }

}

internal sealed class AutoConceptFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoConceptFeatureDependencies _dependencies;
    private readonly AutoConceptNativeAdapter _natives;
    private readonly ServiceRegistration<AutoConceptCycleState, AutoConceptCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly ConfigGeneration _initialConfigurationGeneration;
#if SERVICE_CYCLE_PROFILE
    private readonly AutoConceptCycleActionAdapter _actions;
#endif
    private AutoConceptServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoConceptFeatureRuntime(
        AutoConceptFeatureDependencies dependencies,
        AutoConceptNativeAdapter natives,
        ServiceRegistration<AutoConceptCycleState, AutoConceptCycleAction> registration,
        long lifecycleValue,
        ConfigGeneration initialConfigurationGeneration
#if SERVICE_CYCLE_PROFILE
        , AutoConceptCycleActionAdapter actions
#endif
        )
    {
        _dependencies = dependencies;
        _natives = natives;
        _registration = registration;
        _lifecycleValue = lifecycleValue;
        _initialConfigurationGeneration = initialConfigurationGeneration;
#if SERVICE_CYCLE_PROFILE
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
#endif
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoConceptServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfigurationGeneration,
            _dependencies.OwnsActionFamily(),
            _dependencies.FeatureStatus);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) =>
        _diagnostics?.Observe(pump, in report, _dependencies.OwnsActionFamily());

    public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
        _diagnostics?.ObserveConfiguration(configurationGeneration);

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        _natives.InvalidateLifecycle();
        _diagnostics?.ObserveLifecycle(
            nativeLifecycle,
            configurationGeneration,
            _dependencies.OwnsActionFamily());
    }

    public void DisposeDiagnostics() => _diagnostics = null;

    public void DisposeRegistration()
    {
        _registration.Dispose();
        _natives.Dispose();
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionResult TryExecuteGameMcp(
        in AutoConceptCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        _actions.TryExecuteGameMcp(in action, in config, in context);

    internal AutoConceptSubmission LastGameMcpSubmission => _actions.LastSubmission;
#endif
}
