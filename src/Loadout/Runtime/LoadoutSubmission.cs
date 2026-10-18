using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct LoadoutSubmission
{
    internal LoadoutSubmission(NativeActionOutcome outcome,
        int minimumSlot = -1, int maximumSlot = -1)
    {
        Outcome = outcome;
        MinimumSlot = minimumSlot;
        MaximumSlot = maximumSlot;
    }

    internal NativeActionOutcome Outcome { get; }

    /// <summary>Both bounds of the live snapshot list, on the refusal whose sentence names them.</summary>
    internal int MinimumSlot { get; }

    internal int MaximumSlot { get; }

    internal static LoadoutSubmission Reject(ServiceActionResultCode code, string reason) =>
        new(NativeActionOutcome.Reject(code, reason));

    internal static LoadoutSubmission RejectSlotOutOfRange(
        string reason, int minimumSlot, int maximumSlot) =>
        new(NativeActionOutcome.Reject(LoadoutActionResultCodes.SlotOutOfRange, reason),
            minimumSlot, maximumSlot);
}
