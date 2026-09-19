using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldEquipmentUsageCost
{
    internal WorldEquipmentUsageCost(Guid resourceId, BigDouble cost)
    {
        ResourceId = resourceId;
        Cost = cost;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
}

internal readonly struct WorldEquipmentDecision
{
    private readonly PublicationTable<WorldEquipmentUsageCost>? _costs;

    internal WorldEquipmentDecision(
        bool available,
        string unavailableReason,
        Guid equipmentTypeId,
        int equippedStacks,
        int maximumStacks,
        int usedSlots,
        int maximumSlots,
        int typeUsedSlots,
        int typeMaximumSlots,
        int maximumEquipAmount,
        int maximumUnequipAmount,
        bool usageAffordable,
        PublicationTable<WorldEquipmentUsageCost> costs)
    {
        Available = available;
        UnavailableReason = unavailableReason ?? string.Empty;
        EquipmentTypeId = equipmentTypeId;
        EquippedStacks = equippedStacks;
        MaximumStacks = maximumStacks;
        UsedSlots = usedSlots;
        MaximumSlots = maximumSlots;
        TypeUsedSlots = typeUsedSlots;
        TypeMaximumSlots = typeMaximumSlots;
        MaximumEquipAmount = maximumEquipAmount;
        MaximumUnequipAmount = maximumUnequipAmount;
        UsageAffordable = usageAffordable;
        _costs = costs;
    }

    internal bool Available { get; }
    internal string UnavailableReason { get; }
    internal Guid EquipmentTypeId { get; }
    internal int EquippedStacks { get; }
    internal int MaximumStacks { get; }
    internal int UsedSlots { get; }
    internal int MaximumSlots { get; }
    internal int TypeUsedSlots { get; }
    internal int TypeMaximumSlots { get; }
    internal int MaximumEquipAmount { get; }
    internal int MaximumUnequipAmount { get; }
    internal bool UsageAffordable { get; }
    internal PublicationTable<WorldEquipmentUsageCost> Costs =>
        _costs ?? PublicationTable<WorldEquipmentUsageCost>.Empty;
}

/// <summary>One main-thread read of the exact artifact stack decision the native UI will submit.</summary>
internal sealed class WorldEquipmentDecisionBinding
{
    private readonly Type? _managerType;
    private readonly Type? _listType;
    private readonly Func<object?>? _manager;
    private readonly Func<object, object?>? _equipped;
    private readonly Func<object, IList?>? _values;
    private readonly Func<object, int>? _maximum;
    private readonly Func<object, bool>? _atMaximum;
    private readonly Func<object, object, int>? _stacks;
    private readonly Func<object, object, int>? _typeSlots;
    private readonly Func<object, object?>? _equipmentType;
    private readonly Func<object, Guid>? _typeId;
    private readonly Func<object, int>? _typeMaximum;
    private readonly Func<object, int>? _maximumStacks;
    private readonly Func<object, object?>? _usageCost;
    private readonly Func<object, bool>? _hasEnough;
    private readonly Func<object, BigDouble>? _maximumTimes;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, object?>? _costResource;
    private readonly Func<object, BigDouble>? _costValue;
    private readonly Func<object, Guid>? _resourceId;

    internal WorldEquipmentDecisionBinding(Type equipment, Func<string, Type?> resolve)
    {
        _managerType = resolve("EquipmentManager");
        _listType = resolve("EquipmentListVariable");
        var type = resolve("EquipmentTypeSO");
        var cost = resolve("ResourceCostList");
        var entry = resolve("ResourceTuple");
        var resource = resolve("ResourceSO");

        _manager = BindStaticReference(_managerType, "instance", _managerType);
        _equipped = NativeAccessorBinder.Reference(_managerType, "equippedEquipment", _listType);
        _values = NativeAccessorBinder.CollectionField(_listType, "value");
        _maximum = NativeAccessorBinder.Call<int>(_listType, "GetMax");
        _atMaximum = NativeAccessorBinder.Call<bool>(_listType, "IsAtMax");
        _stacks = NativeAccessorBinder.CallWithObjectArgument<int>(_listType, "GetStacks", equipment);
        _typeSlots = NativeAccessorBinder.CallWithObjectArgument<int>(
            _listType, "GetTypesEquipped", type);
        _equipmentType = NativeAccessorBinder.Reference(equipment, "equipmentType", type);
        _typeId = NativeAccessorBinder.Call<Guid>(type, "GetGuid");
        _typeMaximum = NativeAccessorBinder.Call<int>(type, "GetMaxTypeSlots");
        _maximumStacks = NativeAccessorBinder.Call<int>(equipment, "GetMaxLevel");
        _usageCost = NativeAccessorBinder.CallObject(equipment, "GetUsageCost", cost);
        _hasEnough = NativeAccessorBinder.Call<bool>(cost, "HasEnough");
        _maximumTimes = NativeAccessorBinder.Call<BigDouble>(cost, "MaximumCostTimes");
        _costEntries = NativeAccessorBinder.CallList(cost, "GetEntries", entry);
        _costResource = NativeAccessorBinder.Reference(entry, "resource", resource);
        _costValue = NativeAccessorBinder.Call<BigDouble>(entry, "GetValue");
        _resourceId = NativeAccessorBinder.Call<Guid>(resource, "GetGuid");

        Failure = _managerType is null || _listType is null || type is null || cost is null ||
            entry is null || resource is null ||
            _manager is null || _equipped is null || _values is null || _maximum is null ||
            _atMaximum is null || _stacks is null || _typeSlots is null || _equipmentType is null ||
            _typeId is null || _typeMaximum is null || _maximumStacks is null || _usageCost is null ||
            _hasEnough is null || _maximumTimes is null || _costEntries is null ||
            _costResource is null || _costValue is null || _resourceId is null
                ? "the complete equipment loadout decision binding set was unavailable"
                : string.Empty;
    }

    internal string Failure { get; }

    internal WorldEquipmentDecision Read(object equipment)
    {
        var manager = _manager!();
        if (manager is null || manager.GetType() != _managerType)
            return Unavailable("equipment_manager_unavailable");
        var list = _equipped!(manager);
        if (list is null || list.GetType() != _listType)
            return Unavailable("equipment_loadout_unavailable");
        var values = _values!(list);
        if (values is null) return Unavailable("equipment_loadout_unavailable");
        var equipmentType = _equipmentType!(equipment);
        if (equipmentType is null) return Unavailable("equipment_type_unavailable");
        var cost = _usageCost!(equipment);
        if (cost is null) return Unavailable("usage_cost_unavailable");
        var stacks = Math.Max(_stacks!(list, equipment), 0);
        var maximumStacks = Math.Max(_maximumStacks!(equipment), 0);
        var typeUsed = Math.Max(_typeSlots!(list, equipmentType), 0);
        var typeMaximum = Math.Max(_typeMaximum!(equipmentType), 0);
        var maximum = Math.Max(_maximum!(list), 0);
        var maximumTimes = Math.Max(BigDouble.Floor(_maximumTimes!(cost)).ToInt(), 0);
        var listContains = ContainsExactly(values, equipment, out var duplicate);
        if (duplicate || listContains != (stacks > 0))
            return Unavailable("equipment_stack_identity_inconsistent");
        var hasSlot = listContains || (!_atMaximum!(list) && typeUsed < typeMaximum);
        var maximumEquip = hasSlot
            ? Math.Min(Math.Max(maximumStacks - stacks, 0), maximumTimes)
            : 0;
        var maximumUnequip = stacks;
        var entries = _costEntries!(cost);
        var rows = new WorldEquipmentUsageCost[entries?.Count ?? 0];
        for (var index = 0; index < rows.Length; index++)
        {
            var entryValue = entries![index] ??
                throw new InvalidOperationException("equipment usage cost entry " + index + " was null");
            var resourceValue = _costResource!(entryValue) ??
                throw new InvalidOperationException("equipment usage cost entry " + index + " had no resource");
            rows[index] = new WorldEquipmentUsageCost(
                _resourceId!(resourceValue),
                _costValue!(entryValue));
        }
        return new WorldEquipmentDecision(
            true,
            string.Empty,
            _typeId!(equipmentType),
            stacks,
            maximumStacks,
            values.Count,
            maximum,
            typeUsed,
            typeMaximum,
            maximumEquip,
            maximumUnequip,
            _hasEnough!(cost),
            PublicationTable<WorldEquipmentUsageCost>.Create(rows));
    }

    private static bool ContainsExactly(IList values, object target, out bool duplicate)
    {
        var found = false;
        duplicate = false;
        for (var index = 0; index < values.Count; index++)
        {
            if (!ReferenceEquals(values[index], target)) continue;
            if (found) duplicate = true;
            found = true;
        }
        return found;
    }

    private static WorldEquipmentDecision Unavailable(string reason) =>
        new(false, reason, Guid.Empty, 0, 0, 0, 0, 0, 0, 0, 0, false,
            PublicationTable<WorldEquipmentUsageCost>.Empty);

    private static Func<object?>? BindStaticReference(Type? owner, string name, Type? exactType)
    {
        var field = owner?.GetField(
            name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is null || field.FieldType != exactType || exactType is null) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(
                Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();
        }
        catch (Exception) { return null; }
    }

    private static Func<object?>? BindStaticObjectCall(Type? owner, string name, Type? exactType)
    {
        var method = owner?.GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (method is null || method.ReturnType != exactType || exactType is null) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(
                Expression.Convert(Expression.Call(method), typeof(object))).Compile();
        }
        catch (Exception) { return null; }
    }
}
