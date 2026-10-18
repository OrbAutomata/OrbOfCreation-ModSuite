using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using OrbModding;
using OrbModding.Common;

namespace OrbAutomata;

// Native global-variable contract primitives (multi-buy multiplier + bulk development read
// through IntVariable; safe main-thread multi-buy mutation scope). Extracted verbatim from
// the active ServiceCycle adapters rather than a feature-specific catalog
// scheduler (AB-SC-012): the ServiceCycle Pole-A reader and action adapter reuse these
// already-hardened contracts. Contains no scheduling/dirty/reconciliation concerns.

internal sealed class NativeIntVariableContract
{
    private static readonly object CacheGate = new object();
    private static readonly Dictionary<Type, NativeIntVariableContract> Cache =
        new Dictionary<Type, NativeIntVariableContract>();
    private readonly Type _variableType;
    private readonly MethodInfo _asInt;
    private readonly MethodInfo _setValue;

    private NativeIntVariableContract(Type variableType, MethodInfo asInt, MethodInfo setValue)
    {
        _variableType = variableType;
        _asInt = asInt;
        _setValue = setValue;
    }

    internal static int ResolutionCount { get; private set; }

    public static bool TryResolve(
        object variable,
        out NativeIntVariableContract contract,
        out string reason)
    {
        var variableType = variable.GetType();
        lock (CacheGate)
        {
            if (Cache.TryGetValue(variableType, out contract!))
            {
                reason = string.Empty;
                return true;
            }
        }

        if (!string.Equals(variableType.Name, "IntVariable", StringComparison.Ordinal))
        {
            contract = null!;
            reason = $"unexpected global multi-buy variable type {variableType.FullName}";
            return false;
        }

        var asInt = variableType.GetMethod(
            "AsInt",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        var setValue = variableType.GetMethod(
            "SetValue",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(int) },
            null);
        if (asInt?.ReturnType != typeof(int) || setValue?.ReturnType != typeof(void))
        {
            contract = null!;
            reason = "global multi-buy requires exact IntVariable.AsInt() and SetValue(int) contracts";
            return false;
        }

        contract = new NativeIntVariableContract(variableType, asInt, setValue);
        lock (CacheGate)
        {
            Cache[variableType] = contract;
            ResolutionCount++;
        }

        reason = string.Empty;
        return true;
    }

    public bool TryRead(object variable, out int value, out string reason)
    {
        if (variable.GetType() != _variableType)
        {
            value = 0;
            reason = "global multi-buy variable runtime type changed";
            return false;
        }

        try
        {
            if (_asInt.Invoke(variable, Array.Empty<object>()) is int current)
            {
                value = current;
                reason = string.Empty;
                return true;
            }
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            value = 0;
            reason = DescribeException(ex);
            return false;
        }

        value = 0;
        reason = "global multi-buy AsInt() did not return Int32";
        return false;
    }

    public bool TrySet(object variable, int value, out string reason)
    {
        if (variable.GetType() != _variableType)
        {
            reason = "global multi-buy variable runtime type changed";
            return false;
        }

        try
        {
            _setValue.Invoke(variable, new object[] { value });
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = DescribeException(ex);
            return false;
        }
    }

    internal static void ResetForTests()
    {
        lock (CacheGate)
        {
            Cache.Clear();
            ResolutionCount = 0;
        }
    }

    private static string DescribeException(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null } target
            ? target.InnerException.Message
            : exception.Message;
    }
}

internal static class NativeGlobalVariableAccess
{
    private static readonly object ContractGate = new object();
    private static MethodInfo? _getMultiBuy;

    internal static int ResolutionCount { get; private set; }

    public static bool TryGetMultiBuy(
        out object variable,
        out NativeIntVariableContract contract,
        out string reason)
    {
        variable = null!;
        contract = null!;
        MethodInfo? getMultiBuy;
        lock (ContractGate)
        {
            getMultiBuy = _getMultiBuy;
        }

        if (getMultiBuy is null)
        {
            var globals = ReflectionUtil.FindLoadedType("GlobalVariables");
            var resolved = globals?.GetMethod(
                "GetMultiBuy",
                BindingFlags.Static | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (resolved is null ||
                !string.Equals(resolved.ReturnType.Name, "IntVariable", StringComparison.Ordinal))
            {
                reason = "GlobalVariables.GetMultiBuy() -> IntVariable contract unavailable";
                return false;
            }

            lock (ContractGate)
            {
                _getMultiBuy ??= resolved;
                getMultiBuy = _getMultiBuy;
                ResolutionCount++;
            }
        }

        try
        {
            variable = getMultiBuy.Invoke(null, Array.Empty<object>())!;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            reason = ex is TargetInvocationException { InnerException: not null } target
                ? target.InnerException.Message
                : ex.Message;
            return false;
        }

        if (variable is null)
        {
            reason = "global multi-buy value unavailable";
            return false;
        }

        if (variable.GetType() != getMultiBuy.ReturnType)
        {
            reason = $"global multi-buy runtime type {variable.GetType().FullName} did not match " +
                     $"{getMultiBuy.ReturnType.FullName}";
            return false;
        }

        return NativeIntVariableContract.TryResolve(variable, out contract, out reason);
    }

    internal static void ResetForTests()
    {
        lock (ContractGate)
        {
            _getMultiBuy = null;
            ResolutionCount = 0;
        }
    }
}

internal sealed class NativeMultiBuyScope : IDisposable
{
    private static readonly DecisionLogGate FailureLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private static readonly System.Diagnostics.Stopwatch Lifetime = System.Diagnostics.Stopwatch.StartNew();
    private readonly object _variable;
    private readonly NativeIntVariableContract _contract;
    private readonly int _originalValue;
    private static bool _mutationQuarantined;
    private static string _quarantineReason = string.Empty;
    private bool _disposed;

