using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Why a Spell Leveling action ended the way it did. Codes are append-only.</summary>
internal static class SpellLevelActionResultCodes
{
    /// <summary>Another plugin holds the spell-level action family.</summary>
    /// <remarks>
    /// A rejection rather than a fault: standing down for a plugin that owns the family is the
    /// arbitration working, and the lease can come back at any time.
    /// </remarks>
    public static ServiceActionResultCode ActionFamilyUnavailable => new(2048, "action_family_unavailable");

    /// <summary>
    /// The game says spell leveling is not unlocked yet — no discovered spell's leveling prerequisite
    /// passes.
    /// </summary>
    /// <remarks>
    /// A rejection, and the one refusal the planner cannot avoid: prerequisites are not published, so
    /// the worker plans optimistically and learns the answer here. It is also what tells the feature
    /// status to read <c>Locked</c>, which is why it is its own code rather than a generic native
    /// rejection.
    /// </remarks>
    public static ServiceActionResultCode ProgressionLocked => new(2049, "progression_locked");

    /// <summary>The level's cost is no longer affordable, or the spell is no longer ready.</summary>
    /// <remarks>
    /// Penalty-free: the planner uses published affordability, but holdings may move before action
    /// dispatch, so a live disagreement is ordinary staleness rather than a broken action contract.
    /// </remarks>
    public static ServiceActionResultCode LevelNotAffordable => new(2050, "level_not_affordable");

    /// <summary>Level All Spells was asked for while Unlock Level All Spells has no committed level.</summary>
    public static ServiceActionResultCode LevelAllLocked => new(2051, "level_all_locked");
}
