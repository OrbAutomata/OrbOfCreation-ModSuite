using System;
using System.Collections;
using System.Collections.Generic;

namespace OrbModding.Common;

/// <summary>
/// Complete binding set for the shared alchemy domain classifier: the recipe, alchemy type and
/// ConceptRecipes types, the registry's recipe list, each recipe's alchemy types and both identities.
/// </summary>
/// <remarks>
/// The classifier runs only at action boundaries (Auto Concept, Mentor and the ordinary Alchemy
/// GameAction), never inside world capture, so it binds on the strict action-side binder.
/// </remarks>
internal sealed class AlchemyGameplayDomainClassifierBindings
{
    internal static readonly string[] ContractIds =
    {
        "alchemy-loadout.recipe.type-action",
        "alchemy-type.type-action",
        "alchemy-recipe-list.type-action",
        "abstract-list.value",
        "alchemy-recipe.alchemy-types-action",
        "id-scriptable-object.get-guid-action",
    };

    private AlchemyGameplayDomainClassifierBindings(
        Type recipeType,
        Type alchemyType,
        Type recipeListType,
        Func<object, IList?> recipeValues,
        Func<object, IList?> alchemyTypes,
        Func<object, Guid> recipeId,
        Func<object, Guid> alchemyTypeId)
    {
        RecipeType = recipeType;
        AlchemyType = alchemyType;
        RecipeListType = recipeListType;
        RecipeValues = recipeValues;
        AlchemyTypes = alchemyTypes;
        RecipeId = recipeId;
        AlchemyTypeId = alchemyTypeId;
    }

    internal Type RecipeType { get; }
    internal Type AlchemyType { get; }
    internal Type RecipeListType { get; }
    internal Func<object, IList?> RecipeValues { get; }
    internal Func<object, IList?> AlchemyTypes { get; }
    internal Func<object, Guid> RecipeId { get; }
    internal Func<object, Guid> AlchemyTypeId { get; }

    internal static bool TryCreate(out AlchemyGameplayDomainClassifierBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("alchemy classifier", ContractIds, resolveType, includeContract, set =>
        {
            var recipe = set.Type("alchemy-loadout.recipe.type-action", "AlchemyRecipeSO");
            var alchemyType = set.Type("alchemy-type.type-action", "AlchemyTypeSO");
            var recipeList = set.Type("alchemy-recipe-list.type-action", "AlchemyRecipeListVariable");

            return new AlchemyGameplayDomainClassifierBindings(
                recipe,
                alchemyType,
                recipeList,
                set.Field<Func<object, IList?>>("abstract-list.value", recipeList, "value", ListOf(recipe)),
                set.Field<Func<object, IList?>>(
                    "alchemy-recipe.alchemy-types-action", recipe, "alchemyTypes", ListOf(alchemyType)),
                set.StableUuid("id-scriptable-object.get-guid-action", recipe),
                set.StableUuid("id-scriptable-object.get-guid-action", alchemyType));
        }, out bindings, out reason);

    private static Type ListOf(Type element) => typeof(List<>).MakeGenericType(element);
}
