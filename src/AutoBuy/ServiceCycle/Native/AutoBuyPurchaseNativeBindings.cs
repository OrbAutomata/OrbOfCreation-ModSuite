using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete binding set for one Auto Buy purchase: both purchase kinds, their gates and costs, the
/// live cost rows and the upgrade destination-capacity preflight.
/// </summary>
internal sealed class AutoBuyPurchaseNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "structure-lifecycle.structure.type-action",
        "auto-buy.upgrade.type-action",
        "generic-level.cost.type-action",
        "generic-level.tuple.type-action",
        "generic-level.resource.type-action",
        "auto-buy.destination.view-list-type",
        "auto-buy.destination.view-list-tuple-type",
        "spell-workbench.view-type-action",
        "challenge.int-variable.type-action",
        "structure.can-purchase",
        "auto-buy.structure-queued-quantity-action",
        "structure.purchase",
        "auto-buy.structure-availability-action",
        "structure.get-purchase-cost",
        "upgrade.can-purchase",
        "upgrade.get-queued-purchase-level",
        "upgrade.purchase",
        "auto-buy.upgrade-availability-action",
        "upgrade.is-max-level",
        "upgrade.is-max-queued-level",
        "upgrade.get-purchase-cost",
        "resource-cost-list.has-enough",
        "resource-cost-list.get-entries",
        "resource-tuple.resource-concept",
        "resource-tuple.get-value",
        "resource.is-bandwidth-resource",
        "resource.get-true-quantity",
        "resource.get-missing",
        "id-scriptable-object.get-guid-action",
        "auto-buy.destination.view-list-additions",
        "auto-buy.destination.tuple-list",
        "auto-buy.destination.tuple-element",
        "auto-buy.destination.max-size-variable",
        "auto-scribe.list.has-empty-spot",
        "int-variable.as-int",
    };

    private AutoBuyPurchaseNativeBindings(
        Type structureType,
        Type upgradeType,
        Type costListType,
        Type tupleType,
        Type resourceType,
        Type viewListType,
        Type listTupleType,
        Type viewType,
        Type intVariableType,
        Func<object, bool> structureCanPurchase,
        Func<object, int> structureQueued,
        Action<object, bool> structurePurchase,
        Func<object, bool> structureAvailable,
        Func<object, object?> structureCost,
        Func<object, bool> upgradeCanPurchase,
        Func<object, int> upgradeQueued,
        Action<object> upgradePurchase,
        Func<object, bool> upgradeAvailable,
        Func<object, bool> upgradeMaxLevel,
        Func<object, bool> upgradeMaxQueuedLevel,
        Func<object, object?> upgradeCost,
        Func<object, bool> hasEnough,
        Func<object, IList?> costEntries,
        Func<object, object?> entryResource,
        Func<object, BigDouble> entryValue,
        Func<object, bool> isBandwidth,
        Func<object, BigDouble> trueQuantity,
        Func<object, BigDouble> missing,
        Func<object, Guid> resourceId,
        Func<object, IList?> viewListAdditions,
        Func<object, object?> tupleList,
        Func<object, object?> tupleElement,
        Func<object, object?> maxSizeVariable,
        Func<object, bool> hasEmptySpot,
        Func<object, int> asInt,
        Func<object, Guid> viewListId,
        Func<object, Guid> viewId,
        Func<object, Guid> intVariableId)
    {
        StructureType = structureType;
        UpgradeType = upgradeType;
        CostListType = costListType;
        TupleType = tupleType;
        ResourceType = resourceType;
        ViewListType = viewListType;
        ListTupleType = listTupleType;
        ViewType = viewType;
        IntVariableType = intVariableType;
        StructureCanPurchase = structureCanPurchase;
        StructureQueued = structureQueued;
        StructurePurchase = structurePurchase;
        StructureAvailable = structureAvailable;
        StructureCost = structureCost;
        UpgradeCanPurchase = upgradeCanPurchase;
        UpgradeQueued = upgradeQueued;
        UpgradePurchase = upgradePurchase;
        UpgradeAvailable = upgradeAvailable;
        UpgradeMaxLevel = upgradeMaxLevel;
        UpgradeMaxQueuedLevel = upgradeMaxQueuedLevel;
        UpgradeCost = upgradeCost;
        HasEnough = hasEnough;
        CostEntries = costEntries;
        EntryResource = entryResource;
        EntryValue = entryValue;
        IsBandwidth = isBandwidth;
        TrueQuantity = trueQuantity;
        Missing = missing;
        ResourceId = resourceId;
        ViewListAdditions = viewListAdditions;
        TupleList = tupleList;
        TupleElement = tupleElement;
        MaxSizeVariable = maxSizeVariable;
        HasEmptySpot = hasEmptySpot;
        AsInt = asInt;
        ViewListId = viewListId;
        ViewId = viewId;
        IntVariableId = intVariableId;
    }

    internal Type StructureType { get; }
    internal Type UpgradeType { get; }
    internal Type CostListType { get; }
    internal Type TupleType { get; }
    internal Type ResourceType { get; }
    internal Type ViewListType { get; }
    internal Type ListTupleType { get; }
    internal Type ViewType { get; }
    internal Type IntVariableType { get; }
    internal Func<object, bool> StructureCanPurchase { get; }
    internal Func<object, int> StructureQueued { get; }
    internal Action<object, bool> StructurePurchase { get; }
    internal Func<object, bool> StructureAvailable { get; }
    internal Func<object, object?> StructureCost { get; }
    internal Func<object, bool> UpgradeCanPurchase { get; }
    internal Func<object, int> UpgradeQueued { get; }
    internal Action<object> UpgradePurchase { get; }
    internal Func<object, bool> UpgradeAvailable { get; }
    internal Func<object, bool> UpgradeMaxLevel { get; }
    internal Func<object, bool> UpgradeMaxQueuedLevel { get; }
    internal Func<object, object?> UpgradeCost { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Func<object, IList?> CostEntries { get; }
    internal Func<object, object?> EntryResource { get; }
    internal Func<object, BigDouble> EntryValue { get; }
    internal Func<object, bool> IsBandwidth { get; }
    internal Func<object, BigDouble> TrueQuantity { get; }
    internal Func<object, BigDouble> Missing { get; }
    internal Func<object, Guid> ResourceId { get; }
    internal Func<object, IList?> ViewListAdditions { get; }
    internal Func<object, object?> TupleList { get; }
    internal Func<object, object?> TupleElement { get; }
    internal Func<object, object?> MaxSizeVariable { get; }
    internal Func<object, bool> HasEmptySpot { get; }
    internal Func<object, int> AsInt { get; }
    internal Func<object, Guid> ViewListId { get; }
    internal Func<object, Guid> ViewId { get; }
    internal Func<object, Guid> IntVariableId { get; }

    internal static bool TryCreate(out AutoBuyPurchaseNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("auto-buy", ContractIds, resolveType, includeContract, set =>
        {
            var structure = set.Type("structure-lifecycle.structure.type-action", "StructureSO");
            var upgrade = set.Type("auto-buy.upgrade.type-action", "UpgradeSO");
            var cost = set.Type("generic-level.cost.type-action", "ResourceCostList");
            var tuple = set.Type("generic-level.tuple.type-action", "ResourceTuple");
            var resource = set.Type("generic-level.resource.type-action", "ResourceSO");
            var viewList = set.Type("auto-buy.destination.view-list-type", "ViewListVariable");
            var listTuple = set.Type("auto-buy.destination.view-list-tuple-type", "ViewListVariable+ListTuple");
            var view = set.Type("spell-workbench.view-type-action", "ViewSO");
            var intVariable = set.Type("challenge.int-variable.type-action", "IntVariable");

            return new AutoBuyPurchaseNativeBindings(
                structure, upgrade, cost, tuple, resource, viewList, listTuple, view, intVariable,
                set.Call<Func<object, bool>>("structure.can-purchase", structure, "CanPurchase", typeof(bool)),
                set.Call<Func<object, int>>(
                    "auto-buy.structure-queued-quantity-action", structure, "GetQueuedQuantity", typeof(int)),
                set.Call<Action<object, bool>>(
                    "structure.purchase", structure, "Purchase", typeof(void), typeof(bool)),
                set.Call<Func<object, bool>>(
                    "auto-buy.structure-availability-action", structure, "IsAvailable", typeof(bool)),
                set.Call<Func<object, object?>>("structure.get-purchase-cost", structure, "GetPurchaseCost", cost),
                set.Call<Func<object, bool>>("upgrade.can-purchase", upgrade, "CanPurchase", typeof(bool)),
                set.Call<Func<object, int>>(
                    "upgrade.get-queued-purchase-level", upgrade, "GetQueuedPurchaseLevel", typeof(int)),
                set.Call<Action<object>>("upgrade.purchase", upgrade, "Purchase", typeof(void)),
                set.Call<Func<object, bool>>(
                    "auto-buy.upgrade-availability-action", upgrade, "IsAvailable", typeof(bool)),
                set.Call<Func<object, bool>>("upgrade.is-max-level", upgrade, "IsMaxLevel", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "upgrade.is-max-queued-level", upgrade, "IsMaxQueuedLevel", typeof(bool)),
                set.Call<Func<object, object?>>("upgrade.get-purchase-cost", upgrade, "GetPurchaseCost", cost),
                set.Call<Func<object, bool>>("resource-cost-list.has-enough", cost, "HasEnough", typeof(bool)),
                set.Call<Func<object, IList?>>("resource-cost-list.get-entries", cost, "GetEntries",
                    typeof(List<>).MakeGenericType(tuple)),
                set.Field<Func<object, object?>>("resource-tuple.resource-concept", tuple, "resource", resource),
                set.Call<Func<object, BigDouble>>("resource-tuple.get-value", tuple, "GetValue", typeof(BigDouble)),
                set.Call<Func<object, bool>>(
                    "resource.is-bandwidth-resource", resource, "IsBandwidthResource", typeof(bool)),
                set.Call<Func<object, BigDouble>>(
                    "resource.get-true-quantity", resource, "GetTrueQuantity", typeof(BigDouble)),
                set.Call<Func<object, BigDouble>>("resource.get-missing", resource, "GetMissing", typeof(BigDouble)),
                set.StableUuid("id-scriptable-object.get-guid-action", resource),
                set.Field<Func<object, IList?>>("auto-buy.destination.view-list-additions", upgrade,
                    "viewListAdditions", typeof(List<>).MakeGenericType(listTuple)),
                set.Field<Func<object, object?>>("auto-buy.destination.tuple-list", listTuple, "list", viewList),
                set.Field<Func<object, object?>>("auto-buy.destination.tuple-element", listTuple, "element", view),
                set.Field<Func<object, object?>>(
                    "auto-buy.destination.max-size-variable", viewList, "maxSizeVariable", intVariable),
                set.Call<Func<object, bool>>("auto-scribe.list.has-empty-spot", viewList, "HasEmptySpot", typeof(bool)),
                set.Call<Func<object, int>>("int-variable.as-int", intVariable, "AsInt", typeof(int)),
                set.StableUuid("id-scriptable-object.get-guid-action", viewList),
                set.StableUuid("id-scriptable-object.get-guid-action", view),
                set.StableUuid("id-scriptable-object.get-guid-action", intVariable));
        }, out bindings, out reason);
}
