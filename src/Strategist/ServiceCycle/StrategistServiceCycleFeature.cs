using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

/// <summary>
/// Registers the native-free strategist and relays its existing runner fault evidence to its
/// read-only publication source. It has no native gates or action family.
/// </summary>
internal sealed class StrategistServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context) =>
        new StrategistFeatureRuntime(context.Registry.Register(
            StrategistService.Define(context.Registry.StrategistBulletinPublication),
            ActionDispatchPolicy), context.Registry.StrategistBulletinPublication);
}

internal sealed class StrategistFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly ServiceRegistration<StrategistCycleState, StrategistCycleAction> _registration;
    private readonly IStrategistBulletinSink _publish;
    private ServiceFault _fault;

    internal StrategistFeatureRuntime(
        ServiceRegistration<StrategistCycleState, StrategistCycleAction> registration,
        IStrategistBulletinSink publish)
    {
        _registration = registration;
        _publish = publish;
    }

    public void ActivateDiagnostics()
    {
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        if (!report.Accepted) return;
        var service = ServiceCycleDiagnostics.ReadAvailableService(pump, StrategistServicePolicies.ServiceId);
        if (ServiceCycleDiagnostics.RefreshFault(service, ref _fault))
            _publish.ObserveStrategistFault(in _fault);
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
    }

    public void ObserveLifecycle(long nativeLifecycle, ConfigGeneration configurationGeneration)
    {
        _fault = default;
        _publish.ObserveStrategistFault(in _fault);
    }

    public void DisposeDiagnostics()
    {
    }

    public void DisposeRegistration() => _registration.Dispose();
}
