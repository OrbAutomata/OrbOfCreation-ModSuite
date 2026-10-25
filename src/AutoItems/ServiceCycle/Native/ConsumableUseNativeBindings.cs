using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>The complete native use capability shared by automation and explicit player presses.</summary>
internal abstract class ConsumableUseNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "consumable.type",
        "consumable-usage.type",
        "inventory.type",
        "targeting.manager.type-action",
        "consumable.is-visible",
        "consumable.can-fire",
        "consumable.select-and-fire",
        "consumable.set-randomization",
        "consumable.is-randomized",
        "consumable.can-be-randomized",
        "consumable.get-queued",
        "consumable.consumable-usages",
        "inventory.can-use-consumable",
        "targeting-manager.is-targeting",
    };

    protected ConsumableUseNativeBindings(NativeBindingSet set)
    {
        ConsumableType = set.Type("consumable.type", "ConsumableSO");
        UsageType = set.Type("consumable-usage.type", "ConsumableUsage");
        var inventory = set.Type("inventory.type", "Inventory");
        var manager = set.Type("targeting.manager.type-action", "TargetingManager");
        IsVisible = set.Call<Func<object, bool>>("consumable.is-visible", ConsumableType, "IsVisible", typeof(bool));
        CanFire = set.Call<Func<object, bool>>("consumable.can-fire", ConsumableType, "CanFire", typeof(bool));
        SelectAndFire = set.Call<Action<object>>(
            "consumable.select-and-fire", ConsumableType, "SelectAndFire", typeof(void));
        SetRandomization = set.Call<Action<object, bool>>(
            "consumable.set-randomization", ConsumableType, "SetRandomization", typeof(void), typeof(bool));
        IsRandomized = set.Call<Func<object, bool>>(
            "consumable.is-randomized", ConsumableType, "IsRandomized", typeof(bool));
        CanBeRandomized = set.Field<Func<object, bool>>(
            "consumable.can-be-randomized", ConsumableType, "canBeRandomized", typeof(bool));
        GetQueued = set.Call<Func<object, int>>("consumable.get-queued", ConsumableType, "GetQueued", typeof(int));
        GetUsages = set.Field<Func<object, IList>>(
            "consumable.consumable-usages", ConsumableType, "consumableUsages", typeof(List<>).MakeGenericType(UsageType));
        CanUse = set.StaticCall<Func<bool>>(
            "inventory.can-use-consumable", inventory, "CanUseConsumable", typeof(bool));
        IsTargeting = set.StaticCall<Func<bool>>(
            "targeting-manager.is-targeting", manager, "IsTargeting", typeof(bool));
    }

    internal Type ConsumableType { get; }
    internal Type UsageType { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Func<object, bool> CanFire { get; }
    internal Action<object> SelectAndFire { get; }
    internal Action<object, bool> SetRandomization { get; }
    internal Func<object, bool> IsRandomized { get; }
    internal Func<object, bool> CanBeRandomized { get; }
    internal Func<object, int> GetQueued { get; }
    internal Func<object, IList> GetUsages { get; }
    internal Func<bool> CanUse { get; }
    internal Func<bool> IsTargeting { get; }
}
