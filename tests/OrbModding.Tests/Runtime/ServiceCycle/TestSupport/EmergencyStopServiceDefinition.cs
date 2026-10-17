using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class EmergencyStopState
{
    internal int Evaluations { get; set; }
}

internal readonly struct EmergencyStopAction
{
    internal EmergencyStopAction(int index) => Index = index;
    internal int Index { get; }
}

/// <summary>
/// The smallest service that produces a batch of native mutations, over a configuration the pump can
/// read an emergency stop from.
/// </summary>
internal sealed class EmergencyStopServiceDefinition :
    IServiceCycleDefinition<EmergencyStopState, EmergencyStopAction>
{
    private readonly Control _control = new();

    internal EmergencyStopServiceDefinition(string serviceId) => ServiceId = new ServiceId(serviceId);

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));

    internal int ActionCount { get => _control.ActionCount; set => _control.ActionCount = value; }
    internal int ActionExecutionCount { get; private set; }

    public IServiceCycleWorkerDefinition<EmergencyStopState, EmergencyStopAction>
        CreateWorkerDefinition() => new WorkerDefinition(_control);

    public ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);

    public ServiceActionJournalAttribution DescribeAction(in EmergencyStopAction action) =>
        ServiceActionJournalAttribution.Publication;

    public ServiceActionResult TryExecute(
        in EmergencyStopAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        ActionExecutionCount++;
        return ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));
    }

    private sealed class Control
    {
        internal int ActionCount;
    }

    private sealed class WorkerDefinition :
        IServiceCycleWorkerDefinition<EmergencyStopState, EmergencyStopAction>
    {
        private readonly Control _control;

        internal WorkerDefinition(Control control) => _control = control;

        public EmergencyStopState CreateState(LifecycleGeneration lifecycle) => new();
        public void ReleaseState(ref EmergencyStopState state) => state = null!;
        public WakePolicy Evaluate(
            in SuiteRuntimeConfiguration config,
            GameWorldState world,
            SuiteStrategy strategy,
            in ServiceCycleContext context,
            ref EmergencyStopState state,
            ServiceActionWriter<EmergencyStopAction> actions)
        {
            state.Evaluations++;
            for (var index = 0; index < _control.ActionCount; index++)
            {
                var action = new EmergencyStopAction(index);
                actions.Add(in action);
            }

            return WakePolicy.Immediate;
        }

        public void ProjectState(
            in EmergencyStopState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) =>
            output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Evaluations));
    }
}
