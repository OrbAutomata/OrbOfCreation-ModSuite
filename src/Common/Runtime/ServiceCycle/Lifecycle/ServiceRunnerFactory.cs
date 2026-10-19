using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

/// <summary>
/// Builds a service's runner, and rebuilds it whenever its lifecycle generation changes.
/// </summary>
/// <remarks>
/// Abstract because the two service shapes differ in what they build, not in how they build it: each
/// mints its own worker definition, its own start coordinator, and its own worker, and a source pair
/// additionally shares a capture buffer that an ordinary pair has no use for. Everything around that
/// — the claim admission, the handoff, the action store, the batch machinery, the failure unwind — is
/// one implementation here, so neither shape can drift from the other by accident.
/// </remarks>
internal abstract partial class ServiceRunnerFactory<TState, TAction>
{
    private readonly IServiceCycleMainThreadDefinition<TAction> _definition;
    private readonly ServiceConfigurationPublisher _configuration;
    private readonly ServiceId _serviceId;
    private readonly WakePolicy _defaultWakePolicy;
    private readonly ServiceFaultRecoveryPolicy _faultRecoveryPolicy;
    private readonly IMonotonicClock _clock;
    private readonly bool _measureWorkerAllocations;
    private readonly ServiceResourceClaimLedger _resourceClaims;
    private readonly IServiceCycleWorkerStarter? _workerStarter;
    private readonly IServiceCycleWorkerExitObserver? _workerExitObserver;
    private readonly ServiceStrategyPublisher _strategy;
    private readonly ServiceWorldPublisher<GameWorldState> _world;

    private protected ServiceRunnerFactory(
        IServiceCycleMainThreadDefinition<TAction> definition,
        in ServiceRunnerFactoryParts parts)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _configuration = parts.Configuration ?? throw new ArgumentNullException(nameof(parts.Configuration));
        _serviceId = parts.ServiceId;
        _defaultWakePolicy = parts.DefaultWakePolicy;
        _faultRecoveryPolicy = parts.FaultRecoveryPolicy;
        _clock = parts.Clock ?? throw new ArgumentNullException(nameof(parts.Clock));
        _measureWorkerAllocations = parts.MeasureWorkerAllocations;
        _resourceClaims = parts.ResourceClaims ?? throw new ArgumentNullException(nameof(parts.ResourceClaims));
        _workerStarter = parts.WorkerStarter;
        _workerExitObserver = parts.WorkerExitObserver;
        _strategy = parts.Strategy ?? throw new ArgumentNullException(nameof(parts.Strategy));
        _world = parts.World ?? throw new ArgumentNullException(nameof(parts.World));
    }

    private protected IServiceCycleMainThreadDefinition<TAction> Definition => _definition;
    private protected ServiceConfigurationPublisher Configuration => _configuration;
    private protected ServiceId ServiceIdentity => _serviceId;
    private protected WakePolicy DefaultWake => _defaultWakePolicy;
    private protected ServiceFaultRecoveryPolicy FaultRecoveryPolicy => _faultRecoveryPolicy;
    private protected IMonotonicClock Clock => _clock;
    private protected bool MeasureWorkerAllocations => _measureWorkerAllocations;
    private protected IServiceCycleWorkerExitObserver? WorkerExitObserver => _workerExitObserver;
    private protected ServiceStrategyPublisher Strategy => _strategy;
    private protected ServiceWorldPublisher<GameWorldState> World => _world;

    internal ServiceRunner<TState, TAction> CreateRequired(
        LifecycleGeneration lifecycle) =>
        TryCreate(lifecycle).Runner ?? throw new ServiceRunnerResourceContentionException("resource");

    internal ServiceRunnerConstructionResult<TState, TAction> TryCreate(
        LifecycleGeneration lifecycle)
    {
        ValidateRegistration(
            _definition,
            _configuration,
            lifecycle,
            _serviceId,
            _defaultWakePolicy,
            _faultRecoveryPolicy,
            _clock);
        var preparation = TryPrepare(_resourceClaims, out var prepared);
        if (preparation == ServiceResourceClaimResult.Contended)
            return new ServiceRunnerConstructionResult<TState, TAction>(null, true);
        if (preparation != ServiceResourceClaimResult.Claimed)
            throw new InvalidOperationException("The live service resource claim ledger is at capacity.");

        var parts = Build(lifecycle, prepared!);
        var runner = new ServiceRunner<TState, TAction>(
            _configuration,
            lifecycle,
            _serviceId,
            _defaultWakePolicy,
            _faultRecoveryPolicy,
            in parts);
        return new ServiceRunnerConstructionResult<TState, TAction>(runner, false);
    }
}
