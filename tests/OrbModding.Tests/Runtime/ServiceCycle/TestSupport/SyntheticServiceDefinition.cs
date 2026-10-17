using System;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;
using RuntimeStrategyGeneration = OrbModding.Common.Runtime.StrategyGeneration;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class SyntheticState
{
    public int Evaluations { get; internal set; }
}

internal readonly struct SyntheticAction
{
    public SyntheticAction(int value) => Value = value;
    public int Value { get; }
}

internal sealed class SyntheticServiceDefinition :
    IServiceCycleDefinition<SyntheticState, SyntheticAction>
{
    private readonly SyntheticWorkerControl _worker = new();
    private int _workerDefinitionCreateCount;

    internal SyntheticServiceDefinition(string serviceId) => ServiceId = new ServiceId(serviceId);

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; set; } = WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; set; } = new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
    public bool ThrowFromStateFactory { get => _worker.ThrowFromStateFactory; set => _worker.ThrowFromStateFactory = value; }
    public bool ReturnNullState { get => _worker.ReturnNullState; set => _worker.ReturnNullState = value; }
    public bool ThrowFromWorkerFactory { get; set; }
    public bool ReturnNullWorkerDefinition { get; set; }
    public bool ThrowFromStateRelease { get => _worker.ThrowFromStateRelease; set => _worker.ThrowFromStateRelease = value; }
    public int WorkerDefinitionCreateCount => Volatile.Read(ref _workerDefinitionCreateCount);
    public int StateCreateCount => _worker.StateCreateCount;
    public int StateReleaseCount => _worker.StateReleaseCount;

    public IServiceCycleWorkerDefinition<SyntheticState, SyntheticAction>
        CreateWorkerDefinition()
    {
        Interlocked.Increment(ref _workerDefinitionCreateCount);
        if (ThrowFromWorkerFactory) throw new InvalidOperationException("synthetic worker construction failure");
        return ReturnNullWorkerDefinition ? null! : new WorkerDefinition(_worker);
    }

    public ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);

    public ServiceActionJournalAttribution DescribeAction(in SyntheticAction action) =>
        ServiceActionJournalAttribution.Publication;


    public ServiceActionResult TryExecute(
        in SyntheticAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));

    private sealed class WorkerDefinition :
        IServiceCycleWorkerDefinition<SyntheticState, SyntheticAction>
    {
        private readonly SyntheticWorkerControl _control;
        internal WorkerDefinition(SyntheticWorkerControl control) => _control = control;
        public SyntheticState CreateState(RuntimeLifecycleGeneration lifecycle) => _control.CreateState();
        public void ReleaseState(ref SyntheticState state) => _control.ReleaseState(ref state);

        public WakePolicy Evaluate(
            in SuiteRuntimeConfiguration config,
            GameWorldState world,
            SuiteStrategy strategy,
            in ServiceCycleContext context,
            ref SyntheticState state,
            ServiceActionWriter<SyntheticAction> actions) =>
            _control.Evaluate(ref state);
        public void ProjectState(
            in SyntheticState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) { }
    }
}

internal sealed class SyntheticWorkerControl
{
    internal bool ThrowFromStateFactory;
    internal bool ReturnNullState;
    internal bool ThrowFromStateRelease;
    internal int StateCreateCount;
    internal int StateReleaseCount;

    internal SyntheticState CreateState()
    {
        StateCreateCount++;
        if (ThrowFromStateFactory) throw new InvalidOperationException("synthetic state construction failure");
        return ReturnNullState ? null! : new SyntheticState();
    }
    internal void ReleaseState(ref SyntheticState state)
    {
        StateReleaseCount++;
        if (ThrowFromStateRelease) throw new InvalidOperationException("synthetic state release failure");
        state = null!;
    }
    internal WakePolicy Evaluate(ref SyntheticState state)
    {
        state.Evaluations++;
        return WakePolicy.Immediate;
    }
}
