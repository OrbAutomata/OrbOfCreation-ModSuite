using OrbModding.Common;

namespace OrbAutomata;

internal enum CraftingStationPreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    SelectionUnavailable,
    SelectionHidden,
    LevelOutOfRange,
    NotLoaded,
    AlreadyInRequestedState,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum CraftingStationNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct CraftingStationSubmission
{
    internal CraftingStationSubmission(
        CraftingStationPreflight preflight,
        CraftingStationNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
    }

    internal CraftingStationPreflight Preflight { get; }
    internal CraftingStationNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == CraftingStationPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static CraftingStationSubmission Reject(
        CraftingStationPreflight preflight,
        string reason) =>
        new(preflight, CraftingStationNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
