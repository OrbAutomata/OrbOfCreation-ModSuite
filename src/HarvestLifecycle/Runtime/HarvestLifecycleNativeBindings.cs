using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the active harvest element/action lists.</summary>
internal sealed class HarvestLifecycleNativeBindings
{
    internal static readonly Guid ActiveElementsId =
        new("5a9f8001-3ae2-4799-86b6-5198763e0fe2");
    internal static readonly Guid ActiveActionsId =
        new("e4a9d4c3-61cc-4f94-bab9-7bc8e841cc32");

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "harvest-lifecycle.element.type-action",
        "harvest-lifecycle.action.type-action",
        "harvest-lifecycle.instance.type-action",
        "harvest-lifecycle.element-list.type-action",
        "harvest-lifecycle.action-list.type-action",
        "harvest-lifecycle.cost.type-action",
        "harvest-lifecycle.element-visible-action",
        "harvest-lifecycle.element-available-action",
        "harvest-lifecycle.element-usage-cost-action",
        "harvest-lifecycle.element-maximum-instances-action",
        "harvest-lifecycle.element-action-instances-action",
        "harvest-lifecycle.instance-action-action",
        "harvest-lifecycle.instance-element-action",
        "harvest-lifecycle.instance-visible-action",
        "harvest-lifecycle.instance-maximum-action",
        "harvest-lifecycle.instance-count-action",
        "harvest-lifecycle.element-list-stacks-action",
        "harvest-lifecycle.list-has-empty-spot-action",
        "harvest-lifecycle.element-list-add-action",
        "harvest-lifecycle.element-list-remove-action",
        "harvest-lifecycle.action-list-find-action",
        "harvest-lifecycle.action-list-add-action",
        "harvest-lifecycle.action-list-remove-action",
        "harvest-lifecycle.cost-has-enough-action",
    };

    private HarvestLifecycleNativeBindings(
        Type elementType,
        Type actionType,
        Type instanceType,
        Type elementListType,
        Type actionListType,
        Func<object, bool> elementVisible,
        Func<object, bool> elementAvailable,
        Func<object, object?> elementUsageCost,
        Func<object, BigDouble> elementMaximumInstances,
        Func<object, IList?> elementActionInstances,
        Func<object, object?> instanceAction,
        Func<object, object?> instanceElement,
        Func<object, bool> instanceVisible,
        Func<object, int> instanceMaximum,
        Func<object, int> instanceCount,
        Func<object, object, int> elementStacks,
        Func<object, bool> elementListHasRoom,
        Func<object, bool> actionListHasRoom,
        Action<object, object, BigDouble> addElement,
        Action<object, object, BigDouble> removeElement,
        Func<object, object, object?> findAction,
        Action<object, object, int> addAction,
        Action<object, object, int> removeAction,
        Func<object, bool> costHasEnough)
    {
        ElementType = elementType;
        ActionType = actionType;
        InstanceType = instanceType;
        ElementListType = elementListType;
        ActionListType = actionListType;
        ElementVisible = elementVisible;
        ElementAvailable = elementAvailable;
        ElementUsageCost = elementUsageCost;
        ElementMaximumInstances = elementMaximumInstances;
        ElementActionInstances = elementActionInstances;
        InstanceAction = instanceAction;
        InstanceElement = instanceElement;
        InstanceVisible = instanceVisible;
        InstanceMaximum = instanceMaximum;
        InstanceCount = instanceCount;
        ElementStacks = elementStacks;
        ElementListHasRoom = elementListHasRoom;
        ActionListHasRoom = actionListHasRoom;
        AddElement = addElement;
        RemoveElement = removeElement;
        FindAction = findAction;
        AddAction = addAction;
        RemoveAction = removeAction;
        CostHasEnough = costHasEnough;
    }

    internal Type ElementType { get; }
    internal Type ActionType { get; }
    internal Type InstanceType { get; }
    internal Type ElementListType { get; }
    internal Type ActionListType { get; }
    internal Func<object, bool> ElementVisible { get; }
    internal Func<object, bool> ElementAvailable { get; }
    internal Func<object, object?> ElementUsageCost { get; }
    internal Func<object, BigDouble> ElementMaximumInstances { get; }
    internal Func<object, IList?> ElementActionInstances { get; }
    internal Func<object, object?> InstanceAction { get; }
    internal Func<object, object?> InstanceElement { get; }
    internal Func<object, bool> InstanceVisible { get; }
    internal Func<object, int> InstanceMaximum { get; }
    internal Func<object, int> InstanceCount { get; }
    internal Func<object, object, int> ElementStacks { get; }
    internal Func<object, bool> ElementListHasRoom { get; }
    internal Func<object, bool> ActionListHasRoom { get; }
    internal Action<object, object, BigDouble> AddElement { get; }
    internal Action<object, object, BigDouble> RemoveElement { get; }
    internal Func<object, object, object?> FindAction { get; }
    internal Action<object, object, int> AddAction { get; }
    internal Action<object, object, int> RemoveAction { get; }
    internal Func<object, bool> CostHasEnough { get; }

    internal static bool TryCreate(
        out HarvestLifecycleNativeBindings? bindings,
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

            var element = T(0, "HarvestElementSO");
            var action = T(1, "HarvestActionSO");
            var instance = T(2, "HarvestActionInstance");
            var elementList = T(3, "HarvestElementListVariable");
            var actionList = T(4, "HarvestActionInstanceListVariable");
            var cost = T(5, "ResourceCostList");

            var elementVisible = Method(6, element, "IsVisible", typeof(bool), Type.EmptyTypes, includeContract);
            var elementAvailable = Method(7, element, "IsAvailable", typeof(bool), Type.EmptyTypes, includeContract);
            var usageCost = Field(8, element, "usageCost", cost, includeContract);
            var maximum = Method(9, element, "MaximumNumberInstances", typeof(BigDouble), Type.EmptyTypes, includeContract);
            var actionInstances = Method(10, element, "GetActionInstances", null, Type.EmptyTypes, includeContract);
            if (!typeof(IList).IsAssignableFrom(actionInstances.ReturnType))
                throw new InvalidOperationException("HarvestElementSO.GetActionInstances did not return a list");
            var instanceAction = Method(11, instance, "GetAction", action, Type.EmptyTypes, includeContract);
            var instanceElement = Method(12, instance, "GetElement", element, Type.EmptyTypes, includeContract);
            var instanceVisible = Method(13, instance, "IsVisible", typeof(bool), Type.EmptyTypes, includeContract);
            var instanceMaximum = Method(14, instance, "GetMaximumInstances", typeof(int), Type.EmptyTypes, includeContract);
            var instanceCount = Field(15, instance, "instances", typeof(int), includeContract);
            var elementStacks = Method(16, elementList, "GetStacks", typeof(int),
                new[] { element }, includeContract);
            var elementListBase = FindGenericBase(elementList, "GenericListVariable`1");
            var actionListBase = FindGenericBase(actionList, "GenericListVariable`1");
            var elementRoom = Method(17, elementListBase, "HasEmptySpot", typeof(bool),
                Type.EmptyTypes, includeContract);
            var actionRoom = Method(17, actionListBase, "HasEmptySpot", typeof(bool),
                Type.EmptyTypes, includeContract);
            var addElement = Method(18, elementList, "AddInstance", typeof(void),
                new[] { element, typeof(BigDouble) }, includeContract);
            var removeElement = Method(19, elementList, "RemoveInstance", typeof(void),
                new[] { element, typeof(BigDouble) }, includeContract);
            var findAction = Method(20, actionList, "FindInstance", instance,
                new[] { instance }, includeContract);
            var addAction = Method(21, actionList, "AddInstance", typeof(void),
                new[] { instance, typeof(int) }, includeContract);
            var removeAction = Method(22, actionList, "RemoveInstance", typeof(void),
                new[] { instance, typeof(int) }, includeContract);
            var hasEnough = Method(23, cost, "HasEnough", typeof(bool), Type.EmptyTypes, includeContract);

            bindings = new HarvestLifecycleNativeBindings(
                element, action, instance, elementList, actionList,
                Func<bool>(elementVisible), Func<bool>(elementAvailable),
                ObjectField(usageCost), Func<BigDouble>(maximum), ListFunc(actionInstances),
                ObjectFunc(instanceAction), ObjectFunc(instanceElement), Func<bool>(instanceVisible),
                Func<int>(instanceMaximum), FieldFunc<int>(instanceCount),
                Func2<int>(elementStacks), Func<bool>(elementRoom), Func<bool>(actionRoom),
                Action2<BigDouble>(addElement), Action2<BigDouble>(removeElement),
                ObjectFunc2(findAction), Action2<int>(addAction), Action2<int>(removeAction),
                Func<bool>(hasEnough));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or AmbiguousMatchException or NotSupportedException)
        {
            reason = "Harvest lifecycle contracts are unavailable: " +
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
        if (method is null || method.IsStatic || (result is not null && method.ReturnType != result))
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return method;
    }

    private static FieldInfo Field(int index, Type owner, string name, Type exactType,
        Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != exactType)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return field;
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

    private static Func<object, T> FieldFunc<T>(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(T)), target).Compile();
    }

    private static Func<object, object?> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(object)), target).Compile();
    }

    private static Func<object, IList?> ListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(IList)), target).Compile();
    }

    private static Func<object, object, T> Func2<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        return Expression.Lambda<Func<object, object, T>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(argument, method.GetParameters()[0].ParameterType)), typeof(T)),
            target, argument).Compile();
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
