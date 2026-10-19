using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoBuyFeatureStatus
{
    public AutoBuyFeatureStatus(
        FeatureStatusState state,
        FeatureStatusReasonCode reason,
        string summary)
    {
        State = state;
        Reason = reason;
        Summary = summary;
    }

    public FeatureStatusState State { get; }
    public FeatureStatusReasonCode Reason { get; }
    public string Summary { get; }
}

internal enum AutoBuyDecisionBlockReason
{
    None = 0,
    OwningViewUnavailable,
    OwningViewRelationMissing,
    OwningViewRelationUnreadable,
    OwningViewRelationContradictory,
    MixedPurchaseViewRelations,
}

internal static class AutoBuyFeatureStatusProjector
{
    /// <summary>What the running Auto Buy service reports, independent of saved intent.</summary>
    public static AutoBuyFeatureStatus Project(
        bool emergencyDisabled,
        AutoBuyCandidateKinds owned,
        bool cycleObserved,
        AutoBuyDecisionBlockReason decisionBlock = AutoBuyDecisionBlockReason.None,
        string invariantViolation = "",
        ServiceRetryGateSnapshot? retries = null,
        MonotonicTimestamp now = default)
    {
        if (emergencyDisabled)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Emergency disable blocks Auto Buy.");
        }

        if (invariantViolation.Length != 0)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.InvariantViolation,
                invariantViolation);
        }

        if (owned == AutoBuyCandidateKinds.None)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Auto Buy purchase action-family ownership is unavailable.");
        }

        if (owned != AutoBuyCandidateKinds.All)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "One selected Auto Buy purchase kind is owned by another plugin.");
        }

        if (!cycleObserved)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.Initializing,
                "Auto Buy is waiting for its first evaluation.");
        }


        switch (decisionBlock)
        {
            case AutoBuyDecisionBlockReason.OwningViewUnavailable:
                return new AutoBuyFeatureStatus(
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.ProgressionLocked,
                    "All captured Auto Buy candidates have zero currently-visible authored routes.");
            case AutoBuyDecisionBlockReason.OwningViewRelationMissing:
                return new AutoBuyFeatureStatus(
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.ContractUnavailable,
                    "All captured Auto Buy candidates have zero authored purchase-view routes.");
            case AutoBuyDecisionBlockReason.OwningViewRelationUnreadable:
                return new AutoBuyFeatureStatus(
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.EvidenceUnavailable,
                    "Every captured Auto Buy candidate was excluded by unreadable purchase-view evidence.");
            case AutoBuyDecisionBlockReason.OwningViewRelationContradictory:
                return new AutoBuyFeatureStatus(
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.ContractMismatch,
                    "Every captured Auto Buy candidate was excluded by contradictory purchase-view evidence.");
            case AutoBuyDecisionBlockReason.MixedPurchaseViewRelations:
                return new AutoBuyFeatureStatus(
                    FeatureStatusState.Degraded,
                    FeatureStatusReasonCode.EvidenceUnavailable,
                    "Every captured Auto Buy candidate was excluded by purchase-view evidence, " +
                    "with more than one relation reason.");
        }

        if (retries is not null && retries.TryGetLatestAnnounced(now, out var retry))
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.ContractMismatch,
                $"Auto Buy is backing off {EntityIdentityFormatter.Format(retry.Target.Id)} after " +
                $"{retry.ConsecutiveFailures} refused purchase(s) and retries it when due: {retry.Reason}");
        }

        return new AutoBuyFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            string.Empty);
    }
}
