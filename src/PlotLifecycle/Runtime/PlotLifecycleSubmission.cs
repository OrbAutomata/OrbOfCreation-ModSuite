using OrbModding.Common;

namespace OrbAutomata;

internal enum PlotLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    PlotUnavailable,
    ActionUnavailable,
    ActionListFull,
    QuantityUnavailable,
    NotActive,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum PlotLifecycleNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct PlotLifecycleSubmission
{
    internal PlotLifecycleSubmission(
        PlotLifecyclePreflight preflight,
        PlotLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        int beforeQuantity = 0,
        int afterQuantity = 0,
        int maximumAmount = -1)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        BeforeQuantity = beforeQuantity;
        AfterQuantity = afterQuantity;
        MaximumAmount = maximumAmount;
    }

    internal PlotLifecyclePreflight Preflight { get; }
    internal PlotLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal int BeforeQuantity { get; }
    internal int AfterQuantity { get; }

    /// <summary>
    /// The ceiling the refusal prose names, or <c>-1</c> when the prose names none. It is read from
    /// the same admission capture the prose is written from, so the sentence and the machine field
    /// can never disagree.
    /// </summary>
    internal int MaximumAmount { get; }

    internal bool Verified => Preflight == PlotLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static PlotLifecycleSubmission Reject(
        PlotLifecyclePreflight preflight,
        string reason,
        int maximumAmount = -1) =>
        new(preflight, PlotLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason, 0, 0, maximumAmount);
}
