using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete binding set for Auto Harvest: the registry types its identities resolve to, the plot's
/// offered instances and click-time gates, the active-action queue it reads and adds into, and the
/// action's prerequisite check.
/// </summary>
internal sealed class AutoHarvestNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "plot-lifecycle.plot.type-action",
        "plot-lifecycle.action.type-action",
        "plot-lifecycle.instance.type-action",
        "plot-lifecycle.list.type-action",
        "auto-harvest.scaling-weight.type-action",
        "auto-harvest.reward-pool.type-action",
        "spell-level.prerequisites.type-action",
        "id-scriptable-object.get-guid-action",
        "auto-harvest.plot.is-visible",
        "auto-harvest.plot.get-action-instances",
        "plot-lifecycle.instance-plot-action",
        "plot-lifecycle.instance-action-action",
        "auto-harvest.instance.is-visible",
        "auto-harvest.instance.has-enough-for-one",
        "auto-harvest.instance.maximum-remaining",
        "auto-harvest.instance.is-empty-action",
        "auto-harvest.instance.is-engaged-action",
        "plot-lifecycle.instance-quantity-action",
        "abstract-list.value",
        "auto-harvest.active-list.used-spots-action",
        "plot-lifecycle.list-room-action",
        "auto-harvest.active-list.add",
        "auto-harvest.action.prerequisites-action",
        "prerequisites.check",
    };

    private AutoHarvestNativeBindings(
        Type plotType,
        Type actionType,
        Type instanceType,
        Type activeActionsType,
        Type scalingWeightType,
        Type rewardPoolType,
        Func<object, Guid> plotUuid,
        Func<object, Guid> actionUuid,
        Func<object, bool> plotIsVisible,
        Func<object, IList?> plotActionInstances,
        Func<object, object?> instancePlot,
        Func<object, object?> instanceAction,
        Func<object, bool> instanceIsVisible,
        Func<object, bool> instanceHasEnoughForOne,
        Func<object, int> instanceMaximumRemaining,
        Func<object, bool> instanceIsEmpty,
        Func<object, bool> instanceIsEngaged,
        Func<object, int> instanceQuantity,
        Func<object, IList?> activeValues,
        Func<object, int> activeUsedSpots,
        Func<object, bool> activeHasEmptySpot,
        Action<object, object, int> activeAddInstance,
        Func<object, object?> actionPrerequisites,
        Func<object, bool> prerequisitesCheck)
    {
        PlotType = plotType;
        ActionType = actionType;
        InstanceType = instanceType;
        ActiveActionsType = activeActionsType;
        ScalingWeightType = scalingWeightType;
        RewardPoolType = rewardPoolType;
        PlotUuid = plotUuid;
        ActionUuid = actionUuid;
        PlotIsVisible = plotIsVisible;
        PlotActionInstances = plotActionInstances;
        InstancePlot = instancePlot;
        InstanceAction = instanceAction;
        InstanceIsVisible = instanceIsVisible;
        InstanceHasEnoughForOne = instanceHasEnoughForOne;
        InstanceMaximumRemaining = instanceMaximumRemaining;
        InstanceIsEmpty = instanceIsEmpty;
        InstanceIsEngaged = instanceIsEngaged;
        InstanceQuantity = instanceQuantity;
        ActiveValues = activeValues;
        ActiveUsedSpots = activeUsedSpots;
        ActiveHasEmptySpot = activeHasEmptySpot;
        ActiveAddInstance = activeAddInstance;
        ActionPrerequisites = actionPrerequisites;
        PrerequisitesCheck = prerequisitesCheck;
    }

    internal Type PlotType { get; }
    internal Type ActionType { get; }
    internal Type InstanceType { get; }
    internal Type ActiveActionsType { get; }
    internal Type ScalingWeightType { get; }
    internal Type RewardPoolType { get; }
    internal Func<object, Guid> PlotUuid { get; }
    internal Func<object, Guid> ActionUuid { get; }
    internal Func<object, bool> PlotIsVisible { get; }
    internal Func<object, IList?> PlotActionInstances { get; }
    internal Func<object, object?> InstancePlot { get; }
    internal Func<object, object?> InstanceAction { get; }
    internal Func<object, bool> InstanceIsVisible { get; }
    internal Func<object, bool> InstanceHasEnoughForOne { get; }
    internal Func<object, int> InstanceMaximumRemaining { get; }
    internal Func<object, bool> InstanceIsEmpty { get; }
    internal Func<object, bool> InstanceIsEngaged { get; }
    internal Func<object, int> InstanceQuantity { get; }
    internal Func<object, IList?> ActiveValues { get; }
    internal Func<object, int> ActiveUsedSpots { get; }
    internal Func<object, bool> ActiveHasEmptySpot { get; }
    internal Action<object, object, int> ActiveAddInstance { get; }
    internal Func<object, object?> ActionPrerequisites { get; }
    internal Func<object, bool> PrerequisitesCheck { get; }

    internal static bool TryCreate(out AutoHarvestNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("auto-harvest", ContractIds, resolveType, includeContract, set =>
        {
            var plot = set.Type("plot-lifecycle.plot.type-action", "PlotNodeSO");
            var action = set.Type("plot-lifecycle.action.type-action", "PlotNodeActionSO");
            var instance = set.Type("plot-lifecycle.instance.type-action", "PlotNodeActionInstance");
            var list = set.Type("plot-lifecycle.list.type-action", "PlotNodeActionInstanceListVariable");
            var scalingWeight = set.Type("auto-harvest.scaling-weight.type-action", "ScalingWeightSO");
            var rewardPool = set.Type("auto-harvest.reward-pool.type-action", "TreasurePoolSO");
            var prerequisites = set.Type("spell-level.prerequisites.type-action", "Prerequisites+Container");
            var listBase = set.GenericBase("plot-lifecycle.list-room-action", list, "EmptyTypeListVariable`1");
            var instances = typeof(List<>).MakeGenericType(instance);

            return new AutoHarvestNativeBindings(
                plot,
                action,
                instance,
                list,
                scalingWeight,
                rewardPool,
                set.StableUuid("id-scriptable-object.get-guid-action", plot),
                set.StableUuid("id-scriptable-object.get-guid-action", action),
                set.Call<Func<object, bool>>("auto-harvest.plot.is-visible", plot, "IsVisible", typeof(bool)),
                set.Call<Func<object, IList?>>(
                    "auto-harvest.plot.get-action-instances", plot, "GetActionInstances", instances),
                set.Call<Func<object, object?>>("plot-lifecycle.instance-plot-action", instance, "GetElement", plot),
                set.Call<Func<object, object?>>(
                    "plot-lifecycle.instance-action-action", instance, "GetAction", action),
                set.Call<Func<object, bool>>("auto-harvest.instance.is-visible", instance, "IsVisible", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "auto-harvest.instance.has-enough-for-one", instance, "HasEnoughForOneInstance", typeof(bool)),
                set.Call<Func<object, int>>(
                    "auto-harvest.instance.maximum-remaining", instance, "GetMaximumRemInstances", typeof(int)),
                set.Call<Func<object, bool>>("auto-harvest.instance.is-empty-action", instance, "IsEmpty", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "auto-harvest.instance.is-engaged-action", instance, "IsEngaged", typeof(bool)),
                set.Call<Func<object, int>>(
                    "plot-lifecycle.instance-quantity-action", instance, "GetActualQuantity", typeof(int)),
                set.Field<Func<object, IList?>>("abstract-list.value", list, "value", instances),
                set.Call<Func<object, int>>(
                    "auto-harvest.active-list.used-spots-action", listBase, "GetUsedSpots", typeof(int)),
                set.Call<Func<object, bool>>("plot-lifecycle.list-room-action", listBase, "HasEmptySpot", typeof(bool)),
                set.Call<Action<object, object, int>>(
                    "auto-harvest.active-list.add", list, "AddInstance", typeof(void), instance, typeof(int)),
                set.Field<Func<object, object?>>(
                    "auto-harvest.action.prerequisites-action", action, "prerequisites", prerequisites),
                set.Call<Func<object, bool>>("prerequisites.check", prerequisites, "Check", typeof(bool)));
        }, out bindings, out reason);
}
