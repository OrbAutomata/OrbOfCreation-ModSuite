using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding matrix for the four level-list controls owned here.</summary>
internal sealed class GenericLevelNativeBindings : INativeCostRows
{
    internal static readonly string[] ContractIds =
    {
        "generic-level.levelable.type-action",
        "generic-level.free-levelable.type-action",
        "generic-level.equipment-type.type-action",
        "generic-level.glyph.type-action",
        "generic-level.resource-type.type-action",
        "generic-level.time-rune.type-action",
        "generic-level.research.type-delegated",
        "generic-level.spell-recipe.type-delegated",
        "generic-level.cost.type-action",
        "generic-level.tuple.type-action",
        "generic-level.resource.type-action",
        "generic-level.equipment-get-level-action",
        "generic-level.equipment-can-level-action",
        "generic-level.equipment-get-cost-action",
        "generic-level.equipment-purchase-action",
        "generic-level.glyph-get-level-action",
        "generic-level.glyph-can-level-action",
        "generic-level.glyph-get-cost-action",
        "generic-level.glyph-purchase-action",
        "generic-level.resource-type-get-level-action",
        "generic-level.resource-type-can-level-action",
        "generic-level.resource-type-get-cost-action",
        "generic-level.resource-type-purchase-action",
        "generic-level.time-rune-get-level-action",
        "generic-level.time-rune-can-level-action",
        "generic-level.time-rune-get-cost-action",
        "generic-level.time-rune-purchase-action",
        "generic-level.equipment-get-bonus-level-action",
        "generic-level.equipment-get-bonus-cost-action",
        "generic-level.equipment-purchase-bonus-action",
        "generic-level.glyph-get-bonus-level-action",
        "generic-level.glyph-get-bonus-cost-action",
        "generic-level.glyph-purchase-bonus-action",
        "generic-level.resource-type-get-bonus-level-action",
        "generic-level.resource-type-get-bonus-cost-action",
        "generic-level.resource-type-purchase-bonus-action",
        "generic-level.cost-has-enough-action",
        "generic-level.cost-resources-visible-action",
        "generic-level.cost-entries-action",
        "generic-level.tuple-resource-action",
        "generic-level.tuple-value-action",
        "generic-level.resource-guid-action",
        "generic-level.resource-has-amount-action",
        "generic-level.equipment-visible-action",
        "generic-level.equipment-available-action",
        "generic-level.glyph-visible-action",
        "generic-level.glyph-available-action",
        "generic-level.resource-type-visible-action",
        "generic-level.resource-type-available-action",
        "generic-level.resource-type-hidden-action",
        "generic-level.time-rune-visible-action",
        "generic-level.time-rune-available-action",
        "generic-level.time-rune-discovered-action",
    };

    private readonly Dictionary<string, GenericLevelTargetBinding> _targets;

    private GenericLevelNativeBindings(
        Dictionary<string, GenericLevelTargetBinding> targets,
        Func<object, bool> hasEnough,
        Func<object, bool> resourcesVisible,
        Func<object, IList?> costEntries,
        Func<object, object?> costResource,
        Func<object, BigDouble> costValue,
        Func<object, Guid> resourceGuid,
        Func<object, BigDouble, bool> hasResourceAmount)
    {
        _targets = targets;
        HasEnough = hasEnough;
        ResourcesVisible = resourcesVisible;
        CostEntries = costEntries;
        CostResource = costResource;
        CostValue = costValue;
        ResourceGuid = resourceGuid;
        HasResourceAmount = hasResourceAmount;
    }

    internal Func<object, bool> HasEnough { get; }
    internal Func<object, bool> ResourcesVisible { get; }
    public Func<object, IList?> CostEntries { get; }
    public Func<object, object?> CostResource { get; }
    public Func<object, BigDouble> CostValue { get; }
    public Func<object, Guid> ResourceGuid { get; }
    public Func<object, BigDouble, bool> HasResourceAmount { get; }

    internal bool TryTarget(string nativeType, out GenericLevelTargetBinding binding) =>
        _targets.TryGetValue(nativeType, out binding!);

