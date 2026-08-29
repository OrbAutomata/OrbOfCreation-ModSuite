using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoScribePreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    Quarantined = 2,
    IdentityUnavailable = 3,
    RelationshipMismatch = 4,
    RecipeUnavailable = 5,
    TargetUnavailable = 6,
    QueueFull = 7,
    CompetingSupply = 8,
    Unaffordable = 9,
    MutationPermitUnavailable = 10,
    PostPaymentFault = 11,
    VerificationFailed = 12,
    LifecycleReplaced = 13,
    WrongThread = 14,
}

internal enum AutoScribeNativeStage
{
    None = 0,
    Payment = 1,
    Construction = 2,
    Initiation = 3,
    Admission = 4,
    Verification = 5,
}

internal readonly struct AutoScribeSubmission
{
    internal AutoScribeSubmission(
        AutoScribePreflight preflight,
        AutoScribeNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        bool retryable = false)
    {
        if (preflight != AutoScribePreflight.Proceeded && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("An Auto Scribe failure requires an exact reason.", nameof(reason));
        if (retryable && preflight != AutoScribePreflight.IdentityUnavailable)
            throw new ArgumentException(
                "Only an identity resolution rejection can carry registry retryability.",
                nameof(retryable));
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        Retryable = retryable;
    }

    internal AutoScribePreflight Preflight { get; }
    internal AutoScribeNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Retryable { get; }
    internal bool Verified =>
        Preflight == AutoScribePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static AutoScribeSubmission Reject(
        AutoScribePreflight preflight,
        string reason,
        bool retryable = false) =>
        new(
            preflight,
            AutoScribeNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed,
            default,
            reason,
            retryable);
}
