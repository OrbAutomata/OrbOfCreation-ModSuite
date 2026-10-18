using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

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
    /// The purchase-screen topology was published, for which run, and how much it admits.
    /// </summary>
    /// <remarks>
    /// This is the one fact every purchase is admitted against, and it used to leave no trace: when
    /// it was published under the wrong run the log showed only refusals, so which run it belonged
    /// to had to be recovered from source. Announcing it costs one line per run.
    /// </remarks>
    public static string TopologyPublished(long lifecycleEpoch, int rows) =>
        $"Auto Buy purchase-screen topology published for run {lifecycleEpoch}: {rows} " +
        "candidate(s) admitted.";

    /// <summary>
    /// Why an upgrade's destination could not take it, for the refusals whose wire name is shared
    /// with other features and so cannot carry this upgrade-specific fact on its own.
    /// </summary>
    public static string DestinationCapacityRefusal(ServiceActionResultCode code)
    {
        if (code == AutoBuyActionResultCodes.DestinationCapacityLocked)
            return "No World > Aspects pedestal is unlocked yet, so there is no slot for this " +
                "upgrade to fill.";
        if (code == AutoBuyActionResultCodes.DestinationCapacityContractUnavailable)
            return "The upgrade's destination-capacity contract was unreadable, so no purchase was " +
                "attempted.";
        if (code == AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch)
            return "The upgrade's destination list is not the one the suite has audited.";
        return string.Empty;
    }

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
        if (submission.Outcome.Verified || IsAdmissionRefusal(in submission))
            return null;

        var candidate = $"{kind} {EntityIdentityFormatter.Format(uuid)}";
        var fragment = RefusalFragment(submission.Outcome.Code);
        return fragment is null
            ? $"Auto Buy failed to purchase {candidate}: {submission.Outcome.Reason}"
            : $"Auto Buy failed to purchase {candidate}: {fragment}.";
    }

    private static string? RefusalFragment(ServiceActionResultCode code)
    {
        if (code == AutoBuyActionResultCodes.OwningViewUnavailable) return "owning view unavailable";
        if (code == AutoBuyActionResultCodes.OwningViewRelationMissing) return "owning view relation missing";
        if (code == AutoBuyActionResultCodes.OwningViewRelationUnreadable) return "owning view relation unreadable";
        if (code == AutoBuyActionResultCodes.OwningViewTopologyUnbound)
            return "the owning view topology contract never bound";
        if (code == AutoBuyActionResultCodes.OwningViewRelationStatusUnmodeled)
            return "the owning view relation carries an unmodelled status";
        if (code == AutoBuyActionResultCodes.OwningViewAvailabilityUnreadable)
            return "live owning view availability could not be read";
        if (code == AutoBuyActionResultCodes.OwningViewRelationContradictory)
            return "owning view relation contradictory";
        if (code == AutoBuyActionResultCodes.StructureUnavailable) return "structure unavailable";
        if (code == AutoBuyActionResultCodes.DestinationCapacityFull) return "destination capacity full";
        if (code == AutoBuyActionResultCodes.DestinationCapacityLocked) return "no destination slot unlocked";
        if (code == AutoBuyActionResultCodes.DestinationCapacityContractUnavailable)
            return "destination capacity contract unavailable";
        if (code == AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch)
            return "destination capacity identity mismatch";
        return null;
    }

    public static bool IsAdmissionRefusal(in AutoBuyPurchaseSubmission submission) =>
        submission.Outcome.Code == CommonActionResultCodes.Skipped ||
        submission.Outcome.Code == CommonActionResultCodes.NativeRejected;
}
