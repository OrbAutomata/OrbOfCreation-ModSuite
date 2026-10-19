using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class ExecutionWorkerControl
{
    private int _stateFactoryFailures;
    private int _evaluationFaults;
    private int _projectionFaults;
    private int _stateCreateCount;
    private int _stateReleaseCount;
    private int _evaluationCount;
    private int _failureReports;
    private int _verifiedReports;
    private int _lastContextRetryFailures;

    internal ExecutionWorkerControl(int signalId) => SignalId = signalId;
    internal int SignalId { get; }
    internal int ActionCount;
    internal int PartialActionCountBeforeFault;
    internal WakePolicy EvaluationWake = WakePolicy.Immediate;
    internal ActionPayload? Payload = new(1);
    internal bool MeasureAppendAllocations;
    internal bool DuplicateProjectionKey;
    internal long LastAppendAllocatedBytes;
    internal int LastEvaluatedSetting;
    internal int LastEvaluatedStrategySetting;
    internal int StateCreateCount => Volatile.Read(ref _stateCreateCount);
    internal int StateReleaseCount => Volatile.Read(ref _stateReleaseCount);
    internal int EvaluationCount => Volatile.Read(ref _evaluationCount);

    private int _lastProjectedStructures;
    private int _lastProjectedWasEmptyDefault;

    /// <summary>How many structures the world the runtime handed the evaluation described.</summary>
    /// <remarks>
    /// Recorded as a count rather than as the snapshot itself because a worker may not hold one: the
    /// graph audit rejects a worker field of a Common storage type, which is the rule that stops a
    /// worker from keeping a world across cycles. Reading a number out of it does not keep it.
    /// </remarks>
    internal int LastEvaluatedStructures => Volatile.Read(ref _lastProjectedStructures);

    /// <summary>Whether the runtime handed over the publisher's empty seed rather than a collected world.</summary>
    internal bool LastEvaluatedWorldWasTheEmptyDefault => Volatile.Read(ref _lastProjectedWasEmptyDefault) != 0;

    /// <summary>How many failures the target had on the retry gate the last evaluation was handed.</summary>
    internal int LastContextRetryFailures => Volatile.Read(ref _lastContextRetryFailures);

    internal void ReportFailureOnNextEvaluations(int count) => Volatile.Write(ref _failureReports, count);
    internal void ReportVerifiedOnNextEvaluation() => Volatile.Write(ref _verifiedReports, 1);

    internal void FailNextEvaluations(int count) => Volatile.Write(ref _evaluationFaults, count);
    internal void FailNextProjections(int count) => Volatile.Write(ref _projectionFaults, count);
    internal void FailNextStateFactories(int count) => Volatile.Write(ref _stateFactoryFailures, count);

    internal ExecutionState CreateState()
    {
        Interlocked.Increment(ref _stateCreateCount);
        if (ConsumeOne(ref _stateFactoryFailures)) throw new InvalidOperationException("synthetic state factory fault");
        return new ExecutionState();
    }

    internal void ReleaseState(ref ExecutionState state)
    {
        Interlocked.Increment(ref _stateReleaseCount);
        state = null!;
    }

    internal WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref ExecutionState state,
        ServiceActionWriter<ExecutionAction> actions)
    {
        var signals = ExecutionWorkerSignals.Get(SignalId);
        Volatile.Write(
            ref _lastContextRetryFailures,
            context.RetryGate.TryFind(ExecutionServiceDefinition.Target, out var retry)
                ? retry.ConsecutiveFailures
                : 0);
        if (ConsumeOne(ref _failureReports))
            actions.ReportFailure(ExecutionServiceDefinition.Target, "synthetic surprise");
        if (ConsumeOne(ref _verifiedReports))
            actions.ReportVerified(ExecutionServiceDefinition.Target);
        LastEvaluatedSetting = TestSuiteConfiguration.SettingOf(config);
        LastEvaluatedStrategySetting = TestSuiteStrategy.SettingOf(strategy);
        Volatile.Write(ref _lastProjectedStructures, world.Structures.AsSpan().Length);
        Volatile.Write(
            ref _lastProjectedWasEmptyDefault,
            ReferenceEquals(world, GameWorldStateDefaults.Empty) ? 1 : 0);
        state.Evaluations++;
        Interlocked.Increment(ref _evaluationCount);
        signals.EvaluationEntered?.Set();
        signals.EvaluationRelease?.Wait();
        var shouldFault = ConsumeOne(ref _evaluationFaults);
        var count = shouldFault ? PartialActionCountBeforeFault : ActionCount;
        var before = MeasureAppendAllocations ? GC.GetAllocatedBytesForCurrentThread() : 0;
        var payload = Payload ?? new ActionPayload(-1);
        for (var index = 0; index < count; index++)
        {
            var action = new ExecutionAction(index, payload);
            actions.Add(in action);
        }
        if (MeasureAppendAllocations)
            LastAppendAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        signals.ActionsAppended?.Set();
        signals.ActionsRelease?.Wait();
        if (shouldFault) throw new InvalidOperationException("synthetic evaluation fault");
        return EvaluationWake;
    }

    internal void Project(in ExecutionState state, ServiceStateProjectionBuilder output)
    {
        output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Evaluations));
        if (DuplicateProjectionKey)
            output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(-1));
        if (ConsumeOne(ref _projectionFaults)) throw new InvalidOperationException("synthetic projection fault");
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
}

internal sealed class ExecutionWorkerDefinition :
    IServiceCycleWorkerDefinition<ExecutionState, ExecutionAction>
{
    private readonly ExecutionWorkerControl _control;

    internal ExecutionWorkerDefinition(ExecutionWorkerControl control) => _control = control;

    public ExecutionState CreateState(LifecycleGeneration lifecycle) => _control.CreateState();
    public void ReleaseState(ref ExecutionState state) => _control.ReleaseState(ref state);

    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref ExecutionState state,
        ServiceActionWriter<ExecutionAction> actions) =>
        _control.Evaluate(in config, world, strategy, in context, ref state, actions);

    public void ProjectState(
        in ExecutionState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) => _control.Project(in state, output);
}
