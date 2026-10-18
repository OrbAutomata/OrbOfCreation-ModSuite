using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the manual and automated crafting-instance controls.</summary>
internal sealed class CraftingInstanceLifecycleNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "crafting-instance.page.type-action",
        "crafting-instance.recipe.type-action",
        "crafting-instance.recipe-type.type-action",
        "crafting-instance.recipe-list.type-action",
        "crafting-instance.queue.type-action",
        "auto-scribe.crafting-instance.type",
        "crafting-player.resources-find-all-action",
        "crafting-player.page-available-recipes-action",
        "crafting-player.page-queue-action",
        "crafting-instance.page-automation-action",
        "crafting-player.page-main-type-action",
        "abstract-list.value",
        "auto-scribe.recipe.get-main-type",
        "crafting-instance.queue-get-instance-action",
        "crafting-instance.queue-automate-action",
        "crafting-instance.queue-remove-automation-action",
        "crafting-player.instance-recipe-action",
        "crafting-instance.instance-automation-quantity-action",
        "crafting-instance.instance-is-auto-action",
        "crafting-instance.instance-cancel-action",
        "crafting-instance.queue-remove-action",
        "auto-scribe.recipe.visible",
        "auto-scribe.list.has-empty-spot",
    };

    private readonly Func<Type, Array?> _findPages;
    private readonly Func<object, object?> _pageRecipes;
    private readonly Func<object, object?> _pageQueue;
    private readonly Func<object, object?> _pageAutomation;
    private readonly Func<object, object?> _pageMainType;
    private readonly Func<object, IList?> _recipeValues;
    private readonly Func<object, IList?> _instanceValues;
    private readonly Func<object, object?> _recipeMainType;
    private readonly Func<object, object, object?> _getInstance;
    private readonly Func<object, object, int, object?> _automate;
    private readonly Action<object, object, int> _removeAutomation;
    private readonly Func<object, Guid> _instanceRecipe;
    private readonly Func<object, int> _automationQuantity;
    private readonly Func<object, bool> _isAuto;
    private readonly Action<object> _cancel;
    private readonly Action<object, object> _remove;
    private readonly Func<object, bool> _visible;
    private readonly Func<object, bool> _hasRoom;

    private CraftingInstanceLifecycleNativeBindings(
        Type pageType,
        Type recipeType,
        Type instanceType,
        Type queueType,
        Func<Type, Array?> findPages,
        Func<object, object?> pageRecipes,
        Func<object, object?> pageQueue,
        Func<object, object?> pageAutomation,
        Func<object, object?> pageMainType,
        Func<object, IList?> recipeValues,
        Func<object, IList?> instanceValues,
        Func<object, object?> recipeMainType,
        Func<object, object, object?> getInstance,
        Func<object, object, int, object?> automate,
        Action<object, object, int> removeAutomation,
        Func<object, Guid> instanceRecipe,
        Func<object, int> automationQuantity,
        Func<object, bool> isAuto,
        Action<object> cancel,
        Action<object, object> remove,
        Func<object, bool> visible,
        Func<object, bool> hasRoom)
    {
        PageType = pageType;
        RecipeType = recipeType;
        InstanceType = instanceType;
        QueueType = queueType;
        _findPages = findPages;
        _pageRecipes = pageRecipes;
        _pageQueue = pageQueue;
        _pageAutomation = pageAutomation;
        _pageMainType = pageMainType;
        _recipeValues = recipeValues;
        _instanceValues = instanceValues;
        _recipeMainType = recipeMainType;
        _getInstance = getInstance;
        _automate = automate;
        _removeAutomation = removeAutomation;
        _instanceRecipe = instanceRecipe;
        _automationQuantity = automationQuantity;
        _isAuto = isAuto;
        _cancel = cancel;
        _remove = remove;
        _visible = visible;
        _hasRoom = hasRoom;
    }

    internal Type PageType { get; }
    internal Type RecipeType { get; }
    internal Type InstanceType { get; }
    internal Type QueueType { get; }

    internal Array Pages() => _findPages(PageType) ?? Array.Empty<object>();
    internal IList PageRecipes(object page) => Values(_recipeValues, _pageRecipes(page));
    internal object PageQueue(object page) => Exact(_pageQueue(page), QueueType);
    internal object PageAutomation(object page) => Exact(_pageAutomation(page), QueueType);
    internal object PageMainType(object page) =>
        _pageMainType(page) ?? throw new InvalidOperationException("Crafting page type was null.");
    internal object RecipeMainType(object recipe) =>
        _recipeMainType(recipe) ?? throw new InvalidOperationException("Crafting recipe type was null.");
    internal bool RecipeVisible(object recipe) => _visible(recipe);
    internal IList QueueValues(object queue) => Values(_instanceValues, queue);
    internal object? QueueInstance(object queue, object recipe) => _getInstance(queue, recipe);
    internal bool QueueHasRoom(object queue) => _hasRoom(queue);
    internal object Automate(object queue, object recipe, int amount) =>
        Exact(_automate(queue, recipe, amount), InstanceType);
    internal void RemoveAutomation(object queue, object instance, int amount) =>
        _removeAutomation(queue, instance, amount);
    internal Guid InstanceRecipe(object instance) => _instanceRecipe(instance);
    internal int AutomationQuantity(object instance) => _automationQuantity(instance);
    internal bool IsAuto(object instance) => _isAuto(instance);
    internal void Cancel(object instance) => _cancel(instance);
    internal void QueueRemove(object queue, object instance) => _remove(queue, instance);

    internal static bool TryCreate(
        out CraftingInstanceLifecycleNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("crafting instance", ContractIds, resolveType, includeContract, set =>
        {
            var page = set.Type("crafting-instance.page.type-action", "UICraftingPage");
            var recipe = set.Type("crafting-instance.recipe.type-action", "CraftingRecipeSO");
            var recipeType = set.Type("crafting-instance.recipe-type.type-action", "CraftingRecipeTypeSO");
            var recipeList = set.Type("crafting-instance.recipe-list.type-action", "CraftingRecipeListVariable");
            var queue = set.Type("crafting-instance.queue.type-action", "CraftingInstanceListVariable");
            var instance = set.Type("auto-scribe.crafting-instance.type", "CraftingInstance");
            // UnityEngine.CoreModule is not the game's Assembly-CSharp module. Resolve the
            // framework-owned types from the loaded Unity reference, just like player crafting.
            var resources = typeof(Resources);
            var unityObject = typeof(UnityEngine.Object);
            var abstractInstanceList = set.GenericBase(
                "crafting-instance.queue.type-action", queue, "AbstractListVariable`1");

            return new CraftingInstanceLifecycleNativeBindings(
                page,
                recipe,
                instance,
                queue,
                set.StaticCall<Func<Type, Array?>>("crafting-player.resources-find-all-action",
                    resources, "FindObjectsOfTypeAll", unityObject.MakeArrayType(), typeof(Type)),
                set.Field<Func<object, object?>>(
                    "crafting-player.page-available-recipes-action", page, "availableRecipes", recipeList),
                set.Field<Func<object, object?>>(
                    "crafting-player.page-queue-action", page, "craftingQueueInstances", queue),
                set.Field<Func<object, object?>>(
                    "crafting-instance.page-automation-action", page, "craftingAutomationInstances", queue),
                set.Field<Func<object, object?>>(
                    "crafting-player.page-main-type-action", page, "mainCraftType", recipeType),
                set.Field<Func<object, IList?>>(
                    "abstract-list.value", recipeList, "value", typeof(List<>).MakeGenericType(recipe)),
                set.Field<Func<object, IList?>>(
                    "abstract-list.value", queue, "value", typeof(List<>).MakeGenericType(instance)),
                set.Call<Func<object, object?>>("auto-scribe.recipe.get-main-type", recipe, "GetMainType", recipeType),
                set.Call<Func<object, object, object?>>(
                    "crafting-instance.queue-get-instance-action", queue, "GetInstance", instance, recipe),
                set.Call<Func<object, object, int, object?>>("crafting-instance.queue-automate-action",
                    queue, "AutomateCraft", instance, recipe, typeof(int)),
                set.Call<Action<object, object, int>>("crafting-instance.queue-remove-automation-action",
                    queue, "RemoveAutomation", typeof(void), instance, typeof(int)),
                set.Call<Func<object, Guid>>(
                    "crafting-player.instance-recipe-action", instance, "GetGuidReference", typeof(Guid)),
                set.Call<Func<object, int>>("crafting-instance.instance-automation-quantity-action",
                    instance, "GetAutomationQuantity", typeof(int)),
                set.Call<Func<object, bool>>("crafting-instance.instance-is-auto-action", instance, "IsAuto", typeof(bool)),
                set.Call<Action<object>>("crafting-instance.instance-cancel-action", instance, "CancelCraft", typeof(void)),
                set.Call<Action<object, object>>(
                    "crafting-instance.queue-remove-action", abstractInstanceList, "Remove", typeof(void), instance),
                set.Call<Func<object, bool>>("auto-scribe.recipe.visible", recipe, "IsVisible", typeof(bool)),
                set.Call<Func<object, bool>>("auto-scribe.list.has-empty-spot", queue, "HasEmptySpot", typeof(bool)));
        }, out bindings, out reason);

    private static IList Values(Func<object, IList?> values, object? owner)
    {
        if (owner is null) throw new InvalidOperationException("Crafting list was null.");
        return values(owner) ?? throw new InvalidOperationException("Crafting list value was null.");
    }

    private static object Exact(object? value, Type type) =>
        value is not null && value.GetType() == type
            ? value
            : throw new InvalidOperationException(type.Name + " value was null or wrong-typed.");
}
