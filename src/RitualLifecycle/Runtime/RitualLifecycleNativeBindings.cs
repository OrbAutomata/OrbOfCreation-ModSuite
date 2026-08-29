using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the visible Ritual selection and run controls.</summary>
internal sealed class RitualLifecycleNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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
    internal Func<object, IList?> CostEntries { get; }
    internal Func<object, object?> CostResource { get; }
    internal Func<object, BigDouble> CostValue { get; }
    internal Func<object, Guid> ResourceGuid { get; }
    internal Func<object, BigDouble, bool> HasResourceAmount { get; }
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

            var ritual = T(0, "RitualSO");
            var managerType = T(1, "RitualManager");
            var variable = T(2, "RitualVariable");
            var cost = T(3, "ResourceCostList");
            var tuple = T(4, "ResourceTuple");
            var resource = T(5, "ResourceSO");
            var battleType = T(6, "BattleManager");

            var manager = StaticField(7, managerType, "instance", managerType, includeContract);
            var selected = Field(8, managerType, "selectedRitual", variable, includeContract);
            var toggle = Method(9, variable, "ToggleValue", typeof(void), new[] { ritual }, includeContract);
            var isItem = Method(10, variable, "IsItem", typeof(bool), new[] { ritual }, includeContract);
            var discovered = Method(11, ritual, "IsDiscovered", typeof(bool), Type.EmptyTypes, includeContract);
            var force = Field(12, ritual, "forceLevel", typeof(bool), includeContract);
            var forceValue = Field(13, ritual, "forceLevelValue", typeof(int), includeContract);
            var level = Field(14, ritual, "selectedLevel", typeof(int), includeContract);
            var maximum = Method(15, ritual, "GetMaxSelectedLevel", typeof(int), Type.EmptyTypes, includeContract);
            var change = Method(16, ritual, "ChangeStartingLevel", typeof(void), new[] { typeof(int) }, includeContract);
            var getCost = Method(17, ritual, "GetActivationCost", cost, Type.EmptyTypes, includeContract);
            var enough = Method(18, cost, "HasEnough", typeof(bool), Type.EmptyTypes, includeContract);
            var perform = Method(19, cost, "PerformCost", typeof(void), Type.EmptyTypes, includeContract);
            var entriesMethod = Method(20, cost, "GetEntries", null, Type.EmptyTypes, includeContract);
            if (!typeof(IList).IsAssignableFrom(entriesMethod.ReturnType))
                throw new InvalidOperationException("ResourceCostList.GetEntries did not return a list");
            var rowResource = Field(21, tuple, "resource", resource, includeContract);
            var rowValue = Method(22, tuple, "GetValue", typeof(BigDouble), Type.EmptyTypes, includeContract);
            var resourceId = Method(23, resource, "GetGuid", typeof(Guid), Type.EmptyTypes, includeContract);
            var hasAmount = Method(24, resource, "HasAmount", typeof(bool), new[] { typeof(BigDouble) }, includeContract);
            var activate = Method(25, managerType, "ActivateSelectedRitual", typeof(void), Type.EmptyTypes, includeContract);
            var battleManager = StaticField(26, battleType, "instance", battleType, includeContract);
            var combat = Method(27, battleType, "IsInCombat", typeof(bool), Type.EmptyTypes, includeContract);
            var active = Field(28, ritual, "inBattle", typeof(bool), includeContract);
            var durationKind = Method(29, ritual, "IsDurationRitual", typeof(bool), Type.EmptyTypes, includeContract);
            var durationActive = Method(30, ritual, "IsDurationActive", typeof(bool), Type.EmptyTypes, includeContract);
            var cancel = Method(31, ritual, "Cancel", typeof(void), Type.EmptyTypes, includeContract);
            var activeRitual = Field(32, battleType, "activeRitual", variable, includeContract);
            var endRitual = Method(33, battleType, "EndRitual", typeof(void), Type.EmptyTypes, includeContract);

            bindings = new RitualLifecycleNativeBindings(
                ritual,
                managerType,
                battleType,
                StaticObject(manager),
                ObjectField(selected),
                Action2(toggle),
                Func2<bool>(isItem),
                Func<bool>(discovered),
                FieldFunc<bool>(force),
                FieldFunc<int>(forceValue),
                FieldFunc<int>(level),
                Func<int>(maximum),
                ActionInt(change),
                ObjectFunc(getCost),
                Func<bool>(enough),
                Action1(perform),
                ListFunc(entriesMethod),
                ObjectField(rowResource),
                Func<BigDouble>(rowValue),
                Func<Guid>(resourceId),
                Func2<BigDouble, bool>(hasAmount),
                Action1(activate),
                StaticObject(battleManager),
                Func<bool>(combat),
                FieldFunc<bool>(active),
                Func<bool>(durationKind),
                Func<bool>(durationActive),
                Action1(cancel),
                ObjectField(activeRitual),
                Action1(endRitual));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or NotSupportedException)
        {
            reason = "Ritual lifecycle contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
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

    private static FieldInfo StaticField(
        int index,
        Type owner,
        string name,
        Type exactType,
        Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Static);
        if (field is null || !field.IsStatic || field.FieldType != exactType)
            throw new InvalidOperationException(
                owner.Name + "." + name + " did not match the audited static field");
        return field;
    }

    private static Func<object, T> Func<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(T)),
            target).Compile();
    }

    private static Func<object, object, T> Func2<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        return Expression.Lambda<Func<object, object, T>>(
            Expression.Convert(
                Expression.Call(
                    Expression.Convert(target, method.DeclaringType!),
                    method,
                    Expression.Convert(argument, method.GetParameters()[0].ParameterType)),
                typeof(T)),
            target,
            argument).Compile();
    }

    private static Func<object, TArg, TResult> Func2<TArg, TResult>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(TArg), "argument");
        return Expression.Lambda<Func<object, TArg, TResult>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method, argument),
                typeof(TResult)),
            target,
            argument).Compile();
    }

    private static Func<object, object?> ObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(object)),
            target).Compile();
    }

    private static Func<object, IList?> ListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList?>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(IList)),
            target).Compile();
    }

    private static Action<object> Action1(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Action<object, object> Action2(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        return Expression.Lambda<Action<object, object>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                Expression.Convert(argument, method.GetParameters()[0].ParameterType)),
            target,
            argument).Compile();
    }

    private static Action<object, int> ActionInt(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Action<object, int>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value),
            target,
            value).Compile();
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object, object?> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(
                Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(object)),
            target).Compile();
    }

    private static Func<object, T> FieldFunc<T>(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            target).Compile();
    }
}
