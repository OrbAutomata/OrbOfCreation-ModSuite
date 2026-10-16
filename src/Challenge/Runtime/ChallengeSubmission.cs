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
        bool challengesFetched, int rerollsLeft)
    {
        TargetState = targetState;
        Selected = selected;
        InTimeOffers = inTimeOffers;
        InPrestigeOffers = inPrestigeOffers;
        WorldCycleComplete = worldCycleComplete;
        ChallengesFetched = challengesFetched;
        RerollsLeft = rerollsLeft;
    }

    internal int TargetState { get; }
    internal bool Selected { get; }
    internal bool InTimeOffers { get; }
    internal bool InPrestigeOffers { get; }
    internal bool WorldCycleComplete { get; }
    internal bool ChallengesFetched { get; }
    internal int RerollsLeft { get; }
}

/// <summary>
/// The reroll budget an attempted press started from and the budget settled after it, or -1 on
/// either side when that reading is not this outcome's axis or could not be taken.
/// </summary>
internal readonly struct ChallengeBudget
{
    internal ChallengeBudget(int before, int after)
    {
        Before = before;
        After = after;
    }

    internal static ChallengeBudget NotTheAxis => new(-1, -1);

    internal int Before { get; }
    internal int After { get; }
}

internal readonly struct ChallengeSubmission
{
    internal ChallengeSubmission(ChallengePreflight preflight, ChallengeNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome,
        string reason, int rerollsLeft = -1, int rerollsLeftAfter = -1)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        RerollsLeft = rerollsLeft;
        RerollsLeftAfter = rerollsLeftAfter;
    }

    internal ChallengePreflight Preflight { get; }
    internal ChallengeNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// The reroll budget the refusal's sentence was written from, or the budget an attempted press
    /// started from; -1 when the budget is not this outcome's axis.
    /// </summary>
    internal int RerollsLeft { get; }

    /// <summary>
    /// The settled budget read back after a press that was attempted and then failed, or -1 when no
    /// press was attempted or the reading could not be taken.
    /// </summary>
    internal int RerollsLeftAfter { get; }
    internal bool Verified => Preflight == ChallengePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static ChallengeSubmission Reject(ChallengePreflight preflight, string reason) =>
        new(preflight, ChallengeNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, reason);

    internal static ChallengeSubmission RerollsExhausted(string reason, int rerollsLeft) =>
        new(ChallengePreflight.NoRerolls, ChallengeNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason, rerollsLeft);
}
