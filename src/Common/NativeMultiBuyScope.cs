using System;
using System.Linq;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

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
/// wraps, so no native instance outlives a lifecycle. When the player's value cannot be shown put
/// back, the restore stays outstanding: every entry is refused, and the first entry after each
/// doubling wait reads the setting fresh and puts the remembered value back. Only a verified
/// read-back ends it. A setting that no longer reads as the suite left it was changed by the player
/// or a save, and that value stands.
/// </remarks>
internal sealed class NativeMultiBuyScope : IDisposable
{
    private const string Screen = "the game's multi-buy setting";
    private static readonly MonotonicDuration FailureLogInterval =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(30));
    private static readonly ServiceFaultRecoveryPolicy RestoreRetry = new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1)));
    private static IMonotonicClock _clock = new StopwatchMonotonicClock();
    private static Action<string>? _failureLog;
    private static MonotonicTimestamp? _failureLoggedAt;
    private static Func<string, Type?>? _resolveType;
    private static Func<string, bool>? _includeContract;
    private static NativeMultiBuyBindings? _bindings;
    private static string? _bindReason;
    private static OutstandingRestore? _outstandingRestore;
    private readonly NativeMultiBuyBindings _set;
    private readonly object _variable;
    private readonly int _originalValue;
    private readonly int _target;
    private bool _disposed;

    private NativeMultiBuyScope(NativeMultiBuyBindings set, object variable, int originalValue, int target)
    {
        _set = set;
        _variable = variable;
        _originalValue = originalValue;
        _target = target;
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

        if (!TryBind(out var set, out reason) ||
            _outstandingRestore is { } outstanding && !TrySettle(set, outstanding, out reason))
        {
            return false;
        }

        if (!TryGetVariable(set, out var variable, out reason, out var fetchFailure))
        {
            LogFailure(reason, fetchFailure);
            return false;
        }

        if (!TryRead(set, variable, out var originalValue, out var readFailure))
        {
            reason = "the global multi-buy value could not be read";
            LogFailure(reason, readFailure);
            return false;
        }

        var context = $"global multi-buy SetValue({target})";
        var settled = false;
        try
        {
            if (!TrySet(set, variable, target, out var setterFailure))
            {
                settled = true;
                return Refuse(set, variable, originalValue, target, $"{context} threw", setterFailure, out reason);
            }

            if (!TryRead(set, variable, out var enteredValue, out readFailure) || enteredValue != target)
            {
                settled = true;
                return Refuse(set, variable, originalValue, target, $"{context} could not be verified", readFailure,
                    out reason);
            }

            settled = true;
            scope = new NativeMultiBuyScope(set, variable, originalValue, target);
            return true;
        }
        finally
        {
            if (!settled &&
                TryRestore(set, variable, originalValue, target, $"{context} threw", null, out var detail,
                    out var recoveredFrom) && recoveredFrom is not null)
            {
                LogFailure($"{context} threw; {detail}", null, recoveredFrom);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (TryRestore(_set, _variable, _originalValue, _target, "global multi-buy cleanup failed", null,
                out var detail, out var recoveredFrom) && recoveredFrom is not null)
        {
            LogFailure($"global multi-buy cleanup recovered after an exception: {detail}", null, recoveredFrom);
        }
    }

    /// <summary>
    /// Names where a failed restore is reported on every attempt, and a refused entry or a recovered
    /// cleanup at most once every thirty seconds.
    /// </summary>
    internal static void ConfigureFailureLog(Action<string>? log)
    {
        _failureLog = log;
        _failureLoggedAt = null;
    }

    internal static void ResetForTests(
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        IMonotonicClock? clock = null)
    {
        _outstandingRestore = null;
        _failureLoggedAt = null;
        _clock = clock ?? new VirtualMonotonicClock();
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

    /// <summary>
    /// Refuses while the outstanding restore waits; once its retry is due, reads the setting fresh
    /// and puts the player's value back, and lets the entry through only on a verified read-back.
    /// </summary>
    private static bool TrySettle(NativeMultiBuyBindings set, OutstandingRestore outstanding, out string reason)
    {
        reason = outstanding.Reason;
        if (_clock.Now < outstanding.RetryAt) return false;

        object variable = null!;
        var current = 0;
        Exception? fetchFailure = null;
        Exception? readFailure = null;
        var read = false;
        try
        {
            read = TryGetVariable(set, out variable, out _, out fetchFailure) &&
                TryRead(set, variable, out current, out readFailure);
        }
        finally
        {
            if (!read)
                Outstanding(outstanding.PlayerValue, outstanding.Leftover,
                    "global multi-buy restoration retry could not read the setting", fetchFailure ?? readFailure);
        }

        if (!read)
        {
            reason = _outstandingRestore!.Reason;
            return false;
        }

        if (current != outstanding.Leftover)
        {
            _outstandingRestore = null;
            Warn(current == outstanding.PlayerValue
                ? $"global multi-buy reads {current} again; restoration to {current} verified"
                : $"global multi-buy reads {current}, not the {outstanding.Leftover} the suite left; " +
                  $"that value stands and restoration to {outstanding.PlayerValue} is dropped", null);
            return true;
        }

        if (!TryRestore(set, variable, outstanding.PlayerValue, outstanding.Leftover,
                "global multi-buy restoration retry failed", null, out var detail, out var recoveredFrom))
        {
            reason = _outstandingRestore!.Reason;
            return false;
        }

        Warn($"global multi-buy {detail} on attempt {outstanding.Attempts + 1}", null, recoveredFrom);
        return true;
    }

    private static bool TryGetVariable(
        NativeMultiBuyBindings set,
        out object variable,
        out string reason,
        out Exception? failure)
    {
        failure = null;
        try
        {
            variable = set.GetMultiBuy()!;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            variable = null!;
            failure = ex;
            reason = "the game did not hand out its global multi-buy value";
            return false;
        }

        reason = variable is null ? "global multi-buy value unavailable" : string.Empty;
        return variable is not null;
    }

    private static bool TryRead(NativeMultiBuyBindings set, object variable, out int value, out Exception? failure)
    {
        try
        {
            value = set.AsInt(variable);
            failure = null;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            value = 0;
            failure = ex;
            return false;
        }
    }

    private static bool TrySet(NativeMultiBuyBindings set, object variable, int value, out Exception? failure)
    {
        try
        {
            set.SetValue(variable, value);
            failure = null;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            failure = ex;
            return false;
        }
    }

    private static bool Refuse(
        NativeMultiBuyBindings set,
        object variable,
        int originalValue,
        int target,
        string failure,
        Exception? cause,
        out string reason)
    {
        var restored = TryRestore(set, variable, originalValue, target, failure, cause, out var detail,
            out var recoveredFrom);
        reason = $"{failure}; {detail}";
        if (restored) LogFailure(reason, cause, recoveredFrom);
        return false;
    }

    /// <summary>
    /// Puts <paramref name="playerValue"/> back and reads it again. A verified read-back ends any
    /// outstanding restore; anything short of it, including a throw this does not expect, leaves
    /// the restore outstanding until its next retry, logged under <paramref name="cause"/> first.
    /// </summary>
    private static bool TryRestore(
        NativeMultiBuyBindings set,
        object variable,
        int playerValue,
        int pinned,
        string context,
        Exception? cause,
        out string detail,
        out Exception? recoveredFrom)
    {
        detail = $"restoration to {playerValue} did not complete";
        recoveredFrom = null;
        var verified = false;
        int? readBack = null;
        Exception? setterFailure = null;
        Exception? readFailure = null;
        try
        {
            var setterFailed = !TrySet(set, variable, playerValue, out setterFailure);
            if (TryRead(set, variable, out var restoredValue, out readFailure)) readBack = restoredValue;
            verified = readBack == playerValue;
            detail = (setterFailed, verified) switch
            {
                (false, true) => $"restoration to {playerValue} verified",
                (true, true) => $"restoration setter threw but value {playerValue} was verified",
                (false, false) => $"restoration to {playerValue} could not be verified",
                (true, false) => $"restoration setter threw and value {playerValue} could not be verified",
            };
            if (verified) recoveredFrom = setterFailure;
            return verified;
        }
        finally
        {
            if (verified) _outstandingRestore = null;
            else
                Outstanding(playerValue, readBack ?? pinned, $"{context}; {detail}", cause, setterFailure,
                    readFailure);
        }
    }

    private static void Outstanding(
        int playerValue,
        int leftover,
        string failure,
        Exception? cause,
        Exception? setterFailure = null,
        Exception? readFailure = null)
    {
        var attempts = _outstandingRestore is { } previous ? previous.Attempts + 1 : 1;
        var wait = RestoreRetry.BackoffAfter(attempts);
        _outstandingRestore = new OutstandingRestore(
            playerValue,
            leftover,
            attempts,
            _clock.Now.AddSaturated(wait),
            $"global multi-buy restoration to {playerValue} is outstanding: {failure}");
        Warn($"{failure} (attempt {attempts}); the suite tries again in {wait.ToTimeSpan().TotalSeconds:0.#} s",
            cause, setterFailure, readFailure);
    }

    private static void LogFailure(string message, Exception? cause, Exception? restoreSetterFailure = null)
    {
        var now = _clock.Now;
        if (_failureLoggedAt is { } last && now - last < FailureLogInterval) return;
        _failureLoggedAt = now;
        Warn(message, cause, restoreSetterFailure);
    }

    /// <summary>
    /// Writes <paramref name="message"/>, naming one suite-log reference that holds whichever of
    /// the attempt's exceptions were thrown, the cause first.
    /// </summary>
    private static void Warn(
        string message,
        Exception? cause,
        Exception? restoreSetterFailure = null,
        Exception? readBackFailure = null)
    {
        var log = _failureLog;
        if (log is null) return;
        var detail = string.Join(Environment.NewLine, new[]
        {
            cause?.ToString(),
            restoreSetterFailure is null ? null : "Putting the value back then threw: " + restoreSetterFailure,
            readBackFailure is null ? null : "Reading it back then threw: " + readBackFailure,
        }.Where(part => part is not null));
        log(message + GameActionFaultLog.Sentence(GameActionFaultLog.RecordDetail(detail, Screen)));
    }

    private sealed class OutstandingRestore
    {
        internal OutstandingRestore(int playerValue, int leftover, int attempts, MonotonicTimestamp retryAt,
            string reason)
        {
            PlayerValue = playerValue;
            Leftover = leftover;
            Attempts = attempts;
            RetryAt = retryAt;
            Reason = reason;
        }

        internal int PlayerValue { get; }
        internal int Leftover { get; }
        internal int Attempts { get; }
        internal MonotonicTimestamp RetryAt { get; }
        internal string Reason { get; }
    }
}
