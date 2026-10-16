using System;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the native persistent-reset transaction.</summary>
internal sealed class PrestigeNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "prestige.reset-manager.type-action", "prestige.bool-variable.type-action",
        "prestige.reset-manager-instance-action", "prestige.reset-cycle-complete-action",
        "prestige.reset-fetched-action", "prestige.bool-get-action",
        "prestige.reset-logic-action",
    };

    private PrestigeNativeBindings(Type managerType, Func<object?> manager,
        Func<object, object?> cycleComplete, Func<object, object?> challengesFetched,
        Func<object, bool> getBool, Action<object> reset)
    {
        ManagerType = managerType;
        Manager = manager;
        CycleComplete = cycleComplete;
        ChallengesFetched = challengesFetched;
        GetBool = getBool;
        Reset = reset;
    }

    internal Type ManagerType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> CycleComplete { get; }
    internal Func<object, object?> ChallengesFetched { get; }
    internal Func<object, bool> GetBool { get; }
    internal Action<object> Reset { get; }

    internal static bool TryCreate(out PrestigeNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(int index, string name)
            {
                Require(ContractIds[index], includeContract);
                return resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable");
            }
            var manager = T(0, "PersistentResetManager");
            var boolean = T(1, "BoolVariable");
            bindings = new PrestigeNativeBindings(
                manager,
                StaticObject(StaticField(2, manager, "instance", manager, includeContract)),
                ObjectField(Field(3, manager, "hasCompleteWorldCycle", boolean, includeContract)),
                ObjectField(Field(4, manager, "hasFetchedChallenges", boolean, includeContract)),
                Func<bool>(Method(5, boolean, "GetValue", typeof(bool), includeContract)),
                Action1(Method(6, manager, "PersistentResetLogic", typeof(void), includeContract)));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            AmbiguousMatchException or ArgumentException)
        {
            reason = "The complete prestige binding set is unavailable: " + exception.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    { if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld"); }

    private static MethodInfo Method(int index, Type owner, string name, Type result,
        Func<string, bool> include)
    {
        Require(ContractIds[index], include);
        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private static FieldInfo Field(int index, Type owner, string name, Type type, Func<string, bool> include)
    {
        Require(ContractIds[index], include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited field");
        return field;
    }

    private static FieldInfo StaticField(int index, Type owner, string name, Type type,
        Func<string, bool> include)
    {
        Require(ContractIds[index], include);
        var field = owner.GetField(name, Static);
        if (field is null || !field.IsStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited static field");
        return field;
    }

    private static Func<object, T> Func<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Action<object> Action1(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(Expression.Convert(
            Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object, object?> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)), target).Compile();
    }
}
