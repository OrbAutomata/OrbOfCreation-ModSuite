using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct SpellLevelFeatureStatus
{
    public SpellLevelFeatureStatus(
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
/// What the running Spell Leveling service reports about ownership and progression.
/// </summary>
/// <remarks>
/// Runtime terms are ordered from shared stop state through ownership and a spell backing off to
/// progression. Saved intent is deliberately absent because the central status join owns it.
/// </remarks>
internal static class SpellLevelFeatureStatusProjector
{
    internal const string ProgressionLockedSummary = "Spell leveling has not been unlocked.";

    public static SpellLevelFeatureStatus Project(
        bool emergencyDisabled,
        bool owned,
        bool cycleObserved,
        AutoSpellLevelCapability capability,
        string? waitingReason = null,
        ServiceRetryEntry? backingOff = null)
    {
        if (emergencyDisabled)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Emergency disable blocks spell leveling.");
        }

        if (!owned)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another plugin owns spell-level purchases.");
        }

        if (backingOff is { } retry)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.NativeMutationFailed,
                $"Spell leveling is backing off {EntityIdentityFormatter.Format(retry.Target.Id)} after " +
                $"{retry.ConsecutiveFailures} failed attempt(s) and retries it when due: {retry.Reason}");
        }

        // Only meaningful once something has actually looked. Before the first cycle the capability
        // holder still reads Locked because that is what a lifecycle boundary resets it to, and
        // reporting that as progression would show every new game as locked for a moment.
        if (cycleObserved && capability == AutoSpellLevelCapability.Locked)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.Locked,
                FeatureStatusReasonCode.ProgressionLocked,
                ProgressionLockedSummary);
        }

        if (!cycleObserved)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.RegistryNotReady,
                "Spell leveling is waiting for its first evaluation.");
        }

        if (!string.IsNullOrWhiteSpace(waitingReason))
        {
            var summary = "Spell leveling is waiting: " + waitingReason.Trim();
            if (!summary.EndsWith(".", System.StringComparison.Ordinal)) summary += ".";
            return new SpellLevelFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                summary);
        }

        return new SpellLevelFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            capability == AutoSpellLevelCapability.All
                ? "Spell leveling is active and levels every ready spell at once."
                : "Spell leveling is active.");
    }
}
