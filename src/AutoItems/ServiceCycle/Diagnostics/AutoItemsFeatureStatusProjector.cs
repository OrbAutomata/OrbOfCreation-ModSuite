using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoItemsFeatureStatus
{
    internal AutoItemsFeatureStatus(
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

internal static class AutoItemsFeatureStatusProjector
{
    internal static AutoItemsFeatureStatus Project(
        bool emergencyDisabled,
        bool owned,
        string ownershipReason,
        bool bindingsAvailable,
        string bindingFailure,
        bool cycleObserved,
        AutoItemsDecisionKind decisionKind,
        AutoItemsActionHealth health,
        ServiceRetryGateSnapshot retries)
    {
        if (emergencyDisabled)
            return Blocked(
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        if (!owned)
            return Blocked(
                FeatureStatusReasonCode.ActionFamilyConflict,
                string.IsNullOrWhiteSpace(ownershipReason)
                    ? "Auto Items does not own ConsumableUse and NativeMultiBuyOverride."
                    : ownershipReason);
        if (!bindingsAvailable)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable,
                string.IsNullOrWhiteSpace(bindingFailure)
                    ? "The lifecycle-scoped Auto Items native binding set is unavailable."
                    : bindingFailure);
        if (health.HasFailure)
            return FromActionFailure(health.Code, health.Reason);
        if (retries.Count != 0)
            return BackingOff(retries.Latest);
        if (!cycleObserved)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Items is waiting for its first world publication.");

        return new AutoItemsFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            decisionKind switch
            {
                AutoItemsDecisionKind.Relic => "Auto Items planned a Relic from the latest world.",
                AutoItemsDecisionKind.Scroll => "Auto Items planned a Scroll from the latest world.",
                AutoItemsDecisionKind.TemporaryItem =>
                    "Auto Items planned one exact allowlisted temporary item from the latest world.",
                AutoItemsDecisionKind.AwaitingTemporaryActivation =>
                    "Auto Items is waiting for one submitted temporary usage to engage.",
                AutoItemsDecisionKind.TemporaryEffectActive =>
                    "Auto Items observed a pending or active temporary usage and is excluding every consumable use.",
                _ => "Auto Items is active and waiting for an eligible Scroll, Relic, or exact allowlisted temporary item.",
            });
    }

    private static AutoItemsFeatureStatus FromActionFailure(
        ServiceActionResultCode code,
        string reason)
    {
        var summary = string.IsNullOrWhiteSpace(reason)
            ? "Auto Items failed without a reason."
            : reason;
        if (code == AutoItemsActionResultCodes.ItemUnavailable)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.RegistryNotReady,
                summary);
        if (code == AutoItemsActionResultCodes.FamilyChanged)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.IdentityMismatch,
                summary);
        if (code == AutoItemsActionResultCodes.NativeBusy ||
            code == AutoItemsActionResultCodes.CanFireRefused)
            return Blocked(FeatureStatusReasonCode.NativeBusy, summary);
        if (code == AutoItemsActionResultCodes.TargetingInProgress)
            return Blocked(FeatureStatusReasonCode.TargetingInProgress, summary);
        if (code == AutoItemsActionResultCodes.MutationPermitUnavailable)
            return Blocked(FeatureStatusReasonCode.ActionFamilyConflict, summary);
        return Blocked(FeatureStatusReasonCode.TemporarySafetyBlock, summary);
    }

    private static AutoItemsFeatureStatus BackingOff(in ServiceRetryEntry entry) =>
        new(
            FeatureStatusState.Degraded,
            FeatureStatusReasonCode.NativeMutationFailed,
            $"Auto Items is backing off {EntityIdentityFormatter.Format(entry.Target.Id)} after " +
            $"{entry.ConsecutiveFailures} failed attempt(s) and retries it on a later world: {entry.Reason}");

    private static AutoItemsFeatureStatus Blocked(
        FeatureStatusReasonCode reason,
        string summary) =>
        new(FeatureStatusState.TemporarilyBlocked, reason, summary);
}
