using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for the generic <c>IDiscoverable</c> transaction. Reflection is
/// confined to construction; execution uses only exact compiled delegates and the shared typed
/// identity registry.
/// </summary>
internal sealed class GenericDiscoveryNativeBindings
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "generic-discovery.discoverable.type-action",
        "generic-discovery.cost-list.type-action",
        "generic-discovery.alchemy-recipe.type-action",
        "generic-discovery.equipment.type-action",
        "generic-discovery.glyph.type-action",
        "generic-discovery.ritual.type-action",
        "generic-discovery.spell-recipe.type-action",
        "generic-discovery.time-rune.type-action",
        "generic-discovery.get-cost-action",
        "generic-discovery.is-visible-action",
        "generic-discovery.can-discover-action",
        "generic-discovery.is-discovered-action",
        "generic-discovery.discover-action",
        "generic-discovery.cost-enough-action",
        "generic-discovery.cost-perform-action",
        "generic-discovery.get-glyph-recipe-action",
        "generic-discovery.view.type-action",
        "generic-discovery.owning-view-availability-action",
    };

    private GenericDiscoveryNativeBindings(
        Type discoverableType,
        Type costType,
        Type viewType,
        IReadOnlyDictionary<string, Type> supportedTypes,
        Func<object, object> getCost,
        Func<object, bool> isVisible,
        Func<object, bool> canDiscover,
        Func<object, bool> isDiscovered,
        Action<object> discover,
        Func<object, bool> hasEnough,
        Action<object> performCost,
        Func<object, IList> getGlyphRecipe,
        Func<object, bool> isViewAvailable)
    {
        DiscoverableType = discoverableType;
        CostType = costType;
        ViewType = viewType;
        SupportedTypes = supportedTypes;
        GetCost = getCost;
        IsVisible = isVisible;
        CanDiscover = canDiscover;
        IsDiscovered = isDiscovered;
        Discover = discover;
        HasEnough = hasEnough;
        PerformCost = performCost;
        GetGlyphRecipe = getGlyphRecipe;
        IsViewAvailable = isViewAvailable;
    }

    internal Type DiscoverableType { get; }
    internal Type CostType { get; }

    /// <summary>
    /// The screen a discovery row is drawn on. <c>ViewSO.IsAvailable()</c> is the game's own
    /// question about it, so a locked screen is read rather than inferred from the rows.
    /// </summary>
    internal Type ViewType { get; }
    internal IReadOnlyDictionary<string, Type> SupportedTypes { get; }
    internal Func<object, object> GetCost { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Func<object, bool> CanDiscover { get; }
    internal Func<object, bool> IsDiscovered { get; }
    internal Action<object> Discover { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Action<object> PerformCost { get; }
    internal Func<object, IList> GetGlyphRecipe { get; }
    internal Func<object, bool> IsViewAvailable { get; }

    internal static bool TryCreate(
        out GenericDiscoveryNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(string contract, string name)
            {
                Require(contract, includeContract);
                return resolveType(name) ??
                    throw new InvalidOperationException(name + " was unavailable");
            }
            MethodInfo M(
                string contract,
                Type type,
                string name,
                Type result,
                params Type[] parameters)
            {
                Require(contract, includeContract);
                var method = type.GetMethod(name, Instance, null, parameters, null);
                if (method is null || method.IsStatic || method.ReturnType != result)
                    throw new InvalidOperationException(
                        type.Name + "." + name + " did not match the audited signature");
                return method;
            }
            var discoverable = T(ContractIds[0], "IDiscoverable");
            var cost = T(ContractIds[1], "ResourceCostList");
            var supported = new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["AlchemyRecipeSO"] = T(ContractIds[2], "AlchemyRecipeSO"),
                ["EquipmentSO"] = T(ContractIds[3], "EquipmentSO"),
                ["GlyphSO"] = T(ContractIds[4], "GlyphSO"),
                ["RitualSO"] = T(ContractIds[5], "RitualSO"),
                ["SpellRecipeSO"] = T(ContractIds[6], "SpellRecipeSO"),
                ["TimeRuneSO"] = T(ContractIds[7], "TimeRuneSO"),
            };
            foreach (var pair in supported)
                if (!discoverable.IsAssignableFrom(pair.Value))
                    throw new InvalidOperationException(
                        pair.Key + " does not implement the exact IDiscoverable contract");

            var getCost = M(ContractIds[8], discoverable, "GetDiscoverCost", cost);
            var visible = M(ContractIds[9], discoverable, "IsDiscoverVisible", typeof(bool));
            var canDiscover = M(ContractIds[10], discoverable, "CanDiscover", typeof(bool));
            var discovered = M(ContractIds[11], discoverable, "IsDiscovered", typeof(bool));
            var discover = M(ContractIds[12], discoverable, "Discover", typeof(void));
            var enough = M(ContractIds[13], cost, "HasEnough", typeof(bool));
            var perform = M(ContractIds[14], cost, "PerformCost", typeof(void));
            var glyphRecipe = M(
                ContractIds[15],
                discoverable,
                "GetGlyphRecipe",
                typeof(List<>).MakeGenericType(supported["GlyphSO"]));
            var view = T(ContractIds[16], "ViewSO");
            var viewAvailable = M(ContractIds[17], view, "IsAvailable", typeof(bool));

            bindings = new GenericDiscoveryNativeBindings(
                discoverable,
                cost,
                view,
                supported,
                InstanceObjectFunc(getCost),
                InstanceFunc<bool>(visible),
                InstanceFunc<bool>(canDiscover),
                InstanceFunc<bool>(discovered),
                InstanceAction(discover),
                InstanceFunc<bool>(enough),
                InstanceAction(perform),
                InstanceListFunc(glyphRecipe),
                InstanceFunc<bool>(viewAvailable));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or AmbiguousMatchException or ArgumentException)
        {
            reason = "The complete generic discovery binding set is unavailable: " + exception.Message;
            return false;
        }
    }

    private static void Require(string contract, Func<string, bool> include)
    {
        if (!include(contract))
            throw new InvalidOperationException("Required contract " + contract + " was withheld");
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(call, typeof(T)), target).Compile();
    }

    private static Func<object, object> InstanceObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(call, typeof(object)), target).Compile();
    }

    private static Func<object, IList> InstanceListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(call, typeof(IList)), target).Compile();
    }

    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

}
