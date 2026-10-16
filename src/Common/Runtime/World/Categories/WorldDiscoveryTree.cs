using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One discovery tree as published, including the copied identities and exact native cost evidence
/// needed to decide the next offer action without attempting it.
/// </summary>
internal readonly struct WorldDiscoveryTree : IWorldEntity
{
    internal WorldDiscoveryTree(
        Guid treeId,
        bool visible,
        int actionMode,
        BigDouble actionTime,
        int rerollsLeft,
        bool usedRerollsLastDiscover,
        Guid selectedChoiceId,
        Guid[] currentOfferIds,
        bool hasImmediateRequiredDiscovery,
        bool nextItemAffordable,
        WorldDiscoveryTreeCost[] nextItemCosts,
        Guid overrideRerollsId,
        Guid overrideChoicesId,
        int additionalDiscoveryChoices,
        int discoveryBonusLevelCost,
        bool debugMode,
        int totalDiscoveredCount,
        int poolDiscoveredCount,
        int totalDiscoverableCount,
        int poolDiscoverableCount,
        bool hasRequiredDiscovery,
        bool hasRemainingDiscovery,
        bool hasCompletedAllDiscoveries)
    {
        TreeId = treeId;
        Visible = visible;
        ActionMode = actionMode;
        ActionTime = actionTime;
        RerollsLeft = rerollsLeft;
        UsedRerollsLastDiscover = usedRerollsLastDiscover;
        SelectedChoiceId = selectedChoiceId;
        CurrentOfferIds = currentOfferIds is null || currentOfferIds.Length == 0
            ? PublicationTable<Guid>.Empty
            : PublicationTable<Guid>.Create(currentOfferIds);
        HasImmediateRequiredDiscovery = hasImmediateRequiredDiscovery;
        NextItemAffordable = nextItemAffordable;
        NextItemCosts = nextItemCosts is null || nextItemCosts.Length == 0
            ? PublicationTable<WorldDiscoveryTreeCost>.Empty
            : PublicationTable<WorldDiscoveryTreeCost>.Create(nextItemCosts);
        OverrideRerollsId = overrideRerollsId;
        OverrideChoicesId = overrideChoicesId;
        AdditionalDiscoveryChoices = additionalDiscoveryChoices;
        DiscoveryBonusLevelCost = discoveryBonusLevelCost;
        DebugMode = debugMode;
        TotalDiscoveredCount = totalDiscoveredCount;
        PoolDiscoveredCount = poolDiscoveredCount;
        TotalDiscoverableCount = totalDiscoverableCount;
        PoolDiscoverableCount = poolDiscoverableCount;
        HasRequiredDiscovery = hasRequiredDiscovery;
        HasRemainingDiscovery = hasRemainingDiscovery;
        HasCompletedAllDiscoveries = hasCompletedAllDiscoveries;
    }

    internal Guid TreeId { get; }

    public Guid EntityId => TreeId;

    internal bool Visible { get; }

    /// <summary>The game's mode enum as its underlying integer; see <see cref="WorldChallenge.State"/>.</summary>
    internal int ActionMode { get; }

    /// <summary>Seconds left on the action in progress.</summary>
    internal BigDouble ActionTime { get; }

    internal int RerollsLeft { get; }

    internal bool UsedRerollsLastDiscover { get; }

    /// <summary>
    /// Which choice the player has selected, or <see cref="Guid.Empty"/> when none is. The game holds
    /// this as a <c>GuidContainer</c>, so it is already an identity rather than a live reference.
    /// </summary>
    internal Guid SelectedChoiceId { get; }

    internal PublicationTable<Guid> CurrentOfferIds { get; }

    internal bool HasImmediateRequiredDiscovery { get; }

    internal bool NextItemAffordable { get; }

    internal PublicationTable<WorldDiscoveryTreeCost> NextItemCosts { get; }

    /// <summary>
    /// The variables that override the reroll and choice counts, when the tree is configured to use
    /// them. Empty when it is not. As with the alchemy type's selected level, the values live in the
    /// global registry and only the edge belongs here.
    /// </summary>
    internal Guid OverrideRerollsId { get; }

    internal Guid OverrideChoicesId { get; }

    /// <summary>
    /// The rest of the tree's runtime state: the counts the game caches about what has been discovered,
    /// and the flags it derives from them.
    /// </summary>
    internal int AdditionalDiscoveryChoices { get; }

    internal int DiscoveryBonusLevelCost { get; }

    internal bool DebugMode { get; }

    internal int TotalDiscoveredCount { get; }

    internal int PoolDiscoveredCount { get; }

    /// <summary>
    /// What the two cached counts are counts <em>of</em>: the sizes of the two lists
    /// <c>CountDiscoveredItems()</c> walks. A numerator with no denominator left every progress
    /// question — how far into this tree am I, is this the last one — unanswerable from the wire.
    /// </summary>
    internal int TotalDiscoverableCount { get; }

    internal int PoolDiscoverableCount { get; }

    internal bool HasRequiredDiscovery { get; }

    internal bool HasRemainingDiscovery { get; }

    internal bool HasCompletedAllDiscoveries { get; }
}

