using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed partial class AutoScribeOneShotCraftGameAction
{
    internal CraftingPlayerSubmission Submit(in CraftingPlayerAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.WrongThread,
                "Crafting actions are bound to Unity thread " + _mainThreadId + ".");
        if (_playerBindings is not { } native)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.ContractUnavailable,
                _playerBindingFailure.Length == 0
                    ? "The lifecycle-scoped player crafting binding set is unavailable."
                    : _playerBindingFailure);

        long liveLifecycle;
        try
        {
            liveLifecycle = _readLifecycleEpoch();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.LifecycleReplaced,
                "The live lifecycle could not be read: " + ex.GetBaseException().Message);
        }
        if (liveLifecycle <= 0 || liveLifecycle != action.LifecycleEpoch)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; live lifecycle is " + liveLifecycle + ".");

        var resolution = _registry.Resolve(action.RecipeId, native.RecipeType);
        if (!resolution.IsResolved)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.RecipeUnavailable,
                resolution.Format());
        var recipe = resolution.Value!;
        try
        {
            if (!native.RecipeVisible(recipe))
                return CraftingPlayerSubmission.Reject(
                    in action,
                    CraftingPlayerPreflight.NotVisible,
                    "The game is not showing this recipe yet.");
            if (!TryFindPage(native, recipe, out var page, out var pageReason))
                return CraftingPlayerSubmission.Reject(
                    in action,
                    CraftingPlayerPreflight.PageRelationAmbiguous,
                    pageReason);
            return page is null
                ? SubmitDirect(in action, native, recipe)
                : SubmitQueued(in action, native, recipe, page);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.ContractUnavailable,
                "Crafting preflight failed before mutation: " + ex.GetBaseException().Message);
        }
    }

    private CraftingPlayerSubmission SubmitDirect(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object recipe)
    {
        if (native.RecipeTime(recipe) > 0d)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.PageRelationAmbiguous,
                "A timed recipe had no stable authored UICraftingPage relation; direct execution was refused.");
        var purchase = native.RecipePurchaseAmount(recipe, BigDouble.One);
        if (purchase <= BigDouble.Zero)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.InvalidPurchaseAmount,
                "CraftingRecipeSO.GetPurchaseQuantity(1) returned a non-positive amount.");
        if (!native.RecipeCanBuy(recipe))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.Unaffordable,
                "The game refuses this craft right now.");
        var revisionBefore = native.RecipeEffectRevision(recipe);
        if (!TryPlayerCraftingPermit(in action, out var permitFailure)) return permitFailure;

        try
        {
            native.RecipeExecute(recipe);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (RecipeEffectAdvancedBestEffort(native, recipe, revisionBefore))
                return Verified(
                    in action,
                    1,
                    "The exact recipe advanced its native craft-effect publication before Execute threw.",
                    CraftingPlayerPostcondition.DirectEffectAdvanced);
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.PostCommitFault,
                CraftingPlayerNativeStage.DirectExecute,
                NativeMutationOutcome.ExecutionThrew,
                1,
                "CraftingRecipeSO.Execute threw after the direct composite began: " +
                ex.GetBaseException().Message);
        }
        int revisionAfter;
        try
        {
            revisionAfter = native.RecipeEffectRevision(recipe);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.PostCommitFault,
                CraftingPlayerNativeStage.Verification,
                NativeMutationOutcome.AfterCaptureFailed,
                1,
                "The native craft-effect publication could not be read after Execute: " +
                ex.GetBaseException().Message);
        }
        if (revisionAfter <= revisionBefore)
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.VerificationFailed,
                CraftingPlayerNativeStage.Verification,
                NativeMutationOutcome.PostconditionFailed,
                1,
                "CraftingRecipeSO.Execute did not advance the native craft-effect publication.");
        return Verified(
            in action,
            1,
            "The exact recipe advanced its native craft-effect publication.",
            CraftingPlayerPostcondition.DirectEffectAdvanced);
    }

    private CraftingPlayerSubmission SubmitQueued(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object recipe,
        object page)
    {
        var queue = native.PageQueue(page);
        var mainType = native.PageMainType(page);
        if (!ReferenceEquals(mainType, native.RecipeMainType(recipe)))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.PageRelationAmbiguous,
                "The authored page main type does not match CraftingRecipeSO.GetMainType().");
        var mode = native.PageCraftMode(page);
        if (mode is not 0 and not 1)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.PageRelationAmbiguous,
                "UICraftingPage craftMode was neither native stack mode 0 nor new-instance mode 1.");
        var previous = native.QueueQuantity(queue, recipe);
        var purchase = native.RecipePurchaseAmount(recipe, previous);
        if (purchase <= BigDouble.Zero)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.InvalidPurchaseAmount,
                "CraftingRecipeSO.GetPurchaseQuantity(previous) returned a non-positive amount.");
        var requestedTotal = previous + (purchase < BigDouble.One ? BigDouble.One : purchase);
        if (!native.RecipeCanBuyAt(recipe, requestedTotal))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.Unaffordable,
                "The game refuses to queue another of this craft right now.");
        var totalCost = native.RecipeTotalCost(recipe, previous, purchase);
        if (!native.CostHasEnough(totalCost))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.Unaffordable,
                "GetTotalCost(previous,purchase).HasEnough() refused the exact queued recipe.");

        var existing = mode == 0 ? FindInstance(native, queue, action.RecipeId) : null;
        if (existing is null && !native.QueueHasRoom(queue))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.QueueFull,
                "The authored manual crafting queue has no empty spot for a new instance.");
        var previousInstanceQuantity = existing is null
            ? BigDouble.Zero
            : native.InstanceQuantity(existing);
        if (!TryPlayerCraftingPermit(in action, out var permitFailure)) return permitFailure;

        var stage = CraftingPlayerNativeStage.Payment;
        var calls = 1;
        object? created = null;
        var instant = false;
        var admissionKnown = false;
        try
        {
            native.RecipePurchase(recipe, purchase, previous);
            if (existing is not null)
            {
                stage = CraftingPlayerNativeStage.Admission;
                calls = 2;
                native.InstanceAddQuantity(existing, purchase);
                return VerifyStacked(
                    in action,
                    native,
                    existing,
                    previousInstanceQuantity,
                    calls);
            }

            stage = CraftingPlayerNativeStage.Construction;
            calls = 2;
            created = native.ConstructInstance(recipe, purchase);
            calls = 3;
            instant = native.InstanceIsInstant(created);
            admissionKnown = true;
            if (instant)
            {
                stage = CraftingPlayerNativeStage.Admission;
                calls = 4;
                native.InstanceInstant(created);
                return VerifyInstant(in action, native, created, calls);
            }

            stage = CraftingPlayerNativeStage.Initiation;
            calls = 4;
            native.InstanceInitiate(created);
            stage = CraftingPlayerNativeStage.Admission;
            calls = 5;
            native.QueueAdd(queue, created);
            return VerifyQueued(
                in action,
                native,
                queue,
                created,
                calls);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var landed = existing is not null
                ? InstanceQuantityBestEffort(native, existing) > previousInstanceQuantity
                : created is not null && admissionKnown && (instant
                    ? InstanceExpiredBestEffort(native, created)
                    : ContainsExactInstanceBestEffort(
                        native, queue, created));
            if (landed)
            {
                return new CraftingPlayerSubmission(
                    action.RecipeId,
                    CraftingPlayerPreflight.Proceeded,
                    CraftingPlayerNativeStage.Verification,
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(calls, 1, 1),
                    "The exact queued outcome was observed after native code threw: " +
                    ex.GetBaseException().Message,
                    existing is not null
                        ? CraftingPlayerPostcondition.InstanceQuantityIncreased
                        : instant
                            ? CraftingPlayerPostcondition.InstantCompleted
                            : CraftingPlayerPostcondition.QueueAdmitted);
            }
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.PostCommitFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                calls,
                "Queued crafting failed after payment began: " + ex.GetBaseException().Message);
        }
    }

    private CraftingPlayerSubmission VerifyStacked(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object instance,
        BigDouble previous,
        int calls)
    {
        if (native.InstanceQuantity(instance) <= previous)
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.VerificationFailed,
                CraftingPlayerNativeStage.Verification,
                NativeMutationOutcome.PostconditionFailed,
                calls,
                "The exact crafting instance quantity did not increase.");
        return Verified(in action, calls,
            "The exact crafting instance quantity increased.",
            CraftingPlayerPostcondition.InstanceQuantityIncreased);
    }

    private CraftingPlayerSubmission VerifyInstant(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object instance,
        int calls)
    {
        if (!native.InstanceExpired(instance))
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.VerificationFailed,
                CraftingPlayerNativeStage.Verification,
                NativeMutationOutcome.PostconditionFailed,
                calls,
                "The exact instant crafting instance did not reach native completion.");
        return Verified(in action, calls,
            "The exact instant crafting instance reached native completion.",
            CraftingPlayerPostcondition.InstantCompleted);
    }

    private CraftingPlayerSubmission VerifyQueued(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object queue,
        object instance,
        int calls)
    {
        if (!ContainsExactInstance(native, queue, instance))
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.VerificationFailed,
                CraftingPlayerNativeStage.Verification,
                NativeMutationOutcome.PostconditionFailed,
                calls,
                "The exact crafting instance was not admitted to the authored queue.");
        return Verified(in action, calls,
            "The exact crafting instance was admitted to the authored queue.",
            CraftingPlayerPostcondition.QueueAdmitted);
    }

    private static CraftingPlayerSubmission Verified(
        in CraftingPlayerAction action,
        int calls,
        string reason,
        CraftingPlayerPostcondition postcondition) =>
        new CraftingPlayerSubmission(
            action.RecipeId,
            CraftingPlayerPreflight.Proceeded,
            CraftingPlayerNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(calls, 1, 1),
            reason,
            postcondition);

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
        var values = native.QueueValues(queue);
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is not null && candidate.GetType() == native.InstanceType &&
                native.InstanceRecipe(candidate) == recipeId)
                return candidate;
        }
        return null;
    }

    private static bool ContainsExactInstance(
        CraftingPlayerNativeBindings native,
        object queue,
        object instance)
    {
        var values = native.QueueValues(queue);
        for (var index = 0; index < values.Count; index++)
            if (ReferenceEquals(values[index], instance))
                return true;
        return false;
    }

    private static bool ContainsExactInstanceBestEffort(
        CraftingPlayerNativeBindings native,
        object queue,
        object instance)
    {
        try
        {
            return ContainsExactInstance(native, queue, instance);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private static BigDouble InstanceQuantityBestEffort(
        CraftingPlayerNativeBindings native,
        object instance)
    {
        try
        {
            return native.InstanceQuantity(instance);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return BigDouble.NaN;
        }
    }

    private static bool InstanceExpiredBestEffort(
        CraftingPlayerNativeBindings native,
        object instance)
    {
        try
        {
            return native.InstanceExpired(instance);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private static bool RecipeEffectAdvancedBestEffort(
        CraftingPlayerNativeBindings native,
        object recipe,
        int before)
    {
        try
        {
            return native.RecipeEffectRevision(recipe) > before;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private bool TryPlayerCraftingPermit(
        in CraftingPlayerAction action,
        out CraftingPlayerSubmission failure)
    {
        if (TryCaptureMutationPermit(out var reason))
        {
            failure = default;
            return true;
        }
        failure = CraftingPlayerSubmission.Reject(
            in action,
            CraftingPlayerPreflight.MutationPermitUnavailable,
            reason);
        return false;
    }

    private static CraftingPlayerSubmission PlayerCraftingFault(
        in CraftingPlayerAction action,
        CraftingPlayerPreflight preflight,
        CraftingPlayerNativeStage stage,
        NativeMutationOutcome outcome,
        int calls,
        string reason)
    {
        var exactReason = "One-shot crafting " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.RecipeId) + ": " + reason;
        return new CraftingPlayerSubmission(
            action.RecipeId,
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(calls, 1, 0),
            exactReason);
    }
}
