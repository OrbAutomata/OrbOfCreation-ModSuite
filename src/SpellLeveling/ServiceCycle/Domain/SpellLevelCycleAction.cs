using System;

namespace OrbAutomata;

/// <summary>Which of the game's two level purchases an action asks for.</summary>
internal enum SpellLevelActionKind
{
    /// <summary>One level on one named spell, verified by its mastery level rising.</summary>
    Single = 0,

    /// <summary>
    /// The native batch that levels every ready spell at once, available once the
    /// <c>UnlockLevelAllSpells</c> upgrade has a committed level.
    /// </summary>
    All = 1,
}

/// <summary>
/// One planned mastery-level purchase. A <see cref="SpellLevelActionKind.Single"/> names the spell it
/// buys; the service's <see cref="SpellLevelActionKind.All"/> names the <c>UnlockLevelAllSpells</c>
/// upgrade, the batch's own target, because the native batch takes no argument.
/// </summary>
internal readonly struct SpellLevelCycleAction
{
    public SpellLevelCycleAction(
        SpellLevelActionKind kind,
        Guid uuid,
        long collectedAtEpoch)
    {
        if (kind is not (SpellLevelActionKind.Single or SpellLevelActionKind.All))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == SpellLevelActionKind.Single && uuid == Guid.Empty)
            throw new ArgumentException(
                "A single-spell action requires a non-empty spell UUID.", nameof(uuid));
        Kind = kind;
        Uuid = uuid;
        CollectedAtEpoch = collectedAtEpoch;
    }

    public SpellLevelActionKind Kind { get; }

    public Guid Uuid { get; }

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
