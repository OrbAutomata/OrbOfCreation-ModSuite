using System;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for both global Casting-screen dials.</summary>
internal sealed class SpellCompositionNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-composition.player-instance-action",
        "spell-composition.player-output-level-action",
        "spell-composition.player-maximum-output-level-action",
        "spell-composition.player-reserve-level-action",
        "spell-composition.player-maximum-reserve-level-action",
        "int-variable.as-int",
        "int-variable.set-value",
    };

    private SpellCompositionNativeBindings(
        Func<object?> player,
        Func<object?> outputVariable,
        Func<object, object> maximumOutputVariable,
        Func<object?> reserveVariable,
        Func<object, object> maximumReserveVariable,
        Func<object, int> asInt,
        Action<object, int> setInt)
    {
        ReadPlayer = player;
        ReadOutputVariable = outputVariable;
        ReadMaximumOutputVariable = maximumOutputVariable;
        ReadReserveVariable = reserveVariable;
        ReadMaximumReserveVariable = maximumReserveVariable;
        ReadInt = asInt;
        SetInt = setInt;
    }

    internal Func<object?> ReadPlayer { get; }
    internal Func<object?> ReadOutputVariable { get; }
    internal Func<object, object> ReadMaximumOutputVariable { get; }
    internal Func<object?> ReadReserveVariable { get; }
    internal Func<object, object> ReadMaximumReserveVariable { get; }
    internal Func<object, int> ReadInt { get; }
    internal Action<object, int> SetInt { get; }

    internal static bool TryCreate(
        Func<string, Type?> resolveType,
        Func<string, bool> includeContract,
        out SpellCompositionNativeBindings? bindings,
        out string reason)
    {
        bindings = null;
        try
        {
            foreach (var id in ContractIds) Require(id, includeContract);
            Type T(string name) => resolveType(name) ??
                throw new InvalidOperationException(name + " was unavailable.");

            var playerType = T("Player");
            var intType = T("IntVariable");
            var playerInstance = Field(playerType, "_instance", playerType, isStatic: true);
            var output = StaticMethod(playerType, "GetSpellOutputLevel", intType);
            var maximumOutput = Field(
                playerType,
                "maxSpellOutputLevel",
                intType,
                isStatic: false);
            var reserve = StaticMethod(playerType, "GetReserveLevel", intType);
            var maximumReserve = Field(
                playerType,
                "maxReserveLevel",
                intType,
                isStatic: false);
            var asInt = Method(intType, "AsInt", typeof(int));
            var setInt = Method(intType, "SetValue", typeof(void), typeof(int));

            bindings = new SpellCompositionNativeBindings(
                StaticObject(playerInstance),
                StaticCall(output),
                ObjectField(maximumOutput),
                StaticCall(reserve),
                ObjectField(maximumReserve),
                InstanceFunc<int>(asInt),
                InstanceValueAction<int>(setInt));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or AmbiguousMatchException)
        {
            reason = "The complete Casting-dial binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    {
        if (!include(id))
            throw new InvalidOperationException("Required contract " + id + " was withheld.");
    }

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo Field(
        Type type,
        string name,
        Type valueType,
        bool isStatic)
    {
        var field = type.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.FieldType != valueType || field.IsStatic != isStatic)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return field;
    }

    private static MethodInfo Method(
        Type type,
        string name,
        Type result,
        params Type[] arguments)
    {
        var method = type.GetMethod(name, Instance, null, arguments, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static MethodInfo StaticMethod(
        Type type,
        string name,
        Type result,
        params Type[] arguments)
    {
        var method = type.GetMethod(name, Static, null, arguments, null);
        if (method is null || !method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object?> StaticCall(MethodInfo method) =>
        Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Call(method), typeof(object))).Compile();

    private static Func<object, object> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(
                Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(object)),
            target).Compile();
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, T>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Action<object, T> InstanceValueAction<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        var value = Expression.Parameter(typeof(T));
        return Expression.Lambda<Action<object, T>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                value),
            target,
            value).Compile();
    }
}
