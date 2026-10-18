using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the native ordinary-alchemy list UI pipeline.</summary>
internal sealed class AlchemyLoadoutNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "alchemy-loadout.recipe.type-action", "alchemy-loadout.manager.type-action",
        "alchemy-loadout.list.type-action", "alchemy-loadout.instance.type-action",
        "alchemy-loadout.cost.type-action", "alchemy-loadout.manager-instance-action",
        "alchemy-loadout.manager-active-action", "alchemy-loadout.recipe-discovered-action",
        "alchemy-loadout.recipe-usage-cost-action", "alchemy-loadout.recipe-free-uses-action",
        "alchemy-loadout.recipe-maximum-uses-action", "alchemy-loadout.list-can-add-action",
        "alchemy-loadout.list-values-action", "alchemy-loadout.instance-reference-action",
        "alchemy-loadout.instance-queued-action", "alchemy-loadout.instance-remaining-free-action",
        "alchemy-loadout.instance-remaining-maximum-action", "alchemy-loadout.cost-maximum-times-action",
        "alchemy-loadout.cost-empty-action", "alchemy-loadout.list-add-count-action",
        "alchemy-loadout.list-remove-count-action",
    };

    private AlchemyLoadoutNativeBindings(Type recipeType, Type managerType,
        Func<object?> manager, Func<object, object?> activeList, Func<object, bool> discovered,
        Func<object, object?> usageCost, Func<object, int> freeUses, Func<object, int> maximumUses,
        Func<object, object, bool> canAdd, Func<object, IList?> values,
        Func<object, object?> instanceRecipe, Func<object, int> queued,
        Func<object, int> remainingFree, Func<object, int> remainingMaximum,
        Func<object, BigDouble> maximumTimes, Func<object, bool> costEmpty,
        Action<object, object, int> addInstances,
        Action<object, object, int> removeInstances)
    {
        RecipeType = recipeType; ManagerType = managerType; Manager = manager;
        ActiveList = activeList; Discovered = discovered; UsageCost = usageCost;
        FreeUses = freeUses; MaximumUses = maximumUses; CanAdd = canAdd; Values = values;
        InstanceRecipe = instanceRecipe; Queued = queued; RemainingFree = remainingFree;
        RemainingMaximum = remainingMaximum; MaximumTimes = maximumTimes; CostEmpty = costEmpty;
        AddInstances = addInstances; RemoveInstances = removeInstances;
    }

    internal Type RecipeType { get; }
    internal Type ManagerType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> ActiveList { get; }
    internal Func<object, bool> Discovered { get; }
    internal Func<object, object?> UsageCost { get; }
    internal Func<object, int> FreeUses { get; }
    internal Func<object, int> MaximumUses { get; }
    internal Func<object, object, bool> CanAdd { get; }
    internal Func<object, IList?> Values { get; }
    internal Func<object, object?> InstanceRecipe { get; }
    internal Func<object, int> Queued { get; }
    internal Func<object, int> RemainingFree { get; }
    internal Func<object, int> RemainingMaximum { get; }
    internal Func<object, BigDouble> MaximumTimes { get; }
    internal Func<object, bool> CostEmpty { get; }
    internal Action<object, object, int> AddInstances { get; }
    internal Action<object, object, int> RemoveInstances { get; }

    internal static bool TryCreate(out AlchemyLoadoutNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("ordinary alchemy", ContractIds, resolveType, includeContract, set =>
        {
            var recipe = set.Type("alchemy-loadout.recipe.type-action", "AlchemyRecipeSO");
            var managerType = set.Type("alchemy-loadout.manager.type-action", "AlchemyManager");
            var list = set.Type("alchemy-loadout.list.type-action", "AlchemyInstanceListVariable");
            var instance = set.Type("alchemy-loadout.instance.type-action", "AlchemyInstance");
            var cost = set.Type("alchemy-loadout.cost.type-action", "ResourceCostList");
            return new AlchemyLoadoutNativeBindings(recipe, managerType,
                set.StaticField<Func<object?>>(
                    "alchemy-loadout.manager-instance-action", managerType, "instance", managerType),
                set.Field<Func<object, object?>>(
                    "alchemy-loadout.manager-active-action", managerType, "activeAlchemy", list),
                set.Call<Func<object, bool>>(
                    "alchemy-loadout.recipe-discovered-action", recipe, "IsDiscovered", typeof(bool)),
                set.Call<Func<object, object?>>(
                    "alchemy-loadout.recipe-usage-cost-action", recipe, "GetUsageCost", cost),
                set.Call<Func<object, int>>(
                    "alchemy-loadout.recipe-free-uses-action", recipe, "GetFreeUsageSlots", typeof(int)),
                set.Call<Func<object, int>>(
                    "alchemy-loadout.recipe-maximum-uses-action", recipe, "GetMaxUsageSlots", typeof(int)),
                set.Call<Func<object, object, bool>>(
                    "alchemy-loadout.list-can-add-action", list, "CanAddInstance", typeof(bool), recipe),
                set.Field<Func<object, IList?>>("alchemy-loadout.list-values-action", list, "value",
                    typeof(System.Collections.Generic.List<>).MakeGenericType(instance)),
                set.Call<Func<object, object?>>(
                    "alchemy-loadout.instance-reference-action", instance, "get_reference", recipe),
                set.Call<Func<object, int>>(
                    "alchemy-loadout.instance-queued-action", instance, "GetQueuedQuantity", typeof(int)),
                set.Call<Func<object, int>>("alchemy-loadout.instance-remaining-free-action",
                    instance, "GetRemainingFreeUsageSlots", typeof(int)),
                set.Call<Func<object, int>>("alchemy-loadout.instance-remaining-maximum-action",
                    instance, "GetRemainingMaxUsageSlots", typeof(int)),
                set.Call<Func<object, BigDouble>>(
                    "alchemy-loadout.cost-maximum-times-action", cost, "MaximumCostTimes", typeof(BigDouble)),
                set.Call<Func<object, bool>>("alchemy-loadout.cost-empty-action", cost, "IsEmpty", typeof(bool)),
                set.Call<Action<object, object, int>>("alchemy-loadout.list-add-count-action",
                    list, "AddAlchemyInstances", typeof(void), recipe, typeof(int)),
                set.Call<Action<object, object, int>>("alchemy-loadout.list-remove-count-action",
                    list, "RemoveAlchemyInstances", typeof(void), recipe, typeof(int)));
        }, out bindings, out reason);
}
