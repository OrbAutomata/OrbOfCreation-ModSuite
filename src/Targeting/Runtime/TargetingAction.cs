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

internal enum TargetingPreflight
{
    Proceeded = 0, LifecycleReplaced = 1, ContractUnavailable = 2,
    WrongThread = 4, NoPendingRequest = 5, TargetUnavailable = 6,
    NativeTargetRefused = 7, CancelUnavailable = 8, MutationPermitUnavailable = 9,
    PostCommitFault = 10, VerificationFailed = 11,
}

internal enum TargetingNativeStage { None = 0, SelectRandom = 1, Submit = 2, Cancel = 3, Verification = 4 }

internal readonly struct TargetingSubmission
{
    internal TargetingSubmission(TargetingPreflight preflight, TargetingNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome,
        Guid submittedTarget, string reason)
    {
        Preflight = preflight; Stage = stage; Outcome = outcome; CallOutcome = callOutcome;
        SubmittedTarget = submittedTarget; Reason = reason ?? string.Empty;
    }
    internal TargetingPreflight Preflight { get; }
    internal TargetingNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal Guid SubmittedTarget { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == TargetingPreflight.Proceeded && Outcome == NativeMutationOutcome.Verified;
    internal static TargetingSubmission Reject(TargetingPreflight preflight, string reason) =>
        new(preflight, TargetingNativeStage.None, default, default, Guid.Empty, reason);
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
