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
    private readonly Func<TAction, string> _describe;
    private readonly Action<string> _warn;
    private ServiceActionResultCode? _loggedFaultCode;
    private string _loggedFaultReason = string.Empty;

    public AutomationCycleGuard(Func<TAction, string> describe, Action<string> warn)
    {
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
            _warn($"{_describe(action)}: the game's lifecycle epoch could not be read ({ex.GetBaseException().Message}).");
            refusal = ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
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
    /// An expected throw from the submission: logged with its message and answered without it, so
    /// no exception text reaches the wire. <see cref="Finish"/> does not log it a second time.
    /// </summary>
    public NativeActionOutcome Fault(Exception exception, in TAction action)
    {
        _warn($"{_describe(action)}: adapter fault ({exception.GetBaseException().Message}).");
        _loggedFaultCode = CommonActionResultCodes.AdapterFault;
        _loggedFaultReason = string.Empty;
        return NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, string.Empty);
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
