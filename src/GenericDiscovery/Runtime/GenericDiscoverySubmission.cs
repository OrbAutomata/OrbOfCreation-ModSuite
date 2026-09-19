using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum GenericDiscoveryPreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    WrongThread = 3,
    LifecycleReplaced = 4,
    IdentityUnavailable = 5,
    UnsupportedType = 6,
    NotVisible = 7,
    AlreadyDiscovered = 8,
    DiscoveryUnavailable = 9,
    Unaffordable = 10,
    MutationPermitUnavailable = 11,
    PostCommitFault = 12,
    VerificationFailed = 13,
    GlyphRecipeEmpty = 15,
    ScreenLocked = 16,
}

internal enum GenericDiscoveryNativeStage
{
    None = 0,
    Payment = 1,
    Discover = 2,
    Verification = 3,
}

internal readonly struct GenericDiscoverySubmission
{
    internal GenericDiscoverySubmission(
        GenericDiscoveryPreflight preflight,
        GenericDiscoveryNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        if (preflight != GenericDiscoveryPreflight.Proceeded && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A generic discovery failure requires an exact reason.", nameof(reason));
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
    }

    internal GenericDiscoveryPreflight Preflight { get; }
    internal GenericDiscoveryNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified =>
        Preflight == GenericDiscoveryPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static GenericDiscoverySubmission Reject(
        GenericDiscoveryPreflight preflight,
        string reason) =>
        new(preflight, GenericDiscoveryNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
