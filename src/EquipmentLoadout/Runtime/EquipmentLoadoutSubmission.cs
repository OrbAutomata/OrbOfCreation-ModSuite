using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct EquipmentLoadoutState
{
    internal EquipmentLoadoutState(int equippedStacks, int maximumStacks,
        int usedSlots, int maximumSlots, int typeUsedSlots, int typeMaximumSlots,
        bool usageAffordable, int maximumAffordableStacks)
    {
        EquippedStacks = equippedStacks;
        MaximumStacks = maximumStacks;
        UsedSlots = usedSlots;
        MaximumSlots = maximumSlots;
        TypeUsedSlots = typeUsedSlots;
        TypeMaximumSlots = typeMaximumSlots;
        UsageAffordable = usageAffordable;
        MaximumAffordableStacks = maximumAffordableStacks;
    }
    internal int EquippedStacks { get; }
    internal int MaximumStacks { get; }
    internal int UsedSlots { get; }
    internal int MaximumSlots { get; }
    internal int TypeUsedSlots { get; }
    internal int TypeMaximumSlots { get; }
    internal bool UsageAffordable { get; }
    internal int MaximumAffordableStacks { get; }
}

internal readonly struct EquipmentLoadoutSubmission
{
    internal EquipmentLoadoutSubmission(NativeActionOutcome outcome, int maximumAmount = -1)
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

    internal static EquipmentLoadoutSubmission Reject(
        ServiceActionResultCode code, string reason, int maximumAmount = -1) =>
        new(NativeActionOutcome.Reject(code, reason), maximumAmount);
}
