using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

public static class AutomationCycleGuard
{
    /// <summary>A lease read the game cannot answer is a lease the suite does not hold.</summary>
    public static bool Owns(Func<bool> readOwnership)
    {
        try
        {
            return readOwnership();
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return false;
        }
    }
}

/// <summary>
/// The admission and fault handling every automation cycle adapter shares: the action-family
/// lease, the lifecycle epoch, an expected throw from the submission, and the one log line a fault
/// earns. Each feature keeps its own policy gate, submission call and sentences.
/// </summary>
/// <remarks>
/// Only faults reach the log. Refusals are frequent and ordinary, so their reasons travel on the
/// result to the wire and the health surface instead. A fault repeating with the same code and
/// reason is logged once until an action lands.
/// </remarks>
public sealed class AutomationCycleGuard<TAction>
{
    private readonly string _feature;
    private readonly Func<TAction, string> _describe;
    private readonly Action<string> _warn;
    private ServiceActionResultCode? _loggedFaultCode;
    private string _loggedFaultReason = string.Empty;

    public AutomationCycleGuard(string feature, Func<TAction, string> describe, Action<string> warn)
    {
        _feature = feature ?? throw new ArgumentNullException(nameof(feature));
        _describe = describe ?? throw new ArgumentNullException(nameof(describe));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public bool Owns(Func<bool> readOwnership) => AutomationCycleGuard.Owns(readOwnership);

    /// <summary>
    /// Whether the game is still the run the action was planned for. Zero matches nothing, so an
    /// unstamped plan refuses; an epoch the game cannot answer is a fault, not a replaced run.
    /// </summary>
    public bool TryAdmitEpoch(
        Func<long> readLifecycleEpoch,
        long plannedEpoch,
        in TAction action,
        out ServiceActionResult refusal)
    {
        long current;
        try
        {
            current = readLifecycleEpoch();
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            var reason = Record(ex, "the game's lifecycle epoch could not be read", in action);
            refusal = ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, reason);
            return false;
        }

        if (current > 0 && current == plannedEpoch)
        {
            refusal = default;
            return true;
        }
        refusal = ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);
        return false;
    }

    /// <summary>
    /// An expected throw from the submission. The whole exception goes to the suite log under a
    /// reference, and the answer names that reference instead of the exception text, so the cause
    /// stays findable without reaching the wire. <see cref="Finish"/> does not log it a second time.
    /// </summary>
    public NativeActionOutcome Fault(Exception exception, in TAction action)
    {
        var reason = Record(exception, "adapter fault", in action);
        _loggedFaultCode = CommonActionResultCodes.AdapterFault;
        _loggedFaultReason = reason;
        return NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, reason);
    }

    private string Record(Exception exception, string what, in TAction action)
    {
        var reference = GameActionFaultLog.Record(exception, _feature);
        _warn($"{_describe(action)}: {what} ({exception.GetBaseException().Message}).{reference}");
        return _feature + " faulted at the native boundary." + reference;
    }

    public ServiceActionResult Finish(in NativeActionOutcome outcome, in TAction action)
    {
        if (outcome.Verified)
        {
            _loggedFaultCode = null;
            _loggedFaultReason = string.Empty;
        }
        else if (outcome.IsFault &&
            !(_loggedFaultCode == outcome.Code &&
                string.Equals(_loggedFaultReason, outcome.Reason, StringComparison.Ordinal)))
        {
            _loggedFaultCode = outcome.Code;
            _loggedFaultReason = outcome.Reason;
            _warn(outcome.Reason.Length == 0
                ? $"{_describe(action)}."
                : $"{_describe(action)}: {outcome.Reason}");
        }
        return outcome.ToResult();
    }
}
