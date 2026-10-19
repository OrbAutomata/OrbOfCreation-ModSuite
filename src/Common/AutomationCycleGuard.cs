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
/// result to the wire and the health surface instead. A fault repeating on the same target with
/// the same code and sentence is warned about once until an action lands, whatever reference each
/// attempt's cause was logged under. An unreadable epoch is one fault of the game whichever target
/// met it, so it is warned about once across targets.
/// </remarks>
public sealed class AutomationCycleGuard<TAction>
{
    private const string EpochUnreadable = "the game's lifecycle epoch could not be read";
    private readonly string _feature;
    private readonly Func<TAction, string> _describe;
    private readonly Action<string> _warn;
    private readonly RepeatedFailure _warned = new();

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
            var reason = Record(ex, EpochUnreadable, EpochUnreadable, string.Empty, in action);
            refusal = ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, reason.Text);
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
        var reason = Record(exception, "adapter fault", FaultedSentence, _describe(action), in action);
        return NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, reason);
    }

    private string FaultedSentence => _feature + " faulted at the native boundary.";

    /// <param name="key">
    /// What makes a repeat of this fault: the sentence it answers with, or, for an unreadable epoch,
    /// which answers with that same sentence, what failed.
    /// </param>
    /// <param name="target">The target a repeat must share, or empty when any target repeats it.</param>
    private GameActionReason Record(Exception exception, string what, string key, string target, in TAction action)
    {
        var reason = GameActionFaultLog.Reason(FaultedSentence, exception, _feature);
        if (!_warned.IsRepeat(key, CommonActionResultCodes.AdapterFault, target))
            _warn($"{_describe(action)}: {what} ({exception.GetBaseException().Message})." +
                GameActionFaultLog.Sentence(reason.Reference));
        return reason;
    }

    public ServiceActionResult Finish(in NativeActionOutcome outcome, in TAction action)
    {
        if (outcome.Verified)
            _warned.Clear();
        else if (outcome.IsFault && !_warned.IsRepeat(in outcome, _describe(action)))
        {
            _warn(outcome.Reason.Length == 0
                ? $"{_describe(action)}."
                : $"{_describe(action)}: {outcome.Reason}");
        }
        return outcome.ToResult();
    }
}
