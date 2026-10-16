using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldLevelableCost
{
    internal WorldLevelableCost(Guid resourceId, BigDouble amount)
    {
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal readonly struct WorldLevelableDecision
{
    private readonly PublicationTable<WorldLevelableCost>? _paidCosts;
    private readonly PublicationTable<WorldLevelableCost>? _bonusCosts;

    internal WorldLevelableDecision(
        int totalLevel,
        int bonusLevels,
        bool canPurchase,
        bool purchaseAffordable,
        PublicationTable<WorldLevelableCost> paidCosts,
        bool supportsBonus,
        bool bonusResourcesVisible,
        bool bonusAffordable,
        PublicationTable<WorldLevelableCost> bonusCosts)
    {
        TotalLevel = totalLevel;
        BonusLevels = bonusLevels;
        CanPurchase = canPurchase;
        PurchaseAffordable = purchaseAffordable;
        _paidCosts = paidCosts ?? throw new ArgumentNullException(nameof(paidCosts));
        SupportsBonus = supportsBonus;
        BonusResourcesVisible = bonusResourcesVisible;
        BonusAffordable = bonusAffordable;
        _bonusCosts = bonusCosts ?? throw new ArgumentNullException(nameof(bonusCosts));
    }

    internal int TotalLevel { get; }
    internal int BonusLevels { get; }
    internal bool CanPurchase { get; }
    internal bool PurchaseAffordable { get; }
    internal PublicationTable<WorldLevelableCost> PaidCosts =>
        _paidCosts ?? PublicationTable<WorldLevelableCost>.Empty;
    internal bool SupportsBonus { get; }
    internal bool BonusResourcesVisible { get; }
    internal bool BonusAffordable { get; }
    internal PublicationTable<WorldLevelableCost> BonusCosts =>
        _bonusCosts ?? PublicationTable<WorldLevelableCost>.Empty;
}

/// <summary>Reads the two controls shared by the four concrete level-list families.</summary>
internal sealed class WorldLevelableDecisionBinding
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly bool _supportsBonus;
    private readonly Func<object, int>? _level;
    private readonly Func<object, bool>? _canLevel;
    private readonly Func<object, object?>? _levelCost;
    private readonly Func<object, int>? _bonusLevels;
    private readonly Func<object, object?>? _bonusCost;
    private readonly Func<object, bool>? _hasEnough;
    private readonly Func<object, bool>? _allResourcesVisible;
    private readonly Func<object, IList?>? _entries;
    private readonly Func<object, object?>? _resource;
    private readonly Func<object, BigDouble>? _amount;
    private readonly Func<object, Guid>? _resourceId;

    internal WorldLevelableDecisionBinding(
        Type entityType,
        bool supportsBonus,
        Func<string, Type?> resolveType)
    {
        _supportsBonus = supportsBonus;
        var costType = resolveType("ResourceCostList");
        var tupleType = resolveType("ResourceTuple");
        var resourceType = resolveType("ResourceSO");
        if (costType is null || tupleType is null || resourceType is null)
        {
            Failure = "Level decision cost types were unavailable";
            return;
        }

        var item = new WorldMemberBinding(entityType, entityType.Name);
        _level = item.Call<int>("GetLevel");
        _canLevel = item.Call<bool>("CanLevel");
        _levelCost = item.CallObject("GetLevelCost", costType);
        if (supportsBonus)
        {
            _bonusLevels = item.Call<int>("GetFreeLevels");
            _bonusCost = item.CallObject("GetFreeLevelCost", costType);
        }

        var cost = new WorldMemberBinding(costType, "ResourceCostList");
        _hasEnough = cost.Call<bool>("HasEnough");
        _allResourcesVisible = cost.Call<bool>("AllResourcesVisible");
        var entriesMethod = costType.GetMethod("GetEntries", Instance, null, Type.EmptyTypes, null);
        var entryType = entriesMethod?.ReturnType is { IsGenericType: true } listType
            ? listType.GetGenericArguments()[0]
            : null;
        _entries = cost.CallList("GetEntries", entryType);
        var tuple = new WorldMemberBinding(tupleType, "ResourceTuple");
        _resource = tuple.Reference("resource", resourceType);
        _amount = tuple.Call<BigDouble>("GetValue");
        var resource = new WorldMemberBinding(resourceType, "ResourceSO");
        _resourceId = resource.Call<Guid>("GetGuid");
        Failure = Join(item.Failure, cost.Failure, tuple.Failure, resource.Failure);
    }

    internal string Failure { get; }

    internal WorldLevelableDecision Read(object entity)
    {
        var canLevel = _canLevel!(entity);
        var paidCosts = PublicationTable<WorldLevelableCost>.Empty;
        var paidAffordable = false;
        if (canLevel)
        {
            var cost = _levelCost!(entity) ??
                throw new InvalidOperationException("GetLevelCost returned null");
            paidAffordable = _hasEnough!(cost);
            paidCosts = ReadCosts(cost);
        }

        var bonusLevels = 0;
        var bonusVisible = false;
        var bonusAffordable = false;
        var bonusCosts = PublicationTable<WorldLevelableCost>.Empty;
        if (_supportsBonus)
        {
            bonusLevels = _bonusLevels!(entity);
            var cost = _bonusCost!(entity) ??
                throw new InvalidOperationException("GetFreeLevelCost returned null");
            bonusVisible = _allResourcesVisible!(cost);
            if (bonusVisible)
            {
                bonusAffordable = _hasEnough!(cost);
                bonusCosts = ReadCosts(cost);
            }
        }

        return new WorldLevelableDecision(
            _level!(entity), bonusLevels, canLevel, paidAffordable, paidCosts,
            _supportsBonus, bonusVisible, bonusAffordable, bonusCosts);
    }

    private PublicationTable<WorldLevelableCost> ReadCosts(object cost)
    {
        var entries = _entries!(cost) ??
            throw new InvalidOperationException("ResourceCostList.GetEntries returned null");
        var rows = new WorldLevelableCost[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("Level cost row " + index + " was null");
            var resource = _resource!(entry) ??
                throw new InvalidOperationException("Level cost row " + index + " had no resource");
            rows[index] = new WorldLevelableCost(_resourceId!(resource), _amount!(entry));
        }
        return PublicationTable<WorldLevelableCost>.Create(rows);
    }

    private static string Join(params string[] values)
    {
        var result = string.Empty;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].Length == 0) continue;
            result = result.Length == 0 ? values[index] : result + "; " + values[index];
        }
        return result;
    }
}
