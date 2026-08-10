using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Actionable Auto Buy refusal and failure lines for the general BepInEx log.</summary>
internal static class AutoBuyPurchaseNarration
{
    /// <summary>
    /// The live queue room could not be read, so the reserve cannot be honoured and no purchase is
    /// submitted. A missing native surface is an actionable anomaly.
    /// </summary>
    public static string QueueRoomUnavailable(AutoBuyCandidateKind kind, Guid uuid) =>
        $"Auto Buy failed to purchase {kind} {EntityIdentityFormatter.Format(uuid)}: queue room unavailable.";

    /// <summary>
    /// Why a purchase found no captured purchase-screen entry to be admitted against.
    /// </summary>
    /// <remarks>
    /// All three numbers are load-bearing and none can be inferred from the others. A snapshot
    /// stamped at zero was never published under a lifecycle; one stamped at another epoch means the
    /// game moved on; one stamped at the asked epoch simply has no entry for this target, which is a
    /// different problem with a different fix. Diagnosing the outage that motivated this took
    /// crossing log timestamps against source, because the refusal named none of them.
    /// </remarks>
    public static string TopologyUncaptured(long stampedEpoch, long askedEpoch, int rows) =>
        stampedEpoch == askedEpoch
            ? $"This run's purchase-screen topology (epoch {stampedEpoch}, {rows} row(s)) holds no " +
              "entry for this target."
            : "The purchase-screen topology holds no admission evidence for this run: it is " +
              $"stamped at epoch {stampedEpoch} with {rows} row(s), and this purchase was planned " +
              $"at epoch {askedEpoch}.";

    /// <summary>
    /// Returns one warning for a refusal that requires attention, or null for successful and ordinary
    /// no-op outcomes already represented by the compact action journal.
    /// </summary>
    public static string? DescribeWarning(
        AutoBuyCandidateKind kind,
        Guid uuid,
        in AutoBuyPurchaseSubmission submission)
    {
        if (submission.Verified ||
            submission.Preflight == AutoBuyPurchasePreflight.NotAdmissible ||
            submission.Preflight == AutoBuyPurchasePreflight.SingleBuyUnavailable)
        {
            return null;
        }

        var candidate = $"{kind} {EntityIdentityFormatter.Format(uuid)}";
        return submission.Preflight switch
        {
            AutoBuyPurchasePreflight.CandidateUnavailable =>
                $"Auto Buy failed to purchase {candidate}: candidate could not be resolved.",
            AutoBuyPurchasePreflight.AffordabilityUnavailable =>
                $"Auto Buy failed to purchase {candidate}: live affordability could not be read.",
            AutoBuyPurchasePreflight.OwningViewUnavailable =>
                Refusal(candidate, "owning view unavailable"),
            AutoBuyPurchasePreflight.OwningViewRelationMissing =>
                Refusal(candidate, "owning view relation missing"),
            AutoBuyPurchasePreflight.OwningViewRelationUnreadable =>
                Refusal(candidate, "owning view relation unreadable"),
            AutoBuyPurchasePreflight.OwningViewTopologyUnbound =>
                Refusal(candidate, "the owning view topology contract never bound"),
            AutoBuyPurchasePreflight.OwningViewTopologyUncaptured =>
                $"Auto Buy failed to purchase {candidate}: {submission.Reason}",
            AutoBuyPurchasePreflight.OwningViewRelationStatusUnmodeled =>
                Refusal(candidate, "the owning view relation carries an unmodelled status"),
            AutoBuyPurchasePreflight.OwningViewAvailabilityUnreadable =>
                Refusal(candidate, "live owning view availability could not be read"),
            AutoBuyPurchasePreflight.OwningViewRelationContradictory =>
                Refusal(candidate, "owning view relation contradictory"),
            AutoBuyPurchasePreflight.StructureUnavailable =>
                Refusal(candidate, "structure unavailable"),
            AutoBuyPurchasePreflight.DestinationCapacityFull =>
                Refusal(candidate, "destination capacity full"),
            AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable =>
                Refusal(candidate, "destination capacity contract unavailable"),
            AutoBuyPurchasePreflight.DestinationCapacityIdentityMismatch =>
                Refusal(candidate, "destination capacity identity mismatch"),
            _ =>
                $"Auto Buy failed to purchase {submission.RequestedLevels} levels for {candidate}: native mutation did not apply.",
        };
    }

    private static string Refusal(string candidate, string reason) =>
        $"Auto Buy failed to purchase {candidate}: {reason}.";
}
