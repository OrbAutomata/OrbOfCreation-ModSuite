using OrbModding.Common;

namespace OrbAutomata;

internal enum StructureLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    NotAvailable,
    AlreadyInState,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum StructureLifecycleNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct StructureLifecycleSubmission
{
    internal StructureLifecycleSubmission(
        StructureLifecyclePreflight preflight,
        StructureLifecycleNativeStage stage,
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

    internal StructureLifecyclePreflight Preflight { get; }
    internal StructureLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == StructureLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static StructureLifecycleSubmission Reject(
        StructureLifecyclePreflight preflight,
        string reason) =>
        new(preflight, StructureLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
