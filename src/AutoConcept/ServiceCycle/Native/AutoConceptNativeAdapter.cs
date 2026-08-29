using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
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
    private readonly List<object> _recipes = new();
    private readonly Dictionary<object, string> _recipeUuids = new(ReferenceEqualityComparer.Instance);
    private object? _activeConcepts;
    private TypedRegistryResolution? _activeConceptsResolution;
    private TypedRegistryResolution? _conceptRecipesResolution;
    private Type? _recipeType;
    private Type? _instanceType;
    private FieldInfo? _activeValuesField;
    private FieldInfo? _recipeTypesField;
    private FieldInfo? _recipeDrainField;
    private FieldInfo? _instanceQuantityField;
    private FieldInfo? _instanceQueuedQuantityField;
    private FieldInfo? _instanceDrainField;
    private MethodInfo? _canAddInstance;
    private MethodInfo? _isDiscovered;
    private MethodInfo? _getNumEmptyTypelessSlots;
    private MethodInfo? _getSlotsOnlyForType;
    private MethodInfo? _getNumOfType;
    private MethodInfo? _addInstances;
    private MethodInfo? _removeInstances;
    private string? _blockedReason;
    private NativeMutationEvidence<int>? _lastMutationEvidence;
    private NativeMutationCallOutcome _lastNativeMutationOutcome;

    public string? BlockedReason => _blockedReason;
    public bool IsReady =>
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
    {
        _domainClassifier = domainClassifier ?? throw new ArgumentNullException(nameof(domainClassifier));
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
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
        try
        {
            if (!_domainClassifier.TryInitialize(out var classifierReason))
            {
                return _domainClassifier.Status == AlchemyDomainClassifierStatus.Blocked
                    ? Fail($"Auto Concept domain classifier blocked: {classifierReason}", out reason)
                    : Retry($"Auto Concept domain classifier is not ready: {classifierReason}", out reason);
            }

            _recipeType = ReflectionUtil.FindLoadedType("AlchemyRecipeSO");
            _instanceType = ReflectionUtil.FindLoadedType("AlchemyInstance");
            var alchemyType = ReflectionUtil.FindLoadedType("AlchemyTypeSO");
            var activeType = ReflectionUtil.FindLoadedType(KnownEntities.ActiveConcepts.ManagedTypeName);
            var recipeListType = ReflectionUtil.FindLoadedType(KnownEntities.ConceptRecipes.ManagedTypeName);
            if (_recipeType is null || _instanceType is null || alchemyType is null ||
                activeType is null || recipeListType is null)
                return Retry("native concept types are not registered yet", out reason);

            var activeId = KnownEntities.ActiveConcepts.Uuid;
            var recipesId = AlchemyGameplayDomainClassifier.ConceptRecipesUuid;
            var activeResolution = _registryResolver.Resolve(activeId, activeType);
            if (!activeResolution.IsResolved)
                return HandleRegistryFailure(EntityIdentityFormatter.Format(activeId), activeResolution, out reason);
            var recipesResolution = _registryResolver.Resolve(recipesId, recipeListType);
            if (!recipesResolution.IsResolved)
                return HandleRegistryFailure(EntityIdentityFormatter.Format(recipesId), recipesResolution, out reason);
            var active = activeResolution.Value!;
            var recipes = recipesResolution.Value!;
            _activeConceptsResolution = activeResolution;
            _conceptRecipesResolution = recipesResolution;

            _activeValuesField = FindField(activeType, "value", isStatic: false);
            var recipeValuesField = FindField(recipeListType, "value", isStatic: false);
            _recipeTypesField = FindField(_recipeType, "alchemyTypes", isStatic: false);
            _recipeDrainField = FindField(_recipeType, "drainCost", isStatic: false);
            _instanceQuantityField = FindField(_instanceType, "quantity", isStatic: false);
            _instanceQueuedQuantityField = FindField(_instanceType, "queuedQuantity", isStatic: false);
            _instanceDrainField = FindField(_instanceType, "resourceDrain", isStatic: false);
            _canAddInstance = FindMethod(activeType, "CanAddInstance", _recipeType);
            _isDiscovered = FindMethod(_recipeType, "IsDiscovered");
            _getNumEmptyTypelessSlots = FindMethod(activeType, "GetNumEmptyTypelessSlots");
            _getSlotsOnlyForType = FindMethod(
                activeType, "GetSlotsOnlyForType", alchemyType);
            _getNumOfType = FindMethod(
                activeType, "GetNumOfType", alchemyType);
            _addInstances = FindMethod(activeType, "AddAlchemyInstances", _recipeType, typeof(int));
            _removeInstances = FindMethod(activeType, "RemoveAlchemyInstances", _recipeType, typeof(int));
            if (_activeValuesField is null || recipeValuesField is null || _recipeTypesField is null ||
                _recipeDrainField is null ||
                _instanceQuantityField is null || _instanceQueuedQuantityField is null || _instanceDrainField is null ||
                !Returns(_canAddInstance, typeof(bool)) || !Returns(_isDiscovered, typeof(bool)) ||
                !Returns(_getNumEmptyTypelessSlots, typeof(int)) ||
                !Returns(_getSlotsOnlyForType, typeof(int)) ||
                !Returns(_getNumOfType, typeof(int)) ||
                _addInstances is null || _removeInstances is null)
                return Fail("native Active Concepts accessors are unavailable", out reason);

            if (recipeValuesField.GetValue(recipes) is not IEnumerable scopedRecipes)
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
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
        {
            return Fail($"concept contract initialization failed: {ex.GetBaseException().Message}", out reason);
        }
    }

    private bool TryResolveCandidate(
        Guid recipeId,
        out NativeConceptCandidate? candidate,
        out string reason)
    {
        candidate = null;
        if (!TryInitialize(out reason)) return false;
        try
        {
            var active = ReadActiveByRecipe();
            foreach (var recipe in _recipes)
            {
                var uuid = _recipeUuids[recipe];
                if (!string.Equals(uuid, recipeId.ToString(), StringComparison.Ordinal)) continue;
                if (!InvokeBoolean(_isDiscovered!, recipe))
                {
                    reason = "the planned concept is no longer unlocked";
                    return false;
                }
                var maximum = Math.Max(0, ReadInt(recipe, "GetMaxUsageSlots", "maxUsageSlots"));
                active.TryGetValue(recipe, out var instance);
                var quantity = instance is null ? 0 : Convert.ToInt32(_instanceQuantityField!.GetValue(instance) ?? 0);
                var queued = instance is null ? 0 : Convert.ToInt32(_instanceQueuedQuantityField!.GetValue(instance) ?? 0);
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
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    public bool CanAdd(NativeConceptCandidate candidate) =>
        IsCurrentRecipe(candidate) &&
        InvokeBoolean(_canAddInstance!, _activeConcepts!, candidate.Recipe);

    public bool TryFindSafeTarget(
        NativeConceptCandidate candidate,
        int desiredTarget,
        float rateReservePercent,
        float minimumResourcePercent,
        out int safeTarget,
        out string reason)
        => TryFindSafeTarget(
            candidate, desiredTarget, rateReservePercent, minimumResourcePercent,
            out safeTarget, out reason, out _);

    private bool TryFindSafeTarget(
        NativeConceptCandidate candidate,
        int desiredTarget,
        float rateReservePercent,
        float minimumResourcePercent,
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
            if (TryValidateProjectedDrain(
                    candidate, target, rateReservePercent, minimumResourcePercent,
                    out reason, out refusal))
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
        in AutoConceptConfiguration config)
    {
        _lastMutationEvidence = null;
        _lastNativeMutationOutcome = default;
        if (!TryResolveCandidate(action.RecipeId, out var candidate, out var reason) ||
            candidate is null)
            return AutoConceptSubmission.Rejected(
                IsReady
                    ? AutoConceptPreflight.RecipeIdentityChanged
                    : AutoConceptPreflight.ContractUnavailable,
                string.IsNullOrWhiteSpace(reason) ? "the planned recipe is no longer in ConceptRecipes" : reason);
        if (!candidate.IsSettled)
            return AutoConceptSubmission.Rejected(
                AutoConceptPreflight.AssignmentUnsettled,
                "the live assignment has an in-flight quantity change");
        if (candidate.Quantity != action.Belief.Quantity ||
            candidate.QueuedQuantity != action.Belief.QueuedQuantity)
            return AutoConceptSubmission.Rejected(
                AutoConceptPreflight.OwnershipChanged,
                "the live quantity no longer matches the worker's ownership belief");

        if (action.Kind == AutoConceptActionKind.Add)
            return SubmitAdd(candidate, action.TargetOrDelta, in config);

        if (action.Kind == AutoConceptActionKind.RotateOut)
        {
            if (candidate.Quantity != action.TargetOrDelta)
                return AutoConceptSubmission.Rejected(
                    AutoConceptPreflight.OwnershipChanged,
                    "the rotation no longer owns the exact live assignment");
            if (!TryValidateReplacement(action.ReplacementId, candidate, in config, out reason))
                return AutoConceptSubmission.Rejected(AutoConceptPreflight.SlotUnavailable, reason);
            return SubmitRemove(candidate, action.TargetOrDelta, exact: true);
        }

        if (action.TargetOrDelta > candidate.Quantity)
            return AutoConceptSubmission.Rejected(
                AutoConceptPreflight.OwnershipChanged,
                "the owned quantity is no longer available");
        return SubmitRemove(candidate, action.TargetOrDelta, exact: false);
    }

    private AutoConceptSubmission SubmitAdd(
        NativeConceptCandidate candidate,
        int desiredTarget,
        in AutoConceptConfiguration config)
    {
        if (candidate.MaximumQuantity != ReadInt(candidate.Recipe, "GetMaxUsageSlots", "maxUsageSlots"))
            return AutoConceptSubmission.Rejected(
                AutoConceptPreflight.MasteryLimitChanged,
                "the native mastery quantity limit changed");
        if (!CanAdd(candidate))
            return AutoConceptSubmission.Rejected(
                AutoConceptPreflight.SlotUnavailable,
                "the native slot is no longer available");
        if (!TryFindSafeTarget(
                candidate,
                desiredTarget,
                config.RateReservePercent,
                config.MinimumResourcePercent,
                out var safeTarget,
                out var reason,
                out var refusal))
            return AutoConceptSubmission.Rejected(
                refusal == AutoConceptProjectionRefusal.Backpressure
                    ? AutoConceptPreflight.ResourceBackpressure
                    : AutoConceptPreflight.ProjectionRefused,
                reason);
        if (safeTarget != desiredTarget)
        {
            var maximumAdditional = Math.Max(0, safeTarget - candidate.Quantity);
            return AutoConceptSubmission.Rejected(
                AutoConceptPreflight.ResourceBackpressure,
                "The current resource limits allow at most " + maximumAdditional +
                " more concept " + (maximumAdditional == 1 ? "assignment" : "assignments") + ".",
                maximumAdditional);
        }
        var delta = safeTarget - candidate.Quantity;
        var succeeded = TryAdd(candidate, delta, out reason);
        return _lastNativeMutationOutcome.MutationAttempts == 0
            ? AutoConceptSubmission.Rejected(AutoConceptPreflight.SlotUnavailable, reason)
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
            ? AutoConceptSubmission.Rejected(AutoConceptPreflight.OwnershipChanged, reason)
            : AutoConceptSubmission.Attempted(
                _lastNativeMutationOutcome,
                _lastMutationEvidence?.Outcome ?? NativeMutationOutcome.PostconditionFailed,
                reason,
                -delta);
    }

    private bool TryValidateReplacement(
        Guid replacementId,
        NativeConceptCandidate active,
        in AutoConceptConfiguration config,
        out string reason)
    {
        if (replacementId == Guid.Empty)
        {
            reason = "the rotation no longer names a replacement";
            return false;
        }
        if (!TryResolveCandidate(replacementId, out var replacement, out reason))
            return false;
        if (replacement is null || !replacement.IsSettled || replacement.Quantity != 0 ||
            !CanReplaceAfterRemoval(active, replacement))
        {
            reason = "the replacement identity, prospective slot, unlock, or quantity changed";
            return false;
        }
        return TryFindSafeTarget(
            replacement,
            1,
            config.RateReservePercent,
            config.MinimumResourcePercent,
            out _,
            out reason);
    }

    private bool CanReplaceAfterRemoval(
        NativeConceptCandidate active,
        NativeConceptCandidate replacement)
    {
        if (CanAdd(replacement)) return true;
        if (_activeConcepts is null ||
            _recipeTypesField!.GetValue(active.Recipe) is not IList activeTypes ||
            _recipeTypesField.GetValue(replacement.Recipe) is not IList replacementTypes)
            return false;

        var genericAfter = InvokeInt(_getNumEmptyTypelessSlots!, _activeConcepts) + 1;
        var activeTypeSet = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < activeTypes.Count; index++)
        {
            var type = activeTypes[index];
            if (type is null || !activeTypeSet.Add(type)) continue;
            var count = InvokeInt(_getNumOfType!, _activeConcepts, type);
            var slots = InvokeInt(_getSlotsOnlyForType!, _activeConcepts, type);
            if (count > 0 && count <= slots) genericAfter--;
        }

        for (var index = 0; index < replacementTypes.Count; index++)
        {
            var type = replacementTypes[index];
            if (type is null) continue;
            var count = InvokeInt(_getNumOfType!, _activeConcepts, type);
            if (activeTypeSet.Contains(type) && count > 0) count--;
            var slots = InvokeInt(_getSlotsOnlyForType!, _activeConcepts, type);
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
        var maximum = ReadInt(candidate.Recipe, "GetMaxUsageSlots", "maxUsageSlots");
        var current = FindActiveInstance(candidate.Recipe);
        var quantity = current is null ? 0 : Convert.ToInt32(_instanceQueuedQuantityField!.GetValue(current) ?? 0);
        if (quantity < 0 || quantity + delta > maximum)
        {
            reason = "native mastery quantity limit changed";
            return false;
        }
        return ExecuteQuantityMutation(
            candidate,
            "Auto Concept add",
            $"queued quantity exact delta +{delta}",
            () => _addInstances!.Invoke(_activeConcepts, new object[] { candidate.Recipe, delta }),
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
        var quantity = Convert.ToInt32(_instanceQuantityField!.GetValue(current) ?? 0);
        var queuedQuantity = Convert.ToInt32(_instanceQueuedQuantityField!.GetValue(current) ?? 0);
        if (queuedQuantity < delta || requireExactQuantity && (quantity != delta || queuedQuantity != delta))
        {
            reason = $"native {quantityDescription} changed";
            return false;
        }
        return ExecuteQuantityMutation(
            candidate,
            "Auto Concept remove",
            $"queued quantity exact delta -{delta}",
            () => _removeInstances!.Invoke(_activeConcepts, new object[] { candidate.Recipe, delta }),
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
        return instance is null
            ? 0
            : Convert.ToInt32(_instanceQueuedQuantityField!.GetValue(instance) ?? 0);
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
        float rateReservePercent,
        float minimumResourcePercent,
        out string reason,
        out AutoConceptProjectionRefusal refusal)
    {
        refusal = AutoConceptProjectionRefusal.Contract;
        try
        {
            var temporary = Activator.CreateInstance(
                _instanceType!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new[] { candidate.Recipe },
                culture: null);
            if (temporary is null) return FailProjection("native prospective instance could not be created", out reason);
            _instanceQuantityField!.SetValue(temporary, targetQuantity);
            var multiplier = ReflectionUtil.InvokeNoArgs(temporary, "GetDrainCostMod");
            var percentMultiplier = multiplier is null ? null : ReflectionUtil.InvokeNoArgs(multiplier, "AsPercent");
            var baseDrain = _recipeDrainField!.GetValue(candidate.Recipe);
            var prospective = baseDrain is null || percentMultiplier is null
                ? null
                : InvokeCompatible(baseDrain, "Multiply", percentMultiplier);
            if (prospective is null) return FailProjection("native prospective drain vector is unavailable", out reason);

            object? incremental = prospective;
            if (candidate.Instance is not null)
            {
                var drain = _instanceDrainField!.GetValue(candidate.Instance);
                var current = drain is null ? null : ReflectionUtil.InvokeNoArgs(drain, "GetCurrentDrain");
                if (current is null) return FailProjection("current native drain vector is unavailable", out reason);
                incremental = InvokeCompatible(prospective, "Subtract", current);
            }
            if (incremental is null) return FailProjection("incremental native drain vector is unavailable", out reason);
            if (!TryReadCostEntries(incremental, out var entries))
                return FailProjection("incremental native drain vector is only partially decodable", out reason);
            if (entries.Count == 0)
            {
                reason = "no positive incremental drain";
                refusal = AutoConceptProjectionRefusal.None;
                return true;
            }
            foreach (var entry in entries)
            {
                if (entry.Amount.IsZero || entry.Amount.IsNegative) continue;
                var zeroState = ReflectionUtil.InvokeNoArgs(entry.Resource, "IsAtZero");
                if (!AutoConceptResourcePolicy.TryAcceptPositiveDrain(zeroState, out var zeroReason))
                {
                    refusal = zeroState is true
                        ? AutoConceptProjectionRefusal.Backpressure
                        : AutoConceptProjectionRefusal.Contract;
                    return FailProjection($"{ResourceName(entry.Resource)} {zeroReason}", out reason);
                }
                var trueIncrement = InvokeCompatible(entry.Resource, "GetTrueSpend", entry.NativeAmount);
                if (!BigAmount.TryRead(trueIncrement, out var adjustedIncrement) || adjustedIncrement.IsNegative)
                    return FailProjection("resource quality conversion failed", out reason);
                if (!BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(entry.Resource, "GetTrueRate"), out var currentRate))
                    return FailProjection("resource true rate is unavailable", out reason);
                if (!BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(entry.Resource, "GetModdedDrain"), out var currentDrain))
                    return FailProjection("resource drain rate is unavailable", out reason);
                var grossRate = currentRate.Add(currentDrain);
                var reserve = grossRate.IsNegative
                    ? default
                    : grossRate.Multiply(Math.Clamp(rateReservePercent, 0.0f, 100.0f) / 100.0);
                if (currentRate.Subtract(adjustedIncrement).CompareTo(reserve) < 0)
                {
                    refusal = AutoConceptProjectionRefusal.Backpressure;
                    return FailProjection($"{ResourceName(entry.Resource)} would fall below the configured rate reserve", out reason);
                }

                if (ReflectionUtil.InvokeNoArgs(entry.Resource, "HasMaxQuantity") is true &&
                    BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(entry.Resource, "GetQuantity"), out var quantity) &&
                    BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(entry.Resource, "GetTrueSoftCap"), out var capacity) &&
                    !capacity.IsZero && quantity.DivideApprox(capacity) * 100.0 < minimumResourcePercent)
                {
                    refusal = AutoConceptProjectionRefusal.Backpressure;
                    return FailProjection($"{ResourceName(entry.Resource)} is below the configured quantity floor", out reason);
                }
            }
            reason = "projected native drain is safe";
            refusal = AutoConceptProjectionRefusal.None;
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException || ex is MissingMethodException || ex is MemberAccessException || ex is OverflowException)
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
        var result = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        if (_activeValuesField!.GetValue(_activeConcepts) is not IEnumerable values) return result;
        foreach (var instance in values)
        {
            if (instance is null || instance.GetType() != _instanceType) continue;
            var recipe = ReflectionUtil.ReadMember(instance, "reference");
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
        if (candidate.Recipe.GetType() != _recipeType) return false;
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

    private static bool TryReadCostEntries(object? costList, out List<NativeCostEntry> result)
    {
        result = new List<NativeCostEntry>();
        if (costList is null || ReflectionUtil.InvokeNoArgs(costList, "GetEntries") is not IList entries) return false;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null) return false;
            var resource = ReflectionUtil.ReadMember(entry, "resource");
            var nativeAmount = ReflectionUtil.InvokeNoArgs(entry, "GetValue");
            if (resource is null || nativeAmount is null || !BigAmount.TryRead(nativeAmount, out var amount)) return false;
            result.Add(new NativeCostEntry(resource, nativeAmount, amount));
        }
        return true;
    }

    private static object? InvokeCompatible(object instance, string name, object argument)
    {
        foreach (var method in instance.GetType().GetMethods(ReflectionUtil.InstanceFlags))
        {
            var parameters = method.GetParameters();
            if (method.Name == name && parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument))
                return method.Invoke(instance, new[] { argument });
        }
        return null;
    }

    private static int ReadInt(object instance, string methodName, string fieldName)
    {
        var value = ReflectionUtil.InvokeNoArgs(instance, methodName) ?? FindField(instance.GetType(), fieldName, false)?.GetValue(instance);
        return Convert.ToInt32(value ?? 0);
    }

    private static bool InvokeBoolean(
        MethodInfo method,
        object instance,
        params object[] arguments)
    {
        var value = method.Invoke(instance, arguments);
        return value is bool result
            ? result
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.FullName}.{method.Name} returned no Boolean value.");
    }

    private static int InvokeInt(MethodInfo method, object instance, params object[] arguments)
    {
        var value = method.Invoke(instance, arguments);
        return value is int result
            ? result
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.FullName}.{method.Name} returned no Int32 value.");
    }

    private static bool Returns(MethodInfo? method, Type returnType) =>
        method is not null && method.ReturnType == returnType;

    private static FieldInfo? FindField(Type type, string name, bool isStatic)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, ReflectionUtil.InstanceFlags | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field is not null && field.IsStatic == isStatic) return field;
        }
        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name, params Type[] parameterTypes)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                ReflectionUtil.InstanceFlags | BindingFlags.DeclaredOnly,
                null,
                parameterTypes,
                null);
            if (method is not null) return method;
        }
        return null;
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

    private static string ResourceName(object resource)
    {
        var stableId = ReflectionUtil.ReadStableId(resource);
        return Guid.TryParse(stableId, out var uuid) && uuid != Guid.Empty
            ? EntityIdentityFormatter.Format(uuid)
            : "resource";
    }

    private readonly struct NativeCostEntry
    {
        public NativeCostEntry(object resource, object nativeAmount, BigAmount amount)
        {
            Resource = resource;
            NativeAmount = nativeAmount;
            Amount = amount;
        }
        public object Resource { get; }
        public object NativeAmount { get; }
        public BigAmount Amount { get; }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
