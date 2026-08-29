using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding matrix for the four level-list controls owned here.</summary>
internal sealed class GenericLevelNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
    internal Func<object, IList?> CostEntries { get; }
    internal Func<object, object?> CostResource { get; }
    internal Func<object, BigDouble> CostValue { get; }
    internal Func<object, Guid> ResourceGuid { get; }
    internal Func<object, BigDouble, bool> HasResourceAmount { get; }

    internal bool TryTarget(string nativeType, out GenericLevelTargetBinding binding) =>
        _targets.TryGetValue(nativeType, out binding!);

    internal static bool TryCreate(
        out GenericLevelNativeBindings? bindings,
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

            var levelable = T(0, "ILevelable");
            var freeLevelable = T(1, "ILevelableHasFree");
            var equipment = T(2, "EquipmentTypeSO");
            var glyph = T(3, "GlyphSO");
            var resourceType = T(4, "ResourceTypeSO");
            var timeRune = T(5, "TimeRuneSO");
            var research = T(6, "ResearchSO");
            var spellRecipe = T(7, "SpellRecipeSO");
            var cost = T(8, "ResourceCostList");
            var tuple = T(9, "ResourceTuple");
            var resource = T(10, "ResourceSO");

            AssertImplementationRoster(levelable,
                equipment, glyph, research, resourceType, spellRecipe, timeRune);
            AssertImplementationRoster(freeLevelable, equipment, glyph, resourceType);

            var targets = new Dictionary<string, GenericLevelTargetBinding>(StringComparer.Ordinal)
            {
                [equipment.Name] = BindTarget(equipment, cost, true, 11, 27,
                    43, 44, includeContract),
                [glyph.Name] = BindTarget(glyph, cost, true, 15, 30,
                    45, 46, includeContract),
                [resourceType.Name] = BindTarget(resourceType, cost, true, 19, 33,
                    47, 48, includeContract, hiddenIndex: 49),
                [timeRune.Name] = BindTarget(timeRune, cost, false, 23, -1,
                    50, 51, includeContract, discoveredIndex: 52),
            };

            var enough = Method(36, cost, "HasEnough", typeof(bool), Type.EmptyTypes, includeContract);
            var visible = Method(37, cost, "AllResourcesVisible", typeof(bool), Type.EmptyTypes, includeContract);
            var entries = Method(38, cost, "GetEntries", null, Type.EmptyTypes, includeContract);
            if (!typeof(IList).IsAssignableFrom(entries.ReturnType))
                throw new InvalidOperationException("ResourceCostList.GetEntries did not return a list");
            var rowResource = Field(39, tuple, "resource", resource, includeContract);
            var rowValue = Method(40, tuple, "GetValue", typeof(BigDouble), Type.EmptyTypes, includeContract);
            var resourceId = Method(41, resource, "GetGuid", typeof(Guid), Type.EmptyTypes, includeContract);
            var hasAmount = Method(42, resource, "HasAmount", typeof(bool),
                new[] { typeof(BigDouble) }, includeContract);

            bindings = new GenericLevelNativeBindings(
                targets,
                Func<bool>(enough),
                Func<bool>(visible),
                ListFunc(entries),
                ObjectField(rowResource),
                Func<BigDouble>(rowValue),
                Func<Guid>(resourceId),
                Func2<BigDouble, bool>(hasAmount));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or NotSupportedException or ReflectionTypeLoadException)
        {
            reason = "Generic level contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private static GenericLevelTargetBinding BindTarget(
        Type type,
        Type cost,
        bool supportsBonus,
        int levelStart,
        int bonusStart,
        int visibleIndex,
        int availableIndex,
        Func<string, bool> include,
        int discoveredIndex = -1,
        int hiddenIndex = -1)
    {
        var getLevel = Method(levelStart, type, "GetLevel", typeof(int), Type.EmptyTypes, include);
        var canLevel = Method(levelStart + 1, type, "CanLevel", typeof(bool), Type.EmptyTypes, include);
        var getCost = Method(levelStart + 2, type, "GetLevelCost", cost, Type.EmptyTypes, include);
        var purchase = Method(levelStart + 3, type, "PurchaseLevel", typeof(void), Type.EmptyTypes, include);
        var visible = Method(visibleIndex, type, "IsVisible", typeof(bool), Type.EmptyTypes, include);
        var available = Method(availableIndex, type, "IsAvailable", typeof(bool), Type.EmptyTypes, include);
        var discovered = discoveredIndex < 0 ? null :
            Method(discoveredIndex, type, "IsDiscovered", typeof(bool), Type.EmptyTypes, include);
        var hidden = hiddenIndex < 0 ? null :
            Field(hiddenIndex, type, "specialHidden", typeof(bool), include);
        if (!supportsBonus)
            return new GenericLevelTargetBinding(type, Func<int>(getLevel), Func<bool>(canLevel),
                ObjectFunc(getCost), Action1(purchase), Func<bool>(visible), Func<bool>(available),
                discovered is null ? null : Func<bool>(discovered),
                hidden is null ? null : BoolField(hidden));
        var getBonus = Method(bonusStart, type, "GetFreeLevels", typeof(int), Type.EmptyTypes, include);
        var getBonusCost = Method(bonusStart + 1, type, "GetFreeLevelCost", cost, Type.EmptyTypes, include);
        var purchaseBonus = Method(bonusStart + 2, type, "PurchaseFreeLevel", typeof(void), Type.EmptyTypes, include);
        return new GenericLevelTargetBinding(type, Func<int>(getLevel), Func<bool>(canLevel),
            ObjectFunc(getCost), Action1(purchase), Func<bool>(visible), Func<bool>(available),
            discovered is null ? null : Func<bool>(discovered),
            hidden is null ? null : BoolField(hidden), Func<int>(getBonus),
            ObjectFunc(getBonusCost), Action1(purchaseBonus));
    }

    private static void AssertImplementationRoster(Type contract, params Type[] expected)
    {
        var actual = contract.Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && contract.IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var orderedExpected = expected.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(orderedExpected))
            throw new InvalidOperationException(contract.Name +
                " concrete implementation roster changed from " +
                string.Join(", ", orderedExpected.Select(type => type.Name)));
    }

    private static void Require(int index, Func<string, bool> include)
    {
        if (!include(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }

    private static MethodInfo Method(
        int index,
        Type owner,
        string name,
        Type? result,
        Type[] parameters,
        Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic ||
            (result is not null && method.ReturnType != result))
            throw new InvalidOperationException(
                owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private static FieldInfo Field(
        int index,
        Type owner,
        string name,
        Type exactType,
        Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != exactType)
            throw new InvalidOperationException(
                owner.Name + "." + name + " did not match the audited field");
        return field;
    }

    private static Func<object, T> Func<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(T)), target).Compile();
    }

    private static Func<object, TArg, TResult> Func2<TArg, TResult>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(TArg), "argument");
        return Expression.Lambda<Func<object, TArg, TResult>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method, argument),
                typeof(TResult)), target, argument).Compile();
    }

    private static Func<object, object?> ObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(object)), target).Compile();
    }

    private static Action<object> Action1(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Func<object, IList?> ListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList?>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(IList)), target).Compile();
    }

    private static Func<object, object?> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(
                Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(object)), target).Compile();
    }

    private static Func<object, bool> BoolField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, bool>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field), target).Compile();
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
