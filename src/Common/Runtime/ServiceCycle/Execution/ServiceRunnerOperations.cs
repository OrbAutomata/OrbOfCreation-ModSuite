using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal readonly struct ServiceCycleStartAttempt
{
    internal ServiceCycleStartAttempt(
        bool queued,
        ServiceStartDecisionFact startDecisionFact,
        ServiceCaptureFact captureFact,
        ServiceCycleIdentity cycle,
        BatchId batch,
        MonotonicTimestamp queuedAt,
        ServiceFault fault = default,
        MonotonicTimestamp retryDue = default,
        ServiceFaultRecoveryFact recoveredFault = default,
        ServiceStartInvocationFact startInvocation = default)
    {
        Queued = queued;
        StartDecisionFact = startDecisionFact;
        CaptureFact = captureFact;
        Cycle = cycle;
        Batch = batch;
        QueuedAt = queuedAt;
        Fault = fault;
        RetryDue = retryDue;
        RecoveredFault = recoveredFault;
        StartInvocation = startInvocation;
    }

    internal bool Queued { get; }
    internal ServiceStartDecisionFact StartDecisionFact { get; }
    internal ServiceCaptureFact CaptureFact { get; }
    internal ServiceCycleIdentity Cycle { get; }
    internal BatchId Batch { get; }
    internal MonotonicTimestamp QueuedAt { get; }
    internal ServiceFault Fault { get; }
    internal MonotonicTimestamp RetryDue { get; }
    internal ServiceFaultRecoveryFact RecoveredFault { get; }
    internal ServiceStartInvocationFact StartInvocation { get; }
    internal ServiceStartDecision StartDecision => StartDecisionFact.Decision;
    internal ServiceCaptureResult CaptureResult => CaptureFact.Result;
    internal bool CaptureAttempted => CaptureFact.IsPresent;
}

internal readonly struct ServiceResponseAcquisition
{
    internal ServiceResponseAcquisition(
        in ServiceWorkerResponse response,
        BatchReceipt terminalReceipt)
    {
        Acquired = true;
        Response = response;
        TerminalReceipt = terminalReceipt;
    }

    internal bool Acquired { get; }
    internal ServiceWorkerResponse Response { get; }
    internal BatchReceipt TerminalReceipt { get; }
    internal bool EmergencyRejected =>
        TerminalReceipt.IsPresent && TerminalReceipt.HasEmergencyStopContext;
}

internal readonly struct ServiceActionDispatch
{
    internal ServiceActionDispatch(
        ServiceActionFact actionFact,
        ServiceActionJournalAttribution attribution,
        bool batchTerminal,
        BatchReceipt receipt,
        ServiceFault fault = default,
        MonotonicTimestamp retryDue = default,
        ServiceFaultRecoveryFact recoveredFault = default,
        string? attributionFailureReason = null,
        string? executionFailureReason = null)
    {
        var attributionFailed =
            attribution.RouteStatus == ServiceActionRouteStatus.AttributionFailed;
        if (attributionFailed != !string.IsNullOrWhiteSpace(attributionFailureReason))
            throw new System.ArgumentException(
                "An attribution failure requires exactly one non-empty diagnostic reason.",
                nameof(attributionFailureReason));
        ActionFact = actionFact;
        Attribution = attribution;
        BatchTerminal = batchTerminal;
        Receipt = receipt;
        Fault = fault;
        RetryDue = retryDue;
        RecoveredFault = recoveredFault;
        AttributionFailureReason = attributionFailureReason;
        ExecutionFailureReason = executionFailureReason;
    }

    internal ServiceActionFact ActionFact { get; }
    internal ServiceActionJournalAttribution Attribution { get; }
    internal bool Attempted => ActionFact.IsPresent;
    internal ServiceActionResult Result => ActionFact.Result;
    internal bool BatchTerminal { get; }
    internal BatchReceipt Receipt { get; }
    internal ServiceFault Fault { get; }
    internal MonotonicTimestamp RetryDue { get; }
    internal ServiceFaultRecoveryFact RecoveredFault { get; }
    internal string? AttributionFailureReason { get; }

    /// <summary>What the service's action threw, when a throw is why its batch faulted.</summary>
    internal string? ExecutionFailureReason { get; }
}
