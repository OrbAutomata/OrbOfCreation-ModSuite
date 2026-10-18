using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the native equipment loadout click pipeline.</summary>
internal sealed class EquipmentLoadoutNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "equipment-loadout.equipment.type-action", "equipment-loadout.manager.type-action",
        "equipment-loadout.list.type-action", "equipment-loadout.equipment-type.type-action",
        "equipment-loadout.cost.type-action", "equipment-loadout.manager-instance-action",
        "equipment-loadout.manager-equipped-list-action", "equipment-loadout.equipment-created-action",
        "equipment-loadout.equipment-type-field-action", "equipment-loadout.equipment-maximum-action",
        "equipment-loadout.equipment-cost-action", "equipment-loadout.list-stacks-action",
        "equipment-loadout.list-maximum-action",
        "equipment-loadout.list-values-action", "equipment-loadout.list-type-count-action",
        "equipment-loadout.type-maximum-action", "equipment-loadout.cost-enough-action",
        "equipment-loadout.cost-maximum-times-action", "equipment-loadout.manager-equip-action",
        "equipment-loadout.manager-unequip-action",
    };

    private EquipmentLoadoutNativeBindings(Type equipmentType, Type managerType,
        Func<object?> manager, Func<object, object?> equippedList, Func<object, bool> isCreated,
        Func<object, object?> readEquipmentType, Func<object, int> maximumStacks,
        Func<object, object?> usageCost, Func<object, object, int> stacks,
        Func<object, int> maximumSlots, Func<object, IList?> values,
        Func<object, object, int> typeCount, Func<object, int> typeMaximum,
        Func<object, bool> hasEnough, Func<object, BigDouble> maximumTimes,
        Action<object, object> equip, Action<object, object> unequip)
    {
        EquipmentType = equipmentType; ManagerType = managerType; Manager = manager;
        EquippedList = equippedList; IsCreated = isCreated; ReadEquipmentType = readEquipmentType;
        MaximumStacks = maximumStacks; UsageCost = usageCost; Stacks = stacks;
        MaximumSlots = maximumSlots; Values = values;
        TypeCount = typeCount; TypeMaximum = typeMaximum; HasEnough = hasEnough;
        MaximumTimes = maximumTimes;
        Equip = equip; Unequip = unequip;
    }

    internal Type EquipmentType { get; }
    internal Type ManagerType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> EquippedList { get; }
    internal Func<object, bool> IsCreated { get; }
    internal Func<object, object?> ReadEquipmentType { get; }
    internal Func<object, int> MaximumStacks { get; }
    internal Func<object, object?> UsageCost { get; }
    internal Func<object, object, int> Stacks { get; }
    internal Func<object, int> MaximumSlots { get; }
    internal Func<object, IList?> Values { get; }
    internal Func<object, object, int> TypeCount { get; }
    internal Func<object, int> TypeMaximum { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Func<object, BigDouble> MaximumTimes { get; }
    internal Action<object, object> Equip { get; }
    internal Action<object, object> Unequip { get; }

    internal static bool TryCreate(out EquipmentLoadoutNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("equipment loadout", ContractIds, resolveType, includeContract, set =>
        {
            var equipment = set.Type("equipment-loadout.equipment.type-action", "EquipmentSO");
            var managerType = set.Type("equipment-loadout.manager.type-action", "EquipmentManager");
            var list = set.Type("equipment-loadout.list.type-action", "EquipmentListVariable");
            var equipmentKind = set.Type("equipment-loadout.equipment-type.type-action", "EquipmentTypeSO");
            var cost = set.Type("equipment-loadout.cost.type-action", "ResourceCostList");
            return new EquipmentLoadoutNativeBindings(equipment, managerType,
                set.StaticField<Func<object?>>(
                    "equipment-loadout.manager-instance-action", managerType, "instance", managerType),
                set.Field<Func<object, object?>>(
                    "equipment-loadout.manager-equipped-list-action", managerType, "equippedEquipment", list),
                set.Call<Func<object, bool>>(
                    "equipment-loadout.equipment-created-action", equipment, "IsCreated", typeof(bool)),
                set.Field<Func<object, object?>>(
                    "equipment-loadout.equipment-type-field-action", equipment, "equipmentType", equipmentKind),
                set.Call<Func<object, int>>(
                    "equipment-loadout.equipment-maximum-action", equipment, "GetMaxLevel", typeof(int)),
                set.Call<Func<object, object?>>(
                    "equipment-loadout.equipment-cost-action", equipment, "GetUsageCost", cost),
                set.Call<Func<object, object, int>>(
                    "equipment-loadout.list-stacks-action", list, "GetStacks", typeof(int), equipment),
                set.Call<Func<object, int>>("equipment-loadout.list-maximum-action", list, "GetMax", typeof(int)),
                set.Field<Func<object, IList?>>("equipment-loadout.list-values-action", list, "value",
                    typeof(System.Collections.Generic.List<>).MakeGenericType(equipment)),
                set.Call<Func<object, object, int>>(
                    "equipment-loadout.list-type-count-action", list, "GetTypesEquipped", typeof(int), equipmentKind),
                set.Call<Func<object, int>>(
                    "equipment-loadout.type-maximum-action", equipmentKind, "GetMaxTypeSlots", typeof(int)),
                set.Call<Func<object, bool>>("equipment-loadout.cost-enough-action", cost, "HasEnough", typeof(bool)),
                set.Call<Func<object, BigDouble>>(
                    "equipment-loadout.cost-maximum-times-action", cost, "MaximumCostTimes", typeof(BigDouble)),
                set.Call<Action<object, object>>(
                    "equipment-loadout.manager-equip-action", managerType, "EquipItem", typeof(void), equipment),
                set.Call<Action<object, object>>(
                    "equipment-loadout.manager-unequip-action", managerType, "UnEquipItem", typeof(void), equipment));
        }, out bindings, out reason);
}
