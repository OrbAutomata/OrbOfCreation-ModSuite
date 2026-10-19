using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AlchemyLoadoutSubmission
{
    internal AlchemyLoadoutSubmission(NativeActionOutcome outcome, int maximumAmount = -1)
    {
        Outcome = outcome;
        MaximumAmount = maximumAmount;
    }

    internal NativeActionOutcome Outcome { get; }

    /// <summary>
    /// The ceiling the refusal prose names, or <c>-1</c> when the prose names none. It is read from
    /// the same admission capture the prose is written from, so the sentence and the machine field
    /// can never disagree.
    /// </summary>
    internal int MaximumAmount { get; }

    internal static AlchemyLoadoutSubmission Reject(
        ServiceActionResultCode code, GameActionReason reason, int maximumAmount = -1) =>
        new(NativeActionOutcome.Reject(code, reason), maximumAmount);
}
