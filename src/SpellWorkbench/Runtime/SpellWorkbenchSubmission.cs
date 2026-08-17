using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum SpellWorkbenchPreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    WrongThread = 3,
    LifecycleReplaced = 4,
    IdentityUnavailable = 5,
    SelectionUnavailable = 6,
    WrongSelection = 7,
    AlreadyDiscovered = 8,
    DiscoveryUnavailable = 9,
    RecipeUnavailable = 10,
    Unaffordable = 11,
    LoadoutFull = 12,
    CompositionUnsupported = 14,
    MutationPermitUnavailable = 15,
    PostCommitFault = 16,
    VerificationFailed = 17,
    UsageRequirementsUnavailable = 18,
    UsageUnaffordable = 19,
    UniqueSpellConflict = 20,
    GlyphRequirementsUnavailable = 21,

    /// <summary>The suite's own staging write did not land, proved by reading it back.</summary>
    StagedWriteFailed = 22,

    /// <summary>More different augments than the game's augment selection holds at once.</summary>
    AugmentSlotsExceeded = 25,
}

internal enum SpellWorkbenchNativeStage
{
    None = 0,
    ClearSelection = 1,
    ApplySelection = 2,
    Discover = 3,
    Payment = 4,
    Create = 5,
    Verification = 6,
}

internal readonly struct SpellWorkbenchSubmission
{
    internal SpellWorkbenchSubmission(SpellWorkbenchPreflight preflight,
        SpellWorkbenchNativeStage stage, NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome, string reason)
    {
        if (preflight != SpellWorkbenchPreflight.Proceeded && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A spell workbench failure requires an exact reason.", nameof(reason));
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
    }

    internal SpellWorkbenchPreflight Preflight { get; }
    internal SpellWorkbenchNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == SpellWorkbenchPreflight.Proceeded && Outcome == NativeMutationOutcome.Verified;

    internal static SpellWorkbenchSubmission Reject(SpellWorkbenchPreflight preflight, string reason) =>
        new(preflight, SpellWorkbenchNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, reason);
}