    internal static bool TryCreate(
        out GenericLevelNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("generic level", ContractIds, resolveType, includeContract, set =>
        {
            var levelable = set.Type("generic-level.levelable.type-action", "ILevelable");
            var freeLevelable = set.Type("generic-level.free-levelable.type-action", "ILevelableHasFree");
            var equipment = set.Type("generic-level.equipment-type.type-action", "EquipmentTypeSO");
            var glyph = set.Type("generic-level.glyph.type-action", "GlyphSO");
            var resourceType = set.Type("generic-level.resource-type.type-action", "ResourceTypeSO");
            var timeRune = set.Type("generic-level.time-rune.type-action", "TimeRuneSO");
            var research = set.Type("generic-level.research.type-delegated", "ResearchSO");
            var spellRecipe = set.Type("generic-level.spell-recipe.type-delegated", "SpellRecipeSO");
            var cost = set.Type("generic-level.cost.type-action", "ResourceCostList");
            var tuple = set.Type("generic-level.tuple.type-action", "ResourceTuple");
            var resource = set.Type("generic-level.resource.type-action", "ResourceSO");

            AssertImplementationRoster(levelable,
                equipment, glyph, research, resourceType, spellRecipe, timeRune);
            AssertImplementationRoster(freeLevelable, equipment, glyph, resourceType);

            var targets = new Dictionary<string, GenericLevelTargetBinding>(StringComparer.Ordinal)
            {
                [equipment.Name] = BindTarget(set, "generic-level.equipment", equipment, cost, supportsBonus: true),
                [glyph.Name] = BindTarget(set, "generic-level.glyph", glyph, cost, supportsBonus: true),
                [resourceType.Name] = BindTarget(set, "generic-level.resource-type", resourceType, cost,
                    supportsBonus: true, hidden: true),
                [timeRune.Name] = BindTarget(set, "generic-level.time-rune", timeRune, cost,
                    supportsBonus: false, discovered: true),
            };

            return new GenericLevelNativeBindings(
                targets,
                set.Call<Func<object, bool>>("generic-level.cost-has-enough-action", cost, "HasEnough", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "generic-level.cost-resources-visible-action", cost, "AllResourcesVisible", typeof(bool)),
                set.Call<Func<object, IList?>>(
                    "generic-level.cost-entries-action", cost, "GetEntries", typeof(List<>).MakeGenericType(tuple)),
                set.Field<Func<object, object?>>("generic-level.tuple-resource-action", tuple, "resource", resource),
                set.Call<Func<object, BigDouble>>("generic-level.tuple-value-action", tuple, "GetValue", typeof(BigDouble)),
                set.StableUuid("generic-level.resource-guid-action", resource),
                set.Call<Func<object, BigDouble, bool>>(
                    "generic-level.resource-has-amount-action", resource, "HasAmount", typeof(bool), typeof(BigDouble)));
        }, out bindings, out reason);

    private static GenericLevelTargetBinding BindTarget(
        NativeBindingSet set,
        string ids,
        Type type,
        Type cost,
        bool supportsBonus,
        bool discovered = false,
        bool hidden = false) =>
        new(type,
            set.Call<Func<object, int>>(ids + "-get-level-action", type, "GetLevel", typeof(int)),
            set.Call<Func<object, bool>>(ids + "-can-level-action", type, "CanLevel", typeof(bool)),
            set.Call<Func<object, object?>>(ids + "-get-cost-action", type, "GetLevelCost", cost),
            set.Call<Action<object>>(ids + "-purchase-action", type, "PurchaseLevel", typeof(void)),
            set.Call<Func<object, bool>>(ids + "-visible-action", type, "IsVisible", typeof(bool)),
            set.Call<Func<object, bool>>(ids + "-available-action", type, "IsAvailable", typeof(bool)),
            discovered ? set.Call<Func<object, bool>>(ids + "-discovered-action", type, "IsDiscovered", typeof(bool)) : null,
            hidden ? set.Field<Func<object, bool>>(ids + "-hidden-action", type, "specialHidden", typeof(bool)) : null,
            supportsBonus
                ? set.Call<Func<object, int>>(ids + "-get-bonus-level-action", type, "GetFreeLevels", typeof(int))
                : null,
            supportsBonus
                ? set.Call<Func<object, object?>>(ids + "-get-bonus-cost-action", type, "GetFreeLevelCost", cost)
                : null,
            supportsBonus
                ? set.Call<Action<object>>(ids + "-purchase-bonus-action", type, "PurchaseFreeLevel", typeof(void))
                : null);

    private static void AssertImplementationRoster(Type contract, params Type[] expected)
    {
        Type[] types;
        try
        {
            types = contract.Assembly.GetTypes();
        }
        catch (Exception exception) when (exception is ReflectionTypeLoadException or NotSupportedException)
        {
            throw new NativeBindingException(contract.Name + " implementation roster was unreadable: " +
                exception.Message);
        }
        var actual = types
            .Where(type => type.IsClass && !type.IsAbstract && contract.IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var orderedExpected = expected.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(orderedExpected))
            throw new NativeBindingException(contract.Name +
                " concrete implementation roster changed from " +
                string.Join(", ", orderedExpected.Select(type => type.Name)));
    }
}

internal sealed class GenericLevelTargetBinding
{
    internal GenericLevelTargetBinding(
        Type targetType,
        Func<object, int> getLevel,
        Func<object, bool> canLevel,
        Func<object, object?> getLevelCost,
        Action<object> purchaseLevel,
        Func<object, bool> isVisible,
        Func<object, bool> isAvailable,
        Func<object, bool>? isDiscovered = null,
        Func<object, bool>? isHidden = null,
        Func<object, int>? getBonusLevels = null,
        Func<object, object?>? getBonusCost = null,
        Action<object>? purchaseBonus = null)
    {
        TargetType = targetType;
        GetLevel = getLevel;
        CanLevel = canLevel;
        GetLevelCost = getLevelCost;
        PurchaseLevel = purchaseLevel;
        IsVisible = isVisible;
        IsAvailable = isAvailable;
        IsDiscovered = isDiscovered;
        IsHidden = isHidden;
        GetBonusLevels = getBonusLevels;
        GetBonusCost = getBonusCost;
        PurchaseBonus = purchaseBonus;
    }

    internal Type TargetType { get; }
    internal Func<object, int> GetLevel { get; }
    internal Func<object, bool> CanLevel { get; }
    internal Func<object, object?> GetLevelCost { get; }
    internal Action<object> PurchaseLevel { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Func<object, bool> IsAvailable { get; }
    internal Func<object, bool>? IsDiscovered { get; }
    internal Func<object, bool>? IsHidden { get; }
    internal Func<object, int>? GetBonusLevels { get; }
    internal Func<object, object?>? GetBonusCost { get; }
    internal Action<object>? PurchaseBonus { get; }
    internal bool SupportsBonus => PurchaseBonus is not null;
}
