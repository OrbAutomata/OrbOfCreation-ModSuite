using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum DiscoveryTreeOfferPreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    WrongThread = 3,
    LifecycleReplaced = 4,
    IdentityUnavailable = 5,
    TreeUnavailable = 6,
    WrongMode = 7,
    NoDiscoveries = 8,
    OfferUnavailable = 9,
    AlreadyDiscovered = 10,
    RerollUnavailable = 11,
    Unaffordable = 12,
    MutationPermitUnavailable = 13,
    PostCommitFault = 14,
    VerificationFailed = 15,
}

internal enum DiscoveryTreeOfferNativeStage
{
    None = 0,
    Payment = 1,
    Initiate = 2,
    Select = 3,
    Confirm = 4,
    Reroll = 5,
    ClearSelection = 6,
    Verification = 7,
}

internal readonly struct DiscoveryTreeOfferSubmission
{
    internal DiscoveryTreeOfferSubmission(
        DiscoveryTreeOfferPreflight preflight,
        DiscoveryTreeOfferNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        int rerollsLeft = -1)
    {
        if (preflight != DiscoveryTreeOfferPreflight.Proceeded && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A Discovery Tree offer failure requires an exact reason.", nameof(reason));
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        RerollsLeft = rerollsLeft;
    }

    internal DiscoveryTreeOfferPreflight Preflight { get; }
    internal DiscoveryTreeOfferNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// The reroll budget the refusal's sentence was written from, or -1 when the budget is not the
    /// axis that failed.
    /// </summary>
    internal int RerollsLeft { get; }
    internal bool Verified =>
        Preflight == DiscoveryTreeOfferPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static DiscoveryTreeOfferSubmission Reject(
        DiscoveryTreeOfferPreflight preflight,
        string reason) =>
        new(preflight, DiscoveryTreeOfferNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);

    internal static DiscoveryTreeOfferSubmission RerollsExhausted(string reason, int rerollsLeft) =>
        new(DiscoveryTreeOfferPreflight.RerollUnavailable, DiscoveryTreeOfferNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason, rerollsLeft);
}
