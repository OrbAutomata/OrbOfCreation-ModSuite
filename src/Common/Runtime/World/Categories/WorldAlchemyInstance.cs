using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One recipe named by the native ConceptRecipes registry.</summary>
internal readonly struct WorldConceptRecipe : IWorldEntity
{
    internal WorldConceptRecipe(
        Guid recipeId,
        Guid coreTypeId,
        bool canAddNow = true,
        int slotCount = 0)
    {
        RecipeId = recipeId;
        CoreTypeId = coreTypeId;
        CanAddNow = canAddNow;
        SlotCount = slotCount;
    }

    internal Guid RecipeId { get; }

    public Guid EntityId => RecipeId;

    /// <summary>The native slot family that prevents two incompatible concepts sharing one slot.</summary>
    internal Guid CoreTypeId { get; }

    /// <summary>Whether the authoritative Active Concepts list can admit this recipe now.</summary>
    internal bool CanAddNow { get; }

    /// <summary>
    /// How many slots the Active Concepts list holds, filled or not. A refusal to add is a fact
    /// about a full list, and a caller cannot read fullness out of the assignments alone — those
    /// are only the occupied ones.
    /// </summary>
    internal int SlotCount { get; }
}

/// <summary>One active Concept assignment as it stood when the world was collected.</summary>
internal readonly struct WorldAlchemyInstance
{
    internal WorldAlchemyInstance(
        Guid recipeId,
        int quantity,
        int queuedQuantity,
        bool drainReadable,
        BigDouble drainRatio)
    {
        RecipeId = recipeId;
        Quantity = Math.Max(0, quantity);
        QueuedQuantity = Math.Max(0, queuedQuantity);
        DrainReadable = drainReadable;
        DrainRatio = drainRatio;
    }

    internal Guid RecipeId { get; }
    internal int Quantity { get; }
    internal int QueuedQuantity { get; }
    internal bool IsSettled => Quantity == QueuedQuantity;

    /// <summary>
    /// Whether both the native ratio and current drain vector were readable. False is deliberately
    /// unsafe: the rollback watchdog may not turn missing evidence into permission to keep draining.
    /// </summary>
    internal bool DrainReadable { get; }
    internal BigDouble DrainRatio { get; }
}

/// <summary>The drain inputs captured from one active Concept assignment.</summary>
internal readonly struct RawWorldAlchemyInstance
{
    internal RawWorldAlchemyInstance(
        Guid recipeId,
        int quantity,
        int queuedQuantity,
        bool drainReadable,
        bool isDrainApplied,
        BigDouble currentRatio,
        BigDouble usageRatio)
    {
        RecipeId = recipeId;
        Quantity = quantity;
        QueuedQuantity = queuedQuantity;
        DrainReadable = drainReadable;
        IsDrainApplied = isDrainApplied;
        CurrentRatio = currentRatio;
        UsageRatio = usageRatio;
    }

    internal Guid RecipeId { get; }
    internal int Quantity { get; }
    internal int QueuedQuantity { get; }
    internal bool DrainReadable { get; }
    internal bool IsDrainApplied { get; }
    internal BigDouble CurrentRatio { get; }
    internal BigDouble UsageRatio { get; }
}

internal enum WorldAlchemyCostKind
{
    RecipeDrain = 0,
    CurrentDrain = 1,
    Bandwidth = 2,
}

internal static class WorldConceptRecipeLookup
{
    internal static bool TryFind(
        PublicationTable<WorldConceptRecipe> table,
        Guid recipeId,
        out WorldConceptRecipe recipe) =>
        WorldAlchemyRowLookup.TryFind(table, recipeId, static row => row.RecipeId, out recipe);
}

internal static class WorldAlchemyInstanceLookup
{
    internal static bool TryFind(
        PublicationTable<WorldAlchemyInstance> table,
        Guid recipeId,
        out WorldAlchemyInstance instance) =>
        WorldAlchemyRowLookup.TryFind(table, recipeId, static row => row.RecipeId, out instance);
}

