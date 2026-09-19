using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for the player-facing one-shot crafting verb. Discovery occurs
/// once; execution retains only exact members and never searches by name after admission begins.
/// </summary>
internal sealed class CraftingPlayerNativeBindings
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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
    };

    private readonly MethodInfo _findObjects;
    private readonly FieldInfo _pageRecipes;
    private readonly FieldInfo _pageQueue;
    private readonly FieldInfo _pageCraftMode;
    private readonly FieldInfo _pageMainType;
    private readonly FieldInfo _recipeTime;
    private readonly FieldInfo _recipeListValue;
    private readonly FieldInfo _instanceListValue;
    private readonly MethodInfo _identity;
    private readonly MethodInfo _recipeVisible;
    private readonly MethodInfo _recipeCanBuy;
    private readonly MethodInfo _recipeCanBuyAt;
    private readonly MethodInfo _recipePurchaseAmount;
    private readonly MethodInfo _recipeTotalCost;
    private readonly MethodInfo _recipeMainType;
    private readonly MethodInfo _recipePurchase;
    private readonly MethodInfo _recipeExecute;
    private readonly MethodInfo _recipeEffectChannel;
    private readonly MethodInfo _effectChannelObserve;
    private readonly MethodInfo _passiveObservableId;
    private readonly MethodInfo _costHasEnough;
    private readonly MethodInfo _intAsInt;
    private readonly MethodInfo _queueGetQuantity;
    private readonly MethodInfo _queueHasRoom;
    private readonly MethodInfo _queueAdd;
    private readonly MethodInfo _instanceRecipe;
    private readonly MethodInfo _instanceQuantity;
    private readonly MethodInfo _instanceAddQuantity;
    private readonly MethodInfo _instanceInitiate;
    private readonly MethodInfo _instanceInstantCheck;
    private readonly MethodInfo _instanceInstant;
    private readonly MethodInfo _instanceExpired;
    private readonly ConstructorInfo _constructInstance;

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
        MethodInfo findObjects,
        FieldInfo pageRecipes,
        FieldInfo pageQueue,
        FieldInfo pageCraftMode,
        FieldInfo pageMainType,
        FieldInfo recipeTime,
        FieldInfo recipeListValue,
        FieldInfo instanceListValue,
        MethodInfo identity,
        MethodInfo recipeVisible,
        MethodInfo recipeCanBuy,
        MethodInfo recipeCanBuyAt,
        MethodInfo recipePurchaseAmount,
        MethodInfo recipeTotalCost,
        MethodInfo recipeMainType,
        MethodInfo recipePurchase,
        MethodInfo recipeExecute,
        MethodInfo recipeEffectChannel,
        MethodInfo effectChannelObserve,
        MethodInfo passiveObservableId,
        MethodInfo costHasEnough,
        MethodInfo intAsInt,
        MethodInfo queueGetQuantity,
        MethodInfo queueHasRoom,
        MethodInfo queueAdd,
        MethodInfo instanceRecipe,
        MethodInfo instanceQuantity,
        MethodInfo instanceAddQuantity,
        MethodInfo instanceInitiate,
        MethodInfo instanceInstantCheck,
        MethodInfo instanceInstant,
        MethodInfo instanceExpired,
        ConstructorInfo constructInstance)
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

    internal Array Pages() =>
        (Array)(_findObjects.Invoke(null, new object[] { PageType }) ?? Array.Empty<object>());

    internal IList PageRecipes(object page) =>
        (IList)(_recipeListValue.GetValue(Require(_pageRecipes.GetValue(page), RecipeListType)) ??
            throw new InvalidOperationException("Crafting page recipe list value was null."));

    internal object PageQueue(object page) => Require(_pageQueue.GetValue(page), InstanceListType);
    internal object PageMainType(object page) => Require(_pageMainType.GetValue(page), RecipeTypeType);
    internal int PageCraftMode(object page) =>
        Invoke<int>(_intAsInt, Require(_pageCraftMode.GetValue(page), IntVariableType));
    internal Guid Identity(object value) => Invoke<Guid>(_identity, value);
    internal bool RecipeVisible(object recipe) => Invoke<bool>(_recipeVisible, recipe);
    internal bool RecipeCanBuy(object recipe) => Invoke<bool>(_recipeCanBuy, recipe);
    internal double RecipeTime(object recipe) =>
        _recipeTime.GetValue(recipe) is double value
            ? value
            : throw new InvalidOperationException("CraftingRecipeSO.timeToComplete changed type.");
    internal bool RecipeCanBuyAt(object recipe, BigDouble amount) =>
        Invoke<bool>(_recipeCanBuyAt, recipe, amount);
    internal BigDouble RecipePurchaseAmount(object recipe, BigDouble previous) =>
        Invoke<BigDouble>(_recipePurchaseAmount, recipe, previous);
    internal object RecipeTotalCost(object recipe, BigDouble previous, BigDouble purchase) =>
        Require(_recipeTotalCost.Invoke(recipe, new object[] { previous, purchase }), ResourceCostType);
    internal object RecipeMainType(object recipe) =>
        Require(_recipeMainType.Invoke(recipe, Array.Empty<object>()), RecipeTypeType);
    internal void RecipePurchase(object recipe, BigDouble purchase, BigDouble previous) =>
        _recipePurchase.Invoke(recipe, new object[] { purchase, previous });
    internal void RecipeExecute(object recipe) => _recipeExecute.Invoke(recipe, Array.Empty<object>());
    internal int RecipeEffectRevision(object recipe)
    {
        var channel = Require(
            _recipeEffectChannel.Invoke(recipe, Array.Empty<object>()),
            PassiveChannelType);
        var observable = RequireAssignable(
            _effectChannelObserve.Invoke(channel, new object[] { "craft" }),
            PassiveObservableType);
        return Invoke<int>(_passiveObservableId, observable);
    }
    internal bool CostHasEnough(object cost) => Invoke<bool>(_costHasEnough, cost);
    internal BigDouble QueueQuantity(object queue, object recipe) =>
        Invoke<BigDouble>(_queueGetQuantity, queue, recipe);
    internal bool QueueHasRoom(object queue) => Invoke<bool>(_queueHasRoom, queue);
    internal IList QueueValues(object queue) =>
        (IList)(_instanceListValue.GetValue(queue) ??
            throw new InvalidOperationException("Crafting queue value was null."));
    internal void QueueAdd(object queue, object instance) =>
        _queueAdd.Invoke(queue, new[] { instance });
    internal Guid InstanceRecipe(object instance) => Invoke<Guid>(_instanceRecipe, instance);
    internal BigDouble InstanceQuantity(object instance) => Invoke<BigDouble>(_instanceQuantity, instance);
    internal void InstanceAddQuantity(object instance, BigDouble amount) =>
        _instanceAddQuantity.Invoke(instance, new object[] { amount });
    internal object ConstructInstance(object recipe, BigDouble amount) =>
        Require(_constructInstance.Invoke(new object[] { recipe, amount }), InstanceType);
    internal void InstanceInitiate(object instance) =>
        _instanceInitiate.Invoke(instance, Array.Empty<object>());
    internal bool InstanceIsInstant(object instance) =>
        Invoke<bool>(_instanceInstantCheck, instance);
    internal void InstanceInstant(object instance) =>
        _instanceInstant.Invoke(instance, Array.Empty<object>());
    internal bool InstanceExpired(object instance) => Invoke<bool>(_instanceExpired, instance);

    internal static bool TryCreate(out CraftingPlayerNativeBindings? bindings, out string reason)
    {
        bindings = null;
        try
        {
            var page = Type("UICraftingPage");
            var recipe = Type("CraftingRecipeSO");
            var recipeType = Type("CraftingRecipeTypeSO");
            var recipeList = Type("CraftingRecipeListVariable");
            var instance = Type("CraftingInstance");
            var instanceList = Type("CraftingInstanceListVariable");
            var cost = Type("ResourceCostList");
            var intVariable = Type("IntVariable");
            var big = Type("BigDouble");
            var passiveChannel = Type("PassiveObservable+Channel");
            var passiveObservable = Type("IPassiveObservable");
            var resources = typeof(Resources);
            var unityObject = typeof(UnityEngine.Object);

            bindings = new CraftingPlayerNativeBindings(
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
                StaticMethod(resources, "FindObjectsOfTypeAll", unityObject.MakeArrayType(), typeof(Type)),
                Field(page, "availableRecipes", recipeList),
                Field(page, "craftingQueueInstances", instanceList),
                Field(page, "craftMode", intVariable),
                Field(page, "mainCraftType", recipeType),
                Field(recipe, "timeToComplete", typeof(double)),
                GenericListValue(recipeList, recipe),
                GenericListValue(instanceList, instance),
                MethodFromHierarchy(recipe, "GetGuid", typeof(Guid)),
                Method(recipe, "IsVisible", typeof(bool)),
                Method(recipe, "CanBuy", typeof(bool)),
                Method(recipe, "CanBuyAt", typeof(bool), big),
                Method(recipe, "GetPurchaseQuantity", big, big),
                Method(recipe, "GetTotalCost", cost, big, big),
                Method(recipe, "GetMainType", recipeType),
                Method(recipe, "PurchaseQuantity", typeof(void), big, big),
                Method(recipe, "Execute", typeof(void)),
                Method(recipe, "GetEffectChannel", passiveChannel),
                Method(passiveChannel, "Observe", passiveObservable, typeof(string)),
                Method(passiveObservable, "GetObservableId", typeof(int)),
                Method(cost, "HasEnough", typeof(bool)),
                Method(intVariable, "AsInt", typeof(int)),
                Method(instanceList, "GetQuantity", big, recipe),
                MethodFromHierarchy(instanceList, "HasEmptySpot", typeof(bool)),
                MethodFromHierarchy(instanceList, "Add", typeof(void), instance),
                MethodFromHierarchy(instance, "GetGuidReference", typeof(Guid)),
                Method(instance, "GetQuantity", big),
                Method(instance, "AddQuantity", typeof(void), big),
                Method(instance, "Initiate", typeof(void)),
                Method(instance, "CheckInstantCraft", typeof(bool)),
                Method(instance, "InstantCraft", typeof(void)),
                Method(instance, "IsExpired", typeof(bool)),
                Constructor(instance, recipe, big));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or AmbiguousMatchException)
        {
            reason = "The complete player crafting binding set is unavailable: " + ex.Message;
            return false;
        }
    }

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

    private static T Invoke<T>(MethodInfo method, object target, params object[] arguments)
    {
        var value = method.Invoke(target, arguments);
        return value is T typed
            ? typed
            : throw new InvalidOperationException(method.DeclaringType?.Name + "." +
                method.Name + " returned the wrong type.");
    }

    private static Type Type(string name) =>
        ReflectionUtil.FindLoadedType(name) ??
        throw new InvalidOperationException(name + " was unavailable.");

    private static ConstructorInfo Constructor(Type type, params Type[] parameters) =>
        type.GetConstructor(Instance, null, parameters, null) ??
        throw new InvalidOperationException(type.Name + " constructor was unavailable.");

    private static FieldInfo Field(Type type, string name, Type fieldType)
    {
        var field = type.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != fieldType)
            throw new InvalidOperationException(type.Name + "." + name + " field was unavailable.");
        return field;
    }

    private static FieldInfo GenericListValue(Type listType, Type element)
    {
        for (var current = listType; current is not null; current = current.BaseType)
        {
            var field = current.GetField("value", Instance | BindingFlags.DeclaredOnly);
            if (field?.FieldType == typeof(System.Collections.Generic.List<>).MakeGenericType(element))
                return field;
        }
        throw new InvalidOperationException(listType.Name + ".value was unavailable.");
    }

    private static MethodInfo StaticMethod(
        Type type,
        string name,
        Type returnType,
        params Type[] parameters) =>
        ExactMethod(type, name, returnType, Static, parameters);

    private static MethodInfo Method(
        Type type,
        string name,
        Type returnType,
        params Type[] parameters) =>
        ExactMethod(type, name, returnType, Instance, parameters);

    private static MethodInfo ExactMethod(
        Type type,
        string name,
        Type returnType,
        BindingFlags flags,
        params Type[] parameters)
    {
        var method = type.GetMethod(name, flags, null, parameters, null);
        if (method is null || method.ReturnType != returnType ||
            method.IsStatic != flags.HasFlag(BindingFlags.Static))
            throw new InvalidOperationException(type.Name + "." + name + " method was unavailable.");
        return method;
    }

    private static MethodInfo MethodFromHierarchy(
        Type type,
        string name,
        Type returnType,
        params Type[] parameters)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                Instance | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method?.ReturnType == returnType && !method.IsStatic) return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " method was unavailable.");
    }
}
