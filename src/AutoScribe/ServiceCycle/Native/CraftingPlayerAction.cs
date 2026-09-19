using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct CraftingPlayerAction
{
    internal CraftingPlayerAction(Guid recipeId, long lifecycleEpoch)
    {
        if (recipeId == Guid.Empty)
            throw new ArgumentException("A crafting recipe UUID is required.", nameof(recipeId));
        RecipeId = recipeId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal Guid RecipeId { get; }
    internal long LifecycleEpoch { get; }
}

internal enum CraftingPlayerPreflight
{
    Proceeded = 0,
    LifecycleReplaced = 1,
    ContractUnavailable = 2,
    WrongThread = 4,
    RecipeUnavailable = 5,
    NotVisible = 6,
    PageRelationAmbiguous = 7,
    InvalidPurchaseAmount = 8,
    QueueFull = 9,
    Unaffordable = 10,
    MutationPermitUnavailable = 11,
    PostCommitFault = 12,
    VerificationFailed = 13,
}

internal enum CraftingPlayerNativeStage
{
    None = 0,
    DirectExecute = 1,
    Payment = 2,
    Construction = 3,
    Initiation = 4,
    Admission = 5,
    Verification = 6,
}

internal enum CraftingPlayerPostcondition
{
    None = 0,
    DirectEffectAdvanced = 1,
    InstanceQuantityIncreased = 2,
    InstantCompleted = 3,
    QueueAdmitted = 4,
}

internal readonly struct CraftingPlayerSubmission
{
    internal CraftingPlayerSubmission(
        Guid recipeId,
        CraftingPlayerPreflight preflight,
        CraftingPlayerNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        CraftingPlayerPostcondition postcondition = CraftingPlayerPostcondition.None)
    {
        RecipeId = recipeId;
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        Postcondition = postcondition;
    }

    internal Guid RecipeId { get; }
    internal CraftingPlayerPreflight Preflight { get; }
    internal CraftingPlayerNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal CraftingPlayerPostcondition Postcondition { get; }
    internal bool Verified =>
        Preflight == CraftingPlayerPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static CraftingPlayerSubmission Reject(
        in CraftingPlayerAction action,
        CraftingPlayerPreflight preflight,
        string reason) =>
        new(
            action.RecipeId,
            preflight,
            CraftingPlayerNativeStage.None,
            default,
            default,
            reason);
}

internal static class CraftingPlayerActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(7701);
    internal static readonly ServiceActionResultCode WrongThread = new(7703);
    internal static readonly ServiceActionResultCode RecipeUnavailable = new(7704);
    internal static readonly ServiceActionResultCode NotVisible = new(7705);
    internal static readonly ServiceActionResultCode PageRelationAmbiguous = new(7706);
    internal static readonly ServiceActionResultCode InvalidPurchaseAmount = new(7707);
    internal static readonly ServiceActionResultCode QueueFull = new(7708);
    internal static readonly ServiceActionResultCode Unaffordable = new(7709);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7710);
    internal static readonly ServiceActionResultCode PostCommitFault = new(7711);
    internal static readonly ServiceActionResultCode VerificationFailed = new(7712);
}
