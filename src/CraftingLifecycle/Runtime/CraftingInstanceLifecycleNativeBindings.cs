using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the manual and automated crafting-instance controls.</summary>
internal sealed class CraftingInstanceLifecycleNativeBindings
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
        "crafting-instance.page-automation-action",
        "crafting-player.page-main-type-action",
        "abstract-list.value",
        "id-scriptable-object.get-guid-action",
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
        "crafting-instance.recipe-multi-buy-quantity-action",
        "crafting-instance.recipe-calc-automated-action",
        "crafting-instance.page-current-automation-action",
        "crafting-instance.global-multi-buy-action",
        "crafting-instance.int-as-int-action",
        "auto-scribe.list.has-empty-spot",
        "crafting-player.queue-get-quantity-action",
    };

    private readonly MethodInfo _findPages;
    private readonly FieldInfo _pageRecipes;
    private readonly FieldInfo _pageQueue;
    private readonly FieldInfo _pageAutomation;
    private readonly FieldInfo _pageMainType;
    private readonly FieldInfo _recipeValues;
    private readonly FieldInfo _instanceValues;
    private readonly MethodInfo _identity;
    private readonly MethodInfo _recipeMainType;
    private readonly MethodInfo _getInstance;
    private readonly MethodInfo _automate;
    private readonly MethodInfo _removeAutomation;
    private readonly MethodInfo _instanceRecipe;
    private readonly MethodInfo _automationQuantity;
    private readonly MethodInfo _isAuto;
    private readonly MethodInfo _cancel;
    private readonly MethodInfo _remove;
    private readonly MethodInfo _visible;
    private readonly MethodInfo _multiBuyQuantity;
    private readonly MethodInfo _calcAutomated;
    private readonly MethodInfo _currentAutomation;
    private readonly MethodInfo _multiBuy;
    private readonly MethodInfo _asInt;
    private readonly MethodInfo _hasRoom;
    private readonly MethodInfo _queueQuantity;

    private CraftingInstanceLifecycleNativeBindings(
        Type pageType,
        Type recipeType,
        Type instanceType,
        Type queueType,
        MethodInfo findPages,
        FieldInfo pageRecipes,
        FieldInfo pageQueue,
        FieldInfo pageAutomation,
        FieldInfo pageMainType,
        FieldInfo recipeValues,
        FieldInfo instanceValues,
        MethodInfo identity,
        MethodInfo recipeMainType,
        MethodInfo getInstance,
        MethodInfo automate,
        MethodInfo removeAutomation,
        MethodInfo instanceRecipe,
        MethodInfo automationQuantity,
        MethodInfo isAuto,
        MethodInfo cancel,
        MethodInfo remove,
        MethodInfo visible,
        MethodInfo multiBuyQuantity,
        MethodInfo calcAutomated,
        MethodInfo currentAutomation,
        MethodInfo multiBuy,
        MethodInfo asInt,
        MethodInfo hasRoom,
        MethodInfo queueQuantity)
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
        _identity = identity;
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
        _multiBuyQuantity = multiBuyQuantity;
        _calcAutomated = calcAutomated;
        _currentAutomation = currentAutomation;
        _multiBuy = multiBuy;
        _asInt = asInt;
        _hasRoom = hasRoom;
        _queueQuantity = queueQuantity;
    }

    internal Type PageType { get; }
    internal Type RecipeType { get; }
    internal Type InstanceType { get; }
    internal Type QueueType { get; }

    internal Array Pages() =>
        (Array)(_findPages.Invoke(null, new object[] { PageType }) ?? Array.Empty<object>());
    internal IList PageRecipes(object page) => Values(_recipeValues, _pageRecipes.GetValue(page));
    internal object PageQueue(object page) => Exact(_pageQueue.GetValue(page), QueueType);
    internal object PageAutomation(object page) => Exact(_pageAutomation.GetValue(page), QueueType);
    internal object PageMainType(object page) =>
        _pageMainType.GetValue(page) ?? throw new InvalidOperationException("Crafting page type was null.");
    internal Guid Identity(object value) => Invoke<Guid>(_identity, value);
    internal object RecipeMainType(object recipe) =>
        _recipeMainType.Invoke(recipe, Array.Empty<object>()) ??
        throw new InvalidOperationException("Crafting recipe type was null.");
    internal bool RecipeVisible(object recipe) => Invoke<bool>(_visible, recipe);
    internal IList QueueValues(object queue) => Values(_instanceValues, queue);
    internal object? QueueInstance(object queue, object recipe) =>
        _getInstance.Invoke(queue, new[] { recipe });
    internal bool QueueHasRoom(object queue) => Invoke<bool>(_hasRoom, queue);
    internal BigDouble QueueQuantity(object queue, object recipe) =>
        Invoke<BigDouble>(_queueQuantity, queue, recipe);
    internal object Automate(object queue, object recipe, int amount) =>
        Exact(_automate.Invoke(queue, new object[] { recipe, amount }), InstanceType);
    internal void RemoveAutomation(object queue, object instance, int amount) =>
        _removeAutomation.Invoke(queue, new object[] { instance, amount });
    internal Guid InstanceRecipe(object instance) => Invoke<Guid>(_instanceRecipe, instance);
    internal int AutomationQuantity(object instance) => Invoke<int>(_automationQuantity, instance);
    internal bool IsAuto(object instance) => Invoke<bool>(_isAuto, instance);
    internal void Cancel(object instance) => _cancel.Invoke(instance, Array.Empty<object>());
    internal void QueueRemove(object queue, object instance) =>
        _remove.Invoke(queue, new[] { instance });
    internal BigDouble RecipeMultiBuyQuantity(object recipe, BigDouble current) =>
        Invoke<BigDouble>(_multiBuyQuantity, recipe, current);
    internal int CalcAutomated(BigDouble quantity) =>
        InvokeStatic<int>(_calcAutomated, quantity);
    internal int CurrentAutomation(object page, object recipe) =>
        Invoke<int>(_currentAutomation, page, recipe);
    internal int MultiBuy()
    {
        var value = _multiBuy.Invoke(null, Array.Empty<object>()) ??
            throw new InvalidOperationException("The multi-buy value was null.");
        return Invoke<int>(_asInt, value);
    }

    internal static bool TryCreate(
        out CraftingInstanceLifecycleNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(string name) => resolveType(name) ??
                throw new InvalidOperationException(name + " was unavailable");
            void Require(int index)
            {
                if (!includeContract(ContractIds[index]))
                    throw new InvalidOperationException(ContractIds[index] + " was unavailable");
            }

            var page = T("UICraftingPage");
            var recipe = T("CraftingRecipeSO");
            var recipeType = T("CraftingRecipeTypeSO");
            var recipeList = T("CraftingRecipeListVariable");
            var queue = T("CraftingInstanceListVariable");
            var instance = T("CraftingInstance");
            var integer = T("IntVariable");
            // UnityEngine.CoreModule is not the game's Assembly-CSharp module. Resolve the
            // framework-owned types from the loaded Unity reference, just like player crafting.
            var resources = typeof(Resources);
            var unityObject = typeof(UnityEngine.Object);
            var global = T("GlobalVariables");

            for (var index = 0; index < ContractIds.Length; index++) Require(index);
            var abstractInstanceList = FindGenericBase(queue, "AbstractListVariable`1");
            bindings = new CraftingInstanceLifecycleNativeBindings(
                page,
                recipe,
                instance,
                queue,
                StaticMethod(resources, "FindObjectsOfTypeAll", unityObject.MakeArrayType(), typeof(Type)),
                Field(page, "availableRecipes", recipeList),
                Field(page, "craftingQueueInstances", queue),
                Field(page, "craftingAutomationInstances", queue),
                Field(page, "mainCraftType", recipeType),
                GenericListValue(recipeList, recipe),
                GenericListValue(queue, instance),
                MethodFromHierarchy(recipe, "GetGuid", typeof(Guid)),
                Method(recipe, "GetMainType", recipeType),
                Method(queue, "GetInstance", instance, recipe),
                Method(queue, "AutomateCraft", instance, recipe, typeof(int)),
                Method(queue, "RemoveAutomation", typeof(void), instance, typeof(int)),
                MethodFromHierarchy(instance, "GetGuidReference", typeof(Guid)),
                Method(instance, "GetAutomationQuantity", typeof(int)),
                Method(instance, "IsAuto", typeof(bool)),
                Method(instance, "CancelCraft", typeof(void)),
                Method(abstractInstanceList, "Remove", typeof(void), instance),
                Method(recipe, "IsVisible", typeof(bool)),
                Method(recipe, "GetMultiBuyQuantity", typeof(BigDouble), typeof(BigDouble)),
                StaticMethod(recipe, "CalcAutomatedQuantity", typeof(int), typeof(BigDouble)),
                Method(page, "GetAutoCraftingQuantity", typeof(int), recipe),
                StaticMethod(global, "GetMultiBuy", integer),
                Method(integer, "AsInt", typeof(int)),
                MethodFromHierarchy(queue, "HasEmptySpot", typeof(bool)),
                Method(queue, "GetQuantity", typeof(BigDouble), recipe));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or AmbiguousMatchException)
        {
            reason = "Crafting instance contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private static IList Values(FieldInfo field, object? owner)
    {
        if (owner is null) throw new InvalidOperationException("Crafting list was null.");
        return (IList)(field.GetValue(owner) ??
            throw new InvalidOperationException("Crafting list value was null."));
    }

    private static object Exact(object? value, Type type) =>
        value is not null && value.GetType() == type
            ? value
            : throw new InvalidOperationException(type.Name + " value was null or wrong-typed.");

    private static T Invoke<T>(MethodInfo method, object target, params object[] arguments)
    {
        var value = method.Invoke(target, arguments);
        return value is T typed
            ? typed
            : throw new InvalidOperationException(method.Name + " returned the wrong type.");
    }

    private static T InvokeStatic<T>(MethodInfo method, params object[] arguments)
    {
        var value = method.Invoke(null, arguments);
        return value is T typed
            ? typed
            : throw new InvalidOperationException(method.Name + " returned the wrong type.");
    }

    private static FieldInfo Field(Type type, string name, Type exactType)
    {
        var field = type.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != exactType)
            throw new InvalidOperationException(type.Name + "." + name + " did not match.");
        return field;
    }

    private static FieldInfo GenericListValue(Type type, Type element)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField("value", Instance | BindingFlags.DeclaredOnly);
            if (field?.FieldType == typeof(System.Collections.Generic.List<>).MakeGenericType(element))
                return field;
        }
        throw new InvalidOperationException(type.Name + ".value did not match.");
    }

    private static Type FindGenericBase(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition().Name == name)
                return current;
        throw new InvalidOperationException(type.Name + " has no " + name + " base.");
    }

    private static MethodInfo Method(Type type, string name, Type result, params Type[] arguments)
    {
        var method = type.GetMethod(name, Instance, null, arguments, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " did not match.");
        return method;
    }

    private static MethodInfo MethodFromHierarchy(
        Type type,
        string name,
        Type result,
        params Type[] arguments)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(name, Instance | BindingFlags.DeclaredOnly,
                null, arguments, null);
            if (method is not null && !method.IsStatic && method.ReturnType == result)
                return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " did not match.");
    }

    private static MethodInfo StaticMethod(
        Type type,
        string name,
        Type result,
        params Type[] arguments)
    {
        var method = type.GetMethod(name, Static, null, arguments, null);
        if (method is null || !method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " did not match.");
        return method;
    }
}
