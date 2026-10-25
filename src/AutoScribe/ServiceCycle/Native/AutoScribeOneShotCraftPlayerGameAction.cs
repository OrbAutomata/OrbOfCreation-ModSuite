using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The player's own craft press on Scholar > Scribe: the page's queued craft when the recipe is
/// drawn on one authored page, otherwise the recipe's direct execution under a pinned multi-buy.
/// </summary>
internal sealed class AutoScribeOneShotCraftPlayerGameAction
    : NativeGameAction<CraftingPlayerNativeBindings>
{
    private const string Screen = "Scholar > Scribe";
    private const int MxMultiBuy = 1000;

    private static readonly GameActionCodes Codes = new(
        CraftingPlayerActionResultCodes.WrongThread,
        CraftingPlayerActionResultCodes.ContractUnavailable,
        CraftingPlayerActionResultCodes.MutationPermitUnavailable,
        CraftingPlayerActionResultCodes.PostCommitFault,
        CraftingPlayerActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal AutoScribeOneShotCraftPlayerGameAction(
        TypedRegistryResolver registry,
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure)
        : base("One-shot crafting", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out CraftingPlayerNativeBindings? bindings, out string reason) =>
                CraftingPlayerNativeBindings.TryCreate(out bindings, out reason))
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    internal CraftingPlayerSubmission Submit(in CraftingPlayerAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch,
            outcome => new CraftingPlayerSubmission(outcome, request.RecipeId),
            native =>
            {
                if (!_registry.TryResolveCurrent(request.RecipeId, native.RecipeType,
                        out var recipe, out var reason))
                    return CraftingPlayerSubmission.Reject(in request,
                        CraftingPlayerActionResultCodes.RecipeUnavailable, reason);
                if (!native.Crafting.RecipeVisible(recipe))
                    return CraftingPlayerSubmission.Reject(in request,
                        CraftingPlayerActionResultCodes.NotVisible,
                        "The game is not showing this recipe yet.");
                if (!TryFindPage(native, recipe, out var page, out var pageReason))
                    return CraftingPlayerSubmission.Reject(in request,
                        CraftingPlayerActionResultCodes.PageRelationAmbiguous, pageReason);
                if (page is not null) return SubmitQueued(in request, native, recipe, page);
                if (!NativeMultiBuyScope.TryEnter(
                        request.Max ? MxMultiBuy : 1, out var scope, out var scopeReason))
                    return CraftingPlayerSubmission.Reject(in request,
                        CraftingPlayerActionResultCodes.ContractUnavailable,
                        "The game's multi-buy could not be held for this craft: " + scopeReason);
                using (scope)
                    return SubmitDirect(in request, native, recipe);
            });
    }

    private CraftingPlayerSubmission SubmitDirect(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object recipe)
    {
        if (native.RecipeTime(recipe) > 0d)
            return CraftingPlayerSubmission.Reject(in action,
                CraftingPlayerActionResultCodes.PageRelationAmbiguous,
                "A timed recipe had no stable authored UICraftingPage relation; direct execution was refused.");
        var purchase = native.RecipePurchaseAmount(recipe, BigDouble.One);
        if (purchase <= BigDouble.Zero || !native.RecipeCanBuy(recipe))
            return CraftingPlayerSubmission.Reject(in action,
                CraftingPlayerActionResultCodes.Unaffordable,
                "The cost of this craft is not met.");
        var crafted = native.RecipeLevelled(recipe) ? BigDouble.One : purchase;
        var revisionBefore = native.RecipeEffectRevision(recipe);
        if (!TryPermit(out var refusal)) return new CraftingPlayerSubmission(refusal, action.RecipeId);

        var outcome = Execute(EntityIdentityFormatter.PlayerName(action.RecipeId),
            "The exact recipe advanced its native craft-effect publication.", revisionBefore,
            () => native.RecipeEffectRevision(recipe),
            () => native.RecipeExecute(recipe),
            static (before, after) => after > before);
        return Answer(in action, outcome, CraftingPlayerPostcondition.DirectEffectAdvanced, crafted);
    }

    private CraftingPlayerSubmission SubmitQueued(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object recipe,
        object page)
    {
        var queue = native.PageQueue(page);
        var mainType = native.PageMainType(page);
        if (!ReferenceEquals(mainType, native.Crafting.RecipeMainType(recipe)))
            return CraftingPlayerSubmission.Reject(in action,
                CraftingPlayerActionResultCodes.PageRelationAmbiguous,
                "This recipe is not the one the game draws on this page.");
        var mode = native.PageCraftMode(page);
        if (mode is not 0 and not 1)
            return CraftingPlayerSubmission.Reject(in action,
                CraftingPlayerActionResultCodes.PageRelationAmbiguous,
                "UICraftingPage craftMode was neither native stack mode 0 nor new-instance mode 1.");
        var previous = native.QueueQuantity(queue, recipe);
        var levelled = native.RecipeLevelled(recipe);
        if (!TryPressQuantity(
                native, recipe, previous, levelled && !action.Max ? 1 : MxMultiBuy,
                out var purchase, out var pressFailure))
            return CraftingPlayerSubmission.Reject(in action,
                CraftingPlayerActionResultCodes.ContractUnavailable, pressFailure);
        if (!levelled && !action.Max && purchase > new BigDouble(action.Amount, 0))
            purchase = new BigDouble(action.Amount, 0);
        if (purchase <= BigDouble.Zero)
            return CraftingPlayerSubmission.Reject(in action,
                CraftingPlayerActionResultCodes.Unaffordable,
                "The cost of another of this craft is not met.");
        var crafted = levelled ? BigDouble.One : purchase;
        var requestedTotal = previous + (purchase < BigDouble.One ? BigDouble.One : purchase);
        if (!native.Crafting.RecipeCanBuyAt(recipe, requestedTotal) ||
            !native.Crafting.CostHasEnough(native.Crafting.RecipeTotalCost(recipe, previous, purchase)))
            return CraftingPlayerSubmission.Reject(in action,
                CraftingPlayerActionResultCodes.Unaffordable,
                "The cost of another of this craft is not met.");

        var existing = mode == 0 ? FindInstance(native, queue, action.RecipeId) : null;
        if (existing is null && !native.Crafting.QueueHasRoom(queue))
            return CraftingPlayerSubmission.Reject(in action,
                CraftingPlayerActionResultCodes.QueueFull,
                "The Manual crafting queue has no empty slot.");
        var previousInstanceQuantity = existing is null
            ? BigDouble.Zero
            : native.Crafting.InstanceQuantity(existing);
        if (!TryPermit(out var refusal)) return new CraftingPlayerSubmission(refusal, action.RecipeId);

        object? created = null;
        var instant = false;
        var outcome = Execute(EntityIdentityFormatter.PlayerName(action.RecipeId),
            "The exact craft reached the Manual crafting queue.", false,
            () => existing is not null
                ? native.Crafting.InstanceQuantity(existing) > previousInstanceQuantity
                : native.Crafting.WorkObserved(queue, created, instant),
            () =>
            {
                native.Crafting.RecipePurchase(recipe, purchase, previous);
                if (existing is not null)
                {
                    native.InstanceAddQuantity(existing, purchase);
                    return;
                }
                created = native.Crafting.ConstructInstance(recipe, purchase);
                instant = native.Crafting.InstanceInstantCheck(created);
                if (instant)
                {
                    native.Crafting.InstanceInstant(created);
                    return;
                }
                native.Crafting.InstanceInitiate(created);
                native.Crafting.QueueAdd(queue, created);
            },
            static (_, landed) => landed);
        return Answer(in action, outcome,
            existing is not null
                ? CraftingPlayerPostcondition.InstanceQuantityIncreased
                : instant
                    ? CraftingPlayerPostcondition.InstantCompleted
                    : CraftingPlayerPostcondition.QueueAdmitted,
            crafted);
    }

    private static CraftingPlayerSubmission Answer(
        in CraftingPlayerAction action,
        NativeActionOutcome outcome,
        CraftingPlayerPostcondition postcondition,
        BigDouble crafted) =>
        outcome.Verified
            ? new CraftingPlayerSubmission(outcome, action.RecipeId, postcondition, crafted)
            : new CraftingPlayerSubmission(outcome, action.RecipeId);

    /// <summary>
    /// How many crafts one press buys. The game's own purchase quantity reads the global multi-buy
    /// itself, so the read runs under the multiplier the press will use.
    /// </summary>
    private static bool TryPressQuantity(
        CraftingPlayerNativeBindings native,
        object recipe,
        BigDouble previous,
        int multiBuy,
        out BigDouble quantity,
        out string reason)
    {
        quantity = BigDouble.Zero;
        if (!NativeMultiBuyScope.TryEnter(multiBuy, out var scope, out var scopeReason))
        {
            reason = "The game's multi-buy could not be held for this craft: " + scopeReason;
            return false;
        }
        using (scope)
            quantity = native.RecipePurchaseAmount(recipe, previous);
        reason = string.Empty;
        return true;
    }

    private static bool TryFindPage(
        CraftingPlayerNativeBindings native,
        object recipe,
        out object? page,
        out string reason)
    {
        page = null;
        var pages = native.Pages();
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var candidate = pages.GetValue(pageIndex);
            if (candidate is null || candidate.GetType() != native.PageType) continue;
            var recipes = native.PageRecipes(candidate);
            var contains = false;
            for (var recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
                if (ReferenceEquals(recipes[recipeIndex], recipe))
                {
                    contains = true;
                    break;
                }
            if (!contains) continue;
            if (page is not null)
            {
                reason = "The exact recipe appears on more than one authored UICraftingPage.";
                return false;
            }
            page = candidate;
        }
        reason = string.Empty;
        return true;
    }

    private static object? FindInstance(
        CraftingPlayerNativeBindings native,
        object queue,
        Guid recipeId)
    {
        var values = native.Crafting.QueueValues(queue);
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is not null && candidate.GetType() == native.InstanceType &&
                native.Crafting.InstanceRecipe(candidate) == recipeId)
                return candidate;
        }
        return null;
    }
}
