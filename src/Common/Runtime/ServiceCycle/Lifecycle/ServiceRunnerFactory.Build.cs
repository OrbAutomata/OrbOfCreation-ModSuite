using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

/// <summary>
/// The start coordinator and worker of one lifecycle.
/// </summary>
/// <remarks>
/// Built and returned together because a shape may have to give them something only it knows about:
/// the source pair shares the capture buffer the main thread fills and the worker reads, and building
/// them in one step is what lets that buffer be a local rather than a field whose lifetime nothing
/// states.
/// </remarks>
internal readonly struct ServiceCycleShapeParts<TState, TAction>
{
    internal ServiceCycleShapeParts(
        ServiceCycleStartCoordinator<TState, TAction> starts,
        ServiceCycleWorker<TState, TAction> worker)
    {
        Starts = starts;
        Worker = worker;
    }

    internal ServiceCycleStartCoordinator<TState, TAction> Starts { get; }
    internal ServiceCycleWorker<TState, TAction> Worker { get; }
}

internal abstract partial class ServiceRunnerFactory<TState, TAction>
{
    /// <summary>
    /// Asks the service for its worker half, inside the runtime's serialized factory admission.
    /// </summary>
    private protected abstract IServiceCycleWorkerStateDefinition<TState> CreateWorkerDefinition();

    /// <summary>
    /// Builds this shape's start coordinator and worker for one lifecycle.
    /// </summary>
    private protected abstract ServiceCycleShapeParts<TState, TAction> CreateCycleParts(
        IServiceCycleWorkerStateDefinition<TState> workerDefinition,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        ServiceCycleMainState main,
        LifecycleGeneration lifecycle,
        ServiceRunnerLifetime lifetime,
        ServiceResourceClaimLedger claims,
        ServiceResourceClaim workerDefinitionClaim);

    /// <summary>
    /// Mints the worker definition inside the runtime's serialized factory admission and proves no
    /// live runner already holds it.
    /// </summary>
    private ServiceResourceClaimResult TryPrepare(
        ServiceResourceClaimLedger claims,
        out ServiceRunnerPreparedResources<TState>? prepared)
    {
        prepared = null;
        ServiceResourceClaim? workerClaim = null;
        try
        {
            var admission = claims.TryBeginFactory(
                ServiceResourceRole.WorkerDefinition,
                out workerClaim);
            if (admission != ServiceResourceClaimResult.Claimed) return admission;
            IServiceCycleWorkerStateDefinition<TState> workerDefinition;
            ServiceResourceClaimResult workerResult;
            try
            {
                workerDefinition = CreateWorkerDefinition() ??
                    throw new InvalidOperationException("The service did not create a worker definition.");
                workerResult = claims.FinalizeFactory(workerClaim, workerDefinition);
            }
            finally { claims.EndFactory(workerClaim); }
            if (workerResult == ServiceResourceClaimResult.Aliased)
                throw new ServiceRunnerResourceAliasingException("worker definition");
            ServiceCycleWorkerDefinitionValidator.EnsureSeparated(Definition, workerDefinition);

            prepared = new ServiceRunnerPreparedResources<TState>(
                workerDefinition,
                claims,
                workerClaim);
            return ServiceResourceClaimResult.Claimed;
        }
        catch
        {
            claims.Release(workerClaim);
            throw;
        }
    }

    private ServiceRunnerParts<TState, TAction> Build(
        LifecycleGeneration lifecycle,
        ServiceRunnerPreparedResources<TState> prepared)
    {
        var workerDefinition = prepared.WorkerDefinition;
        var claims = prepared.Claims;
        var workerDefinitionClaim = prepared.WorkerDefinitionClaim;
        ServiceCycleHandoff? actualHandoff = null;
        try
        {
            var actualLifetime = new ServiceRunnerLifetime();
            var main = new ServiceCycleMainState(_faultRecoveryPolicy)
            {
                LatestConfigGeneration = _configuration.ReadLatest().Generation,
            };
            var actions = new ReusableActionStore<TAction>(lifecycle: lifecycle);
            actualHandoff = new ServiceCycleHandoff(lifecycle);
            var shape = CreateCycleParts(
                workerDefinition,
                actions,
                actualHandoff,
                main,
                lifecycle,
                actualLifetime,
                claims,
                workerDefinitionClaim);
            var starts = shape.Starts;
            var worker = shape.Worker;
            var batchRuntime =
                new ServiceBatchRuntime<TState, TAction>(
                    Definition,
                    _configuration,
                    actions,
                    actualHandoff,
                    main,
                    starts,
                    _faultRecoveryPolicy,
                    _clock,
                    lifecycle,
                    actualLifetime);
            var batchCompletion =
                new ServiceBatchCompletion<TState, TAction>(
                    batchRuntime);
            var responses =
                new ServiceBatchResponseHandler<TState, TAction>(
                    batchRuntime,
                    batchCompletion);
            var actionExecutor =
                new ServiceBatchActionExecutor<TState, TAction>(
                    batchRuntime,
                    batchCompletion);
            var diagnostics =
                new ServiceRunnerDiagnosticsAssembler<TState, TAction>(
                    _configuration,
                    actions,
                    actualHandoff,
                    worker,
                    main,
                    starts);
            var resourceIdentity = new ServiceRunnerResourceIdentity(
                workerDefinition,
                actions,
                actualHandoff,
                worker,
                main,
                starts,
                batchCompletion);
            worker.Start(_workerStarter);
            return new ServiceRunnerParts<TState, TAction>(
                actions,
                actualHandoff,
                worker,
                main,
                starts,
                responses,
                actionExecutor,
                batchCompletion,
                diagnostics,
                actualLifetime,
                resourceIdentity);
        }
        catch
        {
            try
            {
                actualHandoff?.DisposeNeverStarted();
            }
            catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
            {
                GameActionFaultLog.Record(exception, _serviceId.Value + " runner construction cleanup");
            }
            claims.Release(workerDefinitionClaim);
            throw;
        }
    }

    private static void ValidateRegistration(
        IServiceCycleMainThreadDefinition<TAction> definition,
        ServiceConfigurationPublisher configuration,
        LifecycleGeneration lifecycle,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock)
    {
        if (definition is null)
            throw new ArgumentNullException(nameof(definition));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));
        if (!serviceId.IsValid)
            throw new ArgumentException(
                "A valid service identity is required.",
                nameof(serviceId));
        if (lifecycle.Value == 0)
            throw new ArgumentException(
                "A valid lifecycle generation is required.",
                nameof(lifecycle));
        if (!defaultWakePolicy.IsValid ||
            defaultWakePolicy.Kind == WakePolicyKind.Default)
        {
            throw new ArgumentException(
                "Registration requires a concrete default wake policy.",
                nameof(defaultWakePolicy));
        }
        if (!faultRecoveryPolicy.IsValid)
            throw new ArgumentException(
                "Registration requires a valid fault-recovery policy.",
                nameof(faultRecoveryPolicy));
        if (clock is null) throw new ArgumentNullException(nameof(clock));
    }
}
