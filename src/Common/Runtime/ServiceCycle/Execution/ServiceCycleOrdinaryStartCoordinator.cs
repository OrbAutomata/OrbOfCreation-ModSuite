using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Opens a cycle for a service that reads nothing on the main thread.
/// </summary>
/// <remarks>
/// The whole of the ordinary shape's difference from the source shape: a ready start decision becomes
/// a queued cycle immediately. There is no stage between them to fail, so an ordinary service's start
/// path cannot report a capture at all — which is why its cycles carry no capture fact rather than an
/// empty one.
/// </remarks>
internal sealed class ServiceCycleOrdinaryStartCoordinator<TState, TAction> :
    ServiceCycleStartCoordinator<TState, TAction>
{
    internal ServiceCycleOrdinaryStartCoordinator(
        IServiceCycleMainThreadDefinition<TAction> definition,
        ServiceConfigurationPublisher configuration,
        ServiceCycleHandoff handoff,
        ServiceCycleMainState state,
        ServiceId serviceId,
        LifecycleGeneration lifecycle,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        ServiceRunnerLifetime lifetime,
        ServiceStrategyPublisher strategy,
        ServiceWorldPublisher<GameWorldState> world)
        : base(
            definition,
            configuration,
            handoff,
            state,
            serviceId,
            lifecycle,
            faultRecoveryPolicy,
            clock,
            lifetime,
            strategy,
            world,
            wakeOnWorldPublication: true)
    {
    }

    private protected override ServiceCycleStartAttempt Open(
        ConfigurationPublication configuration,
        in ServiceStartDecisionFact startFact,
        in ServiceStartInvocationFact startInvocation,
        bool nonBlockingProbe,
        int ordinal,
        IServiceCycleAttemptObserver? observer)
    {
        var opening = OpenSequences();
        var decidedAt = Clock.Now;
        var recoveredFault = RecoverStartFault(decidedAt);
        return Queue(
            configuration,
            in opening,
            decidedAt,
            in startFact,
            in startInvocation,
            default,
            in recoveredFault,
            nonBlockingProbe);
    }
}