internal static class WorldAlchemyRowLookup
{
    internal static bool TryFind<TRow>(
        PublicationTable<TRow> table,
        Guid recipeId,
        Func<TRow, Guid> readId,
        out TRow row)
        where TRow : struct
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = readId(rows[middle]).CompareTo(recipeId);
            if (comparison == 0)
            {
                row = rows[middle];
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        row = default;
        return false;
    }
}

internal sealed class WorldConceptRecipeBuffer
{
    private WorldConceptRecipe[] _samples = new WorldConceptRecipe[32];
    private int _count;
    internal int Count => _count;
    internal ref readonly WorldConceptRecipe this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;
    internal void Append(in WorldConceptRecipe sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal sealed class WorldAlchemyInstanceBuffer
{
    private RawWorldAlchemyInstance[] _samples = new RawWorldAlchemyInstance[16];
    private int _count;
    internal int Count => _count;
    internal ref readonly RawWorldAlchemyInstance this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;
    internal void Append(in RawWorldAlchemyInstance sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldAlchemyRowDeriver
{
    internal static PublicationTable<WorldConceptRecipe> Build(WorldConceptRecipeBuffer buffer)
    {
        var rows = new WorldConceptRecipe[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) => left.RecipeId.CompareTo(right.RecipeId));
        return PublicationTable<WorldConceptRecipe>.Create(rows, rows.Length);
    }

    internal static PublicationTable<WorldAlchemyInstance> Build(WorldAlchemyInstanceBuffer buffer)
    {
        var rows = new WorldAlchemyInstance[buffer.Count];
        for (var index = 0; index < buffer.Count; index++)
        {
            var sample = buffer[index];
            var ratio = !sample.IsDrainApplied
                ? BigDouble.One
                : sample.CurrentRatio.CompareTo(sample.UsageRatio) <= 0
                    ? sample.CurrentRatio
                    : sample.UsageRatio;
            rows[index] = new WorldAlchemyInstance(
                sample.RecipeId,
                sample.Quantity,
                sample.QueuedQuantity,
                sample.DrainReadable,
                ratio);
        }
        Array.Sort(rows, static (left, right) => left.RecipeId.CompareTo(right.RecipeId));
        return PublicationTable<WorldAlchemyInstance>.Create(rows, rows.Length);
    }
}

/// <summary>A single resource row in either a Concept recipe's authored or current drain vector.</summary>
internal readonly struct WorldAlchemyCost
{
    internal WorldAlchemyCost(
        Guid recipeId,
        WorldAlchemyCostKind kind,
        Guid resourceId,
        BigDouble amount,
        int targetQuantity = 0)
    {
        RecipeId = recipeId;
        Kind = kind;
        ResourceId = resourceId;
        Amount = amount;
        TargetQuantity = Math.Max(0, targetQuantity);
    }

    internal Guid RecipeId { get; }
    internal WorldAlchemyCostKind Kind { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
    internal int TargetQuantity { get; }
}

internal static class WorldAlchemyCostLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldAlchemyCost> table,
        Guid recipeId,
        WorldAlchemyCostKind kind,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = Compare(
                rows[middle].RecipeId, rows[middle].Kind, rows[middle].TargetQuantity,
                recipeId, kind, 0);
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               Compare(
                   rows[start + count].RecipeId, rows[start + count].Kind,
                   rows[start + count].TargetQuantity, recipeId, kind, 0) == 0)
        {
            count++;
        }

        return count > 0;
    }

    private static int Compare(
        Guid leftRecipe,
        WorldAlchemyCostKind leftKind,
        int leftTarget,
        Guid rightRecipe,
        WorldAlchemyCostKind rightKind,
        int rightTarget)
    {
        var byRecipe = leftRecipe.CompareTo(rightRecipe);
        if (byRecipe != 0) return byRecipe;
        var byKind = ((int)leftKind).CompareTo((int)rightKind);
        return byKind != 0 ? byKind : leftTarget.CompareTo(rightTarget);
    }
}

internal sealed class WorldAlchemyCostBuffer
{
    private const int InitialCapacity = 64;
    private WorldAlchemyCost[] _samples = new WorldAlchemyCost[InitialCapacity];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldAlchemyCost this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldAlchemyCost sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldAlchemyCostDeriver
{
    internal static PublicationTable<WorldAlchemyCost> Build(WorldAlchemyCostBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldAlchemyCost>.Empty;

        var rows = new WorldAlchemyCost[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, CostComparer.Instance);
        return PublicationTable<WorldAlchemyCost>.Create(rows, rows.Length);
    }

    private sealed class CostComparer : IComparer<WorldAlchemyCost>
    {
        internal static readonly IComparer<WorldAlchemyCost> Instance = new CostComparer();

        public int Compare(WorldAlchemyCost left, WorldAlchemyCost right)
        {
            var byRecipe = left.RecipeId.CompareTo(right.RecipeId);
            if (byRecipe != 0) return byRecipe;
            var byKind = ((int)left.Kind).CompareTo((int)right.Kind);
            if (byKind != 0) return byKind;
            var byTarget = left.TargetQuantity.CompareTo(right.TargetQuantity);
            return byTarget != 0 ? byTarget : left.ResourceId.CompareTo(right.ResourceId);
        }
    }
}

/// <summary>
/// Reads the two Concept registries together: the scoped recipes and their authored drains, followed
/// by the active instances and their current drains.
/// </summary>
internal sealed class WorldAlchemyInstanceReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _registryType;
    private readonly RuntimeIdentityRegistryBinding _registryBinding;
    private readonly Type? _activeListType;
    private readonly Type? _recipeListType;
    private readonly Type? _recipeType;
    private readonly Type? _instanceType;
    private readonly string _unavailable;

