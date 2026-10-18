using System;
using System.Collections;
using System.Reflection;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
#endif

namespace OrbAutomata;

internal static class AutoHarvestReflectionAccess
{
    public static IList RequireList(object? value, string name) =>
        value as IList ?? throw new InvalidOperationException($"{name} is unavailable");

    public static bool InvokeBool(MethodInfo method, object owner) =>
        method.Invoke(owner, Array.Empty<object>()) is bool value
            ? value
            : throw new InvalidOperationException($"{method.Name} did not return Boolean");

    public static int InvokeInt(MethodInfo method, object owner, params object[] args) =>
        Convert.ToInt32(method.Invoke(owner, args));

#if SERVICE_CYCLE_PROFILE
    public static object? GetValue(
        FieldInfo field,
        object owner,
        AutomataProfileOperations operations)
    {
        operations.AddReflectedFieldRead();
        return field.GetValue(owner);
    }

    public static object? Invoke(
        MethodInfo method,
        object owner,
        object[] arguments,
        AutomataProfileOperations operations)
    {
        operations.AddReflectedMethodCall();
        if (arguments.Length != 0) operations.AddInvocationArgumentArray();
        return method.Invoke(owner, arguments);
    }

    public static bool InvokeBool(
        MethodInfo method,
        object owner,
        AutomataProfileOperations operations)
    {
        operations.AddReflectedMethodCall();
        return InvokeBool(method, owner);
    }

    public static int InvokeInt(
        MethodInfo method,
        object owner,
        AutomataProfileOperations operations,
        params object[] args)
    {
        operations.AddReflectedMethodCall();
        if (args.Length != 0) operations.AddInvocationArgumentArray();
        return InvokeInt(method, owner, args);
    }
#endif

    public static AutoHarvestRuntimeFailureKind ClassifyExpectedFailure(Exception ex) =>
        ex is TargetInvocationException
            ? AutoHarvestRuntimeFailureKind.Retryable
            : AutoHarvestRuntimeFailureKind.Contract;
}

internal sealed class AutoHarvestRegistryNotReadyException : Exception
{
    public AutoHarvestRegistryNotReadyException(string message) : base(message)
    {
    }
}
