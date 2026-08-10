using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// Auto Buy's contribution to the one Automata-owned ServiceCycle host. It supplies its typed
/// definition and native adapters and registers them against the shared registry, but never owns
/// the registry, pump, host, or observability.
/// </summary>
internal sealed class AutoBuyServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(16);

    private readonly AutoBuyFeatureDependencies _dependencies;

    public AutoBuyServiceCycleFeature(AutoBuyFeatureDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoBuyServiceAdapterComposition.Create(
            _dependencies,
            context.Registry.WorldGenerations
#if SERVICE_CYCLE_PROFILE
            , context.ProfileProbe
#endif
            );
        var registration = context.Registry.Register(
            adapters.Definition,
            ActionDispatchPolicy);
        return new AutoBuyFeatureRuntime(
            _dependencies,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration,
            adapters.Actions);
    }

}

/// <summary>
/// The non-generic per-frame runtime for Auto Buy inside the shared host. It owns the action adapter's
/// lifecycle-bound purchase-route evidence as well as its feature-status bridge, and invalidates the
/// former whenever the native lifecycle changes or the registration is disposed. The decision
/// journal records every cycle, but it is not what the UI reads: the toggle button, its tooltip, and
/// the Mod Config health row all read the feature status registry, and without the bridge below
/// nothing refreshed that registry once gameplay was live.
/// </summary>
internal sealed class AutoBuyFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoBuyFeatureDependencies _dependencies;
    private readonly ServiceRegistration<
        AutoBuyCycleState,
        AutoBuyCycleAction> _registration;
    private long _lifecycleValue;
    private readonly ConfigGeneration _initialConfigurationGeneration;
    private readonly AutoBuyCycleActionAdapter _actions;
    private AutoBuyServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoBuyFeatureRuntime(
        AutoBuyFeatureDependencies dependencies,
        ServiceRegistration<
            AutoBuyCycleState,
            AutoBuyCycleAction> registration,
        long lifecycleValue,
        ConfigGeneration initialConfigurationGeneration,
        AutoBuyCycleActionAdapter actions)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _lifecycleValue = lifecycleValue;
        _initialConfigurationGeneration = initialConfigurationGeneration;
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoBuyServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfigurationGeneration,
            _dependencies.OwnershipMask(),
            _dependencies.FeatureStatus);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
#if SERVICE_CYCLE_PROFILE
        _actions.EmitRouteDiagnostic(_dependencies.ReadLifecycleEpoch());
#endif
        _diagnostics?.Observe(pump, in report, _dependencies.OwnershipMask());
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
        _diagnostics?.ObserveConfiguration(configurationGeneration);

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        if (_lifecycleValue != nativeLifecycle)
        {
            _actions.InvalidateTopology();
            _lifecycleValue = nativeLifecycle;
        }
        _diagnostics?.ObserveLifecycle(
            nativeLifecycle,
            configurationGeneration,
            _dependencies.OwnershipMask());
    }

    public void DisposeDiagnostics() => _diagnostics = null;

    public void DisposeRegistration()
    {
        _actions.InvalidateTopology();
        _registration.Dispose();
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionResult TryExecuteGameMcp(
        in AutoBuyCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        _actions.TryExecuteGameMcp(in action, in config, in context);

    internal AutoBuyPurchaseSubmission LastGameMcpSubmission => _actions.LastSubmission;
#endif
}