    private readonly Func<object, IList?>? _activeValues;
    private readonly Func<object, IList?>? _recipeValues;
    private readonly Func<object, object, bool>? _canAddInstance;
    private readonly Func<object, Guid>? _recipeId;
    private readonly Func<object, object?>? _coreType;
    private readonly Func<object, Guid>? _coreTypeId;
    private readonly Func<object, object?>? _recipeDrain;
    private readonly Func<object, object?>? _bandwidthCost;
    private readonly Func<object, int>? _advancementLevel;
    private readonly Func<object, object?>? _instanceScalingRef;
    private readonly Func<object, object?>? _instanceScaling;
    private readonly Func<object, Guid>? _instanceScalingId;
    private readonly Func<object, bool>? _useRarity;
    private readonly Func<object, IList?>? _rarityBlacklist;
    private readonly Func<object, object?>? _scalingConversion;
    private readonly Func<object, object?>? _scalingValues;
    private readonly Func<object, object?>? _listReferenceVariable;
    private readonly Func<object, object?>? _listVariableValue;
    private readonly Func<object, object?>? _drainCostMod;
    private readonly Func<object, object?>? _speed;
    private readonly Func<object, object?>? _freeUsageSlots;
    private readonly Func<object, object?>? _overdriveSpeed;
    private readonly Func<object, object?>? _overdriveDrain;
    private readonly Func<object, object?>? _completionCostAdvance;
    private readonly Func<object, object?>? _drainCostLevel;
    private readonly Func<object, object?>? _requirementCostPenalty;
    private readonly Func<object, object?>? _requirementSpeedPenalty;
    private readonly Func<object, int>? _modifierType;
    private readonly Func<object, int>? _modifierOrder;
    private readonly Func<object, BigDouble>? _modifierAmount;
    private readonly NativeModifierProgramReader? _programReader;
    private readonly Func<object, bool>? _instanceIsEmpty;
    private readonly Func<object, Guid>? _instanceRecipeId;
    private readonly Func<object, int>? _quantity;
    private readonly Func<object, int>? _queuedQuantity;
    private readonly Func<object, object?>? _resourceDrain;
    private readonly Func<object, bool>? _isDrainApplied;
    private readonly Func<object, BigDouble>? _currentRatio;
    private readonly Func<object, BigDouble>? _usageRatio;
    private readonly Func<object, object?>? _currentDrain;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _entryResourceId;
    private readonly Func<object, BigDouble>? _entryAmount;

