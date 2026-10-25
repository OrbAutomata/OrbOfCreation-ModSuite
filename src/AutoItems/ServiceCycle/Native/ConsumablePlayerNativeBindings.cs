using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for every player-facing consumable transition.</summary>
internal sealed class ConsumablePlayerNativeBindings : ConsumableUseNativeBindings
{
    internal new static readonly string[] ContractIds = ConsumableUseNativeBindings.ContractIds.Concat(new[]
    {
        "targeting.effect-result-info.type-action",
        "consumable-player.ref-list.type-action",
        "consumable.get-quantity",
        "consumable-usage.engaged",
        "consumable-usage.get-guid",
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
    }).ToArray();

    private ConsumablePlayerNativeBindings(
        NativeBindingSet set,
        Type listType,
        Action<object> cancelUsage,
        Action<object, int> discard,
        Func<object, int> getQuantity,
        Func<object, object?> getNextUsage,
        Func<object, bool> usageEngaged,
        Func<object, Guid> usageGuid,
        Func<object, object?> usageResultInfo,
        Func<object, bool> isCancelled,
        Func<object?> getInventory,
        Func<object, object?> getInventoryList,
        Func<object, object?> getHotbarList,
        Func<object, IList> getListValues,
        Action<object, int, int> swap,
        Action<object> update,
        Action<object, int, object> setAt)
        : base(set)
    {
        ListType = listType;
        CancelUsage = cancelUsage;
        Discard = discard;
        GetQuantity = getQuantity;
        GetNextUsage = getNextUsage;
        UsageEngaged = usageEngaged;
        UsageGuid = usageGuid;
        UsageResultInfo = usageResultInfo;
        IsCancelled = isCancelled;
        GetInventory = getInventory;
        GetInventoryList = getInventoryList;
        GetHotbarList = getHotbarList;
        GetListValues = getListValues;
        Swap = swap;
        Update = update;
        SetAt = setAt;
    }

    internal Type ListType { get; }
    internal Action<object> CancelUsage { get; }
    internal Action<object, int> Discard { get; }
    internal Func<object, int> GetQuantity { get; }
    internal Func<object, object?> GetNextUsage { get; }
    internal Func<object, bool> UsageEngaged { get; }
    internal Func<object, Guid> UsageGuid { get; }
    internal Func<object, object?> UsageResultInfo { get; }
    internal Func<object, bool> IsCancelled { get; }
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
            var consumableList = typeof(List<>).MakeGenericType(consumable);

            return new ConsumablePlayerNativeBindings(
                set,
                list,
                set.Call<Action<object>>(
                    "consumable-player.consumable-cancel-usage-action", consumable, "CancelUsage", typeof(void)),
                set.Call<Action<object, int>>(
                    "consumable-player.consumable-discard-action", consumable, "Discard", typeof(void), typeof(int)),
                set.Call<Func<object, int>>("consumable.get-quantity", consumable, "GetQuantity", typeof(int)),
                set.Field<Func<object, object?>>(
                    "consumable-player.consumable-next-usage-action", consumable, "nextUsage", usage),
                set.Field<Func<object, bool>>("consumable-usage.engaged", usage, "en", typeof(bool)),
                set.StableUuid("consumable-usage.get-guid", usage),
                set.Call<Func<object, object?>>(
                    "consumable-player.usage-result-info-action", usage, "GetResultInfo", resultInfo),
                set.Call<Func<object, bool>>(
                    "consumable-player.result-info-is-cancelled-action", resultInfo, "IsCancelled", typeof(bool)),
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
