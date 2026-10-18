using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using UnityEngine;

namespace OrbAutomata;

internal sealed class NativeConceptCandidate
{
    public NativeConceptCandidate(
        string uuid,
        string displayName,
        object recipe,
        int maximumQuantity,
        object? instance,
        int quantity,
        int queuedQuantity)
    {
        Uuid = uuid;
        DisplayName = displayName;
        Recipe = recipe;
        MaximumQuantity = maximumQuantity;
        Instance = instance;
        Quantity = quantity;
        QueuedQuantity = queuedQuantity;
    }

    public string Uuid { get; }
    public string DisplayName { get; }
    public object Recipe { get; }
    public int MaximumQuantity { get; }
    public object? Instance { get; }
    public int Quantity { get; }
    public int QueuedQuantity { get; }
    public bool IsSettled => Quantity == QueuedQuantity;
}

internal sealed class AutoConceptNativeAdapter :
    IAutoConceptNativePort,
    IDisposable,
    INativeMutationOutcomeSource
{
    internal static readonly string ActiveConceptsUuid = KnownEntities.ActiveConcepts.Uuid.ToString("D");

    private readonly AlchemyGameplayDomainClassifier _domainClassifier;
    private readonly TypedRegistryResolver _registryResolver;
    private readonly AutoConceptNativeBindings? _bindings;
    private readonly string _bindingFailure;
    private readonly List<object> _recipes = new();
    private readonly Dictionary<object, string> _recipeUuids = new(ReferenceEqualityComparer.Instance);
    private object? _activeConcepts;
    private TypedRegistryResolution? _activeConceptsResolution;
    private TypedRegistryResolution? _conceptRecipesResolution;
    private string? _blockedReason;
    private NativeMutationEvidence<int>? _lastMutationEvidence;
    private NativeMutationCallOutcome _lastNativeMutationOutcome;

    public string? BlockedReason => _blockedReason;
    public bool IsReady =>
        _bindings is not null &&
        _activeConcepts is not null &&
        _activeConceptsResolution is not null &&
        _conceptRecipesResolution is not null &&
        _registryResolver.IsCurrent(_activeConceptsResolution) &&
        _registryResolver.IsCurrent(_conceptRecipesResolution) &&
        _blockedReason is null;
    public int ScopedRecipeCount => _recipes.Count;
    public NativeMutationCallOutcome LastNativeMutationOutcome => _lastNativeMutationOutcome;

    public AutoConceptNativeAdapter(
        AlchemyGameplayDomainClassifier domainClassifier,
        TypedRegistryResolver? registryResolver = null)
        : this(domainClassifier, registryResolver, resolveType: null, includeContract: null)
    {
    }

    internal AutoConceptNativeAdapter(
        AlchemyGameplayDomainClassifier domainClassifier,
        TypedRegistryResolver? registryResolver,
        Func<string, Type?>? resolveType,
        Func<string, bool>? includeContract)
    {
        _domainClassifier = domainClassifier ?? throw new ArgumentNullException(nameof(domainClassifier));
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
        AutoConceptNativeBindings.TryCreate(out _bindings, out _bindingFailure, resolveType, includeContract);
    }

    public bool TryInitialize(out string reason)
    {
        if (IsReady)
        {
            reason = string.Empty;
            return true;
        }
        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }
        if (_bindings is not { } native)
        {
            reason = _bindingFailure;
            return false;
        }
        try
        {
            if (!_domainClassifier.TryInitialize(out var classifierReason))
            {
                return _domainClassifier.Status == AlchemyDomainClassifierStatus.Blocked
                    ? Fail($"Auto Concept domain classifier blocked: {classifierReason}", out reason)
                    : Retry($"Auto Concept domain classifier is not ready: {classifierReason}", out reason);
            }

            var activeId = KnownEntities.ActiveConcepts.Uuid;
            var recipesId = AlchemyGameplayDomainClassifier.ConceptRecipesUuid;
            var activeResolution = _registryResolver.Resolve(activeId, native.ActiveListType);
            if (!activeResolution.IsResolved)
                return HandleRegistryFailure(EntityIdentityFormatter.Format(activeId), activeResolution, out reason);
            var recipesResolution = _registryResolver.Resolve(recipesId, native.RecipeListType);
            if (!recipesResolution.IsResolved)
                return HandleRegistryFailure(EntityIdentityFormatter.Format(recipesId), recipesResolution, out reason);
            var active = activeResolution.Value!;
            var recipes = recipesResolution.Value!;
            _activeConceptsResolution = activeResolution;
            _conceptRecipesResolution = recipesResolution;

            if (native.RecipeValues(recipes) is not { } scopedRecipes)
                return Fail("ConceptRecipes runtime contents are unavailable", out reason);
            _recipes.Clear();
            _recipeUuids.Clear();
            foreach (var recipe in scopedRecipes)
            {
                var classification = _domainClassifier.ClassifyRecipe(recipe);
                if (classification.Domain != AlchemyGameplayDomain.ScholarConcept ||
                    !classification.IsMutationGrade ||
                    classification.RecipeUuid is null)
                {
                    return Fail(
                        $"ConceptRecipes entry failed shared domain classification. " +
                        $"RecipeUuid={classification.RecipeUuid?.ToString() ?? "unavailable"}, " +
                        $"Evidence={classification.Evidence}, Level={classification.Assessment.Level}, " +
                        $"Sources={classification.Assessment.Sources}, Reason={classification.Reason}",
                        out reason);
                }
                var uuid = classification.RecipeUuid.Value.ToString();
                _recipes.Add(recipe);
                _recipeUuids.Add(recipe, uuid);
            }
            if (_recipes.Count == 0) return Fail("ConceptRecipes runtime list is empty", out reason);
            _activeConcepts = active;
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return Fail($"concept contract initialization failed: {ex.GetBaseException().Message}", out reason);
        }
    }

    private bool TryResolveCandidate(
        Guid recipeId,
        out NativeConceptCandidate? candidate,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        candidate = null;
        refusal = AutoConceptActionResultCodes.RecipeIdentityChanged;
        if (!TryInitialize(out reason)) return false;
        var native = _bindings!;
        var active = ReadActiveByRecipe();
        foreach (var recipe in _recipes)
        {
            var uuid = _recipeUuids[recipe];
            if (!string.Equals(uuid, recipeId.ToString(), StringComparison.Ordinal)) continue;
            if (!native.IsDiscovered(recipe))
            {
                refusal = AutoConceptActionResultCodes.ConceptNotDiscovered;
                reason = "the planned concept is no longer unlocked";
                return false;
            }
            var maximum = Math.Max(0, native.MaxUsageSlots(recipe));
            active.TryGetValue(recipe, out var instance);
            var quantity = instance is null ? 0 : native.Quantity(instance);
            var queued = instance is null ? 0 : native.QueuedQuantity(instance);
            var identity = EntityIdentityFormatter.Describe(recipeId);
            candidate = new NativeConceptCandidate(
                uuid,
                identity.HasName ? identity.Name : uuid,
                recipe,
                maximum,
                instance,
                Math.Max(0, quantity),
                Math.Max(0, queued));
            reason = string.Empty;
            return true;
        }
        reason = "the planned recipe is no longer in ConceptRecipes";
        return false;
    }

    public bool CanAdd(NativeConceptCandidate candidate) =>
        IsCurrentRecipe(candidate) &&
        _bindings!.CanAddInstance(_activeConcepts!, candidate.Recipe);

    private bool TryFindSafeTarget(
        NativeConceptCandidate candidate,
        int desiredTarget,
        AutoConceptResourceLimits? limits,
        out int safeTarget,
        out string reason,
        out AutoConceptProjectionRefusal refusal)
    {
        safeTarget = candidate.Quantity;
        reason = "no resource-safe quantity was found";
        refusal = AutoConceptProjectionRefusal.Backpressure;
        desiredTarget = Math.Min(desiredTarget, candidate.MaximumQuantity);
        if (desiredTarget <= candidate.Quantity)
        {
            reason = "native mastery quantity limit reached";
            return false;
        }
        var delta = desiredTarget - candidate.Quantity;
        while (delta > 0)
        {
            var target = candidate.Quantity + delta;
            if (TryValidateProjectedDrain(candidate, target, limits, out reason, out refusal))
            {
                safeTarget = target;
                return true;
            }
            if (refusal == AutoConceptProjectionRefusal.Contract) return false;
            delta /= 2;
        }
        return false;
    }

    public AutoConceptSubmission Submit(
        in AutoConceptCycleAction action,
        AutoConceptResourceLimits? limits)
    {
        _lastMutationEvidence = null;
        _lastNativeMutationOutcome = default;
        if (!TryResolveCandidate(action.RecipeId, out var candidate, out var refusal, out var reason) ||
            candidate is null)
        {
            var missing = string.IsNullOrWhiteSpace(reason)
                ? "the planned recipe is no longer in ConceptRecipes"
                : reason;
            return IsReady
                ? AutoConceptSubmission.Rejected(refusal, missing)
                : AutoConceptSubmission.ContractUnavailable(missing);
        }
        if (!candidate.IsSettled)
            return AutoConceptSubmission.Rejected(
                AutoConceptActionResultCodes.AssignmentUnsettled,
                "the live assignment has an in-flight quantity change");
        if (candidate.Quantity != action.Belief.Quantity ||
            candidate.QueuedQuantity != action.Belief.QueuedQuantity)
            return AutoConceptSubmission.Rejected(
                AutoConceptActionResultCodes.OwnershipChanged,
                "the live quantity no longer matches the worker's ownership belief");

        if (action.Kind == AutoConceptActionKind.Add)
            return SubmitAdd(candidate, action.TargetOrDelta, limits);

        if (action.Kind == AutoConceptActionKind.RotateOut)
        {
            if (candidate.Quantity != action.TargetOrDelta)
                return AutoConceptSubmission.Rejected(
                    AutoConceptActionResultCodes.OwnershipChanged,
                    "the rotation no longer owns the exact live assignment");
            if (!TryValidateReplacement(action.ReplacementId, candidate, limits, out reason))
                return AutoConceptSubmission.Rejected(AutoConceptActionResultCodes.SlotUnavailable, reason);
            return SubmitRemove(candidate, action.TargetOrDelta, exact: true);
        }

        if (action.TargetOrDelta > candidate.Quantity)
            return AutoConceptSubmission.Rejected(
                AutoConceptActionResultCodes.OwnershipChanged,
                "the owned quantity is no longer available");
        return SubmitRemove(candidate, action.TargetOrDelta, exact: false);
    }

    private AutoConceptSubmission SubmitAdd(
        NativeConceptCandidate candidate,
        int desiredTarget,
        AutoConceptResourceLimits? limits)
    {
        if (candidate.MaximumQuantity != _bindings!.MaxUsageSlots(candidate.Recipe))
            return AutoConceptSubmission.Rejected(
                AutoConceptActionResultCodes.MasteryLimitChanged,
                "the native mastery quantity limit changed");
        if (!CanAdd(candidate))
            return AutoConceptSubmission.Rejected(
                AutoConceptActionResultCodes.SlotUnavailable,
                "the native slot is no longer available");
        if (!TryFindSafeTarget(
                candidate,
                desiredTarget,
                limits,
                out var safeTarget,
                out var reason,
                out var refusal))
            return refusal == AutoConceptProjectionRefusal.Backpressure
                ? AutoConceptSubmission.Backpressure(reason)
                : AutoConceptSubmission.Rejected(AutoConceptActionResultCodes.ProjectionRefused, reason);
        if (safeTarget != desiredTarget)
        {
            var maximumAdditional = Math.Max(0, safeTarget - candidate.Quantity);
            return AutoConceptSubmission.Backpressure(
                "The current resource limits allow at most " + maximumAdditional +
                " more concept " + (maximumAdditional == 1 ? "assignment" : "assignments") + ".",
                maximumAdditional);
        }
        var delta = safeTarget - candidate.Quantity;
        var succeeded = TryAdd(candidate, delta, out reason);
        return _lastNativeMutationOutcome.MutationAttempts == 0
            ? AutoConceptSubmission.Rejected(AutoConceptActionResultCodes.SlotUnavailable, reason)
            : AutoConceptSubmission.Attempted(
                _lastNativeMutationOutcome,
                _lastMutationEvidence?.Outcome ?? NativeMutationOutcome.PostconditionFailed,
                reason,
                delta);
    }

    private AutoConceptSubmission SubmitRemove(
        NativeConceptCandidate candidate,
        int delta,
        bool exact)
    {
        var succeeded = exact
            ? TryRemoveForRotation(candidate, delta, out var reason)
            : TryRemoveOwned(candidate, delta, out reason);
        _ = succeeded;
        return _lastNativeMutationOutcome.MutationAttempts == 0
            ? AutoConceptSubmission.Rejected(AutoConceptActionResultCodes.OwnershipChanged, reason)
            : AutoConceptSubmission.Attempted(
                _lastNativeMutationOutcome,
                _lastMutationEvidence?.Outcome ?? NativeMutationOutcome.PostconditionFailed,
                reason,
                -delta);
    }

    private bool TryValidateReplacement(
        Guid replacementId,
        NativeConceptCandidate active,
        AutoConceptResourceLimits? limits,
        out string reason)
    {
        if (replacementId == Guid.Empty)
        {
            reason = "the rotation no longer names a replacement";
            return false;
        }
        if (!TryResolveCandidate(replacementId, out var replacement, out _, out reason))
            return false;
        if (replacement is null || !replacement.IsSettled || replacement.Quantity != 0 ||
            !CanReplaceAfterRemoval(active, replacement))
        {
            reason = "the replacement identity, prospective slot, unlock, or quantity changed";
            return false;
        }
        return TryFindSafeTarget(replacement, 1, limits, out _, out reason, out _);
    }

    private bool CanReplaceAfterRemoval(
        NativeConceptCandidate active,
        NativeConceptCandidate replacement)
    {
        if (CanAdd(replacement)) return true;
        var native = _bindings!;
        if (_activeConcepts is null ||
            native.AlchemyTypes(active.Recipe) is not { } activeTypes ||
            native.AlchemyTypes(replacement.Recipe) is not { } replacementTypes)
            return false;

        var genericAfter = native.EmptyTypelessSlots(_activeConcepts) + 1;
        var activeTypeSet = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < activeTypes.Count; index++)
        {
            var type = activeTypes[index];
            if (type is null || !activeTypeSet.Add(type)) continue;
            var count = native.NumOfType(_activeConcepts, type);
            var slots = native.SlotsOnlyForType(_activeConcepts, type);
            if (count > 0 && count <= slots) genericAfter--;
        }

        for (var index = 0; index < replacementTypes.Count; index++)
        {
            var type = replacementTypes[index];
            if (type is null) continue;
            var count = native.NumOfType(_activeConcepts, type);
            if (activeTypeSet.Contains(type) && count > 0) count--;
            var slots = native.SlotsOnlyForType(_activeConcepts, type);
            if (genericAfter + Math.Max(slots - count, 0) > 0) return true;
        }
        return false;
    }

    private bool TryAdd(NativeConceptCandidate candidate, int delta, out string reason)
    {
        _lastNativeMutationOutcome = default;
        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }

        if (delta <= 0 || !IsCurrentRecipe(candidate) || !CanAdd(candidate))
        {
            reason = "candidate identity, compatible slot, or quantity changed";
            return false;
        }
        var native = _bindings!;
        var maximum = native.MaxUsageSlots(candidate.Recipe);
        var current = FindActiveInstance(candidate.Recipe);
        var quantity = current is null ? 0 : native.QueuedQuantity(current);
        if (quantity < 0 || quantity + delta > maximum)
        {
            reason = "native mastery quantity limit changed";
            return false;
        }
        return ExecuteQuantityMutation(
            candidate,
            "Auto Concept add",
            $"queued quantity exact delta +{delta}",
            () => native.AddInstances(_activeConcepts!, candidate.Recipe, delta),
            (before, after) => after == before + delta,
            out reason);
    }

    private bool TryRemoveOwned(NativeConceptCandidate candidate, int delta, out string reason)
    {
        return TryRemove(candidate, delta, requireExactQuantity: false, "owned concept quantity", out reason);
    }

    private bool TryRemoveForRotation(NativeConceptCandidate candidate, int expectedQuantity, out string reason)
    {
        return TryRemove(candidate, expectedQuantity, requireExactQuantity: true, "rotation quantity", out reason);
    }

    private bool TryRemove(
        NativeConceptCandidate candidate,
        int delta,
        bool requireExactQuantity,
        string quantityDescription,
        out string reason)
    {
        _lastNativeMutationOutcome = default;
        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }

        var current = FindActiveInstance(candidate.Recipe);
        if (delta <= 0 || current is null || !IsCurrentRecipe(candidate))
        {
            reason = $"{quantityDescription} is no longer active";
            return false;
        }
        var native = _bindings!;
        var quantity = native.Quantity(current);
        var queuedQuantity = native.QueuedQuantity(current);
        if (queuedQuantity < delta || requireExactQuantity && (quantity != delta || queuedQuantity != delta))
        {
            reason = $"native {quantityDescription} changed";
            return false;
        }
        return ExecuteQuantityMutation(
            candidate,
            "Auto Concept remove",
            $"queued quantity exact delta -{delta}",
            () => native.RemoveInstances(_activeConcepts!, candidate.Recipe, delta),
            (before, after) => after == before - delta,
            out reason);
    }

    private bool ExecuteQuantityMutation(
        NativeConceptCandidate candidate,
        string feature,
        string expectedChange,
        Action execute,
        Func<int, int, bool> verify,
        out string reason)
    {
        var evidence = NativeMutationVerifier.Execute(
            feature,
            candidate.Uuid,
            expectedChange,
            () => CaptureQueuedQuantity(candidate.Recipe),
            execute,
            verify);
        _lastMutationEvidence = evidence;
        _lastNativeMutationOutcome = NativeMutationCallOutcome.FromEvidence(evidence);
        if (evidence.IsVerified)
        {
            reason = string.Empty;
            return true;
        }

        reason = evidence.Format();
        return evidence.MutationWasAttempted
            ? Fail($"native Concept mutation blocked until the next lifecycle: {reason}", out reason)
            : false;
    }

    private int CaptureQueuedQuantity(object recipe)
    {
        var instance = FindActiveInstance(recipe);
        return instance is null ? 0 : _bindings!.QueuedQuantity(instance);
    }

    public void InvalidateLifecycle()
    {
        _domainClassifier.InvalidateLifecycle();
        _activeConcepts = null;
        _activeConceptsResolution = null;
        _conceptRecipesResolution = null;
        _recipes.Clear();
        _recipeUuids.Clear();
        _blockedReason = null;
        _lastMutationEvidence = null;
        _lastNativeMutationOutcome = default;
    }

    public void Dispose() => InvalidateLifecycle();

    private bool TryValidateProjectedDrain(
        NativeConceptCandidate candidate,
        int targetQuantity,
        AutoConceptResourceLimits? limits,
        out string reason,
        out AutoConceptProjectionRefusal refusal)
    {
        refusal = AutoConceptProjectionRefusal.Contract;
        var native = _bindings!;
        try
        {
            var temporary = native.NewInstance(candidate.Recipe);
            native.SetQuantity(temporary, targetQuantity);
            var baseDrain = native.DrainCost(candidate.Recipe);
            var prospective = baseDrain is null
                ? null
                : native.Multiply(baseDrain, native.AsPercent(native.DrainCostMod(temporary)));
            if (prospective is null) return FailProjection("native prospective drain vector is unavailable", out reason);

            var incremental = prospective;
            if (candidate.Instance is not null)
            {
                var drain = native.ResourceDrain(candidate.Instance);
                var current = drain is null ? null : native.CurrentDrain(drain);
                if (current is null) return FailProjection("current native drain vector is unavailable", out reason);
                incremental = native.Subtract(prospective, current);
            }
            if (incremental is null) return FailProjection("incremental native drain vector is unavailable", out reason);
            if (!TryReadCostEntries(native, incremental, out var entries))
                return FailProjection("incremental native drain vector is only partially decodable", out reason);
            if (entries.Count == 0)
            {
                reason = "no positive incremental drain";
                refusal = AutoConceptProjectionRefusal.None;
                return true;
            }
            foreach (var entry in entries)
            {
                if (entry.Amount.CompareTo(default) <= 0) continue;
                // Every check below this line is the worker's own caution, so a press the player
                // asked for skips all of them. The game does not refuse an assignment because a
                // drained resource sits at zero: AlchemyInstanceListVariable.EngageAlchemy gates
                // on a free slot, the usage cost and the multi-buy count, CanAddInstance on a slot
                // alone, and the only reader of ResourceDrain.AnyResourceAtZero in the whole
                // assembly is StatusEffect.CheckIfExpired.
                if (limits is not { } applied) continue;
                if (!AutoConceptResourcePolicy.TryAcceptPositiveDrain(native.IsAtZero(entry.Resource), out var zeroReason))
                {
                    refusal = AutoConceptProjectionRefusal.Backpressure;
                    return FailProjection($"{ResourceName(native, entry.Resource)} {zeroReason}", out reason);
                }
                var adjustedIncrement = native.TrueSpend(entry.Resource, entry.Amount);
                if (adjustedIncrement.CompareTo(default) < 0)
                    return FailProjection("resource quality conversion failed", out reason);
                if (AutoConceptResourcePolicy.BreaksRateReserve(
                        native.TrueRate(entry.Resource),
                        native.ModdedDrain(entry.Resource),
                        adjustedIncrement,
                        applied.RateReservePercent))
                {
                    refusal = AutoConceptProjectionRefusal.Backpressure;
                    return FailProjection(
                        $"{ResourceName(native, entry.Resource)} would fall below the configured rate reserve",
                        out reason);
                }
            }
            reason = "projected native drain is safe";
            refusal = AutoConceptProjectionRefusal.None;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return FailProjection(ex.GetBaseException().Message, out reason);
        }
    }

    private enum AutoConceptProjectionRefusal
    {
        None = 0,
        Backpressure = 1,
        Contract = 2,
    }

    private Dictionary<object, object> ReadActiveByRecipe()
    {
        var native = _bindings!;
        var result = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        if (native.ActiveValues(_activeConcepts!) is not { } values) return result;
        foreach (var instance in values)
        {
            if (instance is null || instance.GetType() != native.InstanceType) continue;
            var recipe = native.Reference(instance);
            if (recipe is not null) result[recipe] = instance;
        }
        return result;
    }

    private object? FindActiveInstance(object recipe)
    {
        return ReadActiveByRecipe().TryGetValue(recipe, out var instance) ? instance : null;
    }

    private bool IsCurrentRecipe(NativeConceptCandidate candidate)
    {
        if (!IsReady) return false;
        if (candidate.Recipe.GetType() != _bindings!.RecipeType) return false;
        var classification = _domainClassifier.ClassifyRecipe(candidate.Recipe);
        if (classification.Domain != AlchemyGameplayDomain.ScholarConcept ||
            !classification.IsMutationGrade ||
            classification.RecipeUuid is null ||
            !string.Equals(classification.RecipeUuid.Value.ToString(), candidate.Uuid, StringComparison.Ordinal) ||
            !_recipeUuids.TryGetValue(candidate.Recipe, out var snapshotUuid) ||
            !string.Equals(snapshotUuid, candidate.Uuid, StringComparison.Ordinal)) return false;
        for (var index = 0; index < _recipes.Count; index++)
            if (ReferenceEquals(_recipes[index], candidate.Recipe)) return true;
        return false;
    }

    private static bool TryReadCostEntries(
        AutoConceptNativeBindings native,
        object costList,
        out List<NativeCostEntry> result)
    {
        result = new List<NativeCostEntry>();
        if (native.CostEntries(costList) is not { } entries) return false;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]!;
            if (native.EntryResource(entry) is not { } resource) return false;
            result.Add(new NativeCostEntry(resource, native.EntryValue(entry)));
        }
        return true;
    }

    private bool HandleRegistryFailure(
        string label,
        TypedRegistryResolution resolution,
        out string reason)
    {
        var message = $"{label} resolution failed. {resolution.Format()}";
        return resolution.IsRetryable
            ? Retry(message, out reason)
            : Fail(message, out reason);
    }

    private bool Fail(string reason, out string output)
    {
        _blockedReason = reason;
        output = reason;
        return false;
    }

    private static bool FailProjection(string reason, out string output)
    {
        output = reason;
        return false;
    }

    private static bool Retry(string reason, out string output)
    {
        output = reason;
        return false;
    }

    private static string ResourceName(AutoConceptNativeBindings native, object resource)
    {
        var uuid = native.ResourceId(resource);
        return uuid != Guid.Empty
            ? EntityIdentityFormatter.Format(uuid)
            : "resource";
    }

    private readonly struct NativeCostEntry
    {
        public NativeCostEntry(object resource, BigDouble amount)
        {
            Resource = resource;
            Amount = amount;
        }
        public object Resource { get; }
        public BigDouble Amount { get; }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
