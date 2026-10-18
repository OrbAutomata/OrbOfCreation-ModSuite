using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for Discovery Tree offers. Reflection is confined to construction;
/// execution uses compiled delegates only, and a missing member disables the whole capability.
/// </summary>
internal sealed class DiscoveryTreeOfferNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "discovery-tree-offer.tree.type",
        "spell-workbench.guid-container-type-action",
        "discovery-tree-offer.discoverable.type",
        "discovery-tree-offer.has-guid.type",
        "generic-level.cost.type-action",
        "discovery-tree-offer.tree-all",
        "discovery-tree-offer.tree-rerolls",
        "discovery-tree-offer.current-choice-ids",
        "discovery-tree-offer.tree-selected",
        "id-scriptable-object.get-guid-action",
        "discovery-tree-offer.guid-container-value",
        "discovery-tree-offer.tree-is-visible",
        "discovery-tree-offer.tree-is-idle",
        "discovery-tree-offer.tree-is-crafting",
        "discovery-tree-offer.tree-is-choice",
        "discovery-tree-offer.tree-has-remaining",
        "discovery-tree-offer.tree-has-immediate-required",
        "discovery-tree-offer.tree-next-cost",
        "discovery-tree-offer.tree-get-item",
        "discovery-tree-offer.tree-initiate",
        "discovery-tree-offer.tree-select",
        "discovery-tree-offer.tree-confirm",
        "discovery-tree-offer.tree-reroll",
        "discovery-tree-offer.has-guid-get-guid",
        "resource-cost-list.has-enough",
        "resource-cost-list.perform-cost",
    };

    private DiscoveryTreeOfferNativeBindings(
        Type treeType,
        Type itemType,
        Type costType,
        Func<IList> readTrees,
        Func<object, Guid> readTreeIdentity,
        Func<object, int> readRerolls,
        Func<object, IList> readCurrentChoices,
        Func<object, object> readSelected,
        Func<object, Guid> readGuid,
        Func<object, bool> isVisible,
        Func<object, bool> isIdle,
        Func<object, bool> isCrafting,
        Func<object, bool> isChoice,
        Func<object, bool> hasRemainingDiscoveries,
        Func<object, bool> hasImmediateRequired,
        Func<object, object> getNextCost,
        Func<object, Guid, object?> getItem,
        Action<object> initiate,
        Action<object, Guid> select,
        Action<object> confirm,
        Action<object> reroll,
        Func<object, Guid> readItemIdentity,
        Func<object, bool> hasEnough,
        Action<object> performCost)
    {
        TreeType = treeType;
        ItemType = itemType;
        CostType = costType;
        ReadTrees = readTrees;
        ReadTreeIdentity = readTreeIdentity;
        ReadRerolls = readRerolls;
        ReadCurrentChoices = readCurrentChoices;
        ReadSelected = readSelected;
        ReadGuid = readGuid;
        IsVisible = isVisible;
        IsIdle = isIdle;
        IsCrafting = isCrafting;
        IsChoice = isChoice;
        HasRemainingDiscoveries = hasRemainingDiscoveries;
        HasImmediateRequired = hasImmediateRequired;
        GetNextCost = getNextCost;
        GetItem = getItem;
        Initiate = initiate;
        Select = select;
        Confirm = confirm;
        Reroll = reroll;
        ReadItemIdentity = readItemIdentity;
        HasEnough = hasEnough;
        PerformCost = performCost;
    }

    internal Type TreeType { get; }
    internal Type ItemType { get; }
    internal Type CostType { get; }
    internal Func<IList> ReadTrees { get; }
    internal Func<object, Guid> ReadTreeIdentity { get; }
    internal Func<object, int> ReadRerolls { get; }
    internal Func<object, IList> ReadCurrentChoices { get; }
    internal Func<object, object> ReadSelected { get; }
    internal Func<object, Guid> ReadGuid { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Func<object, bool> IsIdle { get; }
    internal Func<object, bool> IsCrafting { get; }
    internal Func<object, bool> IsChoice { get; }
    internal Func<object, bool> HasRemainingDiscoveries { get; }
    internal Func<object, bool> HasImmediateRequired { get; }
    internal Func<object, object> GetNextCost { get; }
    internal Func<object, Guid, object?> GetItem { get; }
    internal Action<object> Initiate { get; }
    internal Action<object, Guid> Select { get; }
    internal Action<object> Confirm { get; }
    internal Action<object> Reroll { get; }
    internal Func<object, Guid> ReadItemIdentity { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Action<object> PerformCost { get; }

    internal static bool TryCreate(
        out DiscoveryTreeOfferNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("Discovery Tree offer", ContractIds, resolveType, includeContract, set =>
        {
            var tree = set.Type("discovery-tree-offer.tree.type", "DiscoveryTreeSO");
            var guidContainer = set.Type("spell-workbench.guid-container-type-action", "GuidContainer");
            var item = set.Type("discovery-tree-offer.discoverable.type", "IDiscoverable");
            var hasGuid = set.Type("discovery-tree-offer.has-guid.type", "IHasGuid");
            var cost = set.Type("generic-level.cost.type-action", "ResourceCostList");

            return new DiscoveryTreeOfferNativeBindings(
                tree, item, cost,
                set.StaticField<Func<IList>>(
                    "discovery-tree-offer.tree-all", tree, "All", typeof(List<>).MakeGenericType(tree)),
                set.StableUuid("id-scriptable-object.get-guid-action", tree),
                set.Field<Func<object, int>>("discovery-tree-offer.tree-rerolls", tree, "rerollsLeft", typeof(int)),
                set.Field<Func<object, IList>>("discovery-tree-offer.current-choice-ids",
                    tree, "currentChoiceIds", typeof(List<>).MakeGenericType(guidContainer)),
                set.Field<Func<object, object>>("discovery-tree-offer.tree-selected", tree, "selectedChoiceId", guidContainer),
                set.Call<Func<object, Guid>>(
                    "discovery-tree-offer.guid-container-value", guidContainer, "get_guid", typeof(Guid)),
                set.Call<Func<object, bool>>("discovery-tree-offer.tree-is-visible", tree, "IsVisible", typeof(bool)),
                set.Call<Func<object, bool>>("discovery-tree-offer.tree-is-idle", tree, "IsInIdleMode", typeof(bool)),
                set.Call<Func<object, bool>>("discovery-tree-offer.tree-is-crafting", tree, "IsInCraftingMode", typeof(bool)),
                set.Call<Func<object, bool>>("discovery-tree-offer.tree-is-choice", tree, "IsInChoiceMode", typeof(bool)),
                set.Call<Func<object, bool>>("discovery-tree-offer.tree-has-remaining",
                    tree, "HasCurrentlyRemMainPoolDiscoveries", typeof(bool)),
                set.Call<Func<object, bool>>("discovery-tree-offer.tree-has-immediate-required",
                    tree, "HasImmediateRequiredDiscover", typeof(bool)),
                set.Call<Func<object, object>>("discovery-tree-offer.tree-next-cost", tree, "GetNextItemCost", cost),
                set.Call<Func<object, Guid, object?>>(
                    "discovery-tree-offer.tree-get-item", tree, "GetItemFromGuid", item, typeof(Guid)),
                set.Call<Action<object>>("discovery-tree-offer.tree-initiate", tree, "InitiateCraftingMode", typeof(void)),
                set.Call<Action<object, Guid>>(
                    "discovery-tree-offer.tree-select", tree, "SelectItemId", typeof(void), typeof(Guid)),
                set.Call<Action<object>>("discovery-tree-offer.tree-confirm", tree, "DiscoverSelectedItem", typeof(void)),
                set.Call<Action<object>>("discovery-tree-offer.tree-reroll", tree, "RerollChoices", typeof(void)),
                set.StableUuid("discovery-tree-offer.has-guid-get-guid", hasGuid),
                set.Call<Func<object, bool>>("resource-cost-list.has-enough", cost, "HasEnough", typeof(bool)),
                set.Call<Action<object>>("resource-cost-list.perform-cost", cost, "PerformCost", typeof(void)));
        }, out bindings, out reason);
}