    private NativeMultiBuyScope(
        object variable,
        NativeIntVariableContract contract,
        int originalValue)
    {
        _variable = variable;
        _contract = contract;
        _originalValue = originalValue;
    }

    internal static int GlobalContractResolutionCount => NativeGlobalVariableAccess.ResolutionCount;

    internal static int VariableContractResolutionCount => NativeIntVariableContract.ResolutionCount;

    public static bool TryEnterOne(out NativeMultiBuyScope scope, out string reason) =>
        TryEnter(1, out scope, out reason);

    /// <summary>
    /// Pins the global multi-buy multiplier to <paramref name="target"/> for the lifetime of the
    /// returned scope, restoring the operator's value on <see cref="Dispose"/>. A native upgrade
    /// <c>Purchase()</c> honours this multiplier, so a bulk purchase of N levels is a single call
    /// made under <c>target = N</c>; a single-level purchase pins it to 1.
    /// </summary>
    public static bool TryEnter(int target, out NativeMultiBuyScope scope, out string reason)
    {
        scope = null!;
        reason = string.Empty;
        if (target < 1)
        {
            reason = $"global multi-buy target must be at least 1 but was {target}";
            return false;
        }

        if (_mutationQuarantined)
        {
            reason = $"global multi-buy mutation is quarantined: {_quarantineReason}";
            return false;
        }

        if (!NativeGlobalVariableAccess.TryGetMultiBuy(out var variable, out var contract, out reason) ||
            !contract.TryRead(variable, out var originalValue, out reason))
        {
            return false;
        }

        if (!contract.TrySet(variable, target, out var setterFailure))
        {
            var restored = TryRestore(variable, contract, originalValue, out var restorationDetail);
            reason = $"global multi-buy SetValue({target}) failed: {setterFailure}; {restorationDetail}";
            if (!restored)
            {
                Quarantine(reason);
            }
            else
            {
                LogFailure(reason);
            }

            return false;
        }

        if (!contract.TryRead(variable, out var enteredValue, out _) || enteredValue != target)
        {
            var restored = TryRestore(variable, contract, originalValue, out var restorationDetail);
            reason = $"global multi-buy SetValue({target}) could not be verified; {restorationDetail}";
            if (!restored)
            {
                Quarantine(reason);
            }
            else
            {
                LogFailure(reason);
            }

            return false;
        }

        scope = new NativeMultiBuyScope(variable, contract, originalValue);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!TryRestore(_variable, _contract, _originalValue, out var restorationDetail))
        {
            Quarantine($"global multi-buy cleanup failed: {restorationDetail}");
        }
        else if (restorationDetail.Contains("threw", StringComparison.OrdinalIgnoreCase))
        {
            LogFailure($"global multi-buy cleanup recovered after an exception: {restorationDetail}");
        }
    }

    internal static bool IsMutationQuarantined => _mutationQuarantined;

    internal static bool TryGetMutationQuarantine(out string reason)
    {
        reason = _quarantineReason;
        return _mutationQuarantined;
    }

    internal static void ResetQuarantineForTests()
    {
        _mutationQuarantined = false;
        _quarantineReason = string.Empty;
        NativeGlobalVariableAccess.ResetForTests();
        NativeIntVariableContract.ResetForTests();
    }

    private static bool TryRestore(
        object variable,
        NativeIntVariableContract contract,
        int originalValue,
        out string detail)
    {
        string? setterFailure = null;
        if (!contract.TrySet(variable, originalValue, out var failure))
        {
            setterFailure = failure;
        }

        if (contract.TryRead(variable, out var restoredValue, out _) && restoredValue == originalValue)
        {
            detail = setterFailure is null
                ? $"restoration to {originalValue} verified"
                : $"restoration setter threw ({setterFailure}) but value {originalValue} was verified";
            return true;
        }

        detail = setterFailure is null
            ? $"restoration to {originalValue} could not be verified"
            : $"restoration setter threw ({setterFailure}) and value {originalValue} could not be verified";
        return false;
    }

    private static void Quarantine(string reason)
    {
        _mutationQuarantined = true;
        _quarantineReason = reason;
    }

    private static void LogFailure(string message)
    {
        if (FailureLogGate.ShouldLog("native-multi-buy-failure", Lifetime.Elapsed))
        {
            Plugin.Log?.LogAutomataError(message);
        }
    }
}
