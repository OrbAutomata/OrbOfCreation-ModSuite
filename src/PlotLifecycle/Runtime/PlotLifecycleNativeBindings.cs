using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the player-visible plot action list.</summary>
internal sealed class PlotLifecycleNativeBindings
{
    internal static readonly Guid ActiveActionsId =
        new("70871e86-100b-4ae0-ba9b-fc96e09b7e1f");

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
        Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("plot lifecycle", ContractIds, resolveType, includeContract, set =>
        {
            var plot = set.Type("plot-lifecycle.plot.type-action", "PlotNodeSO");
            var action = set.Type("plot-lifecycle.action.type-action", "PlotNodeActionSO");
            var instance = set.Type("plot-lifecycle.instance.type-action", "PlotNodeActionInstance");
            var list = set.Type("plot-lifecycle.list.type-action", "PlotNodeActionInstanceListVariable");
            var listBase = set.GenericBase("plot-lifecycle.list-room-action", list, "EmptyTypeListVariable`1");

            return new PlotLifecycleNativeBindings(
                plot, action, instance, list,
                set.Call<Func<object, bool>>("plot-lifecycle.plot-visible-action", plot, "IsVisible", typeof(bool)),
                set.Call<Func<object, IList?>>("plot-lifecycle.plot-instances-action", plot, "GetActionInstances",
                    typeof(System.Collections.Generic.List<>).MakeGenericType(instance)),
                set.Call<Func<object, object?>>("plot-lifecycle.instance-action-action", instance, "GetAction", action),
                set.Call<Func<object, object?>>("plot-lifecycle.instance-plot-action", instance, "GetElement", plot),
                set.Call<Func<object, bool>>(
                    "plot-lifecycle.instance-visible-action", instance, "IsVisible", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "plot-lifecycle.instance-affordable-action", instance, "HasEnoughForOneInstance", typeof(bool)),
                set.Call<Func<object, int>>("plot-lifecycle.instance-maximum-remaining-action",
                    instance, "GetMaximumRemInstances", typeof(int)),
                set.Call<Func<object, int>>(
                    "plot-lifecycle.instance-maximum-action", instance, "GetMaximumInstances", typeof(int)),
                set.Call<Func<object, int>>(
                    "plot-lifecycle.instance-quantity-action", instance, "GetActualQuantity", typeof(int)),
                set.Call<Func<object, bool>>(
                    "plot-lifecycle.instance-at-minimum-action", instance, "IsAtMinimumQuantity", typeof(bool)),
                set.Call<Action<object>>("plot-lifecycle.instance-cancel-action", instance, "Cancel", typeof(void)),
                set.Call<Func<object, object, object?>>(
                    "plot-lifecycle.list-find-action", list, "FindInstance", instance, instance),
                set.Call<Func<object, bool>>("plot-lifecycle.list-room-action", listBase, "HasEmptySpot", typeof(bool)),
                set.Call<Action<object, object, int>>(
                    "plot-lifecycle.list-add-action", list, "AddInstance", typeof(void), instance, typeof(int)),
                set.Call<Action<object, object, int>>("plot-lifecycle.list-remove-action",
                    list, "RemoveInstance", typeof(void), instance, typeof(int)));
        }, out bindings, out reason);
}
