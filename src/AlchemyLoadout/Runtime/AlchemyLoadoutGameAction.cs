using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped, Unity-main-thread boundary for the ordinary alchemy list.</summary>
internal sealed class AlchemyLoadoutGameAction : NativeGameAction<AlchemyLoadoutNativeBindings>
{
    private const string Screen = "Alchemy";

    private static readonly GameActionCodes Codes = new(
        AlchemyLoadoutActionResultCodes.WrongThread,
        AlchemyLoadoutActionResultCodes.ContractUnavailable,
        AlchemyLoadoutActionResultCodes.MutationPermitUnavailable,
        AlchemyLoadoutActionResultCodes.PostCommitFault,
        AlchemyLoadoutActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;
    private readonly AlchemyGameplayDomainClassifier _classifier;

    internal AlchemyLoadoutGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null, AlchemyGameplayDomainClassifier? classifier = null)
        : base("Alchemy loadout", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out AlchemyLoadoutNativeBindings? bindings, out string reason) =>
                AlchemyLoadoutNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
        _classifier = classifier ?? new AlchemyGameplayDomainClassifier(_registry);
    }

    internal bool TryValidateStoredEntry(
        long lifecycleEpoch,
        object candidate,
        int quantity,
        out int freeUses,
        out object? usageCost,
        out string reason)
    {
        freeUses = 0;
        usageCost = null;
        if (!TryEnter(lifecycleEpoch, out var native, out var refusal))
        {
            reason = refusal.Reason;
            return false;
        }
        if (candidate is null || candidate.GetType() != native.RecipeType)
        {
            reason = "A saved Alchemy entry has the wrong native type.";
            return false;
        }
        var id = RuntimeIdentityRegistryBinding.Shared.ReadStableUuid(candidate);
        if (id == Guid.Empty)
        {
            reason = "A saved Alchemy entry has no stable identity.";
            return false;
        }
        var resolution = _registry.Resolve(id, native.RecipeType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            !ReferenceEquals(resolution.Value, candidate))
        {
            reason = EntityIdentityFormatter.PlayerName(id) +
                " is not the current Alchemy recipe instance.";
            return false;
        }
        if (!_classifier.TryInitialize(out reason)) return false;
        var classification = _classifier.ClassifyRecipe(candidate);
        if (!classification.IsMutationGrade ||
            classification.Domain != AlchemyGameplayDomain.OrdinaryAlchemy)
        {
            reason = EntityIdentityFormatter.PlayerName(id) +
                " is not an ordinary Alchemy loadout recipe.";
            return false;
        }
        if (!native.Discovered(candidate))
        {
            reason = EntityIdentityFormatter.PlayerName(id) + " has not been discovered.";
            return false;
        }
        var maximum = Math.Max(native.MaximumUses(candidate), 0);
        if (quantity <= 0 || quantity > maximum)
        {
            reason = EntityIdentityFormatter.PlayerName(id) + " stores " + quantity +
                " uses, but the live maximum is " + maximum + ".";
            return false;
        }
        freeUses = Math.Max(native.FreeUses(candidate), 0);
        usageCost = native.UsageCost(candidate);
        if (usageCost is null)
        {
            reason = "The saved Alchemy recipe's usage cost is unavailable.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    internal AlchemyLoadoutSubmission Submit(in AlchemyLoadoutAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new AlchemyLoadoutSubmission(outcome),
            native => Press(in request, native));
    }

    protected override void OnInvalidate() => _classifier.InvalidateLifecycle();

    private AlchemyLoadoutSubmission Press(in AlchemyLoadoutAction action,
        AlchemyLoadoutNativeBindings native)
    {
        if (!_classifier.TryInitialize(out var classifierReason))
            return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                classifierReason);
        var resolution = _registry.Resolve(action.RecipeId, native.RecipeType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.IdentityUnavailable,
                resolution.IsResolved ? GameActionAnswer.Replaced("alchemy recipe") : resolution.Reason);
        var recipe = resolution.Value!;
        var classification = _classifier.ClassifyRecipe(recipe);
        if (!classification.IsMutationGrade ||
            classification.Domain != AlchemyGameplayDomain.OrdinaryAlchemy)
            return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.WrongDomain,
                "This recipe is not part of the ordinary Alchemy loadout.");
        var manager = native.Manager();
        if (manager is null || manager.GetType() != native.ManagerType)
            return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                "The ordinary Alchemy manager is not available in this scene.");
        var list = native.ActiveList(manager);
        var values = list is null ? null : native.Values(list);
        if (list is null || values is null)
            return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                "The ordinary Alchemy loadout is not available in this scene.");
        var beforeIndex = FindIndex(native, values, recipe);
        var beforeTarget = beforeIndex < 0 ? 0 : Math.Max(native.Queued(values[beforeIndex]!), 0);
        var admission = AdmitAmount(in action, native, list, values, recipe, beforeIndex, beforeTarget);
        if (admission.HasValue) return admission.Value;
        if (!TryPermit(out var refusal)) return new AlchemyLoadoutSubmission(refusal);

        var add = action.Kind == AlchemyLoadoutActionKind.Add;
        var amount = action.Amount;
        return new AlchemyLoadoutSubmission(Execute(EntityIdentityFormatter.PlayerName(action.RecipeId),
            "The requested Alchemy loadout transition is visible.", (int?)beforeTarget,
            () => Queued(native, list, recipe),
            () =>
            {
                if (add) native.AddInstances(list, recipe, amount);
                else native.RemoveInstances(list, recipe, amount);
            },
            add
                ? static (previous, after) => after > previous
                : static (previous, after) => after < previous));
    }

    private static AlchemyLoadoutSubmission? AdmitAmount(in AlchemyLoadoutAction action,
        AlchemyLoadoutNativeBindings native, object list, IList values, object recipe,
        int beforeIndex, int beforeTarget)
    {
        if (action.Kind == AlchemyLoadoutActionKind.Add)
        {
            if (!native.Discovered(recipe))
                return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.NotDiscovered,
                    EntityIdentityFormatter.PlayerName(action.RecipeId) + " has not been discovered.");
            if (!native.CanAdd(list, recipe))
                return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.LoadoutFull,
                    "The Alchemy loadout has no compatible open slot for this recipe.");
            var cost = native.UsageCost(recipe);
            if (cost is null)
                return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                    "The recipe's live usage decision is unavailable.");
            var free = beforeIndex < 0 ? native.FreeUses(recipe) : native.RemainingFree(values[beforeIndex]!);
            var remaining = beforeIndex < 0 ? native.MaximumUses(recipe) : native.RemainingMaximum(values[beforeIndex]!);
            var maximumByCost = native.CostEmpty(cost)
                ? int.MaxValue
                : Math.Max((native.MaximumTimes(cost) + new BigDouble(Math.Max(free, 0))).ToInt(), 0);
            var maximumAdditional = Math.Min(Math.Max(remaining, 0), maximumByCost);
            if (action.Amount <= 0 || action.Amount > maximumAdditional)
                return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.UsageUnavailable,
                    "This recipe can add at most " + maximumAdditional +
                    " uses with the current capacity and resources.",
                    maximumAdditional);
            return null;
        }
        if (beforeIndex < 0 || beforeTarget <= 0)
            return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.AlreadyInRequestedState,
                "The recipe is not active in the Alchemy loadout.");
        if (action.Amount <= 0 || action.Amount > beforeTarget)
            return AlchemyLoadoutSubmission.Reject(AlchemyLoadoutActionResultCodes.UsageUnavailable,
                "This recipe has only " + beforeTarget + " active uses to remove.");
        return null;
    }

    private static int? Queued(AlchemyLoadoutNativeBindings native, object list, object recipe)
    {
        var values = native.Values(list);
        if (values is null) return null;
        var index = FindIndex(native, values, recipe);
        return index < 0 ? 0 : Math.Max(native.Queued(values[index]!), 0);
    }

    private static int FindIndex(AlchemyLoadoutNativeBindings native, IList values, object recipe)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var instance = values[index];
            if (instance is not null && ReferenceEquals(native.InstanceRecipe(instance), recipe)) return index;
        }
        return -1;
    }
}