internal readonly struct WorldDiscoveryTreeCost
{
    internal WorldDiscoveryTreeCost(Guid resourceId, BigDouble amount, BigDouble availableAmount)
    {
        ResourceId = resourceId;
        Amount = amount;
        AvailableAmount = availableAmount;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
    internal BigDouble AvailableAmount { get; }
}

internal sealed class WorldDiscoveryTreeBinder : WorldPlainBinder<WorldDiscoveryTree>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _visible;
    private Func<object, int>? _actionMode;
    private Func<object, BigDouble>? _actionTime;
    private Func<object, int>? _rerollsLeft;
    private Func<object, bool>? _usedRerolls;
    private Func<object, Guid>? _selectedChoice;
    private Func<object, IList?>? _currentOffers;
    private Func<object, Guid>? _offerGuid;
    private Func<object, bool>? _hasImmediateRequired;
    private Func<object, object?>? _getNextCost;
    private Func<object, bool>? _hasEnough;
    private Func<object, IList?>? _costEntries;
    private Func<object, object?>? _costResource;
    private Func<object, BigDouble>? _costValue;
    private Func<object, Guid>? _resourceGuid;
    private Func<object, BigDouble>? _resourceQuantity;
    private Func<object, Guid>? _overrideRerolls;
    private Func<object, Guid>? _overrideChoices;
    private Func<object, int>? _additionalDiscoveryChoices;
    private Func<object, int>? _discoveryBonusLevelCost;
    private Func<object, bool>? _debugMode;
    private Func<object, int>? _totalDiscoveredCount;
    private Func<object, int>? _poolDiscoveredCount;
    private Func<object, IList?>? _allDiscoverableItems;
    private Func<object, IList?>? _mainDiscoverableItemPool;
    private Func<object, bool>? _hasRequiredDiscovery;
    private Func<object, bool>? _hasRemainingDiscovery;
    private Func<object, bool>? _hasCompletedAllDiscoveries;

    internal override string Category => "discovery trees";

    internal override string TypeName => "DiscoveryTreeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _visible = bind.Call<bool>("IsVisible");
        _actionMode = bind.EnumField("actionMode");
        _actionTime = bind.Field<BigDouble>("actionTime");
        _rerollsLeft = bind.Field<int>("rerollsLeft");
        _usedRerolls = bind.Field<bool>("usedRerollsLastDiscover");
        _selectedChoice = bind.ReferenceGuid("selectedChoiceId");
        _currentOffers = bind.CollectionField("currentChoiceIds");
        var offer = bind.Elements(
            bind.CollectionElementType("currentChoiceIds"),
            "DiscoveryTreeSO.currentChoiceIds[]");
        _offerGuid = offer.Call<Guid>("get_guid");
        _hasImmediateRequired = bind.Call<bool>("HasImmediateRequiredDiscover");

        var nextCostMethod = type.GetMethod(
            "GetNextItemCost",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        var costType = nextCostMethod?.ReturnType;
        _getNextCost = bind.CallObject("GetNextItemCost", costType);
        var cost = new WorldMemberBinding(costType!, "ResourceCostList");
        _hasEnough = cost.Call<bool>("HasEnough");
        var entriesMethod = costType?.GetMethod(
            "GetEntries",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        var entriesType = entriesMethod?.ReturnType;
        var tupleType = entriesType is { IsGenericType: true }
            ? entriesType.GetGenericArguments()[0]
            : null;
        _costEntries = cost.CallList("GetEntries", tupleType);
        var tuple = new WorldMemberBinding(tupleType!, "ResourceTuple");
        var resourceType = tupleType?.GetField(
            "resource",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.FieldType;
        _costResource = tuple.Reference("resource", resourceType);
        _costValue = tuple.Call<BigDouble>("GetValue");
        var resource = new WorldMemberBinding(resourceType!, "ResourceSO");
        _resourceGuid = resource.Call<Guid>("GetGuid");
        _resourceQuantity = resource.Call<BigDouble>("GetQuantity");
        _overrideRerolls = bind.ReferenceGuid("overrideDiscoveryRerolls");
        _overrideChoices = bind.ReferenceGuid("overrideDiscoveryChoices");
        _additionalDiscoveryChoices = bind.Field<int>("additionalDiscoveryChoices");
        _discoveryBonusLevelCost = bind.Field<int>("discoveryBonusLevelCost");
        _debugMode = bind.Field<bool>("debugMode");
        _totalDiscoveredCount = bind.Field<int>("totalDiscoveredCount");
        _poolDiscoveredCount = bind.Field<int>("poolDiscoveredCount");
        // The two counts the game caches are numerators over exactly these two lists —
        // CountDiscoveredItems() is Count(discovered) over each — so the denominators come from the
        // same two fields rather than from a second walk this suite invented.
        _allDiscoverableItems = bind.CollectionField("allDiscoverableItems");
        _mainDiscoverableItemPool = bind.CollectionField("mainDiscoverableItemPool");
        _hasRequiredDiscovery = bind.Field<bool>("hasRequiredDiscovery");
        _hasRemainingDiscovery = bind.Field<bool>("hasRemainingDiscovery");
        _hasCompletedAllDiscoveries = bind.Field<bool>("hasCompletedAllDiscoveries");
        return JoinFailures(bind.Failure, cost.Failure, tuple.Failure, resource.Failure);
    }

    internal override WorldDiscoveryTree Read(object entity)
    {
        var mode = _actionMode!(entity);
        var immediateRequired = _hasImmediateRequired!(entity);
        var offers = mode == 2 ? ReadOffers(entity) : Array.Empty<Guid>();
        var costs = Array.Empty<WorldDiscoveryTreeCost>();
        var affordable = false;
        if (mode == 0 && (_hasRemainingDiscovery!(entity) || immediateRequired))
        {
            var cost = _getNextCost!(entity) ??
                throw new InvalidOperationException(
                    "DiscoveryTreeSO.GetNextItemCost returned no ResourceCostList");
            affordable = _hasEnough!(cost);
            costs = ReadCosts(cost);
        }

        return new WorldDiscoveryTree(
            _id!(entity),
            _visible!(entity),
            mode,
            _actionTime!(entity),
            _rerollsLeft!(entity),
            _usedRerolls!(entity),
            _selectedChoice!(entity),
            offers,
            immediateRequired,
            affordable,
            costs,
            _overrideRerolls!(entity),
            _overrideChoices!(entity),
            _additionalDiscoveryChoices!(entity),
            _discoveryBonusLevelCost!(entity),
            _debugMode!(entity),
            _totalDiscoveredCount!(entity),
            _poolDiscoveredCount!(entity),
            Size(_allDiscoverableItems!(entity), "allDiscoverableItems"),
            Size(_mainDiscoverableItemPool!(entity), "mainDiscoverableItemPool"),
            _hasRequiredDiscovery!(entity),
            _hasRemainingDiscovery!(entity),
            _hasCompletedAllDiscoveries!(entity));
    }

    private static int Size(IList? source, string name) =>
        source?.Count ??
        throw new InvalidOperationException("DiscoveryTreeSO." + name + " was null");

    private Guid[] ReadOffers(object entity)
    {
        var source = _currentOffers!(entity) ??
            throw new InvalidOperationException("DiscoveryTreeSO.currentChoiceIds was null");
        var result = new Guid[source.Count];
        var seen = new HashSet<Guid>();
        for (var index = 0; index < source.Count; index++)
        {
            var value = source[index] ??
                throw new InvalidOperationException(
                    "DiscoveryTreeSO.currentChoiceIds contained a null identity");
            var id = _offerGuid!(value);
            if (id == Guid.Empty || !seen.Add(id))
                throw new InvalidOperationException(
                    "DiscoveryTreeSO.currentChoiceIds contained an empty or duplicate identity");
            result[index] = id;
        }
        return result;
    }

    private WorldDiscoveryTreeCost[] ReadCosts(object cost)
    {
        var entries = _costEntries!(cost) ??
            throw new InvalidOperationException("ResourceCostList.GetEntries returned no list");
        var result = new WorldDiscoveryTreeCost[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var tuple = entries[index] ??
                throw new InvalidOperationException("ResourceCostList.GetEntries contained null");
            var resource = _costResource!(tuple) ??
                throw new InvalidOperationException("ResourceTuple.resource was null");
            var resourceId = _resourceGuid!(resource);
            if (resourceId == Guid.Empty)
                throw new InvalidOperationException("ResourceTuple.resource had no stable UUID");
            result[index] = new WorldDiscoveryTreeCost(
                resourceId,
                _costValue!(tuple),
                _resourceQuantity!(resource));
        }
        return result;
    }

    private static string JoinFailures(params string[] failures)
    {
        var result = string.Empty;
        for (var index = 0; index < failures.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(failures[index])) continue;
            result = result.Length == 0 ? failures[index] : result + "; " + failures[index];
        }
        return result;
    }
}
