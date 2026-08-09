using OrbModding.Common;

namespace OrbAutomata;

internal enum RitualLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    NotDiscovered,
    AlreadyInRequestedState,
    LevelLocked,
    LevelOutOfRange,
    BattleAlreadyActive,
    NoBattleActive,
    WrongActiveRitual,
    Unaffordable,
    NoDurationEffect,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum RitualLifecycleNativeStage
{
    None = 0,
    Payment = 1,
    NativeCallback = 2,
    Verification = 3,
}

internal readonly struct RitualLifecycleSubmission
{
    internal RitualLifecycleSubmission(
        RitualLifecyclePreflight preflight,
        RitualLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        int minimumAmount = -1,
        int maximumAmount = -1)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
    }

    internal RitualLifecyclePreflight Preflight { get; }
    internal RitualLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// The bounds the refusal prose names, or <c>-1</c> when it names none. Both are read from the
    /// same live capture the sentence is written from, so the prose and the machine fields cannot
    /// disagree.
    /// </summary>
    internal int MinimumAmount { get; }

    internal int MaximumAmount { get; }

    internal bool Verified => Preflight == RitualLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static RitualLifecycleSubmission Reject(
        RitualLifecyclePreflight preflight,
        string reason) =>
        new(preflight, RitualLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);

    internal static RitualLifecycleSubmission RejectLevelOutOfRange(
        string reason,
        int minimumAmount,
        int maximumAmount) =>
        new(RitualLifecyclePreflight.LevelOutOfRange, RitualLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason,
            minimumAmount, maximumAmount);
}
