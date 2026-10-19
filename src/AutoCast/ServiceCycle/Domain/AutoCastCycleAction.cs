using System;

namespace OrbAutomata;

/// <summary>Which spell-button operation the shared cast boundary asks the game to do.</summary>
internal enum AutoCastActionKind
{
    /// <summary>
    /// Cast the spell in a slot, holding it at full charge first when the setting and the spell both
    /// allow it. The hold and the cast are one action because the game's own sequence is one: the
    /// charge input goes down, the cast is submitted, and the spell charges until the input comes up.
    /// </summary>
    Fire = 0,

    /// <summary>Let go of a full-charge hold this service is holding.</summary>
    ReleaseCharge = 1,

    /// <summary>Press an active toggle spell's native cast button again to turn it off.</summary>
    ToggleOff = 2,
}

/// <summary>Whether a ritual battle has to be running, or not running, for a planned cast to still be wanted.</summary>
internal enum AutoCastRitualCondition
{
    None = 0,

    /// <summary>An attack spell: outside a ritual its combat effects fizzle.</summary>
    InRitual = 1,

    /// <summary>A channel planned while an attack spell is equipped: during a ritual it would block that attack.</summary>
    OutsideRitual = 2,
}

/// <summary>
/// One planned cast or one planned release, named by the position it applies to and the spell that
/// position held when the plan was made.
/// </summary>
/// <remarks>
/// Position and identity together are what a native reference used to be. The legacy engine matched a
/// prepared candidate by <c>ReferenceEquals</c> on the native spell object; a plan that crosses to a
/// worker cannot carry one, so the boundary re-resolves the position and checks that the spell sitting
/// in it is still the spell the plan named. A loadout rearranged between planning and casting is
/// refused rather than cast blind.
/// </remarks>
internal readonly struct AutoCastCycleAction
{
    public AutoCastCycleAction(
        AutoCastActionKind kind,
        int slotIndex,
        Guid spellRecipeId,
        long collectedAtEpoch,
        bool chargeable = false,
        bool chargeHold = false,
        AutoCastRitualCondition ritual = AutoCastRitualCondition.None)
    {
        if (kind is not (AutoCastActionKind.Fire or AutoCastActionKind.ReleaseCharge or
            AutoCastActionKind.ToggleOff))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (slotIndex < 0)
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex), "A cast action names a position in the loadout.");
        Kind = kind;
        SlotIndex = slotIndex;
        SpellRecipeId = spellRecipeId;
        CollectedAtEpoch = collectedAtEpoch;
        Chargeable = chargeable;
        ChargeHold = chargeHold;
        Ritual = ritual;
    }

    public AutoCastActionKind Kind { get; }

    /// <summary>The game's own loadout position, which is what a cast is addressed by.</summary>
    public int SlotIndex { get; }

    /// <summary>
    /// Which spell the position held when the plan was made. <see cref="Guid.Empty"/> when the
    /// snapshot could not name it, which the boundary treats as "do not re-identify" rather than as a
    /// match against anything.
    /// </summary>
    public Guid SpellRecipeId { get; }

    /// <summary>Whether the snapshot showed the spell as holdable at charge.</summary>
    public bool Chargeable { get; }

    /// <summary>
    /// Whether the caller asked for this fire to be held at charge rather than released at once.
    /// </summary>
    /// <remarks>
    /// The service decides charging from its own configuration and what the snapshot said the spell
    /// could do. A caller pressing the button through the MCP holds that choice itself, so the ask
    /// travels with the action instead of being inferred from a setting the caller never set.
    /// </remarks>
    public bool ChargeHold { get; }

    public AutoCastRitualCondition Ritual { get; }

    /// <summary>
    /// The lifecycle epoch the world this cast was planned from was collected under.
    /// </summary>
    /// <remarks>
    /// Carried by value rather than looked up at the boundary, because by then the snapshot it names
    /// is no longer reachable. The adapter compares it against a live reading of the game's own epoch
    /// and refuses a plan made against another run, penalty-free.
    /// </remarks>
    public long CollectedAtEpoch { get; }
}
