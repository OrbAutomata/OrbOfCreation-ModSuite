using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldAlchemyLoadoutDecision : IWorldEntity
{
    internal WorldAlchemyLoadoutDecision(Guid recipeId, int position, int slotCount, int amount,
        int targetAmount, int freeUsesRemaining, int maximumAdd,
        bool discovered, bool canAdd)
    {
        RecipeId = recipeId;
        Position = position;
        SlotCount = Math.Max(slotCount, 0);
        Amount = Math.Max(amount, 0);
        TargetAmount = Math.Max(targetAmount, 0);
        FreeUsesRemaining = Math.Max(freeUsesRemaining, 0);
        MaximumAdd = Math.Max(maximumAdd, 0);
        Discovered = discovered;
        CanAdd = canAdd;
    }

    internal Guid RecipeId { get; }
    public Guid EntityId => RecipeId;
    internal int Position { get; }
    internal int SlotCount { get; }
    internal int Amount { get; }
    internal int TargetAmount { get; }
    internal int FreeUsesRemaining { get; }
    internal int MaximumAdd { get; }
    internal bool Discovered { get; }
    internal bool CanAdd { get; }
    internal bool IsActive => Position >= 0 && TargetAmount > 0;
}

internal readonly struct WorldAlchemyUsageCost
{
    internal WorldAlchemyUsageCost(Guid recipeId, Guid resourceId, BigDouble amount)
    {
        RecipeId = recipeId;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid RecipeId { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal static class WorldAlchemyLoadoutLookup
{
    internal static bool TryFind(PublicationTable<WorldAlchemyLoadoutDecision> table,
        Guid recipeId, out WorldAlchemyLoadoutDecision decision) =>
        WorldAlchemyRowLookup.TryFind(table, recipeId, static row => row.RecipeId, out decision);

    internal static bool TryFindCostRange(PublicationTable<WorldAlchemyUsageCost> table,
        Guid recipeId, out int start, out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].RecipeId.CompareTo(recipeId) < 0) low = middle + 1;
            else high = middle - 1;
        }
        start = low;
        count = 0;
        while (start + count < rows.Length && rows[start + count].RecipeId == recipeId) count++;
        return count > 0;
    }
}

