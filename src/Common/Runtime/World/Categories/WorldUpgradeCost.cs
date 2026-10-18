using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One entry of an upgrade's per-level cost modifier list, as read.
/// </summary>
/// <remarks>
/// The game's <c>ValueModifierList</c> is two lists — modifiers and exponents — and the exponents are
/// not decoration: they strengthen the modifiers before any of them touches the cost. So the flag
/// travels with the entry rather than the two being flattened into one sequence.
/// </remarks>
internal readonly struct RawLevelCostModifier
{
    internal RawLevelCostModifier(
        Guid entityId,
        int modifierType,
        BigDouble amount,
        int order,
        bool isExponent)
    {
        EntityId = entityId;
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
        IsExponent = isExponent;
    }

    internal Guid EntityId { get; }
    internal int ModifierType { get; }
    internal BigDouble Amount { get; }
    internal int Order { get; }
    internal bool IsExponent { get; }
}

/// <summary>
/// Each upgrade's per-level cost modifiers, held where a cycle can own them.
/// </summary>
/// <remarks>
/// Same shape and same bargain as <see cref="WorldPurchaseCostBuffer"/>: several rows per entity,
/// contiguous per entity, reused across cycles, readings only.
/// </remarks>
internal sealed class WorldLevelCostModifierBuffer
{
    private const int InitialCapacity = 64;

