using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the player-visible plot action list.</summary>
internal sealed class PlotLifecycleNativeBindings
{
    internal static readonly Guid ActiveActionsId =
        new("70871e86-100b-4ae0-ba9b-fc96e09b7e1f");

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "plot-lifecycle.plot.type-action",
        "plot-lifecycle.action.type-action",
        "plot-lifecycle.instance.type-action",
        "plot-lifecycle.list.type-action",
        "plot-lifecycle.plot-visible-action",
        "plot-lifecycle.plot-instances-action",
        "plot-lifecycle.instance-action-action",
        "plot-lifecycle.instance-plot-action",
        "plot-lifecycle.instance-visible-action",
        "plot-lifecycle.instance-affordable-action",
        "plot-lifecycle.instance-maximum-remaining-action",
        "plot-lifecycle.instance-maximum-action",
        "plot-lifecycle.instance-quantity-action",
        "plot-lifecycle.instance-at-minimum-action",
        "plot-lifecycle.instance-cancel-action",
        "plot-lifecycle.list-find-action",
        "plot-lifecycle.list-room-action",
        "plot-lifecycle.list-add-action",
        "plot-lifecycle.list-remove-action",
    };

    private PlotLifecycleNativeBindings(
        Type plotType,
        Type actionType,
        Type instanceType,
        Type listType,
        Func<object, bool> plotVisible,
        Func<object, IList?> plotInstances,
        Func<object, object?> instanceAction,
        Func<object, object?> instancePlot,
        Func<object, bool> instanceVisible,
        Func<object, bool> instanceAffordable,
        Func<object, int> instanceMaximumRemaining,
        Func<object, int> instanceMaximum,
        Func<object, int> instanceQuantity,
        Func<object, bool> instanceAtMinimum,
        Action<object> instanceCancel,
        Func<object, object, object?> findInstance,
        Func<object, bool> listHasRoom,
        Action<object, object, int> addInstance,
        Action<object, object, int> removeInstance)
    {
        PlotType = plotType;
        ActionType = actionType;
        InstanceType = instanceType;
        ListType = listType;
        PlotVisible = plotVisible;
        PlotInstances = plotInstances;
        InstanceAction = instanceAction;
        InstancePlot = instancePlot;
        InstanceVisible = instanceVisible;
        InstanceAffordable = instanceAffordable;
        InstanceMaximumRemaining = instanceMaximumRemaining;
        InstanceMaximum = instanceMaximum;
        InstanceQuantity = instanceQuantity;
        InstanceAtMinimum = instanceAtMinimum;
        InstanceCancel = instanceCancel;
        FindInstance = findInstance;
        ListHasRoom = listHasRoom;
        AddInstance = addInstance;
        RemoveInstance = removeInstance;
    }

    internal Type PlotType { get; }
    internal Type ActionType { get; }
    internal Type InstanceType { get; }
    internal Type ListType { get; }
    internal Func<object, bool> PlotVisible { get; }
    internal Func<object, IList?> PlotInstances { get; }
    internal Func<object, object?> InstanceAction { get; }
    internal Func<object, object?> InstancePlot { get; }
    internal Func<object, bool> InstanceVisible { get; }
    internal Func<object, bool> InstanceAffordable { get; }
    internal Func<object, int> InstanceMaximumRemaining { get; }
    internal Func<object, int> InstanceMaximum { get; }
    internal Func<object, int> InstanceQuantity { get; }
    internal Func<object, bool> InstanceAtMinimum { get; }
    internal Action<object> InstanceCancel { get; }
    internal Func<object, object, object?> FindInstance { get; }
    internal Func<object, bool> ListHasRoom { get; }
    internal Action<object, object, int> AddInstance { get; }
    internal Action<object, object, int> RemoveInstance { get; }

    internal static bool TryCreate(
        out PlotLifecycleNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(int index, string name)
            {
                Require(index, includeContract);
                return resolveType(name) ??
                    throw new InvalidOperationException(name + " was unavailable");
            }

            var plot = T(0, "PlotNodeSO");
            var action = T(1, "PlotNodeActionSO");
            var instance = T(2, "PlotNodeActionInstance");
            var list = T(3, "PlotNodeActionInstanceListVariable");
            var plotVisible = Method(4, plot, "IsVisible", typeof(bool), Type.EmptyTypes, includeContract);
            var plotInstances = Method(5, plot, "GetActionInstances", null, Type.EmptyTypes, includeContract);
            if (!typeof(IList).IsAssignableFrom(plotInstances.ReturnType))
                throw new InvalidOperationException("PlotNodeSO.GetActionInstances did not return a list");
            var instanceAction = Method(6, instance, "GetAction", action, Type.EmptyTypes, includeContract);
            var instancePlot = Method(7, instance, "GetElement", plot, Type.EmptyTypes, includeContract);
            var instanceVisible = Method(8, instance, "IsVisible", typeof(bool), Type.EmptyTypes, includeContract);
            var affordable = Method(9, instance, "HasEnoughForOneInstance", typeof(bool), Type.EmptyTypes, includeContract);
            var maximumRemaining = Method(10, instance, "GetMaximumRemInstances", typeof(int), Type.EmptyTypes, includeContract);
            var maximum = Method(11, instance, "GetMaximumInstances", typeof(int), Type.EmptyTypes, includeContract);
            var quantity = Method(12, instance, "GetActualQuantity", typeof(int), Type.EmptyTypes, includeContract);
            var atMinimum = Method(13, instance, "IsAtMinimumQuantity", typeof(bool), Type.EmptyTypes, includeContract);
            var cancel = Method(14, instance, "Cancel", typeof(void), Type.EmptyTypes, includeContract);
            var find = Method(15, list, "FindInstance", instance, new[] { instance }, includeContract);
            var listBase = FindGenericBase(list, "EmptyTypeListVariable`1");
            var room = Method(16, listBase, "HasEmptySpot", typeof(bool), Type.EmptyTypes, includeContract);
            var add = Method(17, list, "AddInstance", typeof(void), new[] { instance, typeof(int) }, includeContract);
            var remove = Method(18, list, "RemoveInstance", typeof(void), new[] { instance, typeof(int) }, includeContract);

            bindings = new PlotLifecycleNativeBindings(
                plot, action, instance, list,
                Func<bool>(plotVisible), ListFunc(plotInstances), ObjectFunc(instanceAction),
                ObjectFunc(instancePlot), Func<bool>(instanceVisible), Func<bool>(affordable),
                Func<int>(maximumRemaining), Func<int>(maximum), Func<int>(quantity),
                Func<bool>(atMinimum), Action(cancel), ObjectFunc2(find), Func<bool>(room),
                Action2<int>(add), Action2<int>(remove));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or AmbiguousMatchException or NotSupportedException)
        {
            reason = "Plot lifecycle contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private static void Require(int index, Func<string, bool> include)
    {
        if (!include(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }

    private static MethodInfo Method(int index, Type owner, string name, Type? result,
        Type[] parameters, Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || result is not null && method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return method;
    }

    private static Type FindGenericBase(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition().Name == name)
                return current;
        throw new InvalidOperationException(type.Name + " has no " + name + " base.");
    }

    private static Func<object, T> Func<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(T)), target).Compile();
    }

    private static Func<object, object?> ObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(object)), target).Compile();
    }

    private static Func<object, object, object?> ObjectFunc2(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        return Expression.Lambda<Func<object, object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(argument, method.GetParameters()[0].ParameterType)), typeof(object)),
            target, argument).Compile();
    }

    private static Func<object, IList?> ListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(IList)), target).Compile();
    }

    private static Action<object> Action(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Action<object, object, T> Action2<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var first = Expression.Parameter(typeof(object), "first");
        var second = Expression.Parameter(typeof(T), "second");
        return Expression.Lambda<Action<object, object, T>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(first, method.GetParameters()[0].ParameterType), second),
            target, first, second).Compile();
    }
}
