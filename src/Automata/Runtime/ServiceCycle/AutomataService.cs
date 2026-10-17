using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal delegate IServiceCycleWorkerDefinition<
    TState,
    TAction> AutomataWorkerFactory<TState, TAction>();

internal delegate ServiceStartDecision AutomataStartPolicy(
    in SuiteRuntimeConfiguration configuration,
    in ServiceCycleStartContext context);

internal delegate IServiceCycleSourceWorkerDefinition<TState, TAction>
    AutomataSourceWorkerFactory<TState, TAction>();

/// <summary>
/// Reads the game into the runtime's buffer. Only the source shape has one.
/// </summary>
internal delegate ServiceCaptureResult AutomataSourceCapture(
    GameWorldCycleFrame frame,
    in SuiteRuntimeConfiguration configuration,
    in ServiceCaptureContext context);

internal delegate ServiceActionResult AutomataExecute<TAction>(
    in TAction action,
    in SuiteRuntimeConfiguration configuration,
    in ServiceActionContext context);

internal delegate ServiceActionJournalAttribution AutomataDescribeAction<TAction>(
    in TAction action);

internal readonly struct AutomataServiceMetadata
{
    internal AutomataServiceMetadata(
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy)
    {
        if (!serviceId.IsValid)
            throw new ArgumentException("A valid service identity is required.", nameof(serviceId));
        if (!defaultWakePolicy.IsValid || defaultWakePolicy.Kind == WakePolicyKind.Default)
            throw new ArgumentException("A concrete default wake policy is required.", nameof(defaultWakePolicy));
        if (!faultRecoveryPolicy.IsValid)
            throw new ArgumentException("A valid fault recovery policy is required.", nameof(faultRecoveryPolicy));

        ServiceId = serviceId;
        DefaultWakePolicy = defaultWakePolicy;
        FaultRecoveryPolicy = faultRecoveryPolicy;
    }

    internal ServiceId ServiceId { get; }
    internal WakePolicy DefaultWakePolicy { get; }
    internal ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }
}

internal static class AutomataService
{
    internal static IAutomataServiceDefinition<TState, TAction> Define<TState, TAction>(
        in AutomataServiceMetadata metadata,
        AutomataWorkerFactory<TState, TAction> createWorker,
        AutomataStartPolicy shouldStart,
        AutomataDescribeAction<TAction> describeAction,
        AutomataExecute<TAction> execute) =>
        new ComposedAutomataServiceDefinition<TState, TAction>(
            in metadata,
            createWorker,
            shouldStart,
            describeAction,
            execute);

    /// <summary>
    /// Composes the service that reads the game and publishes what it read.
    /// </summary>
    internal static IServiceCycleSourceDefinition<TState, TAction> DefineSource<TState, TAction>(
        in AutomataServiceMetadata metadata,
        AutomataSourceWorkerFactory<TState, TAction> createWorker,
        AutomataStartPolicy shouldStart,
        AutomataSourceCapture capture,
        AutomataDescribeAction<TAction> describeAction,
        AutomataExecute<TAction> execute) =>
        new ComposedAutomataSourceDefinition<TState, TAction>(
            in metadata,
            createWorker,
            shouldStart,
            capture,
            describeAction,
            execute);
}
