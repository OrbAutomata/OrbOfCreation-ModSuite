using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct DiscoveryTreeOfferSubmission
{
    internal DiscoveryTreeOfferSubmission(
        NativeActionOutcome outcome,
        int rerollsLeft = -1)
    {
        Outcome = outcome;
        RerollsLeft = rerollsLeft;
    }

    internal NativeActionOutcome Outcome { get; }

    /// <summary>
    /// The reroll budget the refusal's sentence was written from, or -1 when the budget is not the
    /// axis that failed.
    /// </summary>
    internal int RerollsLeft { get; }

    internal static DiscoveryTreeOfferSubmission Reject(
        ServiceActionResultCode code,
        string reason) =>
        new(NativeActionOutcome.Reject(code, reason));

    internal static DiscoveryTreeOfferSubmission RerollsExhausted(string reason, int rerollsLeft) =>
        new(NativeActionOutcome.Reject(DiscoveryTreeOfferActionResultCodes.RerollUnavailable, reason),
            rerollsLeft);
}
