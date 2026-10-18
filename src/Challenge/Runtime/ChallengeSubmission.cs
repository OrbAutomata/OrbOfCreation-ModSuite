using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

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
    internal ChallengeSubmission(NativeActionOutcome outcome, int rerollsLeft = -1,
        int rerollsLeftAfter = -1)
    {
        Outcome = outcome;
        RerollsLeft = rerollsLeft;
        RerollsLeftAfter = rerollsLeftAfter;
    }

    internal NativeActionOutcome Outcome { get; }

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

    internal static ChallengeSubmission Reject(ServiceActionResultCode code, string reason) =>
        new(NativeActionOutcome.Reject(code, reason));

    internal static ChallengeSubmission RerollsExhausted(string reason, int rerollsLeft) =>
        new(NativeActionOutcome.Reject(ChallengeActionResultCodes.NoRerolls, reason), rerollsLeft);
}
