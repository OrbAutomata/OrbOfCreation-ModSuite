using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for visible crafting-instance controls.</summary>
internal sealed class CraftingInstanceLifecycleGameAction
    : NativeGameAction<CraftingInstanceLifecycleNativeBindings>
{
    private const string Screen = "Workshop > Crafting";

    private static readonly GameActionCodes Codes = new(
        CraftingInstanceLifecycleActionResultCodes.WrongThread,
        CraftingInstanceLifecycleActionResultCodes.ContractUnavailable,
        CraftingInstanceLifecycleActionResultCodes.MutationPermitUnavailable,
        CraftingInstanceLifecycleActionResultCodes.PostCommitFault,
        CraftingInstanceLifecycleActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal CraftingInstanceLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Crafting", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out CraftingInstanceLifecycleNativeBindings? bindings, out string reason) =>
                CraftingInstanceLifecycleNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal CraftingInstanceLifecycleSubmission Submit(
        in CraftingInstanceLifecycleAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch,
            static outcome => new CraftingInstanceLifecycleSubmission(outcome),
            native => Press(in request, native));
    }

    private CraftingInstanceLifecycleSubmission Press(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecycleNativeBindings native)
    {
        var resolution = _registry.Resolve(action.RecipeId, native.RecipeType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            return CraftingInstanceLifecycleSubmission.Refused(
                CraftingInstanceLifecycleActionResultCodes.IdentityUnavailable,
                resolution.IsResolved ? GameActionAnswer.Replaced("recipe") : resolution.Cause);
        var recipe = resolution.Value!;
        if (!TryResolvePage(native, recipe, out var page, out var pageReason))
            return CraftingInstanceLifecycleSubmission.Refused(
                CraftingInstanceLifecycleActionResultCodes.PageRelationAmbiguous, pageReason);
        var queue = native.PageQueue(page!);
        var automation = native.PageAutomation(page!);
        var kind = action.Kind;
        var instance = native.QueueInstance(
            kind == CraftingInstanceLifecycleActionKind.CancelManual ? queue : automation, recipe);
        var admission = AdmitInstance(in action, native, recipe, automation, instance);
        if (admission.HasValue) return admission.Value;
        if (!TryPermit(out var refusal)) return new CraftingInstanceLifecycleSubmission(refusal);

        var request = action;
        var before = kind == CraftingInstanceLifecycleActionKind.CancelManual ||
            instance is null ? 0 : native.AutomationQuantity(instance);
        var outcome = Execute(EntityIdentityFormatter.PlayerName(action.RecipeId),
            "The requested crafting-instance transition is visible.", false,
            () => OutcomeObserved(in request, native, recipe, queue, automation, instance, before),
            () =>
            {
                switch (kind)
                {
                    case CraftingInstanceLifecycleActionKind.Automate:
                        native.Automate(automation, recipe, 1);
                        break;
                    case CraftingInstanceLifecycleActionKind.CancelAutomation:
                        native.RemoveAutomation(automation, instance!, -1);
                        break;
                    case CraftingInstanceLifecycleActionKind.CancelManual:
                        native.Cancel(instance!);
                        native.QueueRemove(queue, instance!);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported crafting-instance control.");
                }
            },
            static (_, observed) => observed);
        return outcome.Verified
            ? new CraftingInstanceLifecycleSubmission(outcome)
            : new CraftingInstanceLifecycleSubmission(outcome,
                SideEffect(in action, native, recipe, automation, instance, before));
    }

    private static CraftingInstanceLifecycleSubmission? AdmitInstance(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecycleNativeBindings native,
        object recipe,
        object automation,
        object? instance)
    {
        if (action.Kind == CraftingInstanceLifecycleActionKind.Automate)
        {
            if (!native.RecipeVisible(recipe))
                return CraftingInstanceLifecycleSubmission.Refused(
                    CraftingInstanceLifecycleActionResultCodes.NotVisible,
                    "This recipe is not visible yet.");
            if (instance is null && !native.QueueHasRoom(automation))
                return CraftingInstanceLifecycleSubmission.Refused(
                    CraftingInstanceLifecycleActionResultCodes.AutomationFull,
                    "The automated crafting list has no empty slot.");
            return null;
        }
        if (!ValidInstance(native, instance, action.RecipeId,
                action.Kind == CraftingInstanceLifecycleActionKind.CancelAutomation))
            return CraftingInstanceLifecycleSubmission.Refused(
                CraftingInstanceLifecycleActionResultCodes.InstanceUnavailable,
                action.Kind == CraftingInstanceLifecycleActionKind.CancelManual
                    ? "This recipe is not in the manual crafting queue."
                    : "This recipe is not in the automated crafting list.");
        return null;
    }

    /// <summary>
    /// A failing transition still leaves whatever the native call already wrote. Re-reading the
    /// automation quantity is what lets a refusal name the damage instead of hiding it.
    /// </summary>
    private static CraftingInstanceLifecycleSideEffect SideEffect(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecycleNativeBindings native,
        object recipe,
        object automation,
        object? instance,
        int before)
    {
        if (action.Kind == CraftingInstanceLifecycleActionKind.CancelManual) return default;
        var automate = action.Kind == CraftingInstanceLifecycleActionKind.Automate;
        if (!NativeMutationVerifier.TryReadAfterCall(
                () => automate ? native.QueueInstance(automation, recipe) : instance, out var entry) ||
            entry is null ||
            !NativeMutationVerifier.TryReadAfterCall(() => native.AutomationQuantity(entry), out var after) ||
            after == before)
            return default;
        return CraftingInstanceLifecycleSideEffect.Automation(before, after);
    }

    private static bool OutcomeObserved(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecycleNativeBindings native,
        object recipe,
        object queue,
        object automation,
        object? instance,
        int before)
    {
        if (action.Kind == CraftingInstanceLifecycleActionKind.CancelManual)
            return instance is not null && !ContainsReference(native.QueueValues(queue), instance);
        if (action.Kind == CraftingInstanceLifecycleActionKind.Automate)
        {
            var automated = native.QueueInstance(automation, recipe);
            return ValidInstance(native, automated, action.RecipeId, expectedAuto: true) &&
                native.AutomationQuantity(automated!) > before;
        }
        if (instance is null) return false;
        // A cancel that empties the entry makes the game drop it from the automation list, so
        // absence is the same observation as a smaller quantity.
        return !ContainsReference(native.QueueValues(automation), instance) ||
            native.AutomationQuantity(instance) < before;
    }

    private static bool TryResolvePage(
        CraftingInstanceLifecycleNativeBindings native,
        object recipe,
        out object? page,
        out string reason)
    {
        page = null;
        var pages = native.Pages();
        for (var index = 0; index < pages.Length; index++)
        {
            var candidate = pages.GetValue(index);
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
                reason = "This recipe appears on more than one crafting page.";
                return false;
            }
            page = candidate;
        }
        if (page is null)
        {
            reason = "This recipe is not on a loaded crafting page.";
            return false;
        }
        if (!ReferenceEquals(native.PageMainType(page), native.RecipeMainType(recipe)))
        {
            reason = "This recipe does not match its loaded crafting page.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool ValidInstance(
        CraftingInstanceLifecycleNativeBindings native,
        object? instance,
        Guid recipeId,
        bool expectedAuto) =>
        instance is not null && instance.GetType() == native.InstanceType &&
        native.InstanceRecipe(instance) == recipeId && native.IsAuto(instance) == expectedAuto;

    private static bool ContainsReference(IList values, object instance)
    {
        for (var index = 0; index < values.Count; index++)
            if (ReferenceEquals(values[index], instance)) return true;
        return false;
    }
}
