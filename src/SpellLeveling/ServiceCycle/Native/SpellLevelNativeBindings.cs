using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete binding set for Spell Leveling: the SpellManager's available recipes and its level-all
/// batch, each recipe's gates, cost and single-level purchase, and the level-all upgrade's committed
/// level. A profile build also binds the cost entries the paid price is read from, because only the
/// strategist's mastery press reports that price.
/// </summary>
internal sealed class SpellLevelNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-workbench.manager-type-action",
        "loadout.spell-recipe.type-action",
        "auto-buy.upgrade.type-action",
        "spell-level.recipe-list.type-action",
        "spell-level.prerequisites.type-action",
        "generic-level.cost.type-action",
        "spell-manager.instance",
        "spell-manager.available-recipes",
        "spell-manager.try-level-all",
        "abstract-list.value",
        "spell-recipe.leveling-prerequisites",
        "spell-recipe.mastery-level-action",
        "spell-recipe.is-discovered",
        "spell-recipe.is-ready",
        "spell-recipe.get-level-cost",
        "spell-recipe.purchase-level",
        "prerequisites.check",
        "upgrade.get-purchase-level-action",
        "resource-cost-list.has-enough",
        "resource-cost-list.perform-cost",
        "id-scriptable-object.get-guid-action",
#if SERVICE_CYCLE_PROFILE
        "generic-level.tuple.type-action",
        "generic-level.resource.type-action",
        "generic-level.cost-entries-action",
        "generic-level.tuple-resource-action",
        "generic-level.tuple-value-action",
        "generic-level.resource-guid-action",
