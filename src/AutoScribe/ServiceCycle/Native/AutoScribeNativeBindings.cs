using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// One complete lifecycle binding set for the Scribe re-drive. Every execution member, field type,
/// signature, return type, staticness, and constructor is proven before the GameAction can run.
/// </summary>
internal sealed class AutoScribeNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "crafting-instance.recipe.type-action",
        "crafting-instance.recipe-type.type-action",
        "crafting-instance.recipe-list.type-action",
        "auto-scribe.crafting-instance.type",
        "crafting-instance.queue.type-action",
        "consumable.type",
        "enchantment-so.tooltipable-base",
        "generic-level.cost.type-action",
        "generic-level.tuple.type-action",
        "generic-level.resource.type-action",
        "generic-level.cost-entries-action",
        "generic-level.tuple-resource-action",
        "generic-level.tuple-value-action",
        "generic-level.resource-guid-action",
        "spell-loadout.big-double.type-action",
        "auto-items.instant-effect-block.type",
        "auto-items.instant-effect-script.type",
        "auto-scribe.consumable-gain.type",
        "auto-items.request-target-effect-script.type",
        "auto-items.target-select-options.type",
        "auto-items.base-target-selection.type",
        "auto-items.target-structure.type",
        "auto-items.targetable.type",
        "auto-scribe.enchant-script.type",
        "auto-items.scaling-info.type",
        "auto-scribe.instance.construct",
        "abstract-list.value",
        "auto-scribe.recipe.crafting-types",
        "auto-scribe.recipe.complete-effects",
        "crafting-player.recipe-levelled-action",
        "crafting-recipe-type.is-level-type",
        "crafting-recipe-type.max-starting-level",
        "auto-harvest.effect-scripts",
        "auto-scribe.consumable-gain.output",
        "auto-items.consumable.on-use-effects",
        "target-request.options",
        "auto-scribe.enchant-script.enchantment",
        "id-scriptable-object.get-guid-action",
        "auto-scribe.recipe.visible",
        "auto-scribe.recipe.can-buy-at",
        "auto-scribe.recipe.get-total-cost",
        "auto-scribe.recipe.get-main-type",
        "auto-scribe.recipe.purchase-quantity",
        "resource-cost-list.has-enough",
        "auto-scribe.list.has-empty-spot",
        "auto-scribe.list.add",
        "crafting-player.instance-recipe-action",
        "crafting-player.instance-quantity-action",
        "crafting-player.instance-expired-action",
        "auto-scribe.instance.initiate",
        "auto-scribe.instance.check-instant",
        "auto-scribe.instance.instant",
        "auto-scribe.scaling-info.basic",
        "auto-items.target-options.get-targeting",
        "auto-items.target-structure.get-random-list",
    };

    private AutoScribeNativeBindings(
        CraftingNativeOperations crafting,
        Type recipeType,
        Type recipeTypeType,
        Type recipeListType,
        Type instanceType,
        Type instanceListType,
        Type consumableType,
        Type enchantmentType,
        Type bigDoubleType,
        Type instantBlockType,
        Type instantScriptType,
        Type gainType,
        Type requestType,
        Type optionsType,
        Type selectionType,
        Type targetType,
        Type targetableType,
        Type enchantScriptType,
        Type scalingType,
        Func<object, IList?> recipeListValue,
        Func<object, IEnumerable?> craftingTypes,
        Func<object, IEnumerable?> completeEffects,
        Func<object, bool> useQuantityAsLevel,
        Func<object, bool> isLevelType,
        Func<object, int> maxStartingLevel,
        Func<object, IEnumerable?> effectScripts,
        Func<object, object?> gainConsumable,
        Func<object, IEnumerable?> onUseEffects,
        Func<object, object?> targetOptions,
        Func<object, object?> enchantScriptEnchantment,
        Func<object, Guid> identity,
        Func<object, Guid> consumableIdentity,
        Func<object, Guid> enchantmentIdentity,
        Func<object, IList?> costEntries,
        Func<object, object?> costResource,
        Func<object, BigDouble> costValue,
        Func<object, Guid> resourceGuid,
        Func<BigDouble, object?> scalingBasic,
        Func<object, object?> getTargeting,
        Func<object, object, ICollection?> getRandomList)
    {
        Crafting = crafting;
        RecipeType = recipeType;
        RecipeTypeType = recipeTypeType;
        RecipeListType = recipeListType;
        InstanceType = instanceType;
        InstanceListType = instanceListType;
        ConsumableType = consumableType;
        EnchantmentType = enchantmentType;
        BigDoubleType = bigDoubleType;
        InstantBlockType = instantBlockType;
        InstantScriptType = instantScriptType;
        GainType = gainType;
        RequestType = requestType;
        OptionsType = optionsType;
        SelectionType = selectionType;
        TargetType = targetType;
        TargetableType = targetableType;
        EnchantScriptType = enchantScriptType;
        ScalingType = scalingType;
        RecipeListValue = recipeListValue;
        CraftingTypes = craftingTypes;
        CompleteEffects = completeEffects;
        UseQuantityAsLevel = useQuantityAsLevel;
        IsLevelType = isLevelType;
        MaxStartingLevel = maxStartingLevel;
        EffectScripts = effectScripts;
        GainConsumable = gainConsumable;
        OnUseEffects = onUseEffects;
        TargetOptions = targetOptions;
        EnchantScriptEnchantment = enchantScriptEnchantment;
        Identity = identity;
        ConsumableIdentity = consumableIdentity;
        EnchantmentIdentity = enchantmentIdentity;
        CostEntries = costEntries;
        CostResource = costResource;
        CostValue = costValue;
        ResourceGuid = resourceGuid;
        ScalingBasic = scalingBasic;
        GetTargeting = getTargeting;
        GetRandomList = getRandomList;
    }

    internal CraftingNativeOperations Crafting { get; }
    internal Type RecipeType { get; }
    internal Type RecipeTypeType { get; }
    internal Type RecipeListType { get; }
    internal Type InstanceType { get; }
    internal Type InstanceListType { get; }
    internal Type ConsumableType { get; }
    internal Type EnchantmentType { get; }
    internal Type BigDoubleType { get; }
    internal Type InstantBlockType { get; }
    internal Type InstantScriptType { get; }
    internal Type GainType { get; }
    internal Type RequestType { get; }
    internal Type OptionsType { get; }
    internal Type SelectionType { get; }
    internal Type TargetType { get; }
    internal Type TargetableType { get; }
    internal Type EnchantScriptType { get; }
    internal Type ScalingType { get; }
    internal Func<object, IList?> RecipeListValue { get; }
    internal Func<object, IEnumerable?> CraftingTypes { get; }
    internal Func<object, IEnumerable?> CompleteEffects { get; }
    internal Func<object, bool> UseQuantityAsLevel { get; }
    internal Func<object, bool> IsLevelType { get; }
    internal Func<object, int> MaxStartingLevel { get; }
    internal Func<object, IEnumerable?> EffectScripts { get; }
    internal Func<object, object?> GainConsumable { get; }
    internal Func<object, IEnumerable?> OnUseEffects { get; }
    internal Func<object, object?> TargetOptions { get; }
    internal Func<object, object?> EnchantScriptEnchantment { get; }
    internal Func<object, Guid> Identity { get; }
    internal Func<object, Guid> ConsumableIdentity { get; }
    internal Func<object, Guid> EnchantmentIdentity { get; }
    internal Func<object, IList?> CostEntries { get; }
    internal Func<object, object?> CostResource { get; }
    internal Func<object, BigDouble> CostValue { get; }
    internal Func<object, Guid> ResourceGuid { get; }
    internal Func<BigDouble, object?> ScalingBasic { get; }
    internal Func<object, object?> GetTargeting { get; }
    internal Func<object, object, ICollection?> GetRandomList { get; }

    internal static bool TryCreate(out AutoScribeNativeBindings? bindings, out string reason) =>
        NativeBindingSet.TryBind("Auto Scribe native", ContractIds, null, null, set =>
        {
            var recipe = set.Type("crafting-instance.recipe.type-action", "CraftingRecipeSO");
            var recipeType = set.Type("crafting-instance.recipe-type.type-action", "CraftingRecipeTypeSO");
            var recipeList = set.Type("crafting-instance.recipe-list.type-action", "CraftingRecipeListVariable");
            var instance = set.Type("auto-scribe.crafting-instance.type", "CraftingInstance");
            var instanceList = set.Type("crafting-instance.queue.type-action", "CraftingInstanceListVariable");
            var consumable = set.Type("consumable.type", "ConsumableSO");
            var enchantment = set.Type("enchantment-so.tooltipable-base", "EnchantmentSO");
            var cost = set.Type("generic-level.cost.type-action", "ResourceCostList");
            var tuple = set.Type("generic-level.tuple.type-action", "ResourceTuple");
            var resource = set.Type("generic-level.resource.type-action", "ResourceSO");
            var big = set.Type("spell-loadout.big-double.type-action", "BigDouble");
            var block = set.Type("auto-items.instant-effect-block.type", "InstantEffectBlock");
            var script = set.Type("auto-items.instant-effect-script.type", "IInstantEffectScript");
            var gain = set.Type("auto-scribe.consumable-gain.type", "ConsumableSO+ConsumableGainEffect");
            var request = set.Type("auto-items.request-target-effect-script.type", "RequestTargetEffectScript");
            var options = set.Type("auto-items.target-select-options.type", "Targeting.TargetSelectOptions");
            var selection = set.Type("auto-items.base-target-selection.type", "Targeting.BaseTargetSelection");
            var target = set.Type("auto-items.target-structure.type", "Targeting.TargetStructure");
            var targetable = set.Type("auto-items.targetable.type", "Targeting.ITargetable");
            var enchantScript = set.Type("auto-scribe.enchant-script.type", "EnchantmentSO+EnchantItemScript");
            var scaling = set.Type("auto-items.scaling-info.type", "ScalingInfo");

            return new AutoScribeNativeBindings(
                CraftingNativeOperations.Bind(set, recipe, recipeType, instance, instanceList, cost, big),
                recipe,
                recipeType,
                recipeList,
                instance,
                instanceList,
                consumable,
                enchantment,
                big,
                block,
                script,
                gain,
                request,
                options,
                selection,
                target,
                targetable,
                enchantScript,
                scaling,
                set.Field<Func<object, IList?>>(
                    "abstract-list.value", recipeList, "value", typeof(List<>).MakeGenericType(recipe)),
                set.Field<Func<object, IEnumerable?>>("auto-scribe.recipe.crafting-types",
                    recipe, "craftingTypes", typeof(List<>).MakeGenericType(recipeType)),
                set.Field<Func<object, IEnumerable?>>("auto-scribe.recipe.complete-effects",
                    recipe, "completeEffects", typeof(List<>).MakeGenericType(block)),
                set.Field<Func<object, bool>>(
                    "crafting-player.recipe-levelled-action", recipe, "useQuantityAsLevel", typeof(bool)),
                set.Field<Func<object, bool>>(
                    "crafting-recipe-type.is-level-type", recipeType, "isLevelType", typeof(bool)),
                set.Field<Func<object, int>>(
                    "crafting-recipe-type.max-starting-level", recipeType, "maxStartingLevel", typeof(int)),
                set.Field<Func<object, IEnumerable?>>("auto-harvest.effect-scripts",
                    block, "effectScripts", typeof(List<>).MakeGenericType(script)),
                set.Field<Func<object, object?>>("auto-scribe.consumable-gain.output", gain, "consumable", consumable),
                set.Field<Func<object, IEnumerable?>>("auto-items.consumable.on-use-effects",
                    consumable, "onUseEffects", typeof(List<>).MakeGenericType(block)),
                set.Field<Func<object, object?>>("target-request.options", request, "targetOptions", options),
                set.Field<Func<object, object?>>(
                    "auto-scribe.enchant-script.enchantment", enchantScript, "enchantment", enchantment),
                set.StableUuid("id-scriptable-object.get-guid-action", recipe),
                set.StableUuid("id-scriptable-object.get-guid-action", consumable),
                set.StableUuid("id-scriptable-object.get-guid-action", enchantment),
                set.Call<Func<object, IList?>>(
                    "generic-level.cost-entries-action", cost, "GetEntries", typeof(List<>).MakeGenericType(tuple)),
                set.Field<Func<object, object?>>("generic-level.tuple-resource-action", tuple, "resource", resource),
                set.Call<Func<object, BigDouble>>("generic-level.tuple-value-action", tuple, "GetValue", typeof(BigDouble)),
                set.StableUuid("generic-level.resource-guid-action", resource),
                set.StaticCall<Func<BigDouble, object?>>(
                    "auto-scribe.scaling-info.basic", scaling, "Basic", scaling, big),
                set.Call<Func<object, object?>>(
                    "auto-items.target-options.get-targeting", options, "GetTargeting", selection),
                set.Call<Func<object, object, ICollection?>>("auto-items.target-structure.get-random-list",
                    target, "GetRandomList", typeof(List<>).MakeGenericType(targetable), scaling));
        }, out bindings, out reason);
}
