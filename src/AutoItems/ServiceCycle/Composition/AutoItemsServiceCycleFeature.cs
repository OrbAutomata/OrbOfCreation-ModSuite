using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

internal sealed class AutoItemsServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);
    private readonly AutoItemsFeatureDependencies _dependencies;

    internal AutoItemsServiceCycleFeature(AutoItemsFeatureDependencies dependencies) =>
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public IAutomataServiceCycleFeatureRuntime Register(
        in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoItemsServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(
            adapters.Definition,
            ActionDispatchPolicy);
        return new AutoItemsFeatureRuntime(
            _dependencies,
            adapters.GameAction,
            adapters.Health,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration);
    }
}

internal sealed class AutoItemsFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoItemsFeatureDependencies _dependencies;
    private readonly AutoItemsConsumableUseGameAction _gameAction;
    private readonly AutoItemsActionHealth _health;
    private readonly ServiceRegistration<AutoItemsCycleState, AutoItemsCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly ConfigGeneration _initialConfigurationGeneration;
    private AutoItemsServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoItemsFeatureRuntime(
        AutoItemsFeatureDependencies dependencies,
        AutoItemsConsumableUseGameAction gameAction,
        AutoItemsActionHealth health,
        ServiceRegistration<AutoItemsCycleState, AutoItemsCycleAction> registration,
        long lifecycleValue,
        ConfigGeneration initialConfigurationGeneration)
    {
        _dependencies = dependencies;
        _gameAction = gameAction;
        _health = health;
        _registration = registration;
        _lifecycleValue = lifecycleValue;
        _initialConfigurationGeneration = initialConfigurationGeneration;
    }

    public void ActivateDiagnostics() =>
        _diagnostics = new AutoItemsServiceCycleDiagnosticsBridge(
            _dependencies,
            _gameAction,
            _health,
            _lifecycleValue,
            _initialConfigurationGeneration);

    public void ObserveFrame(
        SuiteFramePump pump,
        in SuiteFramePumpReport report) =>
        _diagnostics?.Observe(pump, in report);

    public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
        _diagnostics?.ObserveConfiguration(configurationGeneration);

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        _gameAction.InvalidateLifecycle();
        _health.InvalidateLifecycle();
        _diagnostics?.ObserveLifecycle(nativeLifecycle, configurationGeneration);
    }

    internal ConsumablePlayerSubmission TryExecuteGameMcp(
        in ConsumablePlayerAction action) =>
        _gameAction.Submit(in action);

    public void DisposeDiagnostics() => _diagnostics = null;

    public void DisposeRegistration()
    {
        _registration.Dispose();
        _gameAction.Dispose();
    }
}
