using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set; execution performs no reflection.</summary>
internal sealed class TargetingNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "targeting-manager.is-targeting", "targeting-manager.get-link",
        "targeting-manager.submit-target", "target-link.get-random",
        "targeting.target-link-get-all-targets-action",
        "targeting.target-link-check-target-action",
        "targeting.target-link-has-target-action",
        "targeting.target-link-target-action",
        "targeting.target-link-result-info-action",
        "targeting.effect-result-info-cancel-action",
        "targeting.effect-result-info-is-cancelled-action",
        "id-scriptable-object.get-guid-action",
    };

    private TargetingNativeBindings(Type linkType, Type structureType,
        Func<bool> isTargeting, Func<object?> getLink, Func<object, IList> getAllTargets,
        Func<object, object?> getRandom, Func<object, object, bool> checkTarget,
        Func<object, bool> hasTarget, Func<object, object?> readTarget,
        Func<object, object?> readResultInfo, Action<object> cancel,
        Func<object, bool> isCancelled, Action<object> submitTarget, Func<object, Guid> getGuid)
    {
        LinkType = linkType; StructureType = structureType; IsTargeting = isTargeting;
        GetLink = getLink; GetAllTargets = getAllTargets; GetRandom = getRandom;
        CheckTarget = checkTarget; HasTarget = hasTarget; ReadTarget = readTarget;
        ReadResultInfo = readResultInfo; Cancel = cancel; IsCancelled = isCancelled;
        SubmitTarget = submitTarget; GetGuid = getGuid;
    }

    internal Type LinkType { get; }
    internal Type StructureType { get; }
    internal Func<bool> IsTargeting { get; }
    internal Func<object?> GetLink { get; }
    internal Func<object, IList> GetAllTargets { get; }
    internal Func<object, object?> GetRandom { get; }
    internal Func<object, object, bool> CheckTarget { get; }
    internal Func<object, bool> HasTarget { get; }
    internal Func<object, object?> ReadTarget { get; }
    internal Func<object, object?> ReadResultInfo { get; }
    internal Action<object> Cancel { get; }
    internal Func<object, bool> IsCancelled { get; }
    internal Action<object> SubmitTarget { get; }
    internal Func<object, Guid> GetGuid { get; }

    internal static bool TryCreate(Func<string, Type?> resolveType, Func<string, bool> include,
        out TargetingNativeBindings? bindings, out string reason)
    {
        bindings = null;
        try
        {
            foreach (var id in ContractIds)
                if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld.");
            Type T(string name) => resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable.");
            var manager = T("TargetingManager");
            var link = T("TargetingManager+TargetLink");
            var targetable = T("Targeting.ITargetable");
            var tooltipable = T("ITooltipable");
            var structure = T("StructureSO");
            var resultInfo = T("EffectResultInfo");
            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(tooltipable);
            bindings = new TargetingNativeBindings(
                link, structure,
                StaticFunc<bool>(Method(manager, "IsTargeting", typeof(bool), true)),
                StaticObject(Method(manager, "GetTargetingLink", link, true)),
                InstanceList(Method(link, "GetAllTargets", listType)),
                InstanceObject(Method(link, "GetRandom", targetable)),
                InstanceObjectFunc<bool>(Method(link, "CheckTarget", typeof(bool), false, targetable)),
                InstanceFunc<bool>(Method(link, "HasTarget", typeof(bool))),
                InstanceField(Field(link, "target", targetable)),
                InstanceField(Field(link, "resultInfo", resultInfo)),
                InstanceAction(Method(resultInfo, "Cancel", typeof(void))),
                InstanceFunc<bool>(Method(resultInfo, "IsCancelled", typeof(bool))),
                StaticObjectAction(Method(manager, "SubmitTarget", typeof(void), true, targetable)),
                InstanceFunc<Guid>(HierarchyMethod(structure, "GetGuid", typeof(Guid))));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or AmbiguousMatchException)
        {
            reason = "The complete targeting binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static MethodInfo Method(Type type, string name, Type result, params Type[] parameters) =>
        Method(type, name, result, false, parameters);
    private static MethodInfo Method(Type type, string name, Type result, bool isStatic, params Type[] parameters)
    {
        var method = type.GetMethod(name, isStatic ? Static : Instance, null, parameters, null);
        if (method is null || method.IsStatic != isStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }
    private static MethodInfo HierarchyMethod(Type type, string name, Type result)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(name, Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (method is not null && method.ReturnType == result) return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }
    private static FieldInfo Field(Type type, string name, Type valueType)
    {
        var field = type.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != valueType)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return field;
    }
    private static Func<T> StaticFunc<T>(MethodInfo method) =>
        Expression.Lambda<Func<T>>(Expression.Call(method)).Compile();
    private static Func<object?> StaticObject(MethodInfo method) =>
        Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Call(method), typeof(object))).Compile();
    private static Action<object> StaticObjectAction(MethodInfo method)
    {
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object>>(Expression.Call(method,
            Expression.Convert(value, method.GetParameters()[0].ParameterType)), value).Compile();
    }
    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }
    private static Func<object, object?> InstanceObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), typeof(object)), target).Compile();
    }
    private static Func<object, IList> InstanceList(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList>>(Expression.Convert(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), typeof(IList)), target).Compile();
    }
    private static Func<object, object, T> InstanceObjectFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object, T>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method,
            Expression.Convert(value, method.GetParameters()[0].ParameterType)), target, value).Compile();
    }
    private static Func<object, object?> InstanceField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(Expression.Field(
            Expression.Convert(target, field.DeclaringType!), field), typeof(object)), target).Compile();
    }
    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }
}
