using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for every player-facing consumable transition.</summary>
internal sealed class ConsumablePlayerNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "consumable.is-visible",
        "consumable.can-fire",
        "consumable.select-and-fire",
        "consumable.set-randomization",
        "consumable.is-randomized",
        "consumable.can-be-randomized",
        "consumable.get-quantity",
        "consumable.get-queued",
        "consumable.consumable-usages",
        "consumable-usage.engaged",
        "consumable-usage.get-guid",
        "inventory.can-use-consumable",
        "targeting-manager.is-targeting",
        "consumable-player.consumable-cancel-usage-action",
        "consumable-player.consumable-discard-action",
        "consumable-player.consumable-next-usage-action",
        "consumable-player.usage-result-info-action",
        "consumable-player.result-info-is-cancelled-action",
        "consumable-player.inventory-instance-action",
        "consumable-player.inventory-list-action",
        "consumable-player.hotbar-list-action",
        "consumable-player.list-value-action",
        "consumable-player.list-swap-action",
        "consumable-player.list-set-at-action",
        "consumable-player.list-update-action",
    };

    private ConsumablePlayerNativeBindings(
        Type consumableType,
        Type usageType,
        Type listType,
        Func<object, bool> isVisible,
        Func<object, bool> canFire,
        Action<object> selectAndFire,
        Action<object> cancelUsage,
        Action<object, int> discard,
        Action<object, bool> setRandomization,
        Func<object, bool> isRandomized,
        Func<object, bool> canBeRandomized,
        Func<object, int> getQuantity,
        Func<object, int> getQueued,
        Func<object, IList> getUsages,
        Func<object, object?> getNextUsage,
        Func<object, bool> usageEngaged,
        Func<object, Guid> usageGuid,
        Func<object, object?> usageResultInfo,
        Func<object, bool> isCancelled,
        Func<bool> canUse,
        Func<bool> isTargeting,
        Func<object?> getInventory,
        Func<object, object?> getInventoryList,
        Func<object, object?> getHotbarList,
        Func<object, IList> getListValues,
        Action<object, int, int> swap,
        Action<object> update,
        Action<object, int, object> setAt)
    {
        ConsumableType = consumableType;
        UsageType = usageType;
        ListType = listType;
        IsVisible = isVisible;
        CanFire = canFire;
        SelectAndFire = selectAndFire;
        CancelUsage = cancelUsage;
        Discard = discard;
        SetRandomization = setRandomization;
        IsRandomized = isRandomized;
        CanBeRandomized = canBeRandomized;
        GetQuantity = getQuantity;
        GetQueued = getQueued;
        GetUsages = getUsages;
        GetNextUsage = getNextUsage;
        UsageEngaged = usageEngaged;
        UsageGuid = usageGuid;
        UsageResultInfo = usageResultInfo;
        IsCancelled = isCancelled;
        CanUse = canUse;
        IsTargeting = isTargeting;
        GetInventory = getInventory;
        GetInventoryList = getInventoryList;
        GetHotbarList = getHotbarList;
        GetListValues = getListValues;
        Swap = swap;
        Update = update;
        SetAt = setAt;
    }

    internal Type ConsumableType { get; }
    internal Type UsageType { get; }
    internal Type ListType { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Func<object, bool> CanFire { get; }
    internal Action<object> SelectAndFire { get; }
    internal Action<object> CancelUsage { get; }
    internal Action<object, int> Discard { get; }
    internal Action<object, bool> SetRandomization { get; }
    internal Func<object, bool> IsRandomized { get; }
    internal Func<object, bool> CanBeRandomized { get; }
    internal Func<object, int> GetQuantity { get; }
    internal Func<object, int> GetQueued { get; }
    internal Func<object, IList> GetUsages { get; }
    internal Func<object, object?> GetNextUsage { get; }
    internal Func<object, bool> UsageEngaged { get; }
    internal Func<object, Guid> UsageGuid { get; }
    internal Func<object, object?> UsageResultInfo { get; }
    internal Func<object, bool> IsCancelled { get; }
    internal Func<bool> CanUse { get; }
    internal Func<bool> IsTargeting { get; }
    internal Func<object?> GetInventory { get; }
    internal Func<object, object?> GetInventoryList { get; }
    internal Func<object, object?> GetHotbarList { get; }
    internal Func<object, IList> GetListValues { get; }
    internal Action<object, int, int> Swap { get; }
    internal Action<object> Update { get; }
    internal Action<object, int, object> SetAt { get; }

    internal static bool TryCreate(
        Func<string, Type?> resolveType,
        Func<string, bool> include,
        out ConsumablePlayerNativeBindings? bindings,
        out string reason)
    {
        bindings = null;
        try
        {
            foreach (var id in ContractIds)
                if (!include(id))
                    throw new InvalidOperationException("Required contract " + id + " was withheld.");
            Type T(string name) => resolveType(name) ??
                throw new InvalidOperationException(name + " was unavailable.");
            var consumable = T("ConsumableSO");
            var usage = T("ConsumableUsage");
            var resultInfo = T("EffectResultInfo");
            var inventory = T("Inventory");
            var list = T("ConsumableRefListVariable");
            var manager = T("TargetingManager");
            var usageList = typeof(System.Collections.Generic.List<>).MakeGenericType(usage);
            var consumableList = typeof(System.Collections.Generic.List<>).MakeGenericType(consumable);

            bindings = new ConsumablePlayerNativeBindings(
                consumable,
                usage,
                list,
                InstanceFunc<bool>(Method(consumable, "IsVisible", typeof(bool))),
                InstanceFunc<bool>(Method(consumable, "CanFire", typeof(bool))),
                InstanceAction(Method(consumable, "SelectAndFire", typeof(void))),
                InstanceAction(Method(consumable, "CancelUsage", typeof(void))),
                InstanceAction<int>(Method(consumable, "Discard", typeof(void), typeof(int))),
                InstanceAction<bool>(Method(
                    consumable,
                    "SetRandomization",
                    typeof(void),
                    typeof(bool))),
                InstanceFunc<bool>(Method(consumable, "IsRandomized", typeof(bool))),
                InstanceField<bool>(Field(consumable, "canBeRandomized", typeof(bool))),
                InstanceFunc<int>(Method(consumable, "GetQuantity", typeof(int))),
                InstanceFunc<int>(Method(consumable, "GetQueued", typeof(int))),
                InstanceList(Field(consumable, "consumableUsages", usageList)),
                InstanceObjectField(Field(consumable, "nextUsage", usage)),
                InstanceField<bool>(Field(usage, "en", typeof(bool))),
                InstanceFunc<Guid>(Method(usage, "GetGuid", typeof(Guid))),
                InstanceObject(Method(usage, "GetResultInfo", resultInfo)),
                InstanceFunc<bool>(Method(resultInfo, "IsCancelled", typeof(bool))),
                StaticFunc<bool>(Method(inventory, "CanUseConsumable", typeof(bool), true)),
                StaticFunc<bool>(Method(manager, "IsTargeting", typeof(bool), true)),
                StaticObjectField(Field(inventory, "_instance", inventory, true)),
                InstanceObjectField(Field(inventory, "allConsumables", list)),
                InstanceObjectField(Field(inventory, "hotBar", list)),
                InstanceList(Field(HierarchyFieldOwner(list, "value"), "value", consumableList)),
                InstanceAction<int, int>(HierarchyMethod(
                    list,
                    "SwapPositions",
                    typeof(void),
                    typeof(int),
                    typeof(int))),
                InstanceAction(HierarchyMethod(list, "UpdateObservable", typeof(void))),
                InstanceObjectAction<int>(HierarchyMethod(
                    list,
                    "SetAt",
                    typeof(void),
                    typeof(int),
                    consumable)));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or AmbiguousMatchException)
        {
            reason = "The complete consumable player binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static MethodInfo Method(
        Type type,
        string name,
        Type result,
        params Type[] parameters) => Method(type, name, result, false, parameters);

    private static MethodInfo Method(
        Type type,
        string name,
        Type result,
        bool isStatic,
        params Type[] parameters)
    {
        var method = type.GetMethod(name, isStatic ? Static : Instance, null, parameters, null);
        if (method is null || method.IsStatic != isStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static MethodInfo HierarchyMethod(
        Type type,
        string name,
        Type result,
        params Type[] parameters)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                Instance | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method is not null && method.ReturnType == result) return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static Type HierarchyFieldOwner(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.GetField(name, Instance | BindingFlags.DeclaredOnly) is not null)
                return current;
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static FieldInfo Field(
        Type type,
        string name,
        Type valueType,
        bool isStatic = false)
    {
        var field = type.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.IsStatic != isStatic || field.FieldType != valueType)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return field;
    }

    private static Func<T> StaticFunc<T>(MethodInfo method) =>
        Expression.Lambda<Func<T>>(Expression.Call(method)).Compile();

    private static Func<object?> StaticObjectField(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Func<object, T> InstanceField<T>(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            target).Compile();
    }

    private static Func<object, object?> InstanceObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(object)),
            target).Compile();
    }

    private static Func<object, object?> InstanceObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(
                Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(object)),
            target).Compile();
    }

    private static Func<object, IList> InstanceList(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(
                Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(IList)),
            target).Compile();
    }

    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Action<object, T> InstanceAction<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(T), "value");
        return Expression.Lambda<Action<object, T>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value),
            target,
            value).Compile();
    }

    private static Action<object, T1, T2> InstanceAction<T1, T2>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var first = Expression.Parameter(typeof(T1), "first");
        var second = Expression.Parameter(typeof(T2), "second");
        return Expression.Lambda<Action<object, T1, T2>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                first,
                second),
            target,
            first,
            second).Compile();
    }

    private static Action<object, T, object> InstanceObjectAction<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var first = Expression.Parameter(typeof(T), "first");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, T, object>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                first,
                Expression.Convert(value, method.GetParameters()[1].ParameterType)),
            target,
            first,
            value).Compile();
    }
}
