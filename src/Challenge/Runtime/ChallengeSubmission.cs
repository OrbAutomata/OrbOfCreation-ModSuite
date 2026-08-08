using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum ChallengePreflight
{
    Proceeded = 0,
    WrongThread = 1,
    LifecycleReplaced = 2,
    ContractUnavailable = 3,
    IdentityUnavailable = 5,
    OfferUnavailable = 6,
    SelectionFull = 7,
    SelectionRestricted = 8,
    InvalidState = 9,
    FetchUnavailable = 10,
    NoRerolls = 11,
    MutationPermitUnavailable = 12,
    PostCommitFault = 13,
    VerificationFailed = 14,
}

internal enum ChallengeNativeStage
{
    None = 0,
    DecisionCommit = 1,
    NativeCallback = 2,
    Verification = 3,
}

internal readonly struct ChallengeAdmissionState
{
    internal ChallengeAdmissionState(int targetState, bool selected,
        bool inTimeOffers, bool inPrestigeOffers, bool worldCycleComplete,
        bool challengesFetched, int rerollsLeft, Guid[] offers)
    {
        TargetState = targetState;
        Selected = selected;
        InTimeOffers = inTimeOffers;
        InPrestigeOffers = inPrestigeOffers;
        WorldCycleComplete = worldCycleComplete;
        ChallengesFetched = challengesFetched;
        RerollsLeft = rerollsLeft;
        Offers = offers;
    }

    internal int TargetState { get; }
    internal bool Selected { get; }
    internal bool InTimeOffers { get; }
    internal bool InPrestigeOffers { get; }
    internal bool WorldCycleComplete { get; }
    internal bool ChallengesFetched { get; }
    internal int RerollsLeft { get; }
    internal Guid[] Offers { get; }
}

internal readonly struct ChallengeSubmission
{
    internal ChallengeSubmission(ChallengePreflight preflight, ChallengeNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome,
        string reason, int rerollsLeft = -1)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        RerollsLeft = rerollsLeft;
    }

    internal ChallengePreflight Preflight { get; }
    internal ChallengeNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// The reroll budget the refusal's sentence was written from, or -1 when the budget is not the
    /// axis that failed.
    /// </summary>
    internal int RerollsLeft { get; }
    internal bool Verified => Preflight == ChallengePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static ChallengeSubmission Reject(ChallengePreflight preflight, string reason) =>
        new(preflight, ChallengeNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, reason);

    internal static ChallengeSubmission RerollsExhausted(string reason, int rerollsLeft) =>
        new(ChallengePreflight.NoRerolls, ChallengeNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason, rerollsLeft);
}
