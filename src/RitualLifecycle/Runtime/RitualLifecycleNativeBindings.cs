using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the visible Ritual selection and run controls.</summary>
internal sealed class RitualLifecycleNativeBindings : INativeCostRows
{
    internal static readonly string[] ContractIds =
    {
        "ritual-lifecycle.ritual.type-action",
        "ritual-lifecycle.manager.type-action",
        "ritual-lifecycle.variable.type-action",
        "ritual-lifecycle.cost.type-action",
        "ritual-lifecycle.tuple.type-action",
        "ritual-lifecycle.resource.type-action",
        "ritual-lifecycle.battle-manager.type-action",
        "ritual-lifecycle.manager-instance-action",
        "ritual-lifecycle.manager-selected-action",
        "ritual-lifecycle.variable-toggle-action",
        "ritual-lifecycle.variable-is-item-action",
        "ritual-lifecycle.ritual-discovered-action",
        "ritual-lifecycle.ritual-force-level-action",
        "ritual-lifecycle.ritual-force-level-value-action",
        "ritual-lifecycle.ritual-selected-level-action",
        "ritual-lifecycle.ritual-max-selected-level-action",
        "ritual-lifecycle.ritual-change-level-action",
        "ritual-lifecycle.ritual-activation-cost-action",
        "ritual-lifecycle.cost-has-enough-action",
        "ritual-lifecycle.cost-perform-action",
        "ritual-lifecycle.cost-entries-action",
        "ritual-lifecycle.tuple-resource-action",
        "ritual-lifecycle.tuple-value-action",
        "ritual-lifecycle.resource-guid-action",
        "ritual-lifecycle.resource-has-amount-action",
        "ritual-lifecycle.manager-activate-action",
        "ritual-lifecycle.battle-manager-instance-action",
        "ritual-lifecycle.battle-active-action",
        "ritual-lifecycle.ritual-in-battle-action",
        "ritual-lifecycle.ritual-duration-kind-action",
        "ritual-lifecycle.ritual-duration-active-action",
        "ritual-lifecycle.ritual-cancel-action",
        "ritual-lifecycle.battle-active-ritual-action",
        "ritual-lifecycle.battle-end-ritual-action",
    };

    private RitualLifecycleNativeBindings(
        Type ritualType,
        Type managerType,
        Type battleManagerType,
        Func<object?> manager,
        Func<object, object?> selectedVariable,
        Action<object, object> toggleSelected,
        Func<object, object, bool> isSelected,
        Func<object, bool> isDiscovered,
        Func<object, bool> forceLevel,
        Func<object, int> forceLevelValue,
        Func<object, int> selectedLevel,
        Func<object, int> maximumSelectedLevel,
        Action<object, int> changeStartingLevel,
        Func<object, object?> activationCost,
        Func<object, bool> hasEnough,
        Action<object> performCost,
        Func<object, IList?> costEntries,
        Func<object, object?> costResource,
        Func<object, BigDouble> costValue,
        Func<object, Guid> resourceGuid,
        Func<object, BigDouble, bool> hasResourceAmount,
        Action<object> activateSelected,
        Func<object?> battleManager,
        Func<object, bool> isInCombat,
        Func<object, bool> inBattle,
        Func<object, bool> isDurationRitual,
        Func<object, bool> isDurationActive,
        Action<object> cancel,
        Func<object, object?> activeRitual,
        Action<object> endRitual)
    {
        RitualType = ritualType;
        ManagerType = managerType;
        BattleManagerType = battleManagerType;
        Manager = manager;
        SelectedVariable = selectedVariable;
        ToggleSelected = toggleSelected;
        IsSelected = isSelected;
        IsDiscovered = isDiscovered;
        ForceLevel = forceLevel;
        ForceLevelValue = forceLevelValue;
        SelectedLevel = selectedLevel;
        MaximumSelectedLevel = maximumSelectedLevel;
        ChangeStartingLevel = changeStartingLevel;
        ActivationCost = activationCost;
        HasEnough = hasEnough;
        PerformCost = performCost;
        CostEntries = costEntries;
        CostResource = costResource;
        CostValue = costValue;
        ResourceGuid = resourceGuid;
        HasResourceAmount = hasResourceAmount;
        ActivateSelected = activateSelected;
        BattleManager = battleManager;
        IsInCombat = isInCombat;
        InBattle = inBattle;
        IsDurationRitual = isDurationRitual;
        IsDurationActive = isDurationActive;
        Cancel = cancel;
        ActiveRitual = activeRitual;
        EndRitual = endRitual;
    }

