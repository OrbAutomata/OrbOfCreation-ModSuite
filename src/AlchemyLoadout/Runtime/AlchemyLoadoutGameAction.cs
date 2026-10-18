using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped, Unity-main-thread boundary for the ordinary alchemy list.</summary>
internal sealed class AlchemyLoadoutGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly AlchemyGameplayDomainClassifier _classifier;
    private readonly int _mainThreadId;
    private AlchemyLoadoutNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal AlchemyLoadoutGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null, AlchemyGameplayDomainClassifier? classifier = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(_readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
        _classifier = classifier ?? new AlchemyGameplayDomainClassifier(_registry);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal bool TryValidateStoredEntry(
        object candidate,
        int quantity,
        out int freeUses,
        out object? usageCost,
        out string reason)
    {
        freeUses = 0;
        usageCost = null;
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            reason = "Alchemy loadout validation must run on the Unity main thread.";
            return false;
        }
        if (_bindings is not { } native)
        {
            reason = _bindingFailure;
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
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(AlchemyLoadoutActionResultCodes.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                GameActionAnswer.NotAttached("Alchemy", _bindingFailure));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("Alchemy", exception));
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            if (!_classifier.TryInitialize(out var classifierReason))
                return Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable, classifierReason);
            var resolution = _registry.Resolve(action.RecipeId, native.RecipeType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return Reject(AlchemyLoadoutActionResultCodes.IdentityUnavailable,
                    resolution.IsResolved ? GameActionAnswer.Replaced("alchemy recipe") : resolution.Reason);
            var recipe = resolution.Value!;
            var classification = _classifier.ClassifyRecipe(recipe);
            if (!classification.IsMutationGrade ||
                classification.Domain != AlchemyGameplayDomain.OrdinaryAlchemy)
                return Reject(AlchemyLoadoutActionResultCodes.WrongDomain,
                    "This recipe is not part of the ordinary Alchemy loadout.");
            var manager = native.Manager();
            if (manager is null || manager.GetType() != native.ManagerType)
                return Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                    "The ordinary Alchemy manager is not available in this scene.");
            var list = native.ActiveList(manager);
            var values = list is null ? null : native.Values(list);
            if (list is null || values is null)
                return Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                    "The ordinary Alchemy loadout is not available in this scene.");
            var beforeIndex = FindIndex(native, values, recipe);
            var beforeTarget = beforeIndex < 0 ? 0 : Math.Max(native.Queued(values[beforeIndex]!), 0);

            if (action.Kind == AlchemyLoadoutActionKind.Add)
            {
                if (!native.Discovered(recipe))
                    return Reject(AlchemyLoadoutActionResultCodes.NotDiscovered,
                        EntityIdentityFormatter.PlayerName(action.RecipeId) + " has not been discovered.");
                if (!native.CanAdd(list, recipe))
                    return Reject(AlchemyLoadoutActionResultCodes.LoadoutFull,
                        "The Alchemy loadout has no compatible open slot for this recipe.");
                var cost = native.UsageCost(recipe);
                if (cost is null)
                    return Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                        "The recipe's live usage decision is unavailable.");
                var free = beforeIndex < 0 ? native.FreeUses(recipe) : native.RemainingFree(values[beforeIndex]!);
                var remaining = beforeIndex < 0 ? native.MaximumUses(recipe) : native.RemainingMaximum(values[beforeIndex]!);
                var maximumByCost = native.CostEmpty(cost)
                    ? int.MaxValue
                    : Math.Max((native.MaximumTimes(cost) + new BigDouble(Math.Max(free, 0))).ToInt(), 0);
                var maximumAdditional = Math.Min(Math.Max(remaining, 0), maximumByCost);
                if (action.Amount <= 0 || action.Amount > maximumAdditional)
                    return Reject(AlchemyLoadoutActionResultCodes.UsageUnavailable,
                        "This recipe can add at most " + maximumAdditional +
                        " uses with the current capacity and resources.",
                        maximumAdditional);
            }
            else
            {
                if (beforeIndex < 0 || beforeTarget <= 0)
                    return Reject(AlchemyLoadoutActionResultCodes.AlreadyInRequestedState,
                        "The recipe is not active in the Alchemy loadout.");
                if (action.Amount <= 0 || action.Amount > beforeTarget)
                    return Reject(AlchemyLoadoutActionResultCodes.UsageUnavailable,
                        "This recipe has only " + beforeTarget + " active uses to remove.");
            }

            if (!_tryCaptureMutationPermit())
                return Reject(AlchemyLoadoutActionResultCodes.MutationPermitUnavailable, _readOwnershipFailure());
            return Execute(in action, native, list, recipe, beforeTarget);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return Reject(AlchemyLoadoutActionResultCodes.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Alchemy", exception));
        }
    }

    internal void InvalidateLifecycle()
    {
        _classifier.InvalidateLifecycle();
        _bindings = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _classifier.Dispose();
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private static AlchemyLoadoutSubmission Execute(in AlchemyLoadoutAction action,
        AlchemyLoadoutNativeBindings native, object list, object recipe, int beforeTarget)
    {
        var stage = "NativeCallback";
        try
        {
            if (action.Kind == AlchemyLoadoutActionKind.Add)
                native.AddInstances(list, recipe, action.Amount);
            else
                native.RemoveInstances(list, recipe, action.Amount);
            stage = "Verification";
            return OutcomeObserved(in action, native, list, recipe, beforeTarget)
                ? Verified()
                : Fault(in action, AlchemyLoadoutActionResultCodes.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    GameActionAnswer.ChangeNotSeen("Alchemy"));
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            var request = action;
            if (NativeMutationVerifier.TryReadAfterCall(
                    () => OutcomeObserved(in request, native, list, recipe, beforeTarget), out var observed) && observed)
                return Verified();
            return Fault(in action, AlchemyLoadoutActionResultCodes.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("Alchemy", exception));
        }
    }

    private static bool OutcomeObserved(in AlchemyLoadoutAction action,
        AlchemyLoadoutNativeBindings native, object list, object recipe, int beforeTarget)
    {
        var values = native.Values(list);
        if (values is null) return false;
        var afterIndex = FindIndex(native, values, recipe);
        var afterTarget = afterIndex < 0 ? 0 : Math.Max(native.Queued(values[afterIndex]!), 0);
        return action.Kind switch
        {
            AlchemyLoadoutActionKind.Add => afterTarget > beforeTarget,
            AlchemyLoadoutActionKind.Remove => afterTarget < beforeTarget,
            _ => false,
        };
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

    private static AlchemyLoadoutSubmission Reject(
        ServiceActionResultCode code, string reason, int maximumAmount = -1) =>
        AlchemyLoadoutSubmission.Reject(code, reason, maximumAmount);

    private static AlchemyLoadoutSubmission Verified() =>
        new(NativeActionOutcome.Commit(new NativeMutationCallOutcome(1, 1, 1),
            "The requested Alchemy loadout transition is visible."));

    private static AlchemyLoadoutSubmission Fault(in AlchemyLoadoutAction action,
        ServiceActionResultCode code, string stage,
        NativeMutationOutcome outcome, string reason) =>
        new(NativeActionOutcome.Fault(code, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Alchemy loadout " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.RecipeId) + ": " + reason));

    private void BindLifecycle()
    {
        if (AlchemyLoadoutNativeBindings.TryCreate(out var bindings, out var reason,
                _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

}
