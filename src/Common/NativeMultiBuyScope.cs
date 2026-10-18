using System;
using System.Diagnostics;

namespace OrbModding.Common;

/// <summary>
/// Complete binding set for the global multi-buy multiplier: the accessor the game reads it
/// through, and the integer variable's read and write.
/// </summary>
internal sealed class NativeMultiBuyBindings
{
    internal static readonly string[] ContractIds =
    {
        "loadout.global-variables.type-action",
        "challenge.int-variable.type-action",
        "global-variables.get-multi-buy",
        "int-variable.as-int",
        "int-variable.set-value",
    };

    private NativeMultiBuyBindings(
        Func<object?> getMultiBuy,
        Func<object, int> asInt,
        Action<object, int> setValue)
    {
        GetMultiBuy = getMultiBuy;
        AsInt = asInt;
        SetValue = setValue;
    }

    internal Func<object?> GetMultiBuy { get; }
    internal Func<object, int> AsInt { get; }
    internal Action<object, int> SetValue { get; }

    internal static bool TryCreate(out NativeMultiBuyBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("multi-buy", ContractIds, resolveType, includeContract, set =>
        {
            var globals = set.Type("loadout.global-variables.type-action", "GlobalVariables");
            var integer = set.Type("challenge.int-variable.type-action", "IntVariable");
            return new NativeMultiBuyBindings(
                set.StaticCall<Func<object?>>("global-variables.get-multi-buy", globals, "GetMultiBuy", integer),
                set.Call<Func<object, int>>("int-variable.as-int", integer, "AsInt", typeof(int)),
                set.Call<Action<object, int>>(
                    "int-variable.set-value", integer, "SetValue", typeof(void), typeof(int)));
        }, out bindings, out reason);
}

/// <summary>
/// Pins the global multi-buy multiplier for one native call and puts the player's value back.
/// </summary>
/// <remarks>
/// The set binds once per process: it holds types and compiled delegates only. The multiplier
/// variable itself is fetched from the game on every entry and held only for the one call the scope
/// wraps, so no native instance outlives a lifecycle. Whenever the game may have accepted a write
/// and the player's value cannot be shown restored, every further entry is refused until restart.
/// </remarks>
internal sealed class NativeMultiBuyScope : IDisposable
{
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromSeconds(30);
    private static readonly Stopwatch Lifetime = Stopwatch.StartNew();
    private static Action<string>? _failureLog;
    private static TimeSpan? _failureLoggedAt;
    private static Func<string, Type?>? _resolveType;
    private static Func<string, bool>? _includeContract;
    private static NativeMultiBuyBindings? _bindings;
    private static string? _bindReason;
    private static bool _mutationQuarantined;
    private static string _quarantineReason = string.Empty;
    private readonly NativeMultiBuyBindings _set;
    private readonly object _variable;
    private readonly int _originalValue;
    private bool _disposed;

    private NativeMultiBuyScope(NativeMultiBuyBindings set, object variable, int originalValue)
    {
        _set = set;
        _variable = variable;
        _originalValue = originalValue;
    }

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

        if (!TryBind(out var set, out reason) ||
            !TryGetVariable(set, out var variable, out reason) ||
            !TryRead(set, variable, out var originalValue, out reason))
        {
            return false;
        }

        var context = $"global multi-buy SetValue({target})";
        var settled = false;
        try
        {
            if (!TrySet(set, variable, target, out var setterFailure))
            {
                settled = true;
                return Refuse(set, variable, originalValue, $"{context} failed: {setterFailure}", out reason);
            }

            if (!TryRead(set, variable, out var enteredValue, out _) || enteredValue != target)
            {
                settled = true;
                return Refuse(set, variable, originalValue, $"{context} could not be verified", out reason);
            }

            settled = true;
            scope = new NativeMultiBuyScope(set, variable, originalValue);
            return true;
        }
        finally
        {
            if (!settled) TryRestore(set, variable, originalValue, $"{context} threw", out _);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (TryRestore(_set, _variable, _originalValue, "global multi-buy cleanup failed", out var detail) &&
            detail.Contains("threw", StringComparison.OrdinalIgnoreCase))
        {
            LogFailure($"global multi-buy cleanup recovered after an exception: {detail}");
        }
    }

    /// <summary>
    /// Names where a refused entry or a recovered cleanup is reported, at most once every thirty
    /// seconds.
    /// </summary>
    internal static void ConfigureFailureLog(Action<string>? log)
    {
        _failureLog = log;
        _failureLoggedAt = null;
    }

    internal static bool IsMutationQuarantined => _mutationQuarantined;

    internal static void ResetQuarantineForTests(
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _mutationQuarantined = false;
        _quarantineReason = string.Empty;
        _resolveType = resolveType;
        _includeContract = includeContract;
        _bindings = null;
        _bindReason = null;
    }

    private static bool TryBind(out NativeMultiBuyBindings set, out string reason)
    {
        if (_bindReason is null)
        {
            NativeMultiBuyBindings.TryCreate(out _bindings, out var bindReason, _resolveType, _includeContract);
            _bindReason = bindReason;
        }

        set = _bindings!;
        reason = _bindReason;
        return _bindings is not null;
    }

    private static bool TryGetVariable(NativeMultiBuyBindings set, out object variable, out string reason)
    {
        try
        {
            variable = set.GetMultiBuy()!;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            variable = null!;
            reason = ex.Message;
            return false;
        }

        reason = variable is null ? "global multi-buy value unavailable" : string.Empty;
        return variable is not null;
    }

    private static bool TryRead(NativeMultiBuyBindings set, object variable, out int value, out string reason)
    {
        try
        {
            value = set.AsInt(variable);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            value = 0;
            reason = ex.Message;
            return false;
        }
    }

    private static bool TrySet(NativeMultiBuyBindings set, object variable, int value, out string reason)
    {
        try
        {
            set.SetValue(variable, value);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            reason = ex.Message;
            return false;
        }
    }

    private static bool Refuse(
        NativeMultiBuyBindings set,
        object variable,
        int originalValue,
        string failure,
        out string reason)
    {
        var restored = TryRestore(set, variable, originalValue, failure, out var detail);
        reason = $"{failure}; {detail}";
        if (restored) LogFailure(reason);
        return false;
    }

    /// <summary>
    /// Puts <paramref name="originalValue"/> back and reads it again; anything short of that read,
    /// including a throw this does not expect, leaves further mutation quarantined.
    /// </summary>
    private static bool TryRestore(
        NativeMultiBuyBindings set,
        object variable,
        int originalValue,
        string context,
        out string detail)
    {
        detail = $"restoration to {originalValue} did not complete";
        var verified = false;
        try
        {
            var setterFailed = !TrySet(set, variable, originalValue, out var setterFailure);
            verified = TryRead(set, variable, out var restoredValue, out _) && restoredValue == originalValue;
            detail = (setterFailed, verified) switch
            {
                (false, true) => $"restoration to {originalValue} verified",
                (true, true) => $"restoration setter threw ({setterFailure}) but value {originalValue} was verified",
                (false, false) => $"restoration to {originalValue} could not be verified",
                (true, false) =>
                    $"restoration setter threw ({setterFailure}) and value {originalValue} could not be verified",
            };
            return verified;
        }
        finally
        {
            if (!verified)
            {
                _mutationQuarantined = true;
                _quarantineReason = $"{context}; {detail}";
            }
        }
    }

    private static void LogFailure(string message)
    {
        var now = Lifetime.Elapsed;
        if (_failureLoggedAt is { } last && now - last < FailureLogInterval) return;
        _failureLoggedAt = now;
        _failureLog?.Invoke(message);
    }
}
