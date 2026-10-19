using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;

namespace OrbModding.Tests.Services.AutoScribe;

/// <summary>The native Scribe screen the Auto Scribe GameAction re-drives, built on the game stubs.</summary>
internal sealed class AutoScribeNativeFixture
{
    internal Dictionary<Guid, object> Registry { get; } = new();

    internal static void ResetStubs()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        StructureSO.All.Clear();
        ConsumableSO.All.Clear();
        CraftingRecipeSO.All.Clear();
    }

    internal TypedRegistryResolver Resolver(
        Func<long> readLifecycle,
        Func<TypedRegistrySourceSnapshot>? readRegistry = null)
    {
        IDictionary dictionary = Registry;
        return new TypedRegistryResolver(
            readLifecycle,
            readRegistry ?? (() => TypedRegistrySourceSnapshot.Ready(dictionary)),
            value => value is IdScriptableObject item ? item.GetGuid() : Guid.Empty);
    }

    internal AutoScribeNativeFixtureState Build(AutoScribeIdentityProfile profile, bool withTarget = true)
    {
        var recipeType = Register(
            new CraftingRecipeTypeSO
            {
                maxStartingLevel = 1,
                isLevelType = true,
            },
            profile.RecipeType.Uuid);
        var recipeRegistry = Register(
            new CraftingRecipeListVariable { Maximum = 6 },
            profile.RecipeRegistry.Uuid);
        var active = Register(
            new CraftingInstanceListVariable { Maximum = 4 },
            profile.ActiveInstances.Uuid);
        var automatic = Register(
            new CraftingInstanceListVariable { Maximum = 4, isAutoList = true },
            profile.AutomaticInstances.Uuid);

        CraftingRecipeSO? selectedRecipe = null;
        ConsumableSO? selectedScroll = null;
        ResourceSO? selectedResource = null;
        AutoScribeCycleAction selectedAction = default;
        for (var roleIndex = 0; roleIndex < profile.Roles.Count; roleIndex++)
        {
            var role = profile.Roles[roleIndex];
            var enchantment = Register(new EnchantmentSO(), role.Enchantment.Uuid);
            var scroll = Register(new ConsumableSO { visible = true }, role.Scroll.Uuid);
            var targeting = new Targeting.TargetStructure();
            if (withTarget || roleIndex != 0)
                targeting.Candidates.Add(new StructureSO());
            var onUse = new InstantEffectBlock();
            onUse.effectScripts.Add(new RequestTargetEffectScript
            {
                targetOptions = new Targeting.TargetSelectOptions { Targeting = targeting },
            });
            onUse.effectScripts.Add(new EnchantmentSO.EnchantItemScript
            {
                enchantment = enchantment,
            });
            scroll.onUseEffects.Add(onUse);
            if (!role.IsProducible) continue;

            var recipe = Register(new CraftingRecipeSO
            {
                visible = true,
                useQuantityAsLevel = true,
                MaximumAffordableLevel = 3,
                MainType = recipeType,
                InstantOutput = scroll,
            }, role.Recipe!.Value.Uuid);
            recipe.craftingTypes.Add(recipeType);
            var output = new InstantEffectBlock();
            output.effectScripts.Add(new ConsumableSO.ConsumableGainEffect
            {
                consumable = scroll,
            });
            recipe.completeEffects.Add(output);
            recipeRegistry.value.Add(recipe);
            CraftingRecipeSO.All.Add(recipe);
            if (roleIndex != 0) continue;

            var resource = Register(new ResourceSO
            {
                quantity = new BigDouble(100, 0),
                quality = new ValueModifierRecord(new BigDouble(100, 0)),
            }, new Guid("5c0de000-0000-0000-0000-000000000005"));
            recipe.TotalCost.costs.Add(new ResourceTuple(resource, new BigDouble(5, 0)));
            selectedRecipe = recipe;
            selectedScroll = scroll;
            selectedResource = resource;
            selectedAction = new AutoScribeCycleAction(
                recipe.GetGuid(),
                scroll.GetGuid(),
                level: 3,
                collectedAtEpoch: 1);
        }

        return new AutoScribeNativeFixtureState(
            selectedRecipe!,
            selectedScroll!,
            selectedResource!,
            recipeType,
            active,
            automatic,
            selectedAction);
    }

    internal T Register<T>(T value, Guid id) where T : IdScriptableObject
    {
        value.SetGuid(id);
        Registry.Add(id, value);
        IdScriptableObject.RuntimeLookup[id] = value;
        return value;
    }
}

internal readonly record struct AutoScribeNativeFixtureState(
    CraftingRecipeSO Recipe,
    ConsumableSO Scroll,
    ResourceSO Resource,
    CraftingRecipeTypeSO RecipeType,
    CraftingInstanceListVariable Active,
    CraftingInstanceListVariable Automatic,
    AutoScribeCycleAction Action);