    internal WorldAlchemyInstanceReader(
        Type? registryType,
        Type? activeListType,
        Type? recipeListType,
        Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _registryType = registryType;
        _registryBinding = new RuntimeIdentityRegistryBinding(
            () => registryType, requireStableIdentityContract: false);
        _activeListType = activeListType;
        _recipeListType = recipeListType;
        _recipeType = resolveType("AlchemyRecipeSO");

        if (registryType is null || activeListType is null || recipeListType is null || _recipeType is null)
        {
            _unavailable = "the Concept registry types were not found on this build";
            return;
        }

        _activeValues = NativeAccessorBinder.CollectionField(activeListType, "value");
        _recipeValues = NativeAccessorBinder.CollectionField(recipeListType, "value");
        _canAddInstance = NativeAccessorBinder.CallWithObjectArgument<bool>(
            activeListType.GetMethod("CanAddInstance", Instance, null, new[] { _recipeType! }, null));
        _instanceType = NativeAccessorBinder.CollectionElementType(activeListType, "value");
        _recipeId = NativeAccessorBinder.Call<Guid>(_recipeType, "GetGuid");
        _coreType = NativeAccessorBinder.CallValue(
            _recipeType.GetMethod("GetCoreType", Instance, null, Type.EmptyTypes, null));
        var alchemyType = resolveType("AlchemyTypeSO");
        _coreTypeId = NativeAccessorBinder.Call<Guid>(alchemyType, "GetGuid");
        _recipeDrain = NativeAccessorBinder.Reference(_recipeType, "drainCost");
        _bandwidthCost = NativeAccessorBinder.Reference(_recipeType, "bandwidthCost");
        _advancementLevel = NativeAccessorBinder.Field<int>(_recipeType, "advancementLevel");

        _instanceScalingRef = NativeAccessorBinder.Reference(_recipeType, "instanceScaling");
        var scalingRefType = _recipeType.GetField("instanceScaling", Instance)?.FieldType;
        _instanceScaling = NativeAccessorBinder.Reference(scalingRefType, "scaling");
        var scalingType = resolveType("InstanceScalingSO");
        _instanceScalingId = NativeAccessorBinder.Call<Guid>(scalingType, "GetGuid");
        _useRarity = NativeAccessorBinder.Field<bool>(scalingType, "useRarity");
        _rarityBlacklist = NativeAccessorBinder.CollectionField(scalingType, "rarityAttributeBlacklist");
        _scalingConversion = NativeAccessorBinder.Reference(scalingType, "instanceScaling");
        var conversionType = scalingType?.GetField("instanceScaling", Instance)?.FieldType;
        _scalingValues = NativeAccessorBinder.ReadValue(FindInstanceField(conversionType, "values"));

        var listRefType = resolveType("ModifierListRef");
        var listVariableType = resolveType("ModifierListVariable");
        var listType = resolveType("ValueModifierList");
        _listReferenceVariable = NativeAccessorBinder.Reference(listRefType, "variable");
        _listVariableValue = NativeAccessorBinder.Reference(listVariableType, "value");
        _drainCostMod = NativeAccessorBinder.Reference(_recipeType, "drainCostMod");
        _speed = NativeAccessorBinder.Reference(_recipeType, "speed");
        _freeUsageSlots = NativeAccessorBinder.Reference(_recipeType, "freeUsageSlots");
        _overdriveSpeed = NativeAccessorBinder.Reference(_recipeType, "overdriveSpeed");
        _overdriveDrain = NativeAccessorBinder.Reference(_recipeType, "overdriveDrainCostMod");
        _completionCostAdvance = NativeAccessorBinder.Reference(_recipeType, "completionCostAdvanceMod");
        _drainCostLevel = NativeAccessorBinder.Reference(_recipeType, "drainCostLevelMod");
        _requirementCostPenalty = NativeAccessorBinder.BoxedField(alchemyType, "reqCostPenalty");
        _requirementSpeedPenalty = NativeAccessorBinder.BoxedField(alchemyType, "reqSpeedPenalty");

        var valueModifierType = alchemyType?.GetField("reqCostPenalty", Instance)?.FieldType;
        _modifierType = NativeAccessorBinder.EnumField(valueModifierType, "type");
        _modifierOrder = NativeAccessorBinder.Field<int>(valueModifierType, "order");
        _modifierAmount = NativeAccessorBinder.Field<BigDouble>(valueModifierType, "adjustReal");
        _programReader = new NativeModifierProgramReader(resolveType("ValueModifierRecord"), listType);

        _instanceIsEmpty = NativeAccessorBinder.Call<bool>(_instanceType, "IsEmpty");
        _instanceRecipeId = NativeAccessorBinder.CallReferenceGuid(_instanceType, "get_reference");
        _quantity = NativeAccessorBinder.Field<int>(_instanceType, "quantity");
        _queuedQuantity = NativeAccessorBinder.Field<int>(_instanceType, "queuedQuantity");
        _resourceDrain = NativeAccessorBinder.Reference(_instanceType, "resourceDrain");

        var drainType = _instanceType?.GetField("resourceDrain", Instance)?.FieldType;
        _isDrainApplied = NativeAccessorBinder.Field<bool>(drainType, "isDrainApplied");
        _currentRatio = NativeAccessorBinder.Field<BigDouble>(drainType, "currentRatio");
        _usageRatio = NativeAccessorBinder.Field<BigDouble>(drainType, "usageRatio");
        _currentDrain = NativeAccessorBinder.CallValue(
            drainType?.GetMethod("GetCurrentDrain", Instance, null, Type.EmptyTypes, null));

        var costListType = _recipeType.GetField("drainCost", Instance)?.FieldType;
        var entryType = NativeAccessorBinder.CollectionElementType(costListType, "costs");
        _costEntries = NativeAccessorBinder.CollectionField(costListType, "costs");
        _entryResourceId = NativeAccessorBinder.ReferenceGuid(entryType, "resource");
        _entryAmount = NativeAccessorBinder.Field<BigDouble>(entryType, "valueBig");
        _unavailable = IsBound()
            ? string.Empty
            : "the active Concept instance or drain-vector members were unavailable";
    }

