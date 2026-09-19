using OrbModding.Common;

namespace OrbAutomata;

internal enum LoadoutPreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    WrongTargetType,
    AlreadyInRequestedState,
    SwitchBlocked,
    EntryUnavailable,
    SlotOutOfRange,
    SlotEmpty,
    SlotOccupied,
    ActiveSectionEmpty,
    NameOutOfRange,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum LoadoutNativeStage { None = 0, NativeCallback = 1, Verification = 2 }

internal readonly struct LoadoutSubmission
{
    internal LoadoutSubmission(LoadoutPreflight preflight, LoadoutNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome, string reason,
        int minimumSlot = -1, int maximumSlot = -1)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        MinimumSlot = minimumSlot;
        MaximumSlot = maximumSlot;
    }

    internal LoadoutPreflight Preflight { get; }
    internal LoadoutNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>Both bounds of the live snapshot list, on the refusal whose sentence names them.</summary>
    internal int MinimumSlot { get; }

    internal int MaximumSlot { get; }

    internal bool Verified => Preflight == LoadoutPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static LoadoutSubmission Reject(LoadoutPreflight preflight, string reason) =>
        new(preflight, LoadoutNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);

    internal static LoadoutSubmission RejectSlotOutOfRange(
        string reason, int minimumSlot, int maximumSlot) =>
        new(LoadoutPreflight.SlotOutOfRange, LoadoutNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason,
            minimumSlot, maximumSlot);
}
