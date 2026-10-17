using System;
using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutoCastFeatureStatus
{
    public AutoCastFeatureStatus(
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

/// <summary>
/// What Auto Cast's running service reports, independent of saved configuration intent.
/// </summary>
/// <remarks>
/// <para>
/// The emergency stop comes before ownership, because a suite that has stood down entirely is a
/// bigger fact than which plugin holds a lease. Manual pause comes last of the blocking terms,
/// because it is the only one that resolves on its own. Saved configuration is deliberately absent:
/// the central status join owns intent.
/// </para>
/// </remarks>
internal static class AutoCastFeatureStatusProjector
{
    internal const string EmergencyDisabledSummary = "Automata Emergency Disable is active.";
    internal const string ManualPauseSummary = "Auto Cast is paused after manual spell input.";

    public static AutoCastFeatureStatus Project(
        bool emergencyDisabled,
        bool owned,
        bool manualPaused,
        bool cycleObserved,
        AutoCastExclusion heldFor = AutoCastExclusion.None,
        Guid heldSpellId = default)
    {
        if (emergencyDisabled)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                EmergencyDisabledSummary);
        }

        if (!owned)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another plugin owns spell casting.");
        }

        if (manualPaused)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ManualPause,
                ManualPauseSummary);
        }

        // The legacy engine's gameplay-scene term, reached the way the runtime states it: a service
        // that has not completed a cycle has nothing to report about a world it has not seen.
        if (!cycleObserved)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Cast is waiting for its first evaluation.");
        }

        if (heldFor is AutoCastExclusion.OutsideRitual or AutoCastExclusion.RitualChannel)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.TemporarySafetyBlock,
                "Auto Cast holds " + EntityIdentityFormatter.PlayerName(heldSpellId) +
                (heldFor == AutoCastExclusion.OutsideRitual
                    ? ": no ritual is running."
                    : ": a channel would block attack spells during the ritual.") +
                " AutoCast/RitualAware turns this off.");
        }

        return new AutoCastFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            "Auto Cast is active.");
    }
}