    public string Category => "concept instances";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        frame.ConceptRecipes.Reset();
        frame.AlchemyInstances.Reset();
        frame.AlchemyCosts.Reset();
        frame.ConceptDrainBasis.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var source = _registryBinding.Read();
        if (!source.IsReady || source.Registry is null)
            return WorldCategoryReport.Missing(Category, source.Reason);
        var registry = source.Registry;

        var recipeList = registry[KnownEntities.ConceptRecipes.Uuid];
        var activeList = registry[KnownEntities.ActiveConcepts.Uuid];
        if (recipeList is null || activeList is null)
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 0, 0, string.Empty);
        if (recipeList.GetType() != _recipeListType || activeList.GetType() != _activeListType)
            return WorldCategoryReport.Missing(Category, "a Concept registry held an unexpected native type");

        try
        {
            var recipes = _recipeValues!(recipeList);
            var active = _activeValues!(activeList);
            var conceptIds = new HashSet<Guid>();
            var capturedScalings = new HashSet<Guid>();
            var sampled = 0;
            var skipped = 0;
            var firstFailure = string.Empty;

            for (var index = 0; index < (recipes?.Count ?? 0); index++)
            {
                var recipe = recipes![index];
                if (recipe is null || recipe.GetType() != _recipeType)
                {
                    Skip(ref skipped, ref firstFailure, $"recipe {index} had an unexpected native type");
                    continue;
                }

                var id = _recipeId!(recipe);
                var coreObject = _coreType!(recipe);
                var core = coreObject is null ? Guid.Empty : _coreTypeId!(coreObject);
                if (id == Guid.Empty || core == Guid.Empty || !conceptIds.Add(id))
                {
                    Skip(ref skipped, ref firstFailure, $"recipe {index} had an invalid identity or core type");
                    continue;
                }

                var canAddNow = _canAddInstance!(activeList, recipe);
                frame.ConceptRecipes.Append(
                    new WorldConceptRecipe(id, core, canAddNow, active?.Count ?? 0));
                AppendCosts(id, WorldAlchemyCostKind.RecipeDrain, _recipeDrain!(recipe), frame.AlchemyCosts);
                AppendCosts(id, WorldAlchemyCostKind.Bandwidth, _bandwidthCost!(recipe), frame.AlchemyCosts);
                CaptureFormulaBasis(id, recipe, coreObject!, core, capturedScalings, frame);
                sampled++;
            }

            for (var index = 0; index < (active?.Count ?? 0); index++)
            {
                var instance = active![index];
                if (instance is null || instance.GetType() != _instanceType)
                {
                    Skip(ref skipped, ref firstFailure, $"active instance {index} had an unexpected native type");
                    continue;
                }

                if (_instanceIsEmpty!(instance)) continue;

                var id = _instanceRecipeId!(instance);
                if (!conceptIds.Contains(id))
                {
                    Skip(ref skipped, ref firstFailure, $"active instance {index} did not name a scoped Concept recipe");
                    continue;
                }

                var drain = _resourceDrain!(instance);
                var current = drain is null ? null : _currentDrain!(drain);
                var readable = drain is not null && current is not null;
                if (readable)
                    AppendCosts(id, WorldAlchemyCostKind.CurrentDrain, current, frame.AlchemyCosts);
                frame.AlchemyInstances.Append(new RawWorldAlchemyInstance(
                    id,
                    _quantity!(instance),
                    _queuedQuantity!(instance),
                    readable,
                    readable && _isDrainApplied!(drain!),
                    readable ? _currentRatio!(drain!) : default,
                    readable ? _usageRatio!(drain!) : default));
            }

            return new WorldCategoryReport(
                Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
        }
        // Every native fault degrades this one category rather than the pass. The allowlist this
        // replaces led with TargetInvocationException, which is the wrapper MethodInfo.Invoke put
        // around every fault the game raised; a compiled accessor hands the fault back unwrapped, so
        // naming exception types here would let a native NullReferenceException past a collector that
        // does not wrap reader calls. Same fail-closed answer, same granularity, still loud.
        catch (Exception ex)
        {
            return WorldCategoryReport.Missing(
                Category, $"reading Concept instances threw: {ex.GetBaseException().Message}");
        }
    }

