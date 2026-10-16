using OrbModding.Common;

namespace OrbAutomata;

internal enum CraftingInstanceLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    NotVisible,
    PageRelationAmbiguous,
    InstanceUnavailable,
    AutomationFull,
    MultiBuyUnavailable,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum CraftingInstanceLifecycleNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

/// <summary>
/// The automation quantity a failed transition already moved, so a refusal can name what it
/// changed instead of leaving the caller to retry into more damage.
/// </summary>
internal readonly struct CraftingInstanceLifecycleSideEffect
{
    private CraftingInstanceLifecycleSideEffect(int before, int after)
    {
        Observed = true;
        AutomationBefore = before;
        AutomationAfter = after;
    }

    internal bool Observed { get; }
    internal int AutomationBefore { get; }
    internal int AutomationAfter { get; }

    internal static CraftingInstanceLifecycleSideEffect Automation(int before, int after) =>
        new(before, after);
}

internal readonly struct CraftingInstanceLifecycleSubmission
{
    internal CraftingInstanceLifecycleSubmission(
        CraftingInstanceLifecyclePreflight preflight,
        CraftingInstanceLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        CraftingInstanceLifecycleSideEffect sideEffect = default)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        SideEffect = sideEffect;
    }

    internal CraftingInstanceLifecyclePreflight Preflight { get; }
    internal CraftingInstanceLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal CraftingInstanceLifecycleSideEffect SideEffect { get; }
    internal bool Verified => Preflight == CraftingInstanceLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static CraftingInstanceLifecycleSubmission Reject(
        CraftingInstanceLifecyclePreflight preflight,
        string reason) =>
        new(preflight, CraftingInstanceLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
