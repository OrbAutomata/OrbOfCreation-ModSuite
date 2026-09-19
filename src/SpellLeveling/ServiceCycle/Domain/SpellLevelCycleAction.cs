using System;

namespace OrbAutomata;

/// <summary>Which of the game's two level purchases an action asks for.</summary>
internal enum SpellLevelActionKind
{
    /// <summary>One level on one named spell, verified as an exact <c>masteryLevel + 1</c>.</summary>
    Single = 0,

    /// <summary>
    /// The native batch that levels every ready spell at once, available once the
    /// <c>UnlockLevelAllSpells</c> upgrade has a committed level.
    /// </summary>
    All = 1,
}

/// <summary>
/// What the planner believed about a spell when it decided to buy its next mastery level, carried to
/// the boundary so a native refusal can be read against the snapshot that produced it.
/// </summary>
/// <remarks>
/// Spell Leveling's planner sees strictly less than its boundary does: prerequisites remain live-only,
/// while published readiness and affordability are repeated at the action boundary before spending.
/// The belief records the facts needed to make a refusal legible — a spell refused for "no ready mastery
/// level" while the plan believed <see cref="MasteryLevelReady"/> is a capture bug, and the same
/// refusal against a belief of "not ready" would be a planner bug.
/// </remarks>
internal readonly struct SpellLevelPlanBelief
{
    public SpellLevelPlanBelief(
        bool discovered,
        bool masteryLevelReady,
        bool masteryLevelAffordable,
        int masteryLevel,
        int readySpellCount,
        int levelAllUpgradeLevel)
    {
        Discovered = discovered;
        MasteryLevelReady = masteryLevelReady;
        MasteryLevelAffordable = masteryLevelAffordable;
        MasteryLevel = masteryLevel;
        ReadySpellCount = readySpellCount;
        LevelAllUpgradeLevel = levelAllUpgradeLevel;
    }

    public bool Discovered { get; }
    public bool MasteryLevelReady { get; }
    public bool MasteryLevelAffordable { get; }
    public int MasteryLevel { get; }

    /// <summary>How many spells the snapshot showed as ready, which is what an <c>All</c> would spend on.</summary>
    public int ReadySpellCount { get; }

    /// <summary>The committed level of the level-all upgrade, which is what promoted the capability.</summary>
    public int LevelAllUpgradeLevel { get; }
}

/// <summary>
/// One planned mastery-level purchase. A <see cref="SpellLevelActionKind.Single"/> names the spell it
/// buys; an <see cref="SpellLevelActionKind.All"/> names the spell that made the cycle worth acting on
/// at all, because the native batch takes no argument and the identity is what the mutation evidence
/// is filed under.
/// </summary>
internal readonly struct SpellLevelCycleAction
{
    public SpellLevelCycleAction(
        SpellLevelActionKind kind,
        Guid uuid,
        long collectedAtEpoch)
        : this(kind, uuid, collectedAtEpoch, default)
    {
    }

    public SpellLevelCycleAction(
        SpellLevelActionKind kind,
        Guid uuid,
        long collectedAtEpoch,
        SpellLevelPlanBelief belief)
    {
        if (kind is not (SpellLevelActionKind.Single or SpellLevelActionKind.All))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == SpellLevelActionKind.Single && uuid == Guid.Empty)
            throw new ArgumentException(
                "A single-spell action requires a non-empty spell UUID.", nameof(uuid));
        Kind = kind;
        Uuid = uuid;
        CollectedAtEpoch = collectedAtEpoch;
        Belief = belief;
    }

    public SpellLevelActionKind Kind { get; }

    public Guid Uuid { get; }

    /// <summary>What the planner believed about this spell when it chose it.</summary>
    public SpellLevelPlanBelief Belief { get; }

    /// <summary>
    /// The lifecycle epoch the world this purchase was planned from was collected under.
    /// </summary>
    /// <remarks>
    /// Carried by value rather than looked up at the boundary, because by then the snapshot it names
    /// is no longer reachable. The adapter compares it against a live reading of the game's own epoch
    /// and refuses a plan made against another run, penalty-free. An unstamped world carries zero,
    /// which no live reading matches.
    /// </remarks>
    public long CollectedAtEpoch { get; }
}
