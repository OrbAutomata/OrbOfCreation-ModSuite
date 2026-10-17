using System.Collections.Concurrent;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class CrossRoleResource :
    IServiceCycleWorkerDefinition<
        CrossRoleResource,
        CrossRoleAction>
{
    internal CrossRoleResource() => Id = CrossRoleResourceRuntime.NextId();

    internal int Id { get; }

    CrossRoleResource IServiceCycleWorkerStateDefinition<
        CrossRoleResource>.CreateState(LifecycleGeneration lifecycle) =>
        CrossRoleResourceRuntime.CreateState(Id);

    void IServiceCycleWorkerStateDefinition<
        CrossRoleResource>.ReleaseState(ref CrossRoleResource state)
    {
        CrossRoleResourceRuntime.RecordStateRelease(Id);
        state = null!;
    }

    WakePolicy IServiceCycleWorkerDefinition<
        CrossRoleResource,
        CrossRoleAction>.Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref CrossRoleResource state,
        ServiceActionWriter<CrossRoleAction> actions) => WakePolicy.AfterBatch(new MonotonicDuration(1_000));

    void IServiceCycleWorkerStateDefinition<
        CrossRoleResource>.ProjectState(
        in CrossRoleResource state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Id));
}

internal sealed class CrossRoleState
{
    internal CrossRoleState(int id) => Id = id;
    internal int Id { get; }
}

internal readonly struct CrossRoleAction
{
    internal CrossRoleAction(int value) => Value = value;
    internal int Value { get; }
}

internal sealed class CrossRoleServiceDefinition :
    IServiceCycleDefinition<CrossRoleResource, CrossRoleAction>
{
    internal CrossRoleServiceDefinition(string id)
    {
        ServiceId = new ServiceId(id);
        WorkerResource = new CrossRoleResource();
        StateResource = new CrossRoleResource();
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy => WakePolicy.AfterBatch(new MonotonicDuration(1_000));
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        new MonotonicDuration(10),
        new MonotonicDuration(80));
    internal CrossRoleResource WorkerResource { get; set; }
    internal CrossRoleResource StateResource { get; set; }
    internal bool Ready { get; set; } = true;
    internal int StateCreateCount => CrossRoleResourceRuntime.StateCreateCount(WorkerResource.Id);
    internal int StateReleaseCount => CrossRoleResourceRuntime.StateReleaseCount(WorkerResource.Id);

    public IServiceCycleWorkerDefinition<
        CrossRoleResource,
        CrossRoleAction> CreateWorkerDefinition()
    {
        CrossRoleResourceRuntime.SetState(WorkerResource.Id, StateResource);
        return WorkerResource;
    }

    public ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) => Ready
        ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
        : ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.NotReady,
            WakePolicy.AfterDecision(new MonotonicDuration(1_000)));

    public ServiceActionJournalAttribution DescribeAction(in CrossRoleAction action) =>
        ServiceActionJournalAttribution.Publication;

    public ServiceActionResult TryExecute(
        in CrossRoleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));
}

internal static class CrossRoleResourceRuntime
{
    private static readonly ConcurrentDictionary<int, CrossRoleResource> States = new();
    private static readonly ConcurrentDictionary<int, int> StateCreates = new();
    private static readonly ConcurrentDictionary<int, int> StateReleases = new();
    private static int _nextId;

    internal static int NextId() => Interlocked.Increment(ref _nextId);
    internal static void SetState(int workerId, CrossRoleResource state) => States[workerId] = state;
    internal static CrossRoleResource CreateState(int workerId)
    {
        StateCreates.AddOrUpdate(workerId, 1, static (_, count) => count + 1);
        return States.TryGetValue(workerId, out var state) ? state : new CrossRoleResource();
    }
    internal static void RecordStateRelease(int workerId) =>
        StateReleases.AddOrUpdate(workerId, 1, static (_, count) => count + 1);
    internal static int StateCreateCount(int workerId) =>
        StateCreates.TryGetValue(workerId, out var count) ? count : 0;
    internal static int StateReleaseCount(int workerId) =>
        StateReleases.TryGetValue(workerId, out var count) ? count : 0;
}