    internal Type RitualType { get; }
    internal Type ManagerType { get; }
    internal Type BattleManagerType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> SelectedVariable { get; }
    internal Action<object, object> ToggleSelected { get; }
    internal Func<object, object, bool> IsSelected { get; }
    internal Func<object, bool> IsDiscovered { get; }
    internal Func<object, bool> ForceLevel { get; }
    internal Func<object, int> ForceLevelValue { get; }
    internal Func<object, int> SelectedLevel { get; }
    internal Func<object, int> MaximumSelectedLevel { get; }
    internal Action<object, int> ChangeStartingLevel { get; }
    internal Func<object, object?> ActivationCost { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Action<object> PerformCost { get; }
    public Func<object, IList?> CostEntries { get; }
    public Func<object, object?> CostResource { get; }
    public Func<object, BigDouble> CostValue { get; }
    public Func<object, Guid> ResourceGuid { get; }
    public Func<object, BigDouble, bool> HasResourceAmount { get; }
    internal Action<object> ActivateSelected { get; }
    internal Func<object?> BattleManager { get; }
    internal Func<object, bool> IsInCombat { get; }
    internal Func<object, bool> InBattle { get; }
    internal Func<object, bool> IsDurationRitual { get; }
    internal Func<object, bool> IsDurationActive { get; }
    internal Action<object> Cancel { get; }
    internal Func<object, object?> ActiveRitual { get; }
    internal Action<object> EndRitual { get; }

    internal static bool TryCreate(
        out RitualLifecycleNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("ritual lifecycle", ContractIds, resolveType, includeContract, set =>
        {
            var ritual = set.Type("ritual-lifecycle.ritual.type-action", "RitualSO");
            var managerType = set.Type("ritual-lifecycle.manager.type-action", "RitualManager");
            var variable = set.Type("ritual-lifecycle.variable.type-action", "RitualVariable");
            var cost = set.Type("ritual-lifecycle.cost.type-action", "ResourceCostList");
            var tuple = set.Type("ritual-lifecycle.tuple.type-action", "ResourceTuple");
            var resource = set.Type("ritual-lifecycle.resource.type-action", "ResourceSO");
            var battleType = set.Type("ritual-lifecycle.battle-manager.type-action", "BattleManager");

            return new RitualLifecycleNativeBindings(
                ritual,
                managerType,
                battleType,
                set.StaticField<Func<object?>>("ritual-lifecycle.manager-instance-action", managerType, "instance", managerType),
                set.Field<Func<object, object?>>(
                    "ritual-lifecycle.manager-selected-action", managerType, "selectedRitual", variable),
                set.Call<Action<object, object>>(
                    "ritual-lifecycle.variable-toggle-action", variable, "ToggleValue", typeof(void), ritual),
                set.Call<Func<object, object, bool>>(
                    "ritual-lifecycle.variable-is-item-action", variable, "IsItem", typeof(bool), ritual),
                set.Call<Func<object, bool>>("ritual-lifecycle.ritual-discovered-action", ritual, "IsDiscovered", typeof(bool)),
                set.Field<Func<object, bool>>("ritual-lifecycle.ritual-force-level-action", ritual, "forceLevel", typeof(bool)),
                set.Field<Func<object, int>>(
                    "ritual-lifecycle.ritual-force-level-value-action", ritual, "forceLevelValue", typeof(int)),
                set.Field<Func<object, int>>(
                    "ritual-lifecycle.ritual-selected-level-action", ritual, "selectedLevel", typeof(int)),
                set.Call<Func<object, int>>(
                    "ritual-lifecycle.ritual-max-selected-level-action", ritual, "GetMaxSelectedLevel", typeof(int)),
                set.Call<Action<object, int>>("ritual-lifecycle.ritual-change-level-action",
                    ritual, "ChangeStartingLevel", typeof(void), typeof(int)),
                set.Call<Func<object, object?>>(
                    "ritual-lifecycle.ritual-activation-cost-action", ritual, "GetActivationCost", cost),
                set.Call<Func<object, bool>>("ritual-lifecycle.cost-has-enough-action", cost, "HasEnough", typeof(bool)),
                set.Call<Action<object>>("ritual-lifecycle.cost-perform-action", cost, "PerformCost", typeof(void)),
                set.Call<Func<object, IList?>>(
                    "ritual-lifecycle.cost-entries-action", cost, "GetEntries", typeof(List<>).MakeGenericType(tuple)),
                set.Field<Func<object, object?>>("ritual-lifecycle.tuple-resource-action", tuple, "resource", resource),
                set.Call<Func<object, BigDouble>>("ritual-lifecycle.tuple-value-action", tuple, "GetValue", typeof(BigDouble)),
                set.StableUuid("ritual-lifecycle.resource-guid-action", resource),
                set.Call<Func<object, BigDouble, bool>>(
                    "ritual-lifecycle.resource-has-amount-action", resource, "HasAmount", typeof(bool), typeof(BigDouble)),
                set.Call<Action<object>>(
                    "ritual-lifecycle.manager-activate-action", managerType, "ActivateSelectedRitual", typeof(void)),
                set.StaticField<Func<object?>>(
                    "ritual-lifecycle.battle-manager-instance-action", battleType, "instance", battleType),
                set.Call<Func<object, bool>>("ritual-lifecycle.battle-active-action", battleType, "IsInCombat", typeof(bool)),
                set.Field<Func<object, bool>>("ritual-lifecycle.ritual-in-battle-action", ritual, "inBattle", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "ritual-lifecycle.ritual-duration-kind-action", ritual, "IsDurationRitual", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "ritual-lifecycle.ritual-duration-active-action", ritual, "IsDurationActive", typeof(bool)),
                set.Call<Action<object>>("ritual-lifecycle.ritual-cancel-action", ritual, "Cancel", typeof(void)),
                set.Field<Func<object, object?>>(
                    "ritual-lifecycle.battle-active-ritual-action", battleType, "activeRitual", variable),
                set.Call<Action<object>>("ritual-lifecycle.battle-end-ritual-action", battleType, "EndRitual", typeof(void)));
        }, out bindings, out reason);
}
