using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing;

internal static class ServiceCycleTraceFixtures
{
    internal static readonly ServiceCycleTraceSessionId Session = new(101);
    internal static readonly ServiceCycleTraceServiceId Service = new(7);
    internal static readonly ServiceCycleTraceCaptureIdentity Capture = new(Service, 2, 3, 5, 6);
    internal static readonly ServiceCycleTraceCycleIdentity Cycle = new(Service, 2, 3, 4, 11, 6);

    /// <summary>
    /// The pump frame the framed capture and action fixtures ran inside. The faulted capture and the
    /// faulted action deliberately carry no frame, so both admissible shapes stay covered.
    /// </summary>
    internal const long Frame = 21;
    internal const long Unframed = -1;

    internal static ServiceCycleSemanticPayload Payload(ServiceCycleSemanticEventKind kind) => kind switch
    {
        ServiceCycleSemanticEventKind.ConfigurationPublished =>
            ServiceCycleSemanticPayload.Publication(false, 3, 100),
        ServiceCycleSemanticEventKind.StrategyPublished =>
            ServiceCycleSemanticPayload.Publication(true, 4, 100),
        ServiceCycleSemanticEventKind.LifecycleRetired =>
            ServiceCycleSemanticPayload.LifecycleFact(Service, 2, CommonActionResultCodes.LifecycleReplaced.Value, 100),
        ServiceCycleSemanticEventKind.LifecycleRequested or
        ServiceCycleSemanticEventKind.LifecycleActivated =>
            ServiceCycleSemanticPayload.LifecycleFact(Service, 2, 0, 100),
        ServiceCycleSemanticEventKind.LifecycleConstructionDeferred =>
            ServiceCycleSemanticPayload.LifecycleConstructionDeferred(
                Service, 2, CommonServiceDecisionCodes.TransientContention.Value, 100, 200),
        ServiceCycleSemanticEventKind.EmergencyEntered or ServiceCycleSemanticEventKind.EmergencyCleared =>
            ServiceCycleSemanticPayload.Emergency((int)EmergencyStopReason.UserRequested, 1, 100),
        ServiceCycleSemanticEventKind.CycleQueued =>
            ServiceCycleSemanticPayload.CycleFact(in Cycle, CommonServiceDecisionCodes.Ready.Value, 100, 10),
        ServiceCycleSemanticEventKind.CycleStarted or ServiceCycleSemanticEventKind.CycleCompleted =>
            ServiceCycleSemanticPayload.CycleFact(in Cycle, 0, 100, 10),
        ServiceCycleSemanticEventKind.CycleOrphaned =>
            ServiceCycleSemanticPayload.CycleFact(in Cycle, CommonActionResultCodes.LifecycleReplaced.Value, 100, 10),
        ServiceCycleSemanticEventKind.CycleFaulted =>
            ServiceCycleSemanticPayload.CycleFact(in Cycle, CommonActionResultCodes.AdapterFault.Value, 100, 10),
        ServiceCycleSemanticEventKind.CaptureStarted =>
            ServiceCycleSemanticPayload.CaptureFact(in Capture, 0, 0, 100, 10, Frame),
        ServiceCycleSemanticEventKind.CaptureCompleted =>
            ServiceCycleSemanticPayload.CaptureFact(
                in Capture, 4, CommonServiceDecisionCodes.Captured.Value, 100, 10, Frame),
        ServiceCycleSemanticEventKind.CaptureUnavailable =>
            ServiceCycleSemanticPayload.CaptureUnavailable(
                in Capture,
                CommonServiceDecisionCodes.CaptureUnavailable.Value,
                WakePolicy.AfterDecision(new MonotonicDuration(20)),
                100,
                10,
                Frame),
        ServiceCycleSemanticEventKind.CaptureFaulted =>
            ServiceCycleSemanticPayload.CaptureFact(
                in Capture, 0, CommonActionResultCodes.AdapterFault.Value, 100, 10, Unframed),
        ServiceCycleSemanticEventKind.EvaluationStarted =>
            ServiceCycleSemanticPayload.Evaluation(in Cycle, 0, 0, 100, 10),
        ServiceCycleSemanticEventKind.EvaluationCompleted =>
            ServiceCycleSemanticPayload.EvaluationCompleted(
                in Cycle,
                3,
                WakePolicy.AfterDecision(new MonotonicDuration(20)),
                100,
                10),
        ServiceCycleSemanticEventKind.EvaluationFaulted =>
            ServiceCycleSemanticPayload.Evaluation(in Cycle, CommonActionResultCodes.AdapterFault.Value, 0, 100, 10),
        ServiceCycleSemanticEventKind.ProjectionFaulted =>
            ServiceCycleSemanticPayload.ProjectionFaulted(
                in Cycle,
                CommonActionResultCodes.AdapterFault.Value,
                3,
                WakePolicy.AfterDecision(new MonotonicDuration(20)),
                100,
                10),
        ServiceCycleSemanticEventKind.EvaluationDeferred =>
            ServiceCycleSemanticPayload.EvaluationDeferred(
                in Cycle, CommonServiceDecisionCodes.TransientContention.Value, 100, 10, 200),
        ServiceCycleSemanticEventKind.StatePublished =>
            ServiceCycleSemanticPayload.State(in Cycle, 9, 0xf00dUL, 100),
        ServiceCycleSemanticEventKind.BatchPublished =>
            ServiceCycleSemanticPayload.BatchFact(in Cycle, 8, 0, 0, 3, 0, -1, 0, 0, 0, 0, 100),
        ServiceCycleSemanticEventKind.BatchCompleted =>
            ServiceCycleSemanticPayload.BatchFact(in Cycle, 8, (int)BatchTerminalDisposition.Completed,
                CommonActionResultCodes.Committed.Value, 3, 3, -1, 0, 3, 3, 3, 100),
        ServiceCycleSemanticEventKind.BatchAborted =>
            ServiceCycleSemanticPayload.BatchFact(in Cycle, 8, (int)BatchTerminalDisposition.Rejected,
                CommonActionResultCodes.NativeRejected.Value, 3, 1, 1, 1, 1, 1, 1, 100),
        ServiceCycleSemanticEventKind.BatchOrphaned =>
            ServiceCycleSemanticPayload.BatchFact(in Cycle, 8, (int)BatchTerminalDisposition.Orphaned,
                CommonActionResultCodes.LifecycleReplaced.Value, 3, 1, -1, 2, 1, 1, 1, 100),
        ServiceCycleSemanticEventKind.ActionAttempted =>
            ServiceCycleSemanticPayload.ActionFact(in Cycle, 8, 10, 0, 0, 0, null, 0, 0, 0, 100, 10, Frame),
        ServiceCycleSemanticEventKind.ActionCommitted =>
            ServiceCycleSemanticPayload.ActionFact(in Cycle, 8, 10, 0, (int)ServiceActionDisposition.Committed,
                CommonActionResultCodes.Committed.Value, NativeMutationOutcome.Verified, 1, 1, 1, 100, 10, Frame),
        ServiceCycleSemanticEventKind.ActionSkipped =>
            ServiceCycleSemanticPayload.ActionFact(in Cycle, 8, 10, 0, (int)ServiceActionDisposition.Skipped,
                CommonActionResultCodes.Skipped.Value, NativeMutationOutcome.PostconditionFailed, 1, 1, 0, 100, 10,
                Frame),
        ServiceCycleSemanticEventKind.ActionRejected =>
            ServiceCycleSemanticPayload.ActionFact(in Cycle, 8, 10, 0, (int)ServiceActionDisposition.Rejected,
                CommonActionResultCodes.NativeRejected.Value, null, 0, 0, 0, 100, 10, Frame),
        ServiceCycleSemanticEventKind.ActionFaulted =>
            ServiceCycleSemanticPayload.ActionFact(in Cycle, 8, 10, 0, (int)ServiceActionDisposition.Faulted,
                CommonActionResultCodes.AdapterFault.Value, NativeMutationOutcome.ExecutionThrew, 1, 1, 0, 100, 10,
                Unframed),
        ServiceCycleSemanticEventKind.RetryScheduled =>
            ServiceCycleSemanticPayload.FaultOrRetry(Service, 2, (int)ServiceFaultCategory.Evaluation,
                CommonActionResultCodes.AdapterFault.Value, 1, 100, 200),
        ServiceCycleSemanticEventKind.FaultObserved or ServiceCycleSemanticEventKind.FaultRecovered =>
            ServiceCycleSemanticPayload.FaultOrRetry(Service, 2, (int)ServiceFaultCategory.Evaluation,
                CommonActionResultCodes.AdapterFault.Value, 1, 100, 0),
        ServiceCycleSemanticEventKind.PumpCompleted =>
            ServiceCycleSemanticPayload.Pump(12, true, 3, 1, 2, 3, 9, 13, 4, 5, 6, 7, 8, 30, 100),
        ServiceCycleSemanticEventKind.StartAttempted =>
            ServiceCycleSemanticPayload.StartAttempted(Service, 2, 3, 100),
        ServiceCycleSemanticEventKind.StartDeferred =>
            ServiceCycleSemanticPayload.StartDeferred(
                Service, 2, 3, CommonServiceDecisionCodes.NotReady.Value,
                WakePolicy.AfterDecision(new MonotonicDuration(20)), 100, 10),
        ServiceCycleSemanticEventKind.StartFaulted =>
            ServiceCycleSemanticPayload.StartFaulted(
                Service, 2, 3, CommonActionResultCodes.AdapterFault.Value,
                (int)ServiceFaultCategory.Start, 1, 100, 10, 200),
        ServiceCycleSemanticEventKind.StartReady =>
            ServiceCycleSemanticPayload.StartReady(
                Service, 2, 3, CommonServiceDecisionCodes.Ready.Value, 100, 10),
        ServiceCycleSemanticEventKind.WorldCategoryCollected =>
            ServiceCycleSemanticPayload.WorldCategoryFact(
                category: 5, sampled: 2_628, passCategories: 61, lifecycle: 2, frameIdentity: Frame,
                timestampTicks: 100, durationTicks: 10),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static ServiceCycleSemanticEvent Event(
        ulong sequence,
        ServiceCycleSemanticEventKind kind = ServiceCycleSemanticEventKind.CycleStarted,
        ulong parentSequence = 0,
        ServiceCycleTraceSessionId? eventSession = null,
        ServiceCycleTraceSessionId? parentSession = null)
    {
        var actualSession = eventSession ?? Session;
        var id = new ServiceCycleTraceEventId(actualSession, sequence);
        var parent = parentSequence == 0
            ? default
            : new ServiceCycleTraceEventId(parentSession ?? actualSession, parentSequence);
        var payload = Payload(kind);
        return new ServiceCycleSemanticEvent(id, parent, kind, in payload);
    }

    internal static ServiceCycleSemanticEvent[] EveryEventKind()
    {
        var kinds = (ServiceCycleSemanticEventKind[])Enum.GetValues(typeof(ServiceCycleSemanticEventKind));
        var events = new ServiceCycleSemanticEvent[kinds.Length];
        for (var i = 0; i < kinds.Length; i++) events[i] = Event((ulong)i + 1, kinds[i], i == 0 ? 0 : (ulong)i);
        return events;
    }
}
