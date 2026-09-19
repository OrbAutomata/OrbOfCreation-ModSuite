using System;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for native challenge decisions and offer refresh.</summary>
internal sealed class ChallengeNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "challenge.challenge.type-action", "challenge.manager.type-action",
        "challenge.reset-manager.type-action", "challenge.list.type-action",
        "challenge.int-variable.type-action", "challenge.bool-variable.type-action",
        "challenge.manager-instance-action", "challenge.reset-manager-instance-action",
        "challenge.manager-preferred-action", "challenge.manager-active-action",
        "challenge.reset-active-action", "challenge.reset-rerolls-left-action",
        "challenge.reset-cycle-complete-action", "challenge.reset-fetched-action",
        "challenge.list-empty-spot-action",
        "challenge.list-contains-action", "challenge.list-toggle-action",
        "challenge.list-restricted-action", "challenge.challenge-state-action",
        "challenge.challenge-toggle-queue-action", "challenge.challenge-abandon-action",
        "challenge.int-as-int-action", "challenge.int-set-action",
        "challenge.bool-get-action", "challenge.bool-set-action",
        "challenge.manager-fetch-action",
    };

    private ChallengeNativeBindings(Type challengeType, Type challengeManagerType,
        Type resetManagerType, Func<object?> challengeManager, Func<object?> resetManager,
        Func<object, object?> preferred, Func<object, object?> timeOffers,
        Func<object, object?> prestigeOffers, Func<object, object?> rerollsLeft,
        Func<object, object?> cycleComplete, Func<object, object?> fetched,
        Func<object, bool> hasEmptySpot, Func<object, object, bool> contains,
        Action<object, object> toggle, Func<object, object, bool> restricted,
        Func<object, int> state, Action<object> toggleQueue, Action<object> abandon,
        Func<object, int> asInt, Action<object, int> setInt, Func<object, bool> getBool,
        Action<object, bool> setBool, Action<object> reroll)
    {
        ChallengeType = challengeType;
        ChallengeManagerType = challengeManagerType;
        ResetManagerType = resetManagerType;
        ChallengeManager = challengeManager;
        ResetManager = resetManager;
        Preferred = preferred;
        TimeOffers = timeOffers;
        PrestigeOffers = prestigeOffers;
        RerollsLeft = rerollsLeft;
        CycleComplete = cycleComplete;
        Fetched = fetched;
        HasEmptySpot = hasEmptySpot;
        Contains = contains;
        Toggle = toggle;
        Restricted = restricted;
        State = state;
        ToggleQueue = toggleQueue;
        Abandon = abandon;
        AsInt = asInt;
        SetInt = setInt;
        GetBool = getBool;
        SetBool = setBool;
        Reroll = reroll;
    }

    internal Type ChallengeType { get; }
    internal Type ChallengeManagerType { get; }
    internal Type ResetManagerType { get; }
    internal Func<object?> ChallengeManager { get; }
    internal Func<object?> ResetManager { get; }
    internal Func<object, object?> Preferred { get; }
    internal Func<object, object?> TimeOffers { get; }
    internal Func<object, object?> PrestigeOffers { get; }
    internal Func<object, object?> RerollsLeft { get; }
    internal Func<object, object?> CycleComplete { get; }
    internal Func<object, object?> Fetched { get; }
    internal Func<object, bool> HasEmptySpot { get; }
    internal Func<object, object, bool> Contains { get; }
    internal Action<object, object> Toggle { get; }
    internal Func<object, object, bool> Restricted { get; }
    internal Func<object, int> State { get; }
    internal Action<object> ToggleQueue { get; }
    internal Action<object> Abandon { get; }
    internal Func<object, int> AsInt { get; }
    internal Action<object, int> SetInt { get; }
    internal Func<object, bool> GetBool { get; }
    internal Action<object, bool> SetBool { get; }
    /// <summary>
    /// The offer screen's own new-challenges button. One button, one shared reroll budget: the
    /// Reset modal's copy of it spends the same <c>challengeRerollsLeft</c> and refreshes the same
    /// list, so a second bound path was a second name for one press.
    /// </summary>
    internal Action<object> Reroll { get; }

    internal static bool TryCreate(out ChallengeNativeBindings? bindings, out string reason,
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
            var challenge = T(0, "ChallengeSO");
            var manager = T(1, "ChallengeManager");
            var reset = T(2, "PersistentResetManager");
            var list = T(3, "ChallengeListVariable");
            var integer = T(4, "IntVariable");
            var boolean = T(5, "BoolVariable");
            var challengeState = challenge.GetNestedType("ChallengeState", BindingFlags.Public | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("ChallengeSO.ChallengeState was unavailable");

            bindings = new ChallengeNativeBindings(challenge, manager, reset,
                StaticObject(StaticField(6, manager, "instance", manager, includeContract)),
                StaticObject(StaticField(7, reset, "instance", reset, includeContract)),
                ObjectField(Field(8, manager, "preferredChallenges", list, includeContract)),
                ObjectField(Field(9, manager, "activeChallenges", list, includeContract)),
                ObjectField(Field(10, reset, "activeChallenges", list, includeContract)),
                ObjectField(Field(11, reset, "challengeRerollsLeft", integer, includeContract)),
                ObjectField(Field(12, reset, "hasCompleteWorldCycle", boolean, includeContract)),
                ObjectField(Field(13, reset, "hasFetchedChallenges", boolean, includeContract)),
                Func<bool>(Method(14, list, "HasEmptySpot", typeof(bool), includeContract)),
                Func2<bool>(Method(15, list, "Contains", typeof(bool), includeContract, challenge)),
                Action2(Method(16, list, "Toggle", typeof(void), includeContract, challenge)),
                Func2<bool>(Method(17, list, "IsChallengeRestricted", typeof(bool), includeContract, challenge)),
                EnumField(Field(18, challenge, "state", challengeState, includeContract)),
                Action1(Method(19, challenge, "ToggleQueueActivation", typeof(void), includeContract)),
                Action1(Method(20, challenge, "AbandonChallenge", typeof(void), includeContract)),
                Func<int>(Method(21, integer, "AsInt", typeof(int), includeContract)),
                ActionValue<int>(Method(22, integer, "SetValue", typeof(void), includeContract, typeof(int))),
                Func<bool>(Method(23, boolean, "GetValue", typeof(bool), includeContract)),
                ActionValue<bool>(Method(24, boolean, "SetValue", typeof(void), includeContract, typeof(bool))),
                Action1(Method(25, manager, "LoadNewActiveChallenges", typeof(void), includeContract)));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or AmbiguousMatchException or ArgumentException)
        {
            reason = "The complete challenge binding set is unavailable: " + exception.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    { if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld"); }

    private static MethodInfo Method(int index, Type owner, string name, Type result,
        Func<string, bool> include, params Type[] parameters)
    {
        Require(ContractIds[index], include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
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

    private static FieldInfo StaticField(int index, Type owner, string name, Type type, Func<string, bool> include)
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

    private static Func<object, object, T> Func2<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object, T>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method,
            Expression.Convert(value, method.GetParameters()[0].ParameterType)), target, value).Compile();
    }

    private static Action<object> Action1(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Action<object, object> Action2(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, object>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method,
            Expression.Convert(value, method.GetParameters()[0].ParameterType)), target, value).Compile();
    }

    private static Action<object, T> ActionValue<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(T), "value");
        return Expression.Lambda<Action<object, T>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method, value), target, value).Compile();
    }

    private static Func<object, int> EnumField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, int>>(Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(int)), target).Compile();
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object, object?> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)), target).Compile();
    }

}
