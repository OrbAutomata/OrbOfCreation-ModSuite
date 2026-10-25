using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact native crafting primitives shared by automation and the player press.</summary>
internal sealed class CraftingNativeOperations
{
    private CraftingNativeOperations(NativeBindingSet set, Type recipe, Type recipeType,
        Type instance, Type instanceList, Type cost, Type big)
    {
        _instanceType = instance;
        _mainTypeType = recipeType;
        _costType = cost;
        _constructInstance = set.New<Func<object, BigDouble, object>>("auto-scribe.instance.construct", instance, recipe, big);
        InstanceListValue = set.Field<Func<object, IList?>>(
            "abstract-list.value", instanceList, "value", typeof(List<>).MakeGenericType(instance));
        RecipeVisible = set.Call<Func<object, bool>>("auto-scribe.recipe.visible", recipe, "IsVisible", typeof(bool));
        RecipeCanBuyAt = set.Call<Func<object, BigDouble, bool>>(
            "auto-scribe.recipe.can-buy-at", recipe, "CanBuyAt", typeof(bool), big);
        _recipeTotalCost = set.Call<Func<object, BigDouble, BigDouble, object?>>(
            "auto-scribe.recipe.get-total-cost", recipe, "GetTotalCost", cost, big, big);
        _recipeMainType = set.Call<Func<object, object?>>("auto-scribe.recipe.get-main-type", recipe, "GetMainType", recipeType);
        RecipePurchase = set.Call<Action<object, BigDouble, BigDouble>>(
            "auto-scribe.recipe.purchase-quantity", recipe, "PurchaseQuantity", typeof(void), big, big);
        CostHasEnough = set.Call<Func<object, bool>>("resource-cost-list.has-enough", cost, "HasEnough", typeof(bool));
        QueueHasRoom = set.Call<Func<object, bool>>(
            "auto-scribe.list.has-empty-spot", instanceList, "HasEmptySpot", typeof(bool));
        QueueAdd = set.Call<Action<object, object>>("auto-scribe.list.add", instanceList, "Add", typeof(void), instance);
        InstanceRecipe = set.Call<Func<object, Guid>>(
            "crafting-player.instance-recipe-action", instance, "GetGuidReference", typeof(Guid));
        InstanceQuantity = set.Call<Func<object, BigDouble>>(
            "crafting-player.instance-quantity-action", instance, "GetQuantity", big);
        InstanceExpired = set.Call<Func<object, bool>>(
            "crafting-player.instance-expired-action", instance, "IsExpired", typeof(bool));
        InstanceInitiate = set.Call<Action<object>>("auto-scribe.instance.initiate", instance, "Initiate", typeof(void));
        InstanceInstantCheck = set.Call<Func<object, bool>>(
            "auto-scribe.instance.check-instant", instance, "CheckInstantCraft", typeof(bool));
        InstanceInstant = set.Call<Action<object>>("auto-scribe.instance.instant", instance, "InstantCraft", typeof(void));
    }

    private readonly Type _instanceType;
    private readonly Type _mainTypeType;
    private readonly Type _costType;
    private readonly Func<object, BigDouble, object> _constructInstance;
    private readonly Func<object, BigDouble, BigDouble, object?> _recipeTotalCost;
    private readonly Func<object, object?> _recipeMainType;

    internal Func<object, IList?> InstanceListValue { get; }
    internal Func<object, bool> RecipeVisible { get; }
    internal Func<object, BigDouble, bool> RecipeCanBuyAt { get; }
    internal Action<object, BigDouble, BigDouble> RecipePurchase { get; }
    internal Func<object, bool> CostHasEnough { get; }
    internal Func<object, bool> QueueHasRoom { get; }
    internal Action<object, object> QueueAdd { get; }
    internal Func<object, Guid> InstanceRecipe { get; }
    internal Func<object, BigDouble> InstanceQuantity { get; }
    internal Func<object, bool> InstanceExpired { get; }
    internal Action<object> InstanceInitiate { get; }
    internal Func<object, bool> InstanceInstantCheck { get; }
    internal Action<object> InstanceInstant { get; }

    internal object RecipeTotalCost(object recipe, BigDouble previous, BigDouble purchase) =>
        Require(_recipeTotalCost(recipe, previous, purchase), _costType);

    internal object RecipeMainType(object recipe) => Require(_recipeMainType(recipe), _mainTypeType);

    internal object ConstructInstance(object recipe, BigDouble quantity) =>
        Require(_constructInstance(recipe, quantity), _instanceType);

    internal IList QueueValues(object queue) => InstanceListValue(queue) ??
        throw new InvalidOperationException("Crafting queue value was null.");

    internal bool WorkObserved(object queue, object? instance, bool instant)
    {
        if (instance is null) return false;
        if (instant) return InstanceExpired(instance);
        var work = QueueValues(queue);
        foreach (var value in work)
            if (ReferenceEquals(value, instance)) return true;
        return false;
    }

    private static object Require(object? value, Type type)
    {
        if (value is null || value.GetType() != type)
            throw new InvalidOperationException(type.Name + " value was null or wrong-typed.");
        return value;
    }

    internal static CraftingNativeOperations Bind(NativeBindingSet set, Type recipe, Type recipeType,
        Type instance, Type instanceList, Type cost, Type big) =>
        new(set, recipe, recipeType, instance, instanceList, cost, big);
}
