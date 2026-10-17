using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

/// <summary>
/// Builds the runner for a service that reads the published world.
/// </summary>
internal sealed class ServiceOrdinaryRunnerFactory<TState, TAction> :
    ServiceRunnerFactory<TState, TAction>
{
    private readonly IServiceCycleDefinition<TState, TAction> _definition;

    internal ServiceOrdinaryRunnerFactory(
        IServiceCycleDefinition<TState, TAction> definition,
        in ServiceRunnerFactoryParts parts)
        : base(definition, in parts) =>
        _definition = definition;

    internal ServiceOrdinaryRunnerFactory(
        IServiceCycleDefinition<TState, TAction> definition,
        ServiceConfigurationPublisher configuration,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceResourceClaimLedger resourceClaims,
        IServiceCycleWorkerStarter? workerStarter,
        IServiceCycleWorkerExitObserver? workerExitObserver,
        ServiceStrategyPublisher? strategy = null,
        ServiceWorldPublisher<GameWorldState>? world = null)
        : base(
            definition,
            configuration,
            serviceId,
            defaultWakePolicy,
            faultRecoveryPolicy,
            clock,
            measureWorkerAllocations,
            resourceClaims,
            workerStarter,
            workerExitObserver,
            strategy,
            world) =>
        _definition = definition;

    private protected override IServiceCycleWorkerStateDefinition<TState> CreateWorkerDefinition() =>
        _definition.CreateWorkerDefinition();

    private protected override ServiceCycleShapeParts<TState, TAction> CreateCycleParts(
        IServiceCycleWorkerStateDefinition<TState> workerDefinition,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        ServiceCycleMainState main,
        LifecycleGeneration lifecycle,
        ServiceRunnerLifetime lifetime,
        ServiceResourceClaimLedger claims,
        ServiceResourceClaim workerDefinitionClaim) =>
        new(
            new ServiceCycleOrdinaryStartCoordinator<TState, TAction>(
                _definition,
                Configuration,
                handoff,
                main,
                ServiceIdentity,
                lifecycle,
                FaultRecoveryPolicy,
                Clock,
                lifetime,
                Strategy,
                World),
            new ServiceCycleOrdinaryWorker<TState, TAction>(
                // The narrowing is of this factory's own product: CreateWorkerDefinition above
                // returned an ordinary worker contract and the runtime only widened it to hand it
                // through the claim ledger, which admits both shapes.
                (IServiceCycleWorkerDefinition<TState, TAction>)workerDefinition,
                ServiceIdentity,
                actions,
                handoff,
                Clock,
                DefaultWake,
                FaultRecoveryPolicy,
                MeasureWorkerAllocations,
                lifecycle,
                claims,
                workerDefinitionClaim,
                WorkerExitObserver));
}
