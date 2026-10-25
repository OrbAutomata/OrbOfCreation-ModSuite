using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// Auto Cast's contribution to the one Automata-owned ServiceCycle host. It supplies its typed
/// definition and native adapter and registers them against the shared registry, but never owns the
/// registry, pump, host, or observability.
/// </summary>
internal sealed class AutoCastServiceCycleFeature : IAutomataServiceCycleFeature
{
    // One action per cycle is all the worker ever plans, so the batch bound is one. It is stated
    // rather than left to a default: casting is the game's most conspicuous mutation, and a bound of
    // one is what guarantees a single generation can never fire twice.
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);

    private readonly AutoCastFeatureDependencies _dependencies;

    public AutoCastServiceCycleFeature(AutoCastFeatureDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoCastServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(adapters.Definition, ActionDispatchPolicy);
        return new AutoCastFeatureRuntime(
            _dependencies,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration,
            adapters.Actions);
    }

}

/// <summary>
/// The non-generic per-frame runtime for Auto Cast inside the shared host.
/// </summary>
/// <remarks>
/// The lifecycle boundary drops two things, and both have to go together. The service's charge hold
/// named a spell the game just replaced, and the manual pause is a fact about a run of the game that
/// just ended. Keeping either would let the new run inherit the old one's answers.
/// </remarks>
internal sealed class AutoCastFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoCastFeatureDependencies _dependencies;
    private readonly ServiceRegistration<
        AutoCastCycleState,
        AutoCastCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly ConfigGeneration _initialConfigurationGeneration;
    private readonly AutoCastCycleActionAdapter _actions;
    private AutoCastServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoCastFeatureRuntime(
        AutoCastFeatureDependencies dependencies,
        ServiceRegistration<
            AutoCastCycleState,
            AutoCastCycleAction> registration,
        long lifecycleValue,
        ConfigGeneration initialConfigurationGeneration,
        AutoCastCycleActionAdapter actions)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _lifecycleValue = lifecycleValue;
        _initialConfigurationGeneration = initialConfigurationGeneration;
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoCastServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfigurationGeneration,
            _dependencies.OwnsActionFamily(),
            _dependencies.ManualPause,
            _dependencies.FeatureStatus,
            _dependencies.Warn);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        var owned = _dependencies.OwnsActionFamily();
        if (_actions.HoldsServiceCharge &&
            (!owned || !_actions.Operational || pump.IsEmergencyStopEngaged ||
                _dependencies.ManualPause.IsPaused(pump.DiagnosticsNow)))
            _actions.ReleaseServiceHold();
        _diagnostics?.Observe(pump, in report, owned);
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
        _diagnostics?.ObserveConfiguration(configurationGeneration);

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        _actions.ForgetServiceHold();
        _dependencies.ManualPause.Reset();
        _diagnostics?.ObserveLifecycle(
            nativeLifecycle,
            configurationGeneration,
            _dependencies.OwnsActionFamily());
    }

    public void DisposeDiagnostics() => _diagnostics = null;

    public void DisposeRegistration()
    {
        _registration.Dispose();
        _actions.ReleaseServiceHold();
    }

#if SERVICE_CYCLE_PROFILE
    public OrbAutomata.GameMcp.IGameMcpServiceCommand GameMcpCommand =>
        new OrbAutomata.GameMcp.GameMcpCastCommand(this);

    internal ServiceActionResult TryExecuteGameMcp(
        in AutoCastCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        _actions.TryExecuteGameMcp(in action, in config, in context);

    internal AutoCastSubmission LastGameMcpSubmission => _actions.LastSubmission;
#endif
}
