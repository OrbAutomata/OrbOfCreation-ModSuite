using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete binding set for Auto Concept: the ActiveConcepts slot gates and quantity mutations, the
/// ConceptRecipes list, each assignment's quantities and drain, and the drain preflight that builds
/// a prospective instance and prices it against each drained resource.
/// </summary>
internal sealed class AutoConceptNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "alchemy-loadout.recipe.type-action",
        "alchemy-loadout.instance.type-action",
        "alchemy-loadout.list.type-action",
        "alchemy-recipe-list.type-action",
        "alchemy-type.type-action",
        "generic-level.cost.type-action",
        "generic-level.tuple.type-action",
        "generic-level.resource.type-action",
        "auto-concept.resource-drain.type-action",
        "abstract-list.value",
        "alchemy-recipe.alchemy-types-action",
        "auto-concept.recipe-drain-cost-action",
        "alchemy-recipe.is-discovered",
        "alchemy-recipe.get-max-usage",
        "alchemy-list.can-add",
        "alchemy-list.get-empty-typeless-slots",
        "alchemy-list.get-num-of-type",
        "alchemy-list.get-slots-only-for-type",
        "alchemy-list.add",
        "alchemy-list.remove",
        "alchemy-loadout.instance-reference-action",
        "auto-concept.instance-quantity-action",
        "auto-concept.instance-queued-action",
        "auto-concept.instance-drain-action",
        "auto-concept.instance-construct-action",
        "alchemy-instance.get-drain-cost-mod",
        "big-double.as-percent",
        "loadout.cost-multiply-action",
        "loadout.cost-subtract-action",
        "auto-concept.drain-current-action",
        "resource-cost-list.get-entries",
        "resource-tuple.resource-concept",
        "resource-tuple.get-value",
        "resource.is-at-zero",
        "resource.get-true-spend",
        "auto-concept.resource-true-rate-action",
        "resource.get-modded-drain",
        "id-scriptable-object.get-guid-action",
    };

    private AutoConceptNativeBindings(
        Type recipeType,
        Type instanceType,
        Type activeListType,
        Type recipeListType,
        Func<object, IList?> activeValues,
        Func<object, IList?> recipeValues,
        Func<object, IList?> alchemyTypes,
        Func<object, object?> drainCost,
        Func<object, bool> isDiscovered,
        Func<object, int> maxUsageSlots,
        Func<object, object, bool> canAddInstance,
        Func<object, int> emptyTypelessSlots,
        Func<object, object, int> numOfType,
        Func<object, object, int> slotsOnlyForType,
        Action<object, object, int> addInstances,
        Action<object, object, int> removeInstances,
        Func<object, object?> reference,
        Func<object, int> quantity,
        Action<object, int> setQuantity,
        Func<object, int> queuedQuantity,
        Func<object, object?> resourceDrain,
        Func<object, object> newInstance,
        Func<object, BigDouble> drainCostMod,
        Func<BigDouble, BigDouble> asPercent,
        Func<object, BigDouble, object?> multiply,
        Func<object, object, object?> subtract,
        Func<object, object?> currentDrain,
        Func<object, IList?> costEntries,
        Func<object, object?> entryResource,
        Func<object, BigDouble> entryValue,
        Func<object, bool> isAtZero,
        Func<object, BigDouble, BigDouble> trueSpend,
        Func<object, BigDouble> trueRate,
        Func<object, BigDouble> moddedDrain,
        Func<object, Guid> resourceId)
    {
        RecipeType = recipeType;
        InstanceType = instanceType;
        ActiveListType = activeListType;
        RecipeListType = recipeListType;
        ActiveValues = activeValues;
        RecipeValues = recipeValues;
        AlchemyTypes = alchemyTypes;
        DrainCost = drainCost;
        IsDiscovered = isDiscovered;
        MaxUsageSlots = maxUsageSlots;
        CanAddInstance = canAddInstance;
        EmptyTypelessSlots = emptyTypelessSlots;
        NumOfType = numOfType;
        SlotsOnlyForType = slotsOnlyForType;
        AddInstances = addInstances;
        RemoveInstances = removeInstances;
        Reference = reference;
        Quantity = quantity;
        SetQuantity = setQuantity;
        QueuedQuantity = queuedQuantity;
        ResourceDrain = resourceDrain;
        NewInstance = newInstance;
        DrainCostMod = drainCostMod;
        AsPercent = asPercent;
        Multiply = multiply;
        Subtract = subtract;
        CurrentDrain = currentDrain;
        CostEntries = costEntries;
        EntryResource = entryResource;
        EntryValue = entryValue;
        IsAtZero = isAtZero;
        TrueSpend = trueSpend;
        TrueRate = trueRate;
        ModdedDrain = moddedDrain;
        ResourceId = resourceId;
    }

    internal Type RecipeType { get; }
    internal Type InstanceType { get; }
    internal Type ActiveListType { get; }
    internal Type RecipeListType { get; }
    internal Func<object, IList?> ActiveValues { get; }
    internal Func<object, IList?> RecipeValues { get; }
    internal Func<object, IList?> AlchemyTypes { get; }
    internal Func<object, object?> DrainCost { get; }
    internal Func<object, bool> IsDiscovered { get; }
    internal Func<object, int> MaxUsageSlots { get; }
    internal Func<object, object, bool> CanAddInstance { get; }
    internal Func<object, int> EmptyTypelessSlots { get; }
    internal Func<object, object, int> NumOfType { get; }
    internal Func<object, object, int> SlotsOnlyForType { get; }
    internal Action<object, object, int> AddInstances { get; }
    internal Action<object, object, int> RemoveInstances { get; }
    internal Func<object, object?> Reference { get; }
    internal Func<object, int> Quantity { get; }
    internal Action<object, int> SetQuantity { get; }
    internal Func<object, int> QueuedQuantity { get; }
    internal Func<object, object?> ResourceDrain { get; }
    internal Func<object, object> NewInstance { get; }
    internal Func<object, BigDouble> DrainCostMod { get; }
    internal Func<BigDouble, BigDouble> AsPercent { get; }
    internal Func<object, BigDouble, object?> Multiply { get; }
    internal Func<object, object, object?> Subtract { get; }
    internal Func<object, object?> CurrentDrain { get; }
    internal Func<object, IList?> CostEntries { get; }
    internal Func<object, object?> EntryResource { get; }
    internal Func<object, BigDouble> EntryValue { get; }
    internal Func<object, bool> IsAtZero { get; }
    internal Func<object, BigDouble, BigDouble> TrueSpend { get; }
    internal Func<object, BigDouble> TrueRate { get; }
    internal Func<object, BigDouble> ModdedDrain { get; }
    internal Func<object, Guid> ResourceId { get; }

    internal static bool TryCreate(out AutoConceptNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("auto-concept", ContractIds, resolveType, includeContract, set =>
        {
            var recipe = set.Type("alchemy-loadout.recipe.type-action", "AlchemyRecipeSO");
            var instance = set.Type("alchemy-loadout.instance.type-action", "AlchemyInstance");
            var active = set.Type("alchemy-loadout.list.type-action", "AlchemyInstanceListVariable");
            var recipeList = set.Type("alchemy-recipe-list.type-action", "AlchemyRecipeListVariable");
            var alchemyType = set.Type("alchemy-type.type-action", "AlchemyTypeSO");
            var cost = set.Type("generic-level.cost.type-action", "ResourceCostList");
            var tuple = set.Type("generic-level.tuple.type-action", "ResourceTuple");
            var resource = set.Type("generic-level.resource.type-action", "ResourceSO");
            var drain = set.Type("auto-concept.resource-drain.type-action", "ResourceDrain");

            return new AutoConceptNativeBindings(
                recipe,
                instance,
                active,
                recipeList,
                set.Field<Func<object, IList?>>("abstract-list.value", active, "value", ListOf(instance)),
                set.Field<Func<object, IList?>>("abstract-list.value", recipeList, "value", ListOf(recipe)),
                set.Field<Func<object, IList?>>(
                    "alchemy-recipe.alchemy-types-action", recipe, "alchemyTypes", ListOf(alchemyType)),
                set.Field<Func<object, object?>>("auto-concept.recipe-drain-cost-action", recipe, "drainCost", cost),
                set.Call<Func<object, bool>>("alchemy-recipe.is-discovered", recipe, "IsDiscovered", typeof(bool)),
                set.Call<Func<object, int>>("alchemy-recipe.get-max-usage", recipe, "GetMaxUsageSlots", typeof(int)),
                set.Call<Func<object, object, bool>>(
                    "alchemy-list.can-add", active, "CanAddInstance", typeof(bool), recipe),
                set.Call<Func<object, int>>(
                    "alchemy-list.get-empty-typeless-slots", active, "GetNumEmptyTypelessSlots", typeof(int)),
                set.Call<Func<object, object, int>>(
                    "alchemy-list.get-num-of-type", active, "GetNumOfType", typeof(int), alchemyType),
                set.Call<Func<object, object, int>>(
                    "alchemy-list.get-slots-only-for-type", active, "GetSlotsOnlyForType", typeof(int), alchemyType),
                set.Call<Action<object, object, int>>(
                    "alchemy-list.add", active, "AddAlchemyInstances", typeof(void), recipe, typeof(int)),
                set.Call<Action<object, object, int>>(
                    "alchemy-list.remove", active, "RemoveAlchemyInstances", typeof(void), recipe, typeof(int)),
                set.Call<Func<object, object?>>(
                    "alchemy-loadout.instance-reference-action", instance, "get_reference", recipe),
                set.Field<Func<object, int>>("auto-concept.instance-quantity-action", instance, "quantity", typeof(int)),
                set.Setter<Action<object, int>>(
                    "auto-concept.instance-quantity-action", instance, "quantity", typeof(int)),
                set.Field<Func<object, int>>(
                    "auto-concept.instance-queued-action", instance, "queuedQuantity", typeof(int)),
                set.Field<Func<object, object?>>("auto-concept.instance-drain-action", instance, "resourceDrain", drain),
                set.New<Func<object, object>>("auto-concept.instance-construct-action", instance, recipe),
                set.Call<Func<object, BigDouble>>(
                    "alchemy-instance.get-drain-cost-mod", instance, "GetDrainCostMod", typeof(BigDouble)),
                set.Call<Func<BigDouble, BigDouble>>(
                    "big-double.as-percent", typeof(BigDouble), "AsPercent", typeof(BigDouble)),
                set.Call<Func<object, BigDouble, object?>>(
                    "loadout.cost-multiply-action", cost, "Multiply", cost, typeof(BigDouble)),
                set.Call<Func<object, object, object?>>("loadout.cost-subtract-action", cost, "Subtract", cost, cost),
                set.Call<Func<object, object?>>("auto-concept.drain-current-action", drain, "GetCurrentDrain", cost),
                set.Call<Func<object, IList?>>("resource-cost-list.get-entries", cost, "GetEntries", ListOf(tuple)),
                set.Field<Func<object, object?>>("resource-tuple.resource-concept", tuple, "resource", resource),
                set.Call<Func<object, BigDouble>>("resource-tuple.get-value", tuple, "GetValue", typeof(BigDouble)),
                set.Call<Func<object, bool>>("resource.is-at-zero", resource, "IsAtZero", typeof(bool)),
                set.Call<Func<object, BigDouble, BigDouble>>(
                    "resource.get-true-spend", resource, "GetTrueSpend", typeof(BigDouble), typeof(BigDouble)),
                set.Call<Func<object, BigDouble>>(
                    "auto-concept.resource-true-rate-action", resource, "GetTrueRate", typeof(BigDouble)),
                set.Call<Func<object, BigDouble>>(
                    "resource.get-modded-drain", resource, "GetModdedDrain", typeof(BigDouble)),
                set.StableUuid("id-scriptable-object.get-guid-action", resource));
        }, out bindings, out reason);

    private static Type ListOf(Type element) => typeof(List<>).MakeGenericType(element);
}
