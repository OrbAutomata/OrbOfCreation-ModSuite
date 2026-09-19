using OrbModding.Common;

namespace OrbAutomata;

internal enum AlchemyLoadoutPreflight
{
    Proceeded = 0, LifecycleReplaced, ContractUnavailable, WrongThread,
    IdentityUnavailable, WrongDomain, NotDiscovered, AlreadyInRequestedState,
    LoadoutFull, UsageUnavailable,
    MutationPermitUnavailable, PostCommitFault, VerificationFailed,
}

internal enum AlchemyLoadoutNativeStage { None = 0, NativeCallback = 1, Verification = 2 }

internal readonly struct AlchemyLoadoutSubmission
{
    internal AlchemyLoadoutSubmission(AlchemyLoadoutPreflight preflight,
        AlchemyLoadoutNativeStage stage, NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome, string reason, int maximumAmount = -1)
    {
        Preflight = preflight; Stage = stage; Outcome = outcome;
        CallOutcome = callOutcome; Reason = reason ?? string.Empty;
        MaximumAmount = maximumAmount;
    }

    internal AlchemyLoadoutPreflight Preflight { get; }
    internal AlchemyLoadoutNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// The ceiling the refusal prose names, or <c>-1</c> when the prose names none. It is read from
    /// the same admission capture the prose is written from, so the sentence and the machine field
    /// can never disagree.
    /// </summary>
    internal int MaximumAmount { get; }

    internal bool Verified => Preflight == AlchemyLoadoutPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static AlchemyLoadoutSubmission Reject(
        AlchemyLoadoutPreflight preflight, string reason, int maximumAmount = -1) =>
        new(preflight, AlchemyLoadoutNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, reason, maximumAmount);
}