    private RawLevelCostModifier[] _samples = new RawLevelCostModifier[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly RawLevelCostModifier this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in RawLevelCostModifier sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>
/// Reads every upgrade's authored cost and the modifier list it grows by.
/// </summary>
/// <remarks>
/// <para>
/// An upgrade's cost list is read the same way a structure's is. The per-level modifier is not: it is
/// a <c>ModifierListRef</c>, which resolves either to a variable the upgrade names or to one of nine
/// shared standards hanging off <c>GlobalValues.instance</c>. This reader resolves it by calling
/// <c>GetValue()</c> — a field lookup on either path, with nothing to recalculate — and reads the
/// resulting list's contents.
/// </para>
/// <para>
/// That is deliberately unlike <see cref="WorldStructureBinder"/>, which carries its per-quantity
/// modifier as an <em>identity</em> into the global registry. A single modifier has a registry with a
/// stable id; a modifier <em>list</em> reached through a reference type would need the eight-way
/// <c>refType</c>-to-<c>GlobalValues</c> mapping reproduced here to name which variable it landed on,
/// for a value the math needs by content rather than by identity. Reading the contents is smaller and
/// says the same thing.
/// </para>
/// </remarks>
internal sealed class WorldUpgradeCostReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _upgradeType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _upgradeId;
    private readonly Func<object, object?>? _resourceCost;
    private readonly Func<object, IList?>? _entries;
    private readonly Func<object, Guid>? _entryResource;
    private readonly Func<object, BigDouble>? _entryValue;

    private readonly Func<IList?>? _upgrades;
    private readonly Func<object, object?>? _costModPerLevel;
    private readonly Func<object, object?>? _resolveList;
    private readonly Func<object, IList?>? _listModifiers;
    private readonly Func<object, IList?>? _listExponents;
    private readonly Func<object, int>? _modifierType;
    private readonly Func<object, BigDouble>? _modifierAmount;
    private readonly Func<object, int>? _modifierOrder;

    internal WorldUpgradeCostReader(Type? upgradeType, Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        _upgradeType = upgradeType;
        if (upgradeType is null)
        {
            _unavailable = "the UpgradeSO type was not found on this build";
            return;
        }

        _upgrades = NativeAccessorBinder.StaticListAccessor(upgradeType, "All");
        _upgradeId = NativeAccessorBinder.Call<Guid>(upgradeType, "GetGuid");
        _resourceCost = NativeAccessorBinder.Reference(upgradeType, "resourceCost");
        var costListType = upgradeType.GetField("resourceCost", Instance)?.FieldType;
        var entryType = NativeAccessorBinder.CollectionElementType(costListType, "costs");
        _entries = NativeAccessorBinder.CollectionField(costListType, "costs");
        _entryResource = NativeAccessorBinder.ReferenceGuid(entryType, "resource");
        _entryValue = NativeAccessorBinder.Field<BigDouble>(entryType, "valueBig");

        _costModPerLevel = NativeAccessorBinder.Reference(upgradeType, "resourceCostModPerLevel");
        var refType = upgradeType.GetField("resourceCostModPerLevel", Instance)?.FieldType;
        var listType = refType?.GetMethod("GetValue", Instance, null, Type.EmptyTypes, null)?.ReturnType;
        _resolveList = NativeAccessorBinder.CallObject(refType, "GetValue", listType);
        _listModifiers = NativeAccessorBinder.CollectionField(listType, "modifiers");
        _listExponents = NativeAccessorBinder.CollectionField(listType, "exponents");

        var modifierType = NativeAccessorBinder.CollectionElementType(listType, "modifiers");
        _modifierType = NativeAccessorBinder.EnumField(modifierType, "type");
        _modifierAmount = NativeAccessorBinder.Field<BigDouble>(modifierType, "adjustReal");
        _modifierOrder = NativeAccessorBinder.Field<int>(modifierType, "order");

        _unavailable = IsFullyBound()
            ? string.Empty
            : "UpgradeSO did not expose its authored cost and per-level modifiers on this build";
    }

    public string Category => "upgrade costs";

    public bool IsAvailable => _upgradeType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var costs = frame.PurchaseCosts;
        var modifiers = frame.LevelCostModifiers;
        modifiers.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var upgrades = _upgrades?.Invoke();
        if (upgrades is null)
            return WorldCategoryReport.Missing(Category, "the UpgradeSO registry was unreadable");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        for (var index = 0; index < upgrades.Count; index++)
        {
            var upgrade = upgrades[index];
            if (upgrade is null) continue;

            try
            {
                sampled += Read(upgrade, costs, modifiers);
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = $"reading an upgrade cost threw: {ex.GetBaseException().Message}";
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private int Read(
        object upgrade,
        WorldPurchaseCostBuffer costs,
        WorldLevelCostModifierBuffer modifiers)
    {
        var entityId = _upgradeId!(upgrade);
        if (entityId == Guid.Empty) return 0;

        var costList = _resourceCost!(upgrade);
        if (costList is null) return 0;

        var entries = _entries!(costList);
        if (entries is null) return 0;

        var appended = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null) continue;

            var resourceId = _entryResource!(entry);
            if (resourceId == Guid.Empty) continue;

            costs.Append(new RawPurchaseCost(entityId, resourceId, _entryValue!(entry)));
            appended++;
        }

        if (appended > 0) ReadPerLevelModifiers(upgrade, entityId, modifiers);
        return appended;
    }

    private void ReadPerLevelModifiers(
        object upgrade,
        Guid entityId,
        WorldLevelCostModifierBuffer modifiers)
    {
        var reference = _costModPerLevel!(upgrade);
        if (reference is null) return;

        var list = _resolveList!(reference);
        if (list is null) return;

        Append(_listModifiers!(list), entityId, isExponent: false, modifiers);
        Append(_listExponents!(list), entityId, isExponent: true, modifiers);
    }

    private void Append(
        IList? source,
        Guid entityId,
        bool isExponent,
        WorldLevelCostModifierBuffer modifiers)
    {
        if (source is null) return;

        for (var index = 0; index < source.Count; index++)
        {
            var modifier = source[index];
            if (modifier is null) continue;

            modifiers.Append(new RawLevelCostModifier(
                entityId,
                _modifierType!(modifier),
                _modifierAmount!(modifier),
                _modifierOrder!(modifier),
                isExponent));
        }
    }

    private bool IsFullyBound() =>
        _upgradeId is not null && _resourceCost is not null && _entries is not null &&
        _entryResource is not null && _entryValue is not null && _costModPerLevel is not null &&
        _resolveList is not null && _listModifiers is not null && _listExponents is not null &&
        _modifierType is not null && _modifierAmount is not null && _modifierOrder is not null;
}
