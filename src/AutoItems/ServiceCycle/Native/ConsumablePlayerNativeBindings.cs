using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for every player-facing consumable transition.</summary>
internal sealed class ConsumablePlayerNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "consumable.type",
        "consumable-usage.type",
        "targeting.effect-result-info.type-action",
        "inventory.type",
        "consumable-player.ref-list.type-action",
        "targeting.manager.type-action",
        "consumable.is-visible",
        "consumable.can-fire",
        "consumable.select-and-fire",
        "consumable.set-randomization",
        "consumable.is-randomized",
        "consumable.can-be-randomized",
        "consumable.get-quantity",
        "consumable.get-queued",
        "consumable.consumable-usages",
        "consumable-usage.engaged",
        "consumable-usage.get-guid",
        "inventory.can-use-consumable",
        "targeting-manager.is-targeting",
        "consumable-player.consumable-cancel-usage-action",
        "consumable-player.consumable-discard-action",
        "consumable-player.consumable-next-usage-action",
        "consumable-player.usage-result-info-action",
        "consumable-player.result-info-is-cancelled-action",
        "consumable-player.inventory-instance-action",
        "consumable-player.inventory-list-action",
        "consumable-player.hotbar-list-action",
        "consumable-player.list-value-action",
        "consumable-player.list-swap-action",
        "consumable-player.list-set-at-action",
        "consumable-player.list-update-action",
    };

    private ConsumablePlayerNativeBindings(
        Type consumableType,
        Type usageType,
        Type listType,
        Func<object, bool> isVisible,
        Func<object, bool> canFire,
        Action<object> selectAndFire,
        Action<object> cancelUsage,
        Action<object, int> discard,
        Action<object, bool> setRandomization,
        Func<object, bool> isRandomized,
        Func<object, bool> canBeRandomized,
        Func<object, int> getQuantity,
        Func<object, int> getQueued,
        Func<object, IList> getUsages,
        Func<object, object?> getNextUsage,
        Func<object, bool> usageEngaged,
        Func<object, Guid> usageGuid,
        Func<object, object?> usageResultInfo,
        Func<object, bool> isCancelled,
        Func<bool> canUse,
        Func<bool> isTargeting,
        Func<object?> getInventory,
        Func<object, object?> getInventoryList,
        Func<object, object?> getHotbarList,
        Func<object, IList> getListValues,
        Action<object, int, int> swap,
        Action<object> update,
        Action<object, int, object> setAt)
    {
        ConsumableType = consumableType;
        UsageType = usageType;
        ListType = listType;
        IsVisible = isVisible;
        CanFire = canFire;
        SelectAndFire = selectAndFire;
        CancelUsage = cancelUsage;
        Discard = discard;
        SetRandomization = setRandomization;
        IsRandomized = isRandomized;
        CanBeRandomized = canBeRandomized;
        GetQuantity = getQuantity;
        GetQueued = getQueued;
        GetUsages = getUsages;
        GetNextUsage = getNextUsage;
        UsageEngaged = usageEngaged;
        UsageGuid = usageGuid;
        UsageResultInfo = usageResultInfo;
        IsCancelled = isCancelled;
        CanUse = canUse;
        IsTargeting = isTargeting;
        GetInventory = getInventory;
        GetInventoryList = getInventoryList;
        GetHotbarList = getHotbarList;
        GetListValues = getListValues;
        Swap = swap;
        Update = update;
        SetAt = setAt;
    }

    internal Type ConsumableType { get; }
    internal Type UsageType { get; }
    internal Type ListType { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Func<object, bool> CanFire { get; }
    internal Action<object> SelectAndFire { get; }
    internal Action<object> CancelUsage { get; }
    internal Action<object, int> Discard { get; }
    internal Action<object, bool> SetRandomization { get; }
    internal Func<object, bool> IsRandomized { get; }
    internal Func<object, bool> CanBeRandomized { get; }
    internal Func<object, int> GetQuantity { get; }
    internal Func<object, int> GetQueued { get; }
    internal Func<object, IList> GetUsages { get; }
    internal Func<object, object?> GetNextUsage { get; }
    internal Func<object, bool> UsageEngaged { get; }
    internal Func<object, Guid> UsageGuid { get; }
    internal Func<object, object?> UsageResultInfo { get; }
    internal Func<object, bool> IsCancelled { get; }
    internal Func<bool> CanUse { get; }
    internal Func<bool> IsTargeting { get; }
    internal Func<object?> GetInventory { get; }
    internal Func<object, object?> GetInventoryList { get; }
    internal Func<object, object?> GetHotbarList { get; }
    internal Func<object, IList> GetListValues { get; }
    internal Action<object, int, int> Swap { get; }
    internal Action<object> Update { get; }
    internal Action<object, int, object> SetAt { get; }

    internal static bool TryCreate(
        Func<string, Type?> resolveType,
        Func<string, bool> include,
        out ConsumablePlayerNativeBindings? bindings,
        out string reason) =>
        NativeBindingSet.TryBind("consumable player", ContractIds, resolveType, include, set =>
        {
            var consumable = set.Type("consumable.type", "ConsumableSO");
            var usage = set.Type("consumable-usage.type", "ConsumableUsage");
            var resultInfo = set.Type("targeting.effect-result-info.type-action", "EffectResultInfo");
            var inventory = set.Type("inventory.type", "Inventory");
            var list = set.Type("consumable-player.ref-list.type-action", "ConsumableRefListVariable");
            var manager = set.Type("targeting.manager.type-action", "TargetingManager");
            var usageList = typeof(List<>).MakeGenericType(usage);
            var consumableList = typeof(List<>).MakeGenericType(consumable);

            return new ConsumablePlayerNativeBindings(
                consumable,
                usage,
                list,
                set.Call<Func<object, bool>>("consumable.is-visible", consumable, "IsVisible", typeof(bool)),
                set.Call<Func<object, bool>>("consumable.can-fire", consumable, "CanFire", typeof(bool)),
                set.Call<Action<object>>(
                    "consumable.select-and-fire", consumable, "SelectAndFire", typeof(void)),
                set.Call<Action<object>>(
                    "consumable-player.consumable-cancel-usage-action", consumable, "CancelUsage", typeof(void)),
                set.Call<Action<object, int>>(
                    "consumable-player.consumable-discard-action", consumable, "Discard", typeof(void), typeof(int)),
                set.Call<Action<object, bool>>(
                    "consumable.set-randomization", consumable, "SetRandomization", typeof(void), typeof(bool)),
                set.Call<Func<object, bool>>(
                    "consumable.is-randomized", consumable, "IsRandomized", typeof(bool)),
                set.Field<Func<object, bool>>(
                    "consumable.can-be-randomized", consumable, "canBeRandomized", typeof(bool)),
                set.Call<Func<object, int>>("consumable.get-quantity", consumable, "GetQuantity", typeof(int)),
                set.Call<Func<object, int>>("consumable.get-queued", consumable, "GetQueued", typeof(int)),
                set.Field<Func<object, IList>>(
                    "consumable.consumable-usages", consumable, "consumableUsages", usageList),
                set.Field<Func<object, object?>>(
                    "consumable-player.consumable-next-usage-action", consumable, "nextUsage", usage),
                set.Field<Func<object, bool>>("consumable-usage.engaged", usage, "en", typeof(bool)),
                set.StableUuid("consumable-usage.get-guid", usage),
                set.Call<Func<object, object?>>(
                    "consumable-player.usage-result-info-action", usage, "GetResultInfo", resultInfo),
                set.Call<Func<object, bool>>(
                    "consumable-player.result-info-is-cancelled-action", resultInfo, "IsCancelled", typeof(bool)),
                set.StaticCall<Func<bool>>(
                    "inventory.can-use-consumable", inventory, "CanUseConsumable", typeof(bool)),
                set.StaticCall<Func<bool>>("targeting-manager.is-targeting", manager, "IsTargeting", typeof(bool)),
                set.StaticField<Func<object?>>(
                    "consumable-player.inventory-instance-action", inventory, "_instance", inventory),
                set.Field<Func<object, object?>>(
                    "consumable-player.inventory-list-action", inventory, "allConsumables", list),
                set.Field<Func<object, object?>>(
                    "consumable-player.hotbar-list-action", inventory, "hotBar", list),
                set.Field<Func<object, IList>>(
                    "consumable-player.list-value-action", list, "value", consumableList),
                set.Call<Action<object, int, int>>(
                    "consumable-player.list-swap-action", list, "SwapPositions", typeof(void), typeof(int), typeof(int)),
                set.Call<Action<object>>(
                    "consumable-player.list-update-action", list, "UpdateObservable", typeof(void)),
                set.Call<Action<object, int, object>>(
                    "consumable-player.list-set-at-action", list, "SetAt", typeof(void), typeof(int), consumable));
        }, out bindings, out reason);
}
