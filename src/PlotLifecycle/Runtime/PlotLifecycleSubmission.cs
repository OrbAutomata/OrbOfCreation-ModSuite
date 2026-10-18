using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct PlotLifecycleSubmission
{
    internal PlotLifecycleSubmission(
        NativeActionOutcome outcome,
        int beforeQuantity = 0,
        int afterQuantity = 0,
        int maximumAmount = -1)
    {
        Outcome = outcome;
        BeforeQuantity = beforeQuantity;
        AfterQuantity = afterQuantity;
        MaximumAmount = maximumAmount;
    }

    internal static PlotLifecycleSubmission Refused(
        ServiceActionResultCode code,
        string reason,
        int maximumAmount = -1) =>
        new(NativeActionOutcome.Reject(code, reason), maximumAmount: maximumAmount);

    internal NativeActionOutcome Outcome { get; }
    internal int BeforeQuantity { get; }
    internal int AfterQuantity { get; }

    /// <summary>
    /// The ceiling the refusal prose names, or <c>-1</c> when the prose names none. It is read from
    /// the same admission capture the prose is written from, so the sentence and the machine field
    /// can never disagree.
    /// </summary>
    internal int MaximumAmount { get; }
}
