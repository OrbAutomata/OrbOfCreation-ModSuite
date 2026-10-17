using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

/// <summary>
/// The strategist's contribution to the Automata ServiceCycle host: a registration and nothing else.
/// It touches no native state, so it has no native gates, no action family, and no diagnostics bridge.
/// </summary>
internal sealed class StrategistServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context) =>
        new StrategistFeatureRuntime(context.Registry.Register(
            StrategistService.Define(context.Registry.StrategyPublication),
            ActionDispatchPolicy));
}

internal sealed class StrategistFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly ServiceRegistration<StrategistCycleState, StrategistCycleAction> _registration;

    internal StrategistFeatureRuntime(
        ServiceRegistration<StrategistCycleState, StrategistCycleAction> registration) =>
        _registration = registration;

    public void ActivateDiagnostics()
    {
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
    }

    public void ObserveLifecycle(long nativeLifecycle, ConfigGeneration configurationGeneration)
    {
    }

    public void DisposeDiagnostics()
    {
    }

    public void DisposeRegistration() => _registration.Dispose();
}
