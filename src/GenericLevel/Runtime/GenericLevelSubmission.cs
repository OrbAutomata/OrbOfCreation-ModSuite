using OrbModding.Common;

namespace OrbAutomata;

internal enum GenericLevelPreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    WrongDomain,
    Undiscovered,
    Hidden,
    Unavailable,
    CannotLevel,
    BonusUnavailable,
    ResourcesHidden,
    Unaffordable,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum GenericLevelNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct GenericLevelSubmission
{
    internal GenericLevelSubmission(
        GenericLevelPreflight preflight,
        GenericLevelNativeStage stage,
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

    internal GenericLevelPreflight Preflight { get; }
    internal GenericLevelNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == GenericLevelPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static GenericLevelSubmission Reject(GenericLevelPreflight preflight, string reason) =>
        new(preflight, GenericLevelNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
