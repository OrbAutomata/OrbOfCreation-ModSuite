using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum GenericLevelPreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    WrongDomain,
    Undiscovered,
    Hidden,
    Unavailable,
    CannotLevel,
    BonusUnavailable,
    ResourcesHidden,
    Unaffordable,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum GenericLevelNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

/// <summary>One resource line of what a level press actually asked for.</summary>
internal readonly struct GenericLevelCharge
{
    internal GenericLevelCharge(Guid resourceId, BigDouble amount)
    {
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal readonly struct GenericLevelSubmission
{
    private readonly GenericLevelCharge[]? _charges;

    internal GenericLevelSubmission(
        GenericLevelPreflight preflight,
        GenericLevelNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        int levelsCharged = 0,
        GenericLevelCharge[]? charges = null)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        LevelsCharged = levelsCharged;
        _charges = charges;
    }

    internal GenericLevelPreflight Preflight { get; }
    internal GenericLevelNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// How many levels this press bought, and what the game asked for them.
    /// </summary>
    /// <remarks>
    /// A ladder prices every rung separately, so the first rung's price answers for the whole ask
    /// only when the ask is one. A round bought five rune levels, read <c>free: yes</c> off the
    /// ladder's free first rung, and watched Time Advancements fall anyway. These are the prices
    /// the game itself named immediately before each payment, on the frame that made it — not a
    /// balance comparison, and not evidence: the postcondition is still the one level sentinel.
    /// </remarks>
    internal int LevelsCharged { get; }
    internal ReadOnlySpan<GenericLevelCharge> Charges =>
        _charges is null ? default : _charges.AsSpan(0, _charges.Length);

    /// <summary>Whether every level this press bought asked for nothing.</summary>
    internal bool ChargedNothing
    {
        get
        {
            var charges = Charges;
            for (var index = 0; index < charges.Length; index++)
                if (charges[index].Amount > BigDouble.Zero) return false;
            return true;
        }
    }

    internal bool Verified => Preflight == GenericLevelPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static GenericLevelSubmission Reject(GenericLevelPreflight preflight, string reason) =>
        new(preflight, GenericLevelNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
