using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the active harvest element/action lists.</summary>
internal sealed class HarvestLifecycleNativeBindings
{
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
        Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("harvest lifecycle", ContractIds, resolveType, includeContract, set =>
        {
            var element = set.Type("harvest-lifecycle.element.type-action", "HarvestElementSO");
            var action = set.Type("harvest-lifecycle.action.type-action", "HarvestActionSO");
            var instance = set.Type("harvest-lifecycle.instance.type-action", "HarvestActionInstance");
            var elementList = set.Type("harvest-lifecycle.element-list.type-action", "HarvestElementListVariable");
            var actionList = set.Type("harvest-lifecycle.action-list.type-action", "HarvestActionInstanceListVariable");
            var cost = set.Type("harvest-lifecycle.cost.type-action", "ResourceCostList");
            var elementListBase = set.GenericBase(
                "harvest-lifecycle.element-list.type-action", elementList, "GenericListVariable`1");
            var actionListBase = set.GenericBase(
                "harvest-lifecycle.action-list.type-action", actionList, "GenericListVariable`1");

            return new HarvestLifecycleNativeBindings(
                element, action, instance, elementList, actionList,
                set.Call<Func<object, bool>>("harvest-lifecycle.element-visible-action", element, "IsVisible", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "harvest-lifecycle.element-available-action", element, "IsAvailable", typeof(bool)),
                set.Field<Func<object, object?>>("harvest-lifecycle.element-usage-cost-action", element, "usageCost", cost),
                set.Call<Func<object, BigDouble>>("harvest-lifecycle.element-maximum-instances-action",
                    element, "MaximumNumberInstances", typeof(BigDouble)),
                set.Call<Func<object, IList?>>("harvest-lifecycle.element-action-instances-action",
                    element, "GetActionInstances", typeof(List<>).MakeGenericType(instance)),
                set.Call<Func<object, object?>>("harvest-lifecycle.instance-action-action", instance, "GetAction", action),
                set.Call<Func<object, object?>>("harvest-lifecycle.instance-element-action", instance, "GetElement", element),
                set.Call<Func<object, bool>>("harvest-lifecycle.instance-visible-action", instance, "IsVisible", typeof(bool)),
                set.Call<Func<object, int>>(
                    "harvest-lifecycle.instance-maximum-action", instance, "GetMaximumInstances", typeof(int)),
                set.Field<Func<object, int>>("harvest-lifecycle.instance-count-action", instance, "instances", typeof(int)),
                set.Call<Func<object, object, int>>(
                    "harvest-lifecycle.element-list-stacks-action", elementList, "GetStacks", typeof(int), element),
                set.Call<Func<object, bool>>(
                    "harvest-lifecycle.list-has-empty-spot-action", elementListBase, "HasEmptySpot", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "harvest-lifecycle.list-has-empty-spot-action", actionListBase, "HasEmptySpot", typeof(bool)),
                set.Call<Action<object, object, BigDouble>>("harvest-lifecycle.element-list-add-action",
                    elementList, "AddInstance", typeof(void), element, typeof(BigDouble)),
                set.Call<Action<object, object, BigDouble>>("harvest-lifecycle.element-list-remove-action",
                    elementList, "RemoveInstance", typeof(void), element, typeof(BigDouble)),
                set.Call<Func<object, object, object?>>(
                    "harvest-lifecycle.action-list-find-action", actionList, "FindInstance", instance, instance),
                set.Call<Action<object, object, int>>("harvest-lifecycle.action-list-add-action",
                    actionList, "AddInstance", typeof(void), instance, typeof(int)),
                set.Call<Action<object, object, int>>("harvest-lifecycle.action-list-remove-action",
                    actionList, "RemoveInstance", typeof(void), instance, typeof(int)),
                set.Call<Func<object, bool>>("harvest-lifecycle.cost-has-enough-action", cost, "HasEnough", typeof(bool)));
        }, out bindings, out reason);
}
