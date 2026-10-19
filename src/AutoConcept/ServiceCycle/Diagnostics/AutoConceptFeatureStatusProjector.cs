using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoConceptFeatureStatus
{
    internal AutoConceptFeatureStatus(
        FeatureStatusState state,
        FeatureStatusReasonCode reason,
        string summary)
    {
        State = state;
        Reason = reason;
        Summary = summary;
    }

    internal FeatureStatusState State { get; }
    internal FeatureStatusReasonCode Reason { get; }
    internal string Summary { get; }
}

internal static class AutoConceptFeatureStatusProjector
{
    public static AutoConceptFeatureStatus Project(
        bool emergencyDisabled,
        bool owned,
        bool cycleObserved,
        AutoConceptIdle idle = default,
        ServiceRetryEntry? backingOff = null,
        string contractMismatch = "")
    {
        var idleReason = idle.Reason;
        if (emergencyDisabled)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        if (contractMismatch.Length != 0)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.InvariantViolation,
                contractMismatch);
        if (!owned)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another automation owner holds the native concept-assignment action family.");
        if (backingOff is { } retry)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.NativeMutationFailed,
                $"Auto Concept is backing off {AutoConceptServicePolicies.Describe(retry.Target)} after " +
                $"{retry.ConsecutiveFailures} failed attempt(s) and retries it when due: {retry.Reason}");
        if (!cycleObserved)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Concept is waiting for its first evaluation.");
        if (idleReason == AutoConceptIdleReason.WaitingForTraining)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.NativeBusy,
                "Auto Concept is waiting for the active assignment to settle and complete its training period.");
        if (idleReason == AutoConceptIdleReason.NoUnlockedAssignableReplacement)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.Locked,
                FeatureStatusReasonCode.ProgressionLocked,
                "No other unlocked concept can be assigned.");
        if (idle.Move != AutoConceptIdleMove.None)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                idleReason == AutoConceptIdleReason.DrainUnknown
                    ? FeatureStatusReasonCode.EvidenceUnavailable
                    : FeatureStatusReasonCode.TemporarySafetyBlock,
                Move(in idle) + ", but " + Refusal(in idle) + ".");
        return new AutoConceptFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            string.Empty);
    }

    private static string Move(in AutoConceptIdle idle) => idle.Move switch
    {
        AutoConceptIdleMove.Slot => "Auto Concept would slot " + Name(idle.Recipe),
        AutoConceptIdleMove.Rotate =>
            "Auto Concept would rotate " + Name(idle.Displaced) + " out for " + Name(idle.Recipe),
        _ => "Auto Concept would add stacks to " + Name(idle.Recipe),
    };

    private static string Refusal(in AutoConceptIdle idle) => idle.Reason switch
    {
        AutoConceptIdleReason.ResourceAtZero => Name(idle.Resource) + " is at zero",
        AutoConceptIdleReason.RateReserve =>
            "its drain would leave " + Name(idle.Resource) +
            " less than AutoConcept/RateReservePercent of its income",
        _ => idle.Resource == Guid.Empty
            ? "its drain cannot be projected from the published world"
            : "its " + Name(idle.Resource) + " drain cannot be projected from the published world",
    };

    private static string Name(Guid id) => EntityIdentityFormatter.PlayerName(id);
}
