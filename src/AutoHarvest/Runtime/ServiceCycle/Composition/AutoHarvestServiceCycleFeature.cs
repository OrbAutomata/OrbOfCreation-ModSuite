using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// Auto Harvest's contribution to the one Automata-owned ServiceCycle host. It supplies
/// its typed definition and native adapters and registers them against the shared
/// registry, but never owns the registry, pump, host, or observability.
/// </summary>
internal sealed class AutoHarvestServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly AutoHarvestFeatureDependencies _dependencies;

    public AutoHarvestServiceCycleFeature(AutoHarvestFeatureDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoHarvestServiceAdapterComposition.Create(
            _dependencies
#if SERVICE_CYCLE_PROFILE
            , context.ProfileProbe
#endif
            );
        var registration = context.Registry.Register(adapters.Definition);
        return new AutoHarvestFeatureRuntime(
            _dependencies,
            adapters.Bindings,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration
#if SERVICE_CYCLE_PROFILE
            , adapters.Actions
#endif
            );
    }

}

/// <summary>
/// The non-generic per-frame runtime for Auto Harvest inside the shared host. It owns
/// the feature's typed registration handle, native binding cache, and diagnostics bridge.
/// Disposal is split so the application can tear diagnostics down before host shutdown and
/// the registration/bindings down after it.
/// </summary>
internal sealed class AutoHarvestFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoHarvestFeatureDependencies _dependencies;
    private readonly AutoHarvestBindingResolver _bindings;
    private readonly ServiceRegistration<
        AutoHarvestCycleState,
        AutoHarvestCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly ConfigGeneration _initialConfigurationGeneration;
#if SERVICE_CYCLE_PROFILE
    private readonly AutoHarvestCycleActionAdapter _actions;
#endif
    private AutoHarvestServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoHarvestFeatureRuntime(
        AutoHarvestFeatureDependencies dependencies,
        AutoHarvestBindingResolver bindings,
        ServiceRegistration<
            AutoHarvestCycleState,
            AutoHarvestCycleAction> registration,
        long lifecycleValue,
        ConfigGeneration initialConfigurationGeneration
#if SERVICE_CYCLE_PROFILE
        , AutoHarvestCycleActionAdapter actions
#endif
        )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _lifecycleValue = lifecycleValue;
        _initialConfigurationGeneration = initialConfigurationGeneration;
#if SERVICE_CYCLE_PROFILE
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
#endif
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoHarvestServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfigurationGeneration,
            _dependencies.OwnsActionFamily(),
            _dependencies.RuntimeDiagnostics,
            _dependencies.FeatureStatus,
            _dependencies.Warn);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        _diagnostics?.Observe(pump, in report, _dependencies.OwnsActionFamily());
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
        _diagnostics?.ObserveConfiguration(configurationGeneration);

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        _bindings.InvalidateLifecycle();
        _diagnostics?.ObserveLifecycle(
            nativeLifecycle,
            configurationGeneration,
            _dependencies.OwnsActionFamily());
    }

    public void DisposeDiagnostics() => _diagnostics?.Dispose();

    public void DisposeRegistration()
    {
        try { _registration.Dispose(); }
        finally { _bindings.InvalidateLifecycle(); }
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionResult TryExecuteGameMcp(
        in AutoHarvestCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        _actions.TryExecuteGameMcp(in action, in config, in context);
#endif
}