/// <summary>
/// Reads the ordered ordinary-alchemy list owned by <c>AlchemyManager</c>. Concept assignments
/// remain owned by <see cref="WorldAlchemyInstanceReader"/> and never enter these tables.
/// </summary>
internal sealed class WorldAlchemyLoadoutReader : IWorldCategoryReader
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _managerType;
    private readonly Type? _recipeType;
    private readonly Type? _instanceType;
    private readonly Func<object?>? _manager;
    private readonly Func<object, object?>? _activeList;
    private readonly Func<object, object?>? _recipeList;
    private readonly Func<object, IList?>? _activeValues;
    private readonly Func<object, IList?>? _recipeValues;
    private readonly Func<object, Guid>? _recipeId;
    private readonly Func<object, Guid>? _coreTypeId;
    private readonly Func<object, bool>? _discovered;
    private readonly Func<object, object?>? _usageCost;
    private readonly Func<object, int>? _freeUses;
    private readonly Func<object, int>? _maximumUses;
    private readonly Func<object, object, bool>? _canAdd;
    private readonly Func<object, object?>? _instanceRecipe;
    private readonly Func<object, int>? _amount;
    private readonly Func<object, int>? _targetAmount;
    private readonly Func<object, int>? _remainingFree;
    private readonly Func<object, int>? _remainingMaximum;
    private readonly Func<object, BigDouble>? _maximumCostTimes;
    private readonly Func<object, bool>? _costIsEmpty;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _costResourceId;
    private readonly Func<object, BigDouble>? _costAmount;
    private readonly string _unavailable;

    internal WorldAlchemyLoadoutReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _managerType = resolveType("AlchemyManager");
        var listType = resolveType("AlchemyInstanceListVariable");
        var recipeListType = resolveType("AlchemyRecipeListVariable");
        _recipeType = resolveType("AlchemyRecipeSO");
        _instanceType = resolveType("AlchemyInstance");
        var costType = resolveType("ResourceCostList");
        if (_managerType is null || listType is null || recipeListType is null ||
            _recipeType is null || _instanceType is null || costType is null)
        {
            _unavailable = "the ordinary alchemy types were not found on this build";
            return;
        }

        _manager = NativeAccessorBinder.StaticReference(_managerType, "instance");
        _activeList = NativeAccessorBinder.Reference(_managerType, "activeAlchemy");
        _recipeList = NativeAccessorBinder.Reference(_managerType, "allAlchemy");
        _activeValues = NativeAccessorBinder.CollectionField(listType, "value");
        _recipeValues = NativeAccessorBinder.CollectionField(recipeListType, "value");
        _recipeId = NativeAccessorBinder.Call<Guid>(_recipeType, "GetGuid");
        _coreTypeId = NativeAccessorBinder.CallReferenceGuid(_recipeType, "GetCoreType");
        _discovered = NativeAccessorBinder.Call<bool>(_recipeType, "IsDiscovered");
        _usageCost = NativeAccessorBinder.CallObject(_recipeType, "GetUsageCost", costType);
        _freeUses = NativeAccessorBinder.Call<int>(_recipeType, "GetFreeUsageSlots");
        _maximumUses = NativeAccessorBinder.Call<int>(_recipeType, "GetMaxUsageSlots");
        _canAdd = NativeAccessorBinder.CallWithObjectArgument<bool>(
            listType.GetMethod("CanAddInstance", Instance, null, new[] { _recipeType }, null));
        _instanceRecipe = NativeAccessorBinder.CallObject(_instanceType, "get_reference", _recipeType);
        _amount = NativeAccessorBinder.Field<int>(_instanceType, "quantity");
        _targetAmount = NativeAccessorBinder.Call<int>(_instanceType, "GetQueuedQuantity");
        _remainingFree = NativeAccessorBinder.Call<int>(_instanceType, "GetRemainingFreeUsageSlots");
        _remainingMaximum = NativeAccessorBinder.Call<int>(_instanceType, "GetRemainingMaxUsageSlots");
        _maximumCostTimes = NativeAccessorBinder.Call<BigDouble>(costType, "MaximumCostTimes");
        _costIsEmpty = NativeAccessorBinder.Call<bool>(costType, "IsEmpty");
        _costEntries = NativeAccessorBinder.CollectionField(costType, "costs");
        var costEntryType = NativeAccessorBinder.CollectionElementType(costType, "costs");
        _costResourceId = NativeAccessorBinder.ReferenceGuid(costEntryType, "resource");
        _costAmount = NativeAccessorBinder.Call<BigDouble>(costEntryType, "GetValue");
        _unavailable = IsBound() ? string.Empty :
            "the ordinary alchemy list or decision members were unavailable";
    }

    public string Category => "ordinary alchemy loadout";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        frame.AlchemyLoadout.Reset();
        frame.AlchemyUsageCosts.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);
        try
        {
            var manager = _manager!();
            if (manager is null || manager.GetType() != _managerType)
                return WorldCategoryReport.Missing(Category, "AlchemyManager.instance is unavailable");
            var activeList = _activeList!(manager);
            var recipeList = _recipeList!(manager);
            if (activeList is null || recipeList is null)
                return WorldCategoryReport.Missing(Category, "the ordinary alchemy lists are unavailable");
            var active = _activeValues!(activeList);
            var recipes = _recipeValues!(recipeList);
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
                var recipeId = _recipeId!(recipe);
                if (recipeId == Guid.Empty || !IsOrdinaryCore(_coreTypeId!(recipe))) continue;
                object? instance = null;
                var position = -1;
                for (var activeIndex = 0; activeIndex < (active?.Count ?? 0); activeIndex++)
                {
                    var candidate = active![activeIndex];
                    if (candidate is null || candidate.GetType() != _instanceType) continue;
                    var reference = _instanceRecipe!(candidate);
                    if (reference is null || reference.GetType() != _recipeType ||
                        _recipeId!(reference) != recipeId) continue;
                    instance = candidate;
                    position = activeIndex;
                    break;
                }
                var cost = _usageCost!(recipe) ??
                    throw new InvalidOperationException("AlchemyRecipeSO.GetUsageCost returned null");
                var free = Math.Max(instance is null ? _freeUses!(recipe) : _remainingFree!(instance), 0);
                var remaining = Math.Max(instance is null ? _maximumUses!(recipe) : _remainingMaximum!(instance), 0);
                var maximumByCost = _costIsEmpty!(cost)
                    ? int.MaxValue
                    : Math.Max((_maximumCostTimes!(cost) + new BigDouble(free)).ToInt(), 0);
                var canAdd = _canAdd!(activeList, recipe);
                frame.AlchemyLoadout.Append(new WorldAlchemyLoadoutDecision(
                    recipeId, position, active?.Count ?? 0, instance is null ? 0 : _amount!(instance),
                    instance is null ? 0 : _targetAmount!(instance), free,
                    Math.Min(remaining, maximumByCost), _discovered!(recipe), canAdd));
                AppendCosts(recipeId, cost, frame.AlchemyUsageCosts);
                sampled++;
            }
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected,
                sampled, skipped, firstFailure);
        }
        // Every native fault degrades this one category rather than the pass. The allowlist this
        // replaces led with TargetInvocationException, the wrapper MethodInfo.Invoke put around every
        // fault the game raised; a compiled accessor hands the fault back unwrapped, so naming
        // exception types here would let a native NullReferenceException past a collector that does
        // not wrap reader calls. Same fail-closed answer, same granularity, still loud.
        catch (Exception exception)
        {
            return WorldCategoryReport.Missing(Category,
                "reading ordinary alchemy threw: " + exception.GetBaseException().Message);
        }
    }

    private void AppendCosts(Guid recipeId, object cost,
        WorldRelationBuffer<WorldAlchemyUsageCost> destination)
    {
        var entries = _costEntries!(cost);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index];
            if (entry is null) continue;
            var resourceId = _costResourceId!(entry);
            if (resourceId != Guid.Empty)
                destination.Append(new WorldAlchemyUsageCost(
                    recipeId, resourceId, _costAmount!(entry)));
        }
    }

    private bool IsBound() => _manager is not null && _activeList is not null &&
        _recipeList is not null && _activeValues is not null && _recipeValues is not null &&
        _recipeId is not null && _coreTypeId is not null && _discovered is not null &&
        _usageCost is not null && _freeUses is not null && _maximumUses is not null &&
        _canAdd is not null && _instanceRecipe is not null && _amount is not null &&
        _targetAmount is not null && _remainingFree is not null && _remainingMaximum is not null &&
        _maximumCostTimes is not null && _costIsEmpty is not null && _costEntries is not null &&
        _costResourceId is not null && _costAmount is not null;

    private static bool IsOrdinaryCore(Guid id) => id == KnownEntities.Alchemy.Uuid ||
        id == KnownEntities.Brewing.Uuid || id == KnownEntities.Dismantle.Uuid ||
        id == KnownEntities.Enchantment.Uuid || id == KnownEntities.Refinement.Uuid ||
        id == KnownEntities.Transmutation.Uuid;

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
