using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class LifecycleState
{
    internal LifecycleState(int serial, ulong lifecycle)
    {
        Serial = serial;
        Lifecycle = lifecycle;
    }

    public int Serial { get; }
    public ulong Lifecycle { get; }
    public int Evaluations { get; internal set; }
}

internal readonly struct LifecycleAction
{
    internal LifecycleAction(int index, ulong lifecycle, LifecyclePayload payload)
    {
        Index = index;
        Lifecycle = lifecycle;
        Payload = payload;
    }

    public int Index { get; }
    public ulong Lifecycle { get; }
    public LifecyclePayload Payload { get; }
}

internal sealed class LifecyclePayload
{
    internal LifecyclePayload(int value) => Value = value;
    public int Value { get; }
}

/// <summary>Generation-safe fixture: every worker definition and state has a unique serial.</summary>
internal sealed class LifecycleServiceDefinition :
    IServiceCycleDefinition<LifecycleState, LifecycleAction>
{
    private readonly ConcurrentDictionary<ulong, int> _executionCounts = new();
    private readonly List<object> _workerDefinitions = new();
    private readonly object _identityGate = new();
    private int _nextWorkerSerial;
    private int _workerFactoryFailures;
    private readonly int _runtimeId;

    internal LifecycleServiceDefinition(string id)
    {
        ServiceId = new ServiceId(id);
        _runtimeId = LifecycleWorkerFixtureRuntime.Register();
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; set; } = WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; set; } = new(
        new MonotonicDuration(10), new MonotonicDuration(80));
    internal int ActionCount { get; set; } = 1;
    internal int RejectAtIndex { get; set; } = -1;
    internal int FaultAtIndex { get; set; } = -1;
    internal bool ReuseWorkerDefinition { get; set; }
    internal LifecycleState? SharedState { get; set; }
    internal IServiceCycleWorkerDefinition<LifecycleState, LifecycleAction>?
        SharedWorkerDefinition { get; set; }
    internal StateReleaseGate? StateReleaseGate { get; set; }
    internal Action? WorkerDefinitionFactoryCallback { get; set; }
    internal Action? ShouldStartCallback { get; set; }
    internal Action? ActionCallback { get; set; }
    internal int WorkerDefinitionCreateCount => Volatile.Read(ref _nextWorkerSerial);
    internal int StateReleaseCount => LifecycleWorkerFixtureRuntime.StateReleaseCount(_runtimeId);
    internal int ActionExecutionCount => Sum(_executionCounts);
    internal bool IsPayloadAlive(ulong lifecycle) =>
        LifecycleWorkerFixtureRuntime.IsPayloadAlive(_runtimeId, lifecycle);

    internal void FailNextWorkerFactories(int count) => Volatile.Write(ref _workerFactoryFailures, count);

    internal EvaluationGate BlockEvaluation(ulong lifecycle)
    {
        var gate = new EvaluationGate();
        if (!LifecycleWorkerFixtureRuntime.TryAddGate(_runtimeId, lifecycle, gate))
            throw new InvalidOperationException("The lifecycle already has an evaluation gate.");
        return gate;
    }

    internal int EvaluationCount(ulong lifecycle) =>
        LifecycleWorkerFixtureRuntime.EvaluationCount(_runtimeId, lifecycle);
    internal int ExecutionCount(ulong lifecycle) =>
        _executionCounts.TryGetValue(lifecycle, out var count) ? count : 0;
    internal int StateSerial(ulong lifecycle) =>
        LifecycleWorkerFixtureRuntime.StateSerial(_runtimeId, lifecycle);

    public IServiceCycleWorkerDefinition<LifecycleState, LifecycleAction>
        CreateWorkerDefinition()
    {
        var serial = Interlocked.Increment(ref _nextWorkerSerial);
        WorkerDefinitionFactoryCallback?.Invoke();
        if (ConsumeOne(ref _workerFactoryFailures))
            throw new InvalidOperationException("synthetic worker-definition construction failure");
        lock (_identityGate)
        {
            if (SharedWorkerDefinition is not null) return SharedWorkerDefinition;
            if (ReuseWorkerDefinition && _workerDefinitions.Count != 0)
            {
                return (IServiceCycleWorkerDefinition<LifecycleState, LifecycleAction>)_workerDefinitions[0];
            }
            var worker = new WorkerDefinition(
                _runtimeId,
                serial,
                ActionCount,
                SharedState);
            if (StateReleaseGate is not null)
                LifecycleWorkerFixtureRuntime.SetStateReleaseGate(_runtimeId, StateReleaseGate);
            _workerDefinitions.Add(worker);
            return worker;
        }
    }

    public ServiceStartDecision ShouldStart(in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context)
    {
        ShouldStartCallback?.Invoke();
        return ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    }

    public ServiceActionJournalAttribution DescribeAction(in LifecycleAction action) =>
        ServiceActionJournalAttribution.Publication;

    public ServiceActionResult TryExecute(
        in LifecycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        ActionCallback?.Invoke();
        if (action.Lifecycle != context.Cycle.Lifecycle.Value)
            throw new InvalidOperationException("A stale action reached the native adapter fixture.");
        _executionCounts.AddOrUpdate(action.Lifecycle, 1, static (_, count) => count + 1);
        if (action.Index == RejectAtIndex)
            return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        if (action.Index == FaultAtIndex)
        {
            return ServiceActionResult.Faulted(
                CommonActionResultCodes.AdapterFault,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.PostconditionFailed,
                    new NativeMutationCallOutcome(1, 1, 0)));
        }
        return ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));
    }

    private static int Sum(ConcurrentDictionary<ulong, int> values)
    {
        var total = 0;
        foreach (var pair in values) total += pair.Value;
        return total;
    }

    private static bool ConsumeOne(ref int count)
    {
        while (true)
        {
            var current = Volatile.Read(ref count);
            if (current <= 0) return false;
            if (Interlocked.CompareExchange(ref count, current - 1, current) == current) return true;
        }
    }

    private sealed class WorkerDefinition :
        IServiceCycleWorkerDefinition<LifecycleState, LifecycleAction>
    {
        private readonly int _serial;
        private readonly int _actionCount;
        private readonly int _runtimeId;
        private readonly LifecycleState? _sharedState;

        internal WorkerDefinition(
            int runtimeId,
            int serial,
            int actionCount,
            LifecycleState? sharedState)
        {
            _runtimeId = runtimeId;
            _serial = serial;
            _actionCount = actionCount;
            _sharedState = sharedState;
        }

        public LifecycleState CreateState(LifecycleGeneration lifecycle)
        {
            var state = _sharedState ?? new LifecycleState(_serial, lifecycle.Value);
            LifecycleWorkerFixtureRuntime.RecordState(_runtimeId, lifecycle.Value, _serial);
            return state;
        }

        public void ReleaseState(ref LifecycleState state)
        {
            LifecycleWorkerFixtureRuntime.RecordStateRelease(_runtimeId);
            if (LifecycleWorkerFixtureRuntime.TryGetStateReleaseGate(
                    _runtimeId, out var stateReleaseGate))
            {
                stateReleaseGate.Entered.Set();
                stateReleaseGate.Release.Wait();
            }
            state = null!;
        }

        public WakePolicy Evaluate(
            in SuiteRuntimeConfiguration config,
            GameWorldState world,
            SuiteStrategy strategy,
            in ServiceCycleContext context,
            ref LifecycleState state,
            ServiceActionWriter<LifecycleAction> actions)
        {
            if (_sharedState is null && state.Lifecycle != context.Identity.Lifecycle.Value)
                throw new InvalidOperationException("Generation stamps diverged in the evaluator fixture.");
            state.Evaluations++;
            LifecycleWorkerFixtureRuntime.RecordEvaluation(_runtimeId, context.Identity.Lifecycle.Value);
            if (LifecycleWorkerFixtureRuntime.TryGetGate(
                    _runtimeId, context.Identity.Lifecycle.Value, out var gate))
            {
                gate.Entered.Set();
                gate.Release.Wait();
            }
            var payload = new LifecyclePayload(_serial);
            LifecycleWorkerFixtureRuntime.RecordPayload(
                _runtimeId,
                context.Identity.Lifecycle.Value,
                payload);
            for (var index = 0; index < _actionCount; index++)
            {
                var action = new LifecycleAction(
                    index,
                    context.Identity.Lifecycle.Value,
                    payload);
                actions.Add(in action);
            }
            return WakePolicy.Immediate;
        }

        public void ProjectState(
            in LifecycleState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output)
        {
            output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Serial));
            output.Add(new ServiceProjectionKey(2), ServiceProjectionValue.FromInteger(state.Evaluations));
        }
    }
}

