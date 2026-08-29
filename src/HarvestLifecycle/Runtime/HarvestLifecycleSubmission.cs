using OrbModding.Common;

namespace OrbAutomata;

internal enum HarvestLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    NotVisible,
    ElementListFull,
    ElementUsageUnavailable,
    ActionUnavailable,
    ActionListFull,
    AmountUnavailable,
    NotActive,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum HarvestLifecycleNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct HarvestLifecycleSubmission
{
    internal HarvestLifecycleSubmission(
        HarvestLifecyclePreflight preflight,
        HarvestLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        int maximumAmount = -1)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        MaximumAmount = maximumAmount;
    }

    internal HarvestLifecyclePreflight Preflight { get; }
    internal HarvestLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// The ceiling the refusal prose names, or <c>-1</c> when the prose names none. It is read from
    /// the same admission capture the prose is written from, so the sentence and the machine field
    /// can never disagree.
    /// </summary>
    internal int MaximumAmount { get; }

    internal bool Verified => Preflight == HarvestLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static HarvestLifecycleSubmission Reject(
        HarvestLifecyclePreflight preflight,
        string reason,
        int maximumAmount = -1) =>
        new(preflight, HarvestLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason, maximumAmount);
}