    private void AppendCosts(
        Guid recipeId,
        WorldAlchemyCostKind kind,
        object? costList,
        WorldAlchemyCostBuffer destination)
    {
        if (costList is null) return;
        var entries = _costEntries!(costList);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index];
            if (entry is null) continue;
            var resourceId = _entryResourceId!(entry);
            if (resourceId == Guid.Empty) continue;
            destination.Append(new WorldAlchemyCost(
                recipeId, kind, resourceId, _entryAmount!(entry)));
        }
    }

    private void CaptureFormulaBasis(
        Guid recipeId,
        object recipe,
        object core,
        Guid coreId,
        HashSet<Guid> capturedScalings,
        GameWorldCycleFrame frame)
    {
        var scalingReference = _instanceScalingRef!(recipe) ??
            throw new InvalidOperationException("instanceScaling was null");
        var scaling = _instanceScaling!(scalingReference) ??
            throw new InvalidOperationException("instanceScaling.scaling was null");
        var scalingId = _instanceScalingId!(scaling);
        if (scalingId == Guid.Empty)
            throw new InvalidOperationException("instanceScaling carried no identity");

        var useRarity = _useRarity!(scaling);
        var blacklist = _rarityBlacklist!(scaling);
        var costUsesRarity = useRarity && !ContainsScalingKind(blacklist, 4);
        var speedUsesRarity = useRarity && !ContainsScalingKind(blacklist, 6);

        var reqCost = ReadModifier(_requirementCostPenalty!(core));
        var reqSpeed = ReadModifier(_requirementSpeedPenalty!(core));
        frame.ConceptDrainBasis.Append(new RawConceptDrainBasis(
            recipeId,
            coreId,
            scalingId,
            _advancementLevel!(recipe),
            in reqCost,
            in reqSpeed,
            costUsesRarity,
            speedUsesRarity));

        CaptureRecord(recipeId, WorldModifierProgramRole.ConceptDrain, _drainCostMod!(recipe), frame);
        CaptureRecord(recipeId, WorldModifierProgramRole.ConceptSpeed, _speed!(recipe), frame);
        CaptureRecord(
            recipeId, WorldModifierProgramRole.ConceptFreeUsageSlots, _freeUsageSlots!(recipe), frame);
        CaptureRecord(
            recipeId, WorldModifierProgramRole.ConceptOverdriveSpeed, _overdriveSpeed!(recipe), frame);
        CaptureRecord(
            recipeId, WorldModifierProgramRole.ConceptOverdriveDrain, _overdriveDrain!(recipe), frame);
        CaptureList(
            recipeId, WorldModifierProgramRole.ConceptCompletionCost,
            _completionCostAdvance!(recipe), frame);
        CaptureList(
            recipeId, WorldModifierProgramRole.ConceptDrainLevel,
            _drainCostLevel!(recipe), frame);

        if (!capturedScalings.Add(scalingId)) return;
        var conversion = _scalingConversion!(scaling) ??
            throw new InvalidOperationException("instanceScaling.instanceScaling was null");
        var values = _scalingValues!(conversion) as IDictionary;
        CaptureList(
            scalingId, WorldModifierProgramRole.InstanceScalingCost,
            FindScalingList(values, 4), frame, resolved: true);
        CaptureList(
            scalingId, WorldModifierProgramRole.InstanceScalingSpeed,
            FindScalingList(values, 6), frame, resolved: true);
    }

    private void CaptureRecord(
        Guid owner,
        WorldModifierProgramRole role,
        object? record,
        GameWorldCycleFrame frame)
    {
        if (record is null) throw new InvalidOperationException($"{role} record was null");
        _programReader!.CaptureRecord(
            owner, role, record, frame.ModifierPrograms, frame.ModifierProgramEntries);
    }

    private void CaptureList(
        Guid owner,
        WorldModifierProgramRole role,
        object? referenceOrList,
        GameWorldCycleFrame frame,
        bool resolved = false)
    {
        object? list = referenceOrList;
        if (!resolved)
        {
            var variable = referenceOrList is null ? null : _listReferenceVariable!(referenceOrList);
            list = variable is null ? null : _listVariableValue!(variable);
        }
        _programReader!.CaptureList(
            owner, role, list, frame.ModifierPrograms, frame.ModifierProgramEntries);
    }

    private GameValueModifier ReadModifier(object? modifier)
    {
        if (modifier is null) throw new InvalidOperationException("a requirement penalty was null");
        return new GameValueModifier(
            (GameValueModifierType)_modifierType!(modifier),
            _modifierAmount!(modifier),
            _modifierOrder!(modifier));
    }

    private static bool ContainsScalingKind(IList? source, int kind)
    {
        for (var index = 0; index < (source?.Count ?? 0); index++)
            if (source![index] is not null && Convert.ToInt32(source[index]) == kind) return true;
        return false;
    }

    private static object? FindScalingList(IDictionary? source, int kind)
    {
        if (source is null) return null;
        foreach (DictionaryEntry pair in source)
            if (pair.Key is not null && Convert.ToInt32(pair.Key) == kind) return pair.Value;
        return null;
    }

    private static FieldInfo? FindInstanceField(Type? type, string name)
    {
        while (type is not null)
        {
            var field = type.GetField(name, Instance | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
            type = type.BaseType;
        }
        return null;
    }

    private bool IsBound() =>
        _activeValues is not null && _recipeValues is not null && _canAddInstance is not null &&
        _instanceType is not null &&
        _recipeId is not null && _coreType is not null && _coreTypeId is not null &&
        _recipeDrain is not null && _bandwidthCost is not null && _advancementLevel is not null &&
        _instanceScalingRef is not null && _instanceScaling is not null &&
        _instanceScalingId is not null && _useRarity is not null && _rarityBlacklist is not null &&
        _scalingConversion is not null && _scalingValues is not null &&
        _listReferenceVariable is not null && _listVariableValue is not null &&
        _drainCostMod is not null && _speed is not null && _freeUsageSlots is not null &&
        _overdriveSpeed is not null && _overdriveDrain is not null &&
        _completionCostAdvance is not null && _drainCostLevel is not null &&
        _requirementCostPenalty is not null && _requirementSpeedPenalty is not null &&
        _modifierType is not null && _modifierOrder is not null && _modifierAmount is not null &&
        _programReader?.IsAvailable == true &&
        _instanceIsEmpty is not null && _instanceRecipeId is not null &&
        _quantity is not null && _queuedQuantity is not null &&
        _resourceDrain is not null && _isDrainApplied is not null &&
        _currentRatio is not null && _usageRatio is not null && _currentDrain is not null &&
        _costEntries is not null && _entryResourceId is not null && _entryAmount is not null;

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
