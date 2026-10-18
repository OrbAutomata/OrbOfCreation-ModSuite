using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct CraftingPlayerAction
{
    internal CraftingPlayerAction(Guid recipeId, long lifecycleEpoch, int amount, bool max)
    {
        if (recipeId == Guid.Empty)
            throw new ArgumentException("A crafting recipe UUID is required.", nameof(recipeId));
        if (amount < 1)
            throw new ArgumentOutOfRangeException(nameof(amount), "A craft amount is at least 1.");
        RecipeId = recipeId;
        LifecycleEpoch = lifecycleEpoch;
        Amount = amount;
        Max = max;
    }

    internal Guid RecipeId { get; }
    internal long LifecycleEpoch { get; }
    internal int Amount { get; }
    internal bool Max { get; }
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
        NativeActionOutcome outcome,
        Guid recipeId,
        CraftingPlayerPostcondition postcondition = CraftingPlayerPostcondition.None,
        BigDouble crafted = default)
    {
        Outcome = outcome;
        RecipeId = recipeId;
        Postcondition = postcondition;
        Crafted = crafted;
    }

    internal NativeActionOutcome Outcome { get; }
    internal Guid RecipeId { get; }
    internal CraftingPlayerPostcondition Postcondition { get; }
    internal BigDouble Crafted { get; }

    internal static CraftingPlayerSubmission Reject(
        in CraftingPlayerAction action,
        ServiceActionResultCode code,
        string reason) =>
        new(NativeActionOutcome.Reject(code, reason), action.RecipeId);
}

internal static class CraftingPlayerActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(7701, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(7703, "wrong_thread");
    internal static readonly ServiceActionResultCode RecipeUnavailable = new(7704, "recipe_unavailable");
    internal static readonly ServiceActionResultCode NotVisible = new(7705, "not_visible");
    internal static readonly ServiceActionResultCode PageRelationAmbiguous = new(7706, "page_relation_ambiguous");
    internal static readonly ServiceActionResultCode QueueFull = new(7708, "queue_full");
    internal static readonly ServiceActionResultCode Unaffordable = new(7709, "unaffordable");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7710, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(7711, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(7712, "verification_failed");
}
