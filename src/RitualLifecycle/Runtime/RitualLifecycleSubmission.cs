using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct RitualLifecycleSubmission
{
    internal RitualLifecycleSubmission(
        NativeActionOutcome outcome,
        int minimumAmount = -1,
        int maximumAmount = -1)
    {
        Outcome = outcome;
        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
    }

    internal NativeActionOutcome Outcome { get; }

    /// <summary>
    /// The bounds the refusal prose names, or <c>-1</c> when it names none. Both are read from the
    /// same live capture the sentence is written from, so the prose and the machine fields cannot
    /// disagree.
    /// </summary>
    internal int MinimumAmount { get; }

    internal int MaximumAmount { get; }

    internal static RitualLifecycleSubmission RejectLevelOutOfRange(
        string reason,
        int minimumAmount,
        int maximumAmount) =>
        new(NativeActionOutcome.Reject(RitualLifecycleActionResultCodes.LevelOutOfRange, reason),
            minimumAmount, maximumAmount);
}
