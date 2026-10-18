using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The complete reflected consumable-use transaction schema. Only compiled accessors are retained,
/// and one instance is validated for each game lifecycle before any item is resolved or mutated.
/// </summary>
internal sealed class AutoItemsNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "consumable.type",
        "consumable-type.type",
        "generic-level.resource.type-action",
        "inventory.type",
        "targeting.manager.type-action",
        "consumable-usage.type",
        "auto-items.consumable-count.type",
        "auto-items.scaling-info.type",
        "auto-items.instant-effect-block.type",
        "auto-items.instant-effect-script.type",
        "auto-items.request-target-effect-script.type",
        "auto-items.target-select-options.type",
        "auto-items.base-target-selection.type",
        "auto-items.target-structure.type",
        "auto-items.targetable.type",
        "generic-level.cost.type-action",
        "generic-level.tuple.type-action",
        "consumable.consumable-types",
        "consumable.all",
        "consumable.consumable-usages",
        "consumable.can-be-randomized",
        "consumable.has-duration",
        "consumable.duration-base",
        "consumable.consume-cost",
        "consumable.usage-cost",
        "resource-cost-list.costs",
        "resource-tuple.resource",
        "resource-tuple.value-big",
        "auto-items.consumable.on-use-effects",
        "auto-harvest.effect-scripts",
        "target-request.options",
        "id-scriptable-object.get-guid-action",
        "consumable.can-fire",
        "consumable.is-visible",
        "consumable.select-and-fire",
        "consumable.set-randomization",
        "consumable.is-randomized",
        "consumable.get-queued",
        "inventory.can-use-consumable",
        "targeting-manager.is-targeting",
        "auto-items.consumable.strongest",
        "auto-items.consumable.strongest-level",
        "auto-items.consumable.count-scaling",
        "auto-items.target-options.get-targeting",
        "auto-items.target-structure.get-random-list",
    };

    private AutoItemsNativeBindings(
        Type consumableType,
        Type familyType,
        Type resourceType,
        Type costEntryType,
        Type countType,
        Type scalingType,
        Type instantBlockType,
        Type instantScriptType,
        Type requestType,
        Type optionsType,
        Type baseSelectionType,
        Type targetStructureType,
        Func<object, IList?> families,
        Func<IEnumerable?> allConsumables,
        Func<object, ICollection?> usages,
        Func<object, bool> canBeRandomized,
        Func<object, bool> hasDuration,
        Func<object, double> durationBase,
        Func<object, object?> consumeCost,
        Func<object, object?> usageCost,
        Func<object, IEnumerable?> costs,
        Func<object, object?> costResource,
        Func<object, BigDouble> costAmount,
        Func<object, IEnumerable?> onUseEffects,
        Func<object, IEnumerable?> effectScripts,
        Func<object, object?> targetOptions,
        Func<object, Guid> familyGuid,
        Func<object, Guid> resourceGuid,
        Func<object, bool> canFire,
        Func<object, bool> isVisible,
        Action<object> selectAndFire,
        Action<object, bool> setRandomization,
        Func<object, bool> isRandomized,
        Func<object, int> getQueued,
        Func<bool> canUseConsumable,
        Func<bool> isTargeting,
        Func<object, object?> strongest,
        Func<object, int> strongestLevel,
        Func<object, object, object?> countScaling,
        Func<object, object?> getTargeting,
        Func<object, object, ICollection?> getRandomList)
    {
        ConsumableType = consumableType;
        FamilyType = familyType;
        ResourceType = resourceType;
        CostEntryType = costEntryType;
        CountType = countType;
        ScalingType = scalingType;
        InstantBlockType = instantBlockType;
        InstantScriptType = instantScriptType;
        RequestType = requestType;
        OptionsType = optionsType;
        BaseSelectionType = baseSelectionType;
        TargetStructureType = targetStructureType;
        Families = families;
        AllConsumables = allConsumables;
        Usages = usages;
        CanBeRandomized = canBeRandomized;
        HasDuration = hasDuration;
        DurationBase = durationBase;
        ConsumeCost = consumeCost;
        UsageCost = usageCost;
        Costs = costs;
        CostResource = costResource;
        CostAmount = costAmount;
        OnUseEffects = onUseEffects;
        EffectScripts = effectScripts;
        TargetOptions = targetOptions;
        FamilyGuid = familyGuid;
        ResourceGuid = resourceGuid;
        CanFire = canFire;
        IsVisible = isVisible;
        SelectAndFire = selectAndFire;
        SetRandomization = setRandomization;
        IsRandomized = isRandomized;
        GetQueued = getQueued;
        CanUseConsumable = canUseConsumable;
        IsTargeting = isTargeting;
        Strongest = strongest;
        StrongestLevel = strongestLevel;
        CountScaling = countScaling;
        GetTargeting = getTargeting;
        GetRandomList = getRandomList;
    }

    internal Type ConsumableType { get; }
    internal Type FamilyType { get; }
    internal Type ResourceType { get; }
    internal Type CostEntryType { get; }
    internal Type CountType { get; }
    internal Type ScalingType { get; }
    internal Type InstantBlockType { get; }
    internal Type InstantScriptType { get; }
    internal Type RequestType { get; }
    internal Type OptionsType { get; }
    internal Type BaseSelectionType { get; }
    internal Type TargetStructureType { get; }
    internal Func<object, IList?> Families { get; }
    internal Func<IEnumerable?> AllConsumables { get; }
    internal Func<object, ICollection?> Usages { get; }
    internal Func<object, bool> CanBeRandomized { get; }
    internal Func<object, bool> HasDuration { get; }
    internal Func<object, double> DurationBase { get; }
    internal Func<object, object?> ConsumeCost { get; }
    internal Func<object, object?> UsageCost { get; }
    internal Func<object, IEnumerable?> Costs { get; }
    internal Func<object, object?> CostResource { get; }
    internal Func<object, BigDouble> CostAmount { get; }
    internal Func<object, IEnumerable?> OnUseEffects { get; }
    internal Func<object, IEnumerable?> EffectScripts { get; }
    internal Func<object, object?> TargetOptions { get; }
    internal Func<object, Guid> FamilyGuid { get; }
    internal Func<object, Guid> ResourceGuid { get; }
    internal Func<object, bool> CanFire { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Action<object> SelectAndFire { get; }
    internal Action<object, bool> SetRandomization { get; }
    internal Func<object, bool> IsRandomized { get; }
    internal Func<object, int> GetQueued { get; }
    internal Func<bool> CanUseConsumable { get; }
    internal Func<bool> IsTargeting { get; }
    internal Func<object, object?> Strongest { get; }
    internal Func<object, int> StrongestLevel { get; }
    internal Func<object, object, object?> CountScaling { get; }
    internal Func<object, object?> GetTargeting { get; }
    internal Func<object, object, ICollection?> GetRandomList { get; }

    internal static bool TryCreate(out AutoItemsNativeBindings? bindings, out string reason) =>
        NativeBindingSet.TryBind("Auto Items", ContractIds, null, null, set =>
        {
            var consumable = set.Type("consumable.type", "ConsumableSO");
            var family = set.Type("consumable-type.type", "ConsumableTypeSO");
            var resource = set.Type("generic-level.resource.type-action", "ResourceSO");
            var inventory = set.Type("inventory.type", "Inventory");
            var targetingManager = set.Type("targeting.manager.type-action", "TargetingManager");
            var usage = set.Type("consumable-usage.type", "ConsumableUsage");
            var count = set.Type("auto-items.consumable-count.type", "ConsumableCount");
            var scaling = set.Type("auto-items.scaling-info.type", "ScalingInfo");
            var block = set.Type("auto-items.instant-effect-block.type", "InstantEffectBlock");
            var script = set.Type("auto-items.instant-effect-script.type", "IInstantEffectScript");
            var request = set.Type("auto-items.request-target-effect-script.type", "RequestTargetEffectScript");
            var options = set.Type("auto-items.target-select-options.type", "Targeting.TargetSelectOptions");
            var selection = set.Type("auto-items.base-target-selection.type", "Targeting.BaseTargetSelection");
            var structure = set.Type("auto-items.target-structure.type", "Targeting.TargetStructure");
            var targetable = set.Type("auto-items.targetable.type", "Targeting.ITargetable");
            var costList = set.Type("generic-level.cost.type-action", "ResourceCostList");
            var costEntry = set.Type("generic-level.tuple.type-action", "ResourceTuple");
            if (!script.IsAssignableFrom(request))
                throw new NativeBindingException(
                    "auto-items.request-target-effect-script.type: RequestTargetEffectScript is not an IInstantEffectScript.");

            return new AutoItemsNativeBindings(
                consumable,
                family,
                resource,
                costEntry,
                count,
                scaling,
                block,
                script,
                request,
                options,
                selection,
                structure,
                set.Field<Func<object, IList?>>(
                    "consumable.consumable-types", consumable, "consumableTypes", ListOf(family)),
                set.StaticField<Func<IEnumerable?>>("consumable.all", consumable, "All", ListOf(consumable)),
                set.Field<Func<object, ICollection?>>(
                    "consumable.consumable-usages", consumable, "consumableUsages", ListOf(usage)),
                set.Field<Func<object, bool>>(
                    "consumable.can-be-randomized", consumable, "canBeRandomized", typeof(bool)),
                set.Field<Func<object, bool>>("consumable.has-duration", consumable, "hasDuration", typeof(bool)),
                set.Field<Func<object, double>>(
                    "consumable.duration-base", consumable, "durationBase", typeof(double)),
                set.Field<Func<object, object?>>("consumable.consume-cost", consumable, "consumeCost", costList),
                set.Field<Func<object, object?>>("consumable.usage-cost", consumable, "usageCost", costList),
                set.Field<Func<object, IEnumerable?>>(
                    "resource-cost-list.costs", costList, "costs", ListOf(costEntry)),
                set.Field<Func<object, object?>>("resource-tuple.resource", costEntry, "resource", resource),
                set.Field<Func<object, BigDouble>>(
                    "resource-tuple.value-big", costEntry, "valueBig", typeof(BigDouble)),
                set.Field<Func<object, IEnumerable?>>(
                    "auto-items.consumable.on-use-effects", consumable, "onUseEffects", ListOf(block)),
                set.Field<Func<object, IEnumerable?>>(
                    "auto-harvest.effect-scripts", block, "effectScripts", ListOf(script)),
                set.Field<Func<object, object?>>("target-request.options", request, "targetOptions", options),
                set.StableUuid("id-scriptable-object.get-guid-action", family),
                set.StableUuid("id-scriptable-object.get-guid-action", resource),
                set.Call<Func<object, bool>>("consumable.can-fire", consumable, "CanFire", typeof(bool)),
                set.Call<Func<object, bool>>("consumable.is-visible", consumable, "IsVisible", typeof(bool)),
                set.Call<Action<object>>(
                    "consumable.select-and-fire", consumable, "SelectAndFire", typeof(void)),
                set.Call<Action<object, bool>>(
                    "consumable.set-randomization", consumable, "SetRandomization", typeof(void), typeof(bool)),
                set.Call<Func<object, bool>>(
                    "consumable.is-randomized", consumable, "IsRandomized", typeof(bool)),
                set.Call<Func<object, int>>("consumable.get-queued", consumable, "GetQueued", typeof(int)),
                set.StaticCall<Func<bool>>(
                    "inventory.can-use-consumable", inventory, "CanUseConsumable", typeof(bool)),
                set.StaticCall<Func<bool>>(
                    "targeting-manager.is-targeting", targetingManager, "IsTargeting", typeof(bool)),
                set.Call<Func<object, object?>>(
                    "auto-items.consumable.strongest", consumable, "GetStrongest", count),
                set.Call<Func<object, int>>(
                    "auto-items.consumable.strongest-level", consumable, "GetStrongestLevel", typeof(int)),
                set.Call<Func<object, object, object?>>(
                    "auto-items.consumable.count-scaling", consumable, "GetCountScalingInfo", scaling, count),
                set.Call<Func<object, object?>>(
                    "auto-items.target-options.get-targeting", options, "GetTargeting", selection),
                set.Call<Func<object, object, ICollection?>>(
                    "auto-items.target-structure.get-random-list",
                    structure,
                    "GetRandomList",
                    ListOf(targetable),
                    scaling));
        }, out bindings, out reason);

    private static Type ListOf(Type element) => typeof(List<>).MakeGenericType(element);
}