internal sealed class StateReleaseGate : IDisposable
{
    internal ManualResetEventSlim Entered { get; } = new(false);
    internal ManualResetEventSlim Release { get; } = new(false);

    public void Dispose()
    {
        Release.Set();
        Entered.Dispose();
        Release.Dispose();
    }
}

internal sealed class EvaluationGate : IDisposable
{
    internal ManualResetEventSlim Entered { get; } = new(false);
    internal ManualResetEventSlim Release { get; } = new(false);

    public void Dispose()
    {
        Release.Set();
        Entered.Dispose();
        Release.Dispose();
    }
}

internal static class LifecycleWorkerFixtureRuntime
{
    private static readonly ConcurrentDictionary<int, RuntimeData> Instances = new();
    private static int _nextId;

    internal static int Register()
    {
        var id = Interlocked.Increment(ref _nextId);
        if (!Instances.TryAdd(id, new RuntimeData()))
            throw new InvalidOperationException("Duplicate lifecycle fixture runtime id.");
        return id;
    }

    internal static bool TryAddGate(int id, ulong lifecycle, EvaluationGate gate) =>
        Get(id).Gates.TryAdd(lifecycle, gate);
    internal static bool TryGetGate(int id, ulong lifecycle, out EvaluationGate gate) =>
        Get(id).Gates.TryGetValue(lifecycle, out gate!);
    internal static void RecordEvaluation(int id, ulong lifecycle) =>
        Get(id).EvaluationCounts.AddOrUpdate(lifecycle, 1, static (_, count) => count + 1);
    internal static int EvaluationCount(int id, ulong lifecycle) =>
        Get(id).EvaluationCounts.TryGetValue(lifecycle, out var count) ? count : 0;
    internal static void RecordState(int id, ulong lifecycle, int serial) =>
        Get(id).StateSerials[lifecycle] = serial;
    internal static int StateSerial(int id, ulong lifecycle) =>
        Get(id).StateSerials.TryGetValue(lifecycle, out var serial) ? serial : 0;
    internal static void RecordPayload(int id, ulong lifecycle, LifecyclePayload payload) =>
        Get(id).Payloads[lifecycle] = new WeakReference<LifecyclePayload>(payload);
    internal static void RecordStateRelease(int id) =>
        Interlocked.Increment(ref Get(id).StateReleaseCount);
    internal static int StateReleaseCount(int id) => Volatile.Read(ref Get(id).StateReleaseCount);
    internal static bool IsPayloadAlive(int id, ulong lifecycle) =>
        Get(id).Payloads.TryGetValue(lifecycle, out var reference) &&
        reference.TryGetTarget(out _);
    internal static void SetStateReleaseGate(int id, StateReleaseGate gate) =>
        Get(id).StateReleaseGate = gate;
    internal static bool TryGetStateReleaseGate(int id, out StateReleaseGate gate)
    {
        gate = Get(id).StateReleaseGate!;
        return gate is not null;
    }

    private static RuntimeData Get(int id) =>
        Instances.TryGetValue(id, out var data)
            ? data
            : throw new InvalidOperationException("Unknown lifecycle fixture runtime id.");

    private sealed class RuntimeData
    {
        internal ConcurrentDictionary<ulong, EvaluationGate> Gates { get; } = new();
        internal ConcurrentDictionary<ulong, int> EvaluationCounts { get; } = new();
        internal ConcurrentDictionary<ulong, int> StateSerials { get; } = new();
        internal ConcurrentDictionary<ulong, WeakReference<LifecyclePayload>> Payloads { get; } = new();
        internal StateReleaseGate? StateReleaseGate;
        internal int StateReleaseCount;
    }
}
