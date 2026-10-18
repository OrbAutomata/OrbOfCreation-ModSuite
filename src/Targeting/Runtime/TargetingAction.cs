using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum TargetingActionKind { Submit = 0, Randomize = 1, Cancel = 2 }

internal readonly struct TargetingAction
{
    internal TargetingAction(TargetingActionKind kind, Guid targetId, long lifecycleEpoch)
    {
        if (kind == TargetingActionKind.Submit && targetId == Guid.Empty)
            throw new ArgumentException("Submit requires a target UUID.", nameof(targetId));
        if (kind != TargetingActionKind.Submit && targetId != Guid.Empty)
            throw new ArgumentException("Only submit accepts a target UUID.", nameof(targetId));
        Kind = kind;
        TargetId = targetId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal TargetingActionKind Kind { get; }
    internal Guid TargetId { get; }
    internal long LifecycleEpoch { get; }
}

internal readonly struct TargetingSubmission
{
    internal TargetingSubmission(NativeActionOutcome outcome, Guid submittedTarget)
    {
        Outcome = outcome; SubmittedTarget = submittedTarget;
    }
    internal NativeActionOutcome Outcome { get; }
    internal Guid SubmittedTarget { get; }
    internal static TargetingSubmission Reject(ServiceActionResultCode code, string reason) =>
        new(NativeActionOutcome.Reject(code, reason), Guid.Empty);
}

internal static class TargetingActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(7501);
    internal static readonly ServiceActionResultCode WrongThread = new(7503);
    internal static readonly ServiceActionResultCode NoPendingRequest = new(7504);
    internal static readonly ServiceActionResultCode TargetUnavailable = new(7505);
    internal static readonly ServiceActionResultCode NativeTargetRefused = new(7506);
    internal static readonly ServiceActionResultCode CancelUnavailable = new(7507);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7508);
    internal static readonly ServiceActionResultCode PostCommitFault = new(7509);
    internal static readonly ServiceActionResultCode VerificationFailed = new(7510);
}
