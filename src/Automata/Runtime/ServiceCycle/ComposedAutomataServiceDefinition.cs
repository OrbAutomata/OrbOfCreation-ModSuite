using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

internal sealed class ComposedAutomataServiceDefinition<TState, TAction> :
    IAutomataServiceDefinition<TState, TAction>
{
    private readonly AutomataWorkerFactory<TState, TAction> _createWorker;
    private readonly AutomataStartPolicy _shouldStart;
    private readonly AutomataDescribeAction<TAction> _describeAction;
    private readonly AutomataExecute<TAction> _execute;

    internal ComposedAutomataServiceDefinition(
        in AutomataServiceMetadata metadata,
        AutomataWorkerFactory<TState, TAction> createWorker,
        AutomataStartPolicy shouldStart,
        AutomataDescribeAction<TAction> describeAction,
        AutomataExecute<TAction> execute)
    {
        ServiceId = metadata.ServiceId;
        DefaultWakePolicy = metadata.DefaultWakePolicy;
        FaultRecoveryPolicy = metadata.FaultRecoveryPolicy;
        _createWorker = createWorker ?? throw new ArgumentNullException(nameof(createWorker));
        _shouldStart = shouldStart ?? throw new ArgumentNullException(nameof(shouldStart));
        _describeAction = describeAction ?? throw new ArgumentNullException(nameof(describeAction));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; }
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }

    public IServiceCycleWorkerDefinition<
        TState,
        TAction> CreateWorkerDefinition() =>
        _createWorker() ??
        throw new InvalidOperationException("The service did not create a worker definition.");

    public ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        _shouldStart(in config, in context);

    public ServiceActionResult TryExecute(
        in TAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        _execute(in action, in config, in context);

    public ServiceActionJournalAttribution DescribeAction(in TAction action) =>
        _describeAction(in action);
}