#endif
    };

    private SpellLevelNativeBindings(
        Type recipeType,
        Type upgradeType,
        Func<object?> manager,
        Func<object, object?> availableRecipes,
        Action<object> tryLevelAll,
        Func<object, IList?> recipeValues,
        Func<object, object?> levelingPrerequisites,
        Func<object, int> masteryLevel,
        Func<object, bool> isDiscovered,
        Func<object, bool> isReady,
        Func<object, object?> levelCost,
        Action<object> purchaseLevel,
        Func<object, bool> prerequisitesCheck,
        Func<object, int> purchasedLevel,
        Func<object, bool> hasEnough,
        Action<object> performCost,
        Func<object, Guid> recipeId
#if SERVICE_CYCLE_PROFILE
        , Func<object, IList?> costEntries
        , Func<object, object?> entryResource
        , Func<object, BigDouble> entryValue
        , Func<object, Guid> resourceId
#endif
        )
    {
        RecipeType = recipeType;
        UpgradeType = upgradeType;
        Manager = manager;
        AvailableRecipes = availableRecipes;
        TryLevelAll = tryLevelAll;
        RecipeValues = recipeValues;
        LevelingPrerequisites = levelingPrerequisites;
        MasteryLevel = masteryLevel;
        IsDiscovered = isDiscovered;
        IsReady = isReady;
        LevelCost = levelCost;
        PurchaseLevel = purchaseLevel;
        PrerequisitesCheck = prerequisitesCheck;
        PurchasedLevel = purchasedLevel;
        HasEnough = hasEnough;
        PerformCost = performCost;
        RecipeId = recipeId;
#if SERVICE_CYCLE_PROFILE
        CostEntries = costEntries;
        EntryResource = entryResource;
        EntryValue = entryValue;
        ResourceId = resourceId;
#endif
    }

    internal Type RecipeType { get; }
    internal Type UpgradeType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> AvailableRecipes { get; }
    internal Action<object> TryLevelAll { get; }
    internal Func<object, IList?> RecipeValues { get; }
    internal Func<object, object?> LevelingPrerequisites { get; }
    internal Func<object, int> MasteryLevel { get; }
    internal Func<object, bool> IsDiscovered { get; }
    internal Func<object, bool> IsReady { get; }
    internal Func<object, object?> LevelCost { get; }
    internal Action<object> PurchaseLevel { get; }
    internal Func<object, bool> PrerequisitesCheck { get; }
    internal Func<object, int> PurchasedLevel { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Action<object> PerformCost { get; }
    internal Func<object, Guid> RecipeId { get; }
#if SERVICE_CYCLE_PROFILE
    internal Func<object, IList?> CostEntries { get; }
    internal Func<object, object?> EntryResource { get; }
    internal Func<object, BigDouble> EntryValue { get; }
    internal Func<object, Guid> ResourceId { get; }
#endif

    internal static bool TryCreate(out SpellLevelNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("spell-level", ContractIds, resolveType, includeContract, set =>
        {
            var manager = set.Type("spell-workbench.manager-type-action", "SpellManager");
            var recipe = set.Type("loadout.spell-recipe.type-action", "SpellRecipeSO");
            var upgrade = set.Type("auto-buy.upgrade.type-action", "UpgradeSO");
            var recipeList = set.Type("spell-level.recipe-list.type-action", "SpellRecipeListVariable");
            var prerequisites = set.Type("spell-level.prerequisites.type-action", "Prerequisites+Container");
            var cost = set.Type("generic-level.cost.type-action", "ResourceCostList");
#if SERVICE_CYCLE_PROFILE
            var tuple = set.Type("generic-level.tuple.type-action", "ResourceTuple");
            var resource = set.Type("generic-level.resource.type-action", "ResourceSO");
#endif

            return new SpellLevelNativeBindings(
                recipe,
                upgrade,
                set.StaticField<Func<object?>>("spell-manager.instance", manager, "instance", manager),
                set.Field<Func<object, object?>>(
                    "spell-manager.available-recipes", manager, "availableSpellRecipes", recipeList),
                set.Call<Action<object>>("spell-manager.try-level-all", manager, "TryLevelAllSpells", typeof(void)),
                set.Field<Func<object, IList?>>("abstract-list.value", recipeList, "value", ListOf(recipe)),
                set.Field<Func<object, object?>>(
                    "spell-recipe.leveling-prerequisites", recipe, "levelingPrerequisites", prerequisites),
                set.Field<Func<object, int>>("spell-recipe.mastery-level-action", recipe, "masteryLevel", typeof(int)),
                set.Call<Func<object, bool>>("spell-recipe.is-discovered", recipe, "IsDiscovered", typeof(bool)),
                set.Call<Func<object, bool>>("spell-recipe.is-ready", recipe, "IsReadyToLevelMastery", typeof(bool)),
                set.Call<Func<object, object?>>("spell-recipe.get-level-cost", recipe, "GetLevelCost", cost),
                set.Call<Action<object>>("spell-recipe.purchase-level", recipe, "PurchaseLevel", typeof(void)),
                set.Call<Func<object, bool>>("prerequisites.check", prerequisites, "Check", typeof(bool)),
                set.Call<Func<object, int>>(
                    "upgrade.get-purchase-level-action", upgrade, "GetPurchaseLevel", typeof(int)),
                set.Call<Func<object, bool>>("resource-cost-list.has-enough", cost, "HasEnough", typeof(bool)),
                set.Call<Action<object>>("resource-cost-list.perform-cost", cost, "PerformCost", typeof(void)),
                set.StableUuid("id-scriptable-object.get-guid-action", recipe)
#if SERVICE_CYCLE_PROFILE
                , set.Call<Func<object, IList?>>("generic-level.cost-entries-action", cost, "GetEntries", ListOf(tuple))
                , set.Field<Func<object, object?>>("generic-level.tuple-resource-action", tuple, "resource", resource)
                , set.Call<Func<object, BigDouble>>("generic-level.tuple-value-action", tuple, "GetValue", typeof(BigDouble))
                , set.StableUuid("generic-level.resource-guid-action", resource)
#endif
                );
        }, out bindings, out reason);

    private static Type ListOf(Type element) => typeof(List<>).MakeGenericType(element);
}
