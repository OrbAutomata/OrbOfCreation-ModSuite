using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoScribeSubmission
{
    internal AutoScribeSubmission(NativeActionOutcome outcome, bool retryable = false)
    {
        if (!outcome.Verified && string.IsNullOrWhiteSpace(outcome.Reason))
            throw new ArgumentException("An Auto Scribe failure requires an exact reason.", nameof(outcome));
        if (retryable && outcome.Code != AutoScribeActionResultCodes.IdentityUnavailable)
            throw new ArgumentException(
                "Only an identity resolution rejection can carry registry retryability.",
                nameof(retryable));
        Outcome = outcome;
        Retryable = retryable;
    }

    internal NativeActionOutcome Outcome { get; }
    internal bool Retryable { get; }

    internal static AutoScribeSubmission Reject(
        ServiceActionResultCode code,
        GameActionReason reason,
        bool retryable = false) =>
        new(NativeActionOutcome.Reject(code, reason), retryable);

    internal static AutoScribeSubmission Fault(ServiceActionResultCode code, GameActionReason reason) =>
        new(NativeActionOutcome.FaultBeforeCall(code, reason));
}
