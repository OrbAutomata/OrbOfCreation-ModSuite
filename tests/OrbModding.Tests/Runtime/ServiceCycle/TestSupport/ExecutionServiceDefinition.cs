using System;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal class ExecutionServiceDefinition :
    IServiceCycleDefinition<ExecutionState, ExecutionAction>
{
    private readonly ExecutionWorkerSignals _signals;
    private readonly ExecutionWorkerControl _worker;

    internal ExecutionServiceDefinition(string id)
    {
        ServiceId = new ServiceId(id);
        _signals = ExecutionWorkerSignals.Create();
        _worker = new ExecutionWorkerControl(_signals.Id);
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; set; } = WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; set; } = new(
        new MonotonicDuration(10), new MonotonicDuration(80));

    internal int ActionCount { get => _worker.ActionCount; set => _worker.ActionCount = value; }
    internal int PartialActionCountBeforeFault
    {
        get => _worker.PartialActionCountBeforeFault;
        set => _worker.PartialActionCountBeforeFault = value;
    }
    internal int RejectAtIndex { get; set; } = -1;
    internal int FaultAtIndex { get; set; } = -1;
    internal int SkipAtIndex { get; set; } = -1;
    internal int SkipWithoutNativeAtIndex { get; set; } = -1;
    internal NativeMutationCallOutcome CommittedNativeOutcome { get; set; } = new(1, 1, 1);

    /// <summary>When set, actions commit as publications instead of native mutations.</summary>
    internal WorldGeneration? PublishesGeneration { get; set; }
    internal WakePolicy EvaluationWake { get => _worker.EvaluationWake; set => _worker.EvaluationWake = value; }
    internal ManualResetEventSlim? EvaluationEntered
    {
        get => _signals.EvaluationEntered;
        set => _signals.EvaluationEntered = value;
    }
    internal ManualResetEventSlim? EvaluationRelease
    {
        get => _signals.EvaluationRelease;
        set => _signals.EvaluationRelease = value;
    }
    internal ManualResetEventSlim? ActionsAppended
    {
        get => _signals.ActionsAppended;
        set => _signals.ActionsAppended = value;
    }
    internal ManualResetEventSlim? ActionsRelease
    {
        get => _signals.ActionsRelease;
        set => _signals.ActionsRelease = value;
    }
    internal ActionPayload? Payload { get => _worker.Payload; set => _worker.Payload = value; }
    internal bool MeasureAppendAllocations
    {
        get => _worker.MeasureAppendAllocations;
        set => _worker.MeasureAppendAllocations = value;
    }
    internal long LastAppendAllocatedBytes => _worker.LastAppendAllocatedBytes;
    internal bool DuplicateProjectionKey
    {
        get => _worker.DuplicateProjectionKey;
        set => _worker.DuplicateProjectionKey = value;
    }
    internal int LastEvaluatedSetting => _worker.LastEvaluatedSetting;
    internal int LastEvaluatedStrategySetting => _worker.LastEvaluatedStrategySetting;
    internal int LastExecutedSetting { get; private set; }
    internal int ActionExecutionCount { get; private set; }
    /// <summary>
    /// How many times the runtime asked this service whether to start.
    /// </summary>
    /// <remarks>
    /// Observed on the pump thread inside <see cref="ShouldStart"/>, which is where the world
    /// freshness gate is decided, so a test can read it straight after pumping. The worker's
    /// evaluation count cannot serve: it is raced against the pump call that provoked it.
    /// </remarks>
    internal int StartCount { get; private set; }
    internal ServiceStartDecision StartDecision { get; set; } =
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    internal Action? ShouldStartCallback { get; set; }
    internal Action? ActionCallback { get; set; }
    internal bool ThrowOnDescribeAction { get; set; }
    internal MonotonicTimestamp LastActionAttemptedAt { get; private set; }
    internal int StateCreateCount => _worker.StateCreateCount;
    internal int StateReleaseCount => _worker.StateReleaseCount;
    internal int EvaluationCount => _worker.EvaluationCount;
    internal int LastEvaluatedStructures => _worker.LastEvaluatedStructures;
    internal bool LastEvaluatedWorldWasTheEmptyDefault => _worker.LastEvaluatedWorldWasTheEmptyDefault;

    internal void FailNextEvaluations(int count) => _worker.FailNextEvaluations(count);
    internal void FailNextProjections(int count) => _worker.FailNextProjections(count);
    internal void FailNextStateFactories(int count) => _worker.FailNextStateFactories(count);

    public IServiceCycleWorkerDefinition<ExecutionState, ExecutionAction>
        CreateWorkerDefinition() => new ExecutionWorkerDefinition(_worker);

    public ServiceStartDecision ShouldStart(in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context)
    {
        StartCount++;
        ShouldStartCallback?.Invoke();
        return StartDecision;
    }

    public ServiceActionJournalAttribution DescribeAction(in ExecutionAction action)
    {
        if (ThrowOnDescribeAction)
            throw new InvalidOperationException("attribution exploded");
        return PublishesGeneration.HasValue
            ? ServiceActionJournalAttribution.Publication
            : ServiceActionJournalAttribution.Native(
                new Guid("11111111-1111-1111-1111-111111111111"),
                ServiceActionNativeTypeId.StructureSO);
    }

    public ServiceActionResult TryExecute(
        in ExecutionAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        ActionExecutionCount++;
        LastActionAttemptedAt = context.AttemptedAt;
        ActionCallback?.Invoke();
        LastExecutedSetting = TestSuiteConfiguration.SettingOf(config);
        if (context.ActionIndex == FaultAtIndex)
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        if (context.ActionIndex == RejectAtIndex)
            return ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
        if (context.ActionIndex == SkipAtIndex)
            return ServiceActionResult.Skipped(
                CommonActionResultCodes.Skipped,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.PostconditionFailed,
                    new NativeMutationCallOutcome(1, 1, 0)));
        if (context.ActionIndex == SkipWithoutNativeAtIndex)
            return ServiceActionResult.Skipped(CommonActionResultCodes.Skipped);
        if (PublishesGeneration is { } published)
        {
            return ServiceActionResult.CommittedPublication(
                CommonActionResultCodes.Committed,
                ServicePublicationEvidence.World(published));
        }

        return ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, CommittedNativeOutcome));
    }
}
