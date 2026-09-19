using OrbModding.Common;

namespace OrbAutomata;

internal enum EquipmentLoadoutPreflight
{
    Proceeded = 0, ContractUnavailable = 1, WrongThread = 3,
    LifecycleReplaced = 4, IdentityUnavailable = 5, NotCreated = 6,
    AlreadyInRequestedState = 7, LoadoutFull = 8, EquipmentTypeFull = 9,
    UsageUnaffordable = 10, MultiBuyUnavailable = 11, MutationPermitUnavailable = 12,
    PostCommitFault = 13, VerificationFailed = 14, AmountUnavailable = 15,
}

internal enum EquipmentLoadoutNativeStage { None = 0, NativeCallback = 1, Verification = 2 }

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
    internal EquipmentLoadoutSubmission(EquipmentLoadoutPreflight preflight,
        EquipmentLoadoutNativeStage stage, NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome, string reason, int maximumAmount = -1)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        MaximumAmount = maximumAmount;
    }
    internal EquipmentLoadoutPreflight Preflight { get; }
    internal EquipmentLoadoutNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// The ceiling the refusal prose names, or <c>-1</c> when the prose names none. It is read from
    /// the same admission capture the prose is written from, so the sentence and the machine field
    /// can never disagree.
    /// </summary>
    internal int MaximumAmount { get; }

    internal bool Verified => Preflight == EquipmentLoadoutPreflight.Proceeded && Outcome == NativeMutationOutcome.Verified;
    internal static EquipmentLoadoutSubmission Reject(
        EquipmentLoadoutPreflight preflight, string reason, int maximumAmount = -1) =>
        new(preflight, EquipmentLoadoutNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, reason, maximumAmount);
}
