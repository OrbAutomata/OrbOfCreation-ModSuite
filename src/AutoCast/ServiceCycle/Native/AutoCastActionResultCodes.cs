using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Why an Auto Cast action ended the way it did. Codes are append-only.</summary>
internal static class AutoCastActionResultCodes
{
    /// <summary>Another plugin holds the spell-cast action family.</summary>
    /// <remarks>
    /// A rejection rather than a fault: standing down for a plugin that owns the family is the
    /// arbitration working, and the lease can come back at any time.
    /// </remarks>
    public static ServiceActionResultCode ActionFamilyUnavailable => new(3072);

    /// <summary>The player cast something by hand, so the service is standing down for a moment.</summary>
    public static ServiceActionResultCode ManualPause => new(3073);

    /// <summary>A target request is already open, so no cast can be submitted into it.</summary>
    public static ServiceActionResultCode TargetingInProgress => new(3074);

    /// <summary>The game says the caster is not free right now; ordinary snapshot staleness.</summary>
    public static ServiceActionResultCode NativeCasterBusy => new(3075);

    /// <summary>
    /// The position no longer holds the spell the plan named, or holds nothing at all.
    /// </summary>
    /// <remarks>
    /// Penalty-free. A loadout rearranged between planning and casting is the ordinary case for a
    /// planner working from a snapshot, and casting the wrong spell is the one outcome worth refusing
    /// every plan to avoid.
    /// </remarks>
    public static ServiceActionResultCode SlotIdentityChanged => new(3076);

    /// <summary>The spell became unready after publication; ordinary snapshot staleness.</summary>
    public static ServiceActionResultCode SpellNotReady => new(3077);

    /// <summary>
    /// The spell has nothing to aim at. A live answer by necessity: the preflight walks the recipe's
    /// effect graph, which is main-thread work no snapshot can carry (W60).
    /// </summary>
    public static ServiceActionResultCode NoValidTarget => new(3078);

    /// <summary>A full-charge hold could not be established, so the cast was not submitted.</summary>
    public static ServiceActionResultCode ChargeHoldRefused => new(3079);

    /// <summary>The requested equipped spell does not use the toggle lifecycle.</summary>
    public static ServiceActionResultCode SpellNotToggleable => new(3080);

    /// <summary>The requested toggle spell is already inactive.</summary>
    public static ServiceActionResultCode SpellAlreadyInactive => new(3081);

    /// <summary>The native Cancellable Spells setting disables turning active toggles off.</summary>
    public static ServiceActionResultCode CancellationDisabled => new(3082);

    /// <summary>The spell was already running, so the fire press would have started no cast.</summary>
    public static ServiceActionResultCode SpellAlreadyCasting => new(3083);
}
