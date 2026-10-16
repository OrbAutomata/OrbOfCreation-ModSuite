using System;
using OrbModding.Common;

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
        Guid quarantinedTemporaryItem,
        AutoItemsTemporaryQuarantineCause temporaryQuarantineCause,
        AutoItemsActionHealth health)
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
        if (temporaryQuarantineCause != AutoItemsTemporaryQuarantineCause.None)
            return TemporaryQuarantine(
                quarantinedTemporaryItem,
                temporaryQuarantineCause);
        if (health.HasFailure)
            return FromActionFailure(health.Preflight, health.Reason);
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

    private static AutoItemsFeatureStatus TemporaryQuarantine(
        Guid itemId,
        AutoItemsTemporaryQuarantineCause cause)
    {
        var evidence = cause switch
        {
            AutoItemsTemporaryQuarantineCause.MultipleUsages =>
                "more than one native usage appeared",
            AutoItemsTemporaryQuarantineCause.PrematureExpiry =>
                "its usage expired before the activation proof completed",
            AutoItemsTemporaryQuarantineCause.MissingEngagementEvidence =>
                "the usage disappeared without any observed engagement",
            _ => "its activation evidence was invalid",
        };
        return new AutoItemsFeatureStatus(
            FeatureStatusState.Faulted,
            FeatureStatusReasonCode.MutationQuarantined,
            $"Temporary item {EntityIdentityFormatter.Format(itemId)} is quarantined for this lifecycle because {evidence}.");
    }

    private static AutoItemsFeatureStatus FromActionFailure(
        AutoItemsPreflight preflight,
        string reason)
    {
        var summary = string.IsNullOrWhiteSpace(reason)
            ? $"Auto Items failed at {preflight}."
            : reason;
        return preflight switch
        {
            AutoItemsPreflight.ContractUnavailable =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.ContractUnavailable,
                    FeatureStatusReasonCode.ContractUnavailable,
                    summary),
            AutoItemsPreflight.Quarantined =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.Faulted,
                    FeatureStatusReasonCode.MutationQuarantined,
                    summary),
            AutoItemsPreflight.ItemUnavailable =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.NotReady,
                    FeatureStatusReasonCode.RegistryNotReady,
                    summary),
            AutoItemsPreflight.FamilyChanged =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.ContractUnavailable,
                    FeatureStatusReasonCode.IdentityMismatch,
                    summary),
            AutoItemsPreflight.MultiBuyUnavailable =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.Faulted,
                    FeatureStatusReasonCode.ContractUnavailable,
                    summary),
            AutoItemsPreflight.NativeBusy or AutoItemsPreflight.CanFireRefused =>
                Blocked(FeatureStatusReasonCode.NativeBusy, summary),
            AutoItemsPreflight.TargetingInProgress =>
                Blocked(FeatureStatusReasonCode.TargetingInProgress, summary),
            AutoItemsPreflight.MutationPermitUnavailable =>
                Blocked(FeatureStatusReasonCode.ActionFamilyConflict, summary),
            _ => Blocked(FeatureStatusReasonCode.TemporarySafetyBlock, summary),
        };
    }

    private static AutoItemsFeatureStatus Blocked(
        FeatureStatusReasonCode reason,
        string summary) =>
        new(FeatureStatusState.TemporarilyBlocked, reason, summary);
}
