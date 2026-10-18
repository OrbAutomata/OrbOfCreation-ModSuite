using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for the player-facing one-shot crafting verb. Discovery occurs
/// once; execution retains only exact members and never searches by name after admission begins.
/// </summary>
internal sealed class CraftingPlayerNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "crafting-player.resources-find-all-action",
        "crafting-player.page-available-recipes-action",
        "crafting-player.page-queue-action",
        "crafting-player.page-craft-mode-action",
        "crafting-player.page-main-type-action",
        "crafting-player.recipe-can-buy-action",
        "crafting-player.recipe-get-purchase-quantity-action",
        "crafting-player.recipe-execute-action",
        "crafting-player.recipe-effect-channel-action",
        "crafting-player.effect-channel-observe-action",
        "crafting-player.passive-observable-id-action",
        "crafting-player.recipe-time-action",
        "crafting-player.recipe-levelled-action",
        "crafting-player.queue-get-quantity-action",
        "crafting-player.instance-add-quantity-action",
        "crafting-player.instance-quantity-action",
        "crafting-player.instance-recipe-action",
        "int-variable.as-int",
        "auto-scribe.recipe.can-buy-at",
        "auto-scribe.recipe.get-main-type",
        "auto-scribe.recipe.get-total-cost",
        "auto-scribe.recipe.purchase-quantity",
        "auto-scribe.recipe.visible",
        "auto-scribe.list.has-empty-spot",
        "auto-scribe.list.add",
        "auto-scribe.instance.initiate",
        "auto-scribe.instance.check-instant",
        "auto-scribe.instance.instant",
        "crafting-player.instance-expired-action",
        "auto-scribe.crafting-instance.type",
        "abstract-list.value",
        "resource-cost-list.has-enough",
        "id-scriptable-object.get-guid-action",
        "crafting-instance.page.type-action",
        "crafting-instance.recipe.type-action",
        "crafting-instance.recipe-type.type-action",
        "crafting-instance.recipe-list.type-action",
        "crafting-instance.queue.type-action",
        "generic-level.cost.type-action",
        "challenge.int-variable.type-action",
        "spell-loadout.big-double.type-action",
        "crafting-player.effect-channel.type-action",
        "crafting-player.passive-observable.type-action",
        "auto-scribe.instance.construct",
    };

    private readonly Func<Type, Array?> _findObjects;
    private readonly Func<object, object?> _pageRecipes;
    private readonly Func<object, object?> _pageQueue;
    private readonly Func<object, object?> _pageCraftMode;
    private readonly Func<object, object?> _pageMainType;
    private readonly Func<object, double> _recipeTime;
    private readonly Func<object, bool> _recipeLevelled;
    private readonly Func<object, IList?> _recipeListValue;
    private readonly Func<object, IList?> _instanceListValue;
    private readonly Func<object, Guid> _identity;
    private readonly Func<object, bool> _recipeVisible;
    private readonly Func<object, bool> _recipeCanBuy;
    private readonly Func<object, BigDouble, bool> _recipeCanBuyAt;
    private readonly Func<object, BigDouble, BigDouble> _recipePurchaseAmount;
    private readonly Func<object, BigDouble, BigDouble, object?> _recipeTotalCost;
    private readonly Func<object, object?> _recipeMainType;
    private readonly Action<object, BigDouble, BigDouble> _recipePurchase;
    private readonly Action<object> _recipeExecute;
    private readonly Func<object, object?> _recipeEffectChannel;
    private readonly Func<object, string, object?> _effectChannelObserve;
    private readonly Func<object, int> _passiveObservableId;
    private readonly Func<object, bool> _costHasEnough;
    private readonly Func<object, int> _intAsInt;
    private readonly Func<object, object, BigDouble> _queueGetQuantity;
    private readonly Func<object, bool> _queueHasRoom;
    private readonly Action<object, object> _queueAdd;
    private readonly Func<object, Guid> _instanceRecipe;
    private readonly Func<object, BigDouble> _instanceQuantity;
    private readonly Action<object, BigDouble> _instanceAddQuantity;
    private readonly Action<object> _instanceInitiate;
    private readonly Func<object, bool> _instanceInstantCheck;
    private readonly Action<object> _instanceInstant;
    private readonly Func<object, bool> _instanceExpired;
    private readonly Func<object, BigDouble, object> _constructInstance;

    private CraftingPlayerNativeBindings(
        Type pageType,
        Type recipeType,
        Type recipeTypeType,
        Type recipeListType,
        Type instanceType,
        Type instanceListType,
        Type resourceCostType,
        Type intVariableType,
        Type bigDoubleType,
        Type passiveChannelType,
        Type passiveObservableType,
        Func<Type, Array?> findObjects,
        Func<object, object?> pageRecipes,
        Func<object, object?> pageQueue,
        Func<object, object?> pageCraftMode,
        Func<object, object?> pageMainType,
        Func<object, double> recipeTime,
        Func<object, bool> recipeLevelled,
        Func<object, IList?> recipeListValue,
        Func<object, IList?> instanceListValue,
        Func<object, Guid> identity,
        Func<object, bool> recipeVisible,
        Func<object, bool> recipeCanBuy,
        Func<object, BigDouble, bool> recipeCanBuyAt,
        Func<object, BigDouble, BigDouble> recipePurchaseAmount,
        Func<object, BigDouble, BigDouble, object?> recipeTotalCost,
        Func<object, object?> recipeMainType,
        Action<object, BigDouble, BigDouble> recipePurchase,
        Action<object> recipeExecute,
        Func<object, object?> recipeEffectChannel,
        Func<object, string, object?> effectChannelObserve,
        Func<object, int> passiveObservableId,
        Func<object, bool> costHasEnough,
        Func<object, int> intAsInt,
        Func<object, object, BigDouble> queueGetQuantity,
        Func<object, bool> queueHasRoom,
        Action<object, object> queueAdd,
        Func<object, Guid> instanceRecipe,
        Func<object, BigDouble> instanceQuantity,
        Action<object, BigDouble> instanceAddQuantity,
        Action<object> instanceInitiate,
        Func<object, bool> instanceInstantCheck,
        Action<object> instanceInstant,
        Func<object, bool> instanceExpired,
        Func<object, BigDouble, object> constructInstance)
    {
        PageType = pageType;
        RecipeType = recipeType;
        RecipeTypeType = recipeTypeType;
        RecipeListType = recipeListType;
        InstanceType = instanceType;
        InstanceListType = instanceListType;
        ResourceCostType = resourceCostType;
        IntVariableType = intVariableType;
        BigDoubleType = bigDoubleType;
        PassiveChannelType = passiveChannelType;
        PassiveObservableType = passiveObservableType;
        _findObjects = findObjects;
        _pageRecipes = pageRecipes;
        _pageQueue = pageQueue;
        _pageCraftMode = pageCraftMode;
        _pageMainType = pageMainType;
        _recipeTime = recipeTime;
        _recipeLevelled = recipeLevelled;
        _recipeListValue = recipeListValue;
        _instanceListValue = instanceListValue;
        _identity = identity;
        _recipeVisible = recipeVisible;
        _recipeCanBuy = recipeCanBuy;
        _recipeCanBuyAt = recipeCanBuyAt;
        _recipePurchaseAmount = recipePurchaseAmount;
        _recipeTotalCost = recipeTotalCost;
        _recipeMainType = recipeMainType;
        _recipePurchase = recipePurchase;
        _recipeExecute = recipeExecute;
        _recipeEffectChannel = recipeEffectChannel;
        _effectChannelObserve = effectChannelObserve;
        _passiveObservableId = passiveObservableId;
        _costHasEnough = costHasEnough;
        _intAsInt = intAsInt;
        _queueGetQuantity = queueGetQuantity;
        _queueHasRoom = queueHasRoom;
        _queueAdd = queueAdd;
        _instanceRecipe = instanceRecipe;
        _instanceQuantity = instanceQuantity;
        _instanceAddQuantity = instanceAddQuantity;
        _instanceInitiate = instanceInitiate;
        _instanceInstantCheck = instanceInstantCheck;
        _instanceInstant = instanceInstant;
        _instanceExpired = instanceExpired;
        _constructInstance = constructInstance;
    }

    internal Type PageType { get; }
    internal Type RecipeType { get; }
    internal Type RecipeTypeType { get; }
    internal Type RecipeListType { get; }
    internal Type InstanceType { get; }
    internal Type InstanceListType { get; }
    internal Type ResourceCostType { get; }
    internal Type IntVariableType { get; }
    internal Type BigDoubleType { get; }
    internal Type PassiveChannelType { get; }
    internal Type PassiveObservableType { get; }

    internal Array Pages() => _findObjects(PageType) ?? Array.Empty<object>();

    internal IList PageRecipes(object page) =>
        _recipeListValue(Require(_pageRecipes(page), RecipeListType)) ??
            throw new InvalidOperationException("Crafting page recipe list value was null.");

    internal object PageQueue(object page) => Require(_pageQueue(page), InstanceListType);
    internal object PageMainType(object page) => Require(_pageMainType(page), RecipeTypeType);
    internal int PageCraftMode(object page) =>
        _intAsInt(Require(_pageCraftMode(page), IntVariableType));
    internal Guid Identity(object value) => _identity(value);
    internal bool RecipeVisible(object recipe) => _recipeVisible(recipe);
    internal bool RecipeCanBuy(object recipe) => _recipeCanBuy(recipe);
    internal double RecipeTime(object recipe) => _recipeTime(recipe);
    internal bool RecipeLevelled(object recipe) => _recipeLevelled(recipe);
    internal bool RecipeCanBuyAt(object recipe, BigDouble amount) => _recipeCanBuyAt(recipe, amount);
    internal BigDouble RecipePurchaseAmount(object recipe, BigDouble previous) =>
        _recipePurchaseAmount(recipe, previous);
    internal object RecipeTotalCost(object recipe, BigDouble previous, BigDouble purchase) =>
        Require(_recipeTotalCost(recipe, previous, purchase), ResourceCostType);
    internal object RecipeMainType(object recipe) => Require(_recipeMainType(recipe), RecipeTypeType);
    internal void RecipePurchase(object recipe, BigDouble purchase, BigDouble previous) =>
        _recipePurchase(recipe, purchase, previous);
    internal void RecipeExecute(object recipe) => _recipeExecute(recipe);
    internal int RecipeEffectRevision(object recipe)
    {
        var channel = Require(_recipeEffectChannel(recipe), PassiveChannelType);
        var observable = RequireAssignable(_effectChannelObserve(channel, "craft"), PassiveObservableType);
        return _passiveObservableId(observable);
    }
    internal bool CostHasEnough(object cost) => _costHasEnough(cost);
    internal BigDouble QueueQuantity(object queue, object recipe) => _queueGetQuantity(queue, recipe);
    internal bool QueueHasRoom(object queue) => _queueHasRoom(queue);
    internal IList QueueValues(object queue) =>
        _instanceListValue(queue) ?? throw new InvalidOperationException("Crafting queue value was null.");
    internal void QueueAdd(object queue, object instance) => _queueAdd(queue, instance);
    internal Guid InstanceRecipe(object instance) => _instanceRecipe(instance);
    internal BigDouble InstanceQuantity(object instance) => _instanceQuantity(instance);
    internal void InstanceAddQuantity(object instance, BigDouble amount) =>
        _instanceAddQuantity(instance, amount);
    internal object ConstructInstance(object recipe, BigDouble amount) =>
        Require(_constructInstance(recipe, amount), InstanceType);
    internal void InstanceInitiate(object instance) => _instanceInitiate(instance);
    internal bool InstanceIsInstant(object instance) => _instanceInstantCheck(instance);
    internal void InstanceInstant(object instance) => _instanceInstant(instance);
    internal bool InstanceExpired(object instance) => _instanceExpired(instance);

    internal static bool TryCreate(out CraftingPlayerNativeBindings? bindings, out string reason) =>
        NativeBindingSet.TryBind("player crafting", ContractIds, null, null, set =>
        {
            var page = set.Type("crafting-instance.page.type-action", "UICraftingPage");
            var recipe = set.Type("crafting-instance.recipe.type-action", "CraftingRecipeSO");
            var recipeType = set.Type("crafting-instance.recipe-type.type-action", "CraftingRecipeTypeSO");
            var recipeList = set.Type("crafting-instance.recipe-list.type-action", "CraftingRecipeListVariable");
            var instance = set.Type("auto-scribe.crafting-instance.type", "CraftingInstance");
            var instanceList = set.Type("crafting-instance.queue.type-action", "CraftingInstanceListVariable");
            var cost = set.Type("generic-level.cost.type-action", "ResourceCostList");
            var intVariable = set.Type("challenge.int-variable.type-action", "IntVariable");
            var big = set.Type("spell-loadout.big-double.type-action", "BigDouble");
            var passiveChannel = set.Type("crafting-player.effect-channel.type-action", "PassiveObservable+Channel");
            var passiveObservable = set.Type("crafting-player.passive-observable.type-action", "IPassiveObservable");
            var resources = typeof(Resources);
            var unityObject = typeof(UnityEngine.Object);

            return new CraftingPlayerNativeBindings(
                page,
                recipe,
                recipeType,
                recipeList,
                instance,
                instanceList,
                cost,
                intVariable,
                big,
                passiveChannel,
                passiveObservable,
                set.StaticCall<Func<Type, Array?>>("crafting-player.resources-find-all-action",
                    resources, "FindObjectsOfTypeAll", unityObject.MakeArrayType(), typeof(Type)),
                set.Field<Func<object, object?>>(
                    "crafting-player.page-available-recipes-action", page, "availableRecipes", recipeList),
                set.Field<Func<object, object?>>(
                    "crafting-player.page-queue-action", page, "craftingQueueInstances", instanceList),
                set.Field<Func<object, object?>>(
                    "crafting-player.page-craft-mode-action", page, "craftMode", intVariable),
                set.Field<Func<object, object?>>(
                    "crafting-player.page-main-type-action", page, "mainCraftType", recipeType),
                set.Field<Func<object, double>>(
                    "crafting-player.recipe-time-action", recipe, "timeToComplete", typeof(double)),
                set.Field<Func<object, bool>>(
                    "crafting-player.recipe-levelled-action", recipe, "useQuantityAsLevel", typeof(bool)),
                set.Field<Func<object, IList?>>(
                    "abstract-list.value", recipeList, "value", typeof(List<>).MakeGenericType(recipe)),
                set.Field<Func<object, IList?>>(
                    "abstract-list.value", instanceList, "value", typeof(List<>).MakeGenericType(instance)),
                set.StableUuid("id-scriptable-object.get-guid-action", recipe),
                set.Call<Func<object, bool>>("auto-scribe.recipe.visible", recipe, "IsVisible", typeof(bool)),
                set.Call<Func<object, bool>>("crafting-player.recipe-can-buy-action", recipe, "CanBuy", typeof(bool)),
                set.Call<Func<object, BigDouble, bool>>(
                    "auto-scribe.recipe.can-buy-at", recipe, "CanBuyAt", typeof(bool), big),
                set.Call<Func<object, BigDouble, BigDouble>>(
                    "crafting-player.recipe-get-purchase-quantity-action", recipe, "GetPurchaseQuantity", big, big),
                set.Call<Func<object, BigDouble, BigDouble, object?>>(
                    "auto-scribe.recipe.get-total-cost", recipe, "GetTotalCost", cost, big, big),
                set.Call<Func<object, object?>>("auto-scribe.recipe.get-main-type", recipe, "GetMainType", recipeType),
                set.Call<Action<object, BigDouble, BigDouble>>(
                    "auto-scribe.recipe.purchase-quantity", recipe, "PurchaseQuantity", typeof(void), big, big),
                set.Call<Action<object>>("crafting-player.recipe-execute-action", recipe, "Execute", typeof(void)),
                set.Call<Func<object, object?>>(
                    "crafting-player.recipe-effect-channel-action", recipe, "GetEffectChannel", passiveChannel),
                set.Call<Func<object, string, object?>>("crafting-player.effect-channel-observe-action",
                    passiveChannel, "Observe", passiveObservable, typeof(string)),
                set.Call<Func<object, int>>(
                    "crafting-player.passive-observable-id-action", passiveObservable, "GetObservableId", typeof(int)),
                set.Call<Func<object, bool>>("resource-cost-list.has-enough", cost, "HasEnough", typeof(bool)),
                set.Call<Func<object, int>>("int-variable.as-int", intVariable, "AsInt", typeof(int)),
                set.Call<Func<object, object, BigDouble>>(
                    "crafting-player.queue-get-quantity-action", instanceList, "GetQuantity", big, recipe),
                set.Call<Func<object, bool>>(
                    "auto-scribe.list.has-empty-spot", instanceList, "HasEmptySpot", typeof(bool)),
                set.Call<Action<object, object>>("auto-scribe.list.add", instanceList, "Add", typeof(void), instance),
                set.Call<Func<object, Guid>>(
                    "crafting-player.instance-recipe-action", instance, "GetGuidReference", typeof(Guid)),
                set.Call<Func<object, BigDouble>>(
                    "crafting-player.instance-quantity-action", instance, "GetQuantity", big),
                set.Call<Action<object, BigDouble>>(
                    "crafting-player.instance-add-quantity-action", instance, "AddQuantity", typeof(void), big),
                set.Call<Action<object>>("auto-scribe.instance.initiate", instance, "Initiate", typeof(void)),
                set.Call<Func<object, bool>>(
                    "auto-scribe.instance.check-instant", instance, "CheckInstantCraft", typeof(bool)),
                set.Call<Action<object>>("auto-scribe.instance.instant", instance, "InstantCraft", typeof(void)),
                set.Call<Func<object, bool>>(
                    "crafting-player.instance-expired-action", instance, "IsExpired", typeof(bool)),
                set.New<Func<object, BigDouble, object>>("auto-scribe.instance.construct", instance, recipe, big));
        }, out bindings, out reason);

    private static object Require(object? value, Type type)
    {
        if (value is null || value.GetType() != type)
            throw new InvalidOperationException(type.Name + " value was null or wrong-typed.");
        return value;
    }

    private static object RequireAssignable(object? value, Type type)
    {
        if (value is null || !type.IsInstanceOfType(value))
            throw new InvalidOperationException(type.Name + " value was null or wrong-typed.");
        return value;
    }
}
