using System;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// The Auto Cast worker's per-lifecycle state: the lifecycle it was minted under, the round-robin
/// cursor, the hold it believes it is holding, and the last cycle's decision metrics.
/// </summary>
/// <remarks>
/// <para>
/// Two pieces of control state survive a cycle, and both are memory of something the worker did rather
/// than of something it read. The cursor is what makes the rotation a rotation: without it every cycle
/// would start at slot zero and the first eligible spell would be the only one ever cast. The held
/// slot is the same kind of fact — the game cannot be asked "is this service holding a charge", only
/// "is this spell charging", and those differ when the player is the one charging it.
/// </para>
/// <para>
/// Nothing read from the game is remembered. Readiness, charges and cost all come from the world
/// every cycle, so keeping a copy would only let the two disagree.
/// </para>
/// </remarks>
internal struct AutoCastCycleState
{
    /// <summary>The cursor value meaning "no hold", chosen so a slot index never collides with it.</summary>
    internal const int NoHeldSlot = -1;

    private AutoCastCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        NextSlotIndex = 0;
        HeldChargeSlot = NoHeldSlot;
        HeldChargeSpellId = Guid.Empty;
        Decision = default;
    }

    public LifecycleGeneration Lifecycle { get; private set; }

    /// <summary>Where the next rotation starts. Advanced only by a cast, never by a refusal.</summary>
    public int NextSlotIndex { get; private set; }

    /// <summary>
    /// The position this service last put into a full-charge hold, or <see cref="NoHeldSlot"/>.
    /// </summary>
    public int HeldChargeSlot { get; private set; }

    /// <summary>Which spell was in that position, so a rearranged loadout does not keep the hold.</summary>
    public Guid HeldChargeSpellId { get; private set; }

    public AutoCastDecisionMetrics Decision { get; private set; }

    public static AutoCastCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);

    /// <summary>
    /// Records that a cast was planned for a position, moving the rotation past it.
    /// </summary>
    /// <remarks>
    /// The cursor moves when the cast is <em>planned</em>, not when it commits, and this is the one
    /// place the port deliberately differs from the engine it replaces. The legacy engine advanced on
    /// a successful fire only, and could afford to: its admission scan ran the target preflight
    /// itself, so a spell with no valid target was skipped within the pass and never became the
    /// pending candidate. The preflight is a live graph walk that cannot leave the main thread (W60),
    /// so this planner cannot see it — and a cursor that only moved on success would re-pick the same
    /// targetless spell every cycle and starve the rest of the loadout. Advancing on the plan keeps
    /// the observable behaviour the legacy scan had: a slot that cannot cast costs itself its turn
    /// rather than costing every other slot theirs.
    /// </remarks>
    public void RecordPlannedCast(int slotIndex, Guid spellId, bool holdsCharge)
    {
        NextSlotIndex = slotIndex + 1;
        if (!holdsCharge) return;
        HeldChargeSlot = slotIndex;
        HeldChargeSpellId = spellId;
    }

    /// <summary>
    /// Forgets the hold. Called when the release is planned rather than when it commits, so a hold the
    /// suite cannot release never freezes the rotation; a release that faults stays owed on the retry
    /// gate, which plans it again when it is due.
    /// </summary>
    public void ReleaseHeldCharge()
    {
        HeldChargeSlot = NoHeldSlot;
        HeldChargeSpellId = Guid.Empty;
    }

    public void RecordDecision(AutoCastDecisionMetrics decision) => Decision = decision;
}

/// <summary>
/// Why an equipped slot did not reach the plan, one member per term the evaluator tests, in order.
/// </summary>
/// <remarks>
/// Auto Cast's ordinary output is one cast or none, so "nothing happened" says nothing on its own and
/// the interesting question is always which term produced it. The order is the legacy engine's
/// admission order, because the order is load-bearing: the resource terms run before the target
/// preflight so that a spell nobody can pay for never costs a graph walk.
/// </remarks>
internal enum AutoCastExclusion
{
    /// <summary>Not excluded — the slot reached the plan.</summary>
    None = 0,

    /// <summary>The position holds no spell.</summary>
    Empty = 1,

    /// <summary>The slot is mid-cast, or is an aura that is already up.</summary>
    Busy = 2,

    /// <summary>The game's own readiness answer said no: cooling down, attuning, or unaffordable.</summary>
    NotReady = 3,

    /// <summary>Spending it would break the operator's reserve floor on some resource.</summary>
    ReserveFloor = 4,

    /// <summary>A resource it touches sits below the configured start threshold.</summary>
    BelowStartThreshold = 5,

    /// <summary>An attack spell while no ritual battle runs, so its combat effects would fizzle.</summary>
    OutsideRitual = 7,

    /// <summary>A channel during a ritual battle, which would keep an equipped attack spell from going.</summary>
    RitualChannel = 8,

    /// <summary>Its last cast faulted, and the retry gate has not let it go again yet.</summary>
    BackingOff = 9,
}

/// <summary>How many slots each exclusion term accounted for in one cycle.</summary>
/// <remarks>
/// A fixed-width value rather than a dictionary, because it rides in a struct the worker hands back
/// once per cycle and the framework's validator forbids a worker definition holding collections.
/// </remarks>
internal readonly struct AutoCastExclusionHistogram
{
    internal AutoCastExclusionHistogram(
        int empty,
        int busy,
        int notReady,
        int reserveFloor,
        int belowStartThreshold,
        int outsideRitual = 0,
        int ritualChannel = 0,
        int backingOff = 0)
    {
        Empty = empty;
        Busy = busy;
        NotReady = notReady;
        ReserveFloor = reserveFloor;
        BelowStartThreshold = belowStartThreshold;
        OutsideRitual = outsideRitual;
        RitualChannel = ritualChannel;
        BackingOff = backingOff;
    }

    public int Empty { get; }
    public int Busy { get; }
    public int NotReady { get; }
    public int ReserveFloor { get; }
    public int BelowStartThreshold { get; }
    public int OutsideRitual { get; }
    public int RitualChannel { get; }
    public int BackingOff { get; }

    public int Total =>
        Empty + Busy + NotReady + ReserveFloor + BelowStartThreshold + OutsideRitual +
        RitualChannel + BackingOff;

    public int For(AutoCastExclusion exclusion) => exclusion switch
    {
        AutoCastExclusion.Empty => Empty,
        AutoCastExclusion.Busy => Busy,
        AutoCastExclusion.NotReady => NotReady,
        AutoCastExclusion.ReserveFloor => ReserveFloor,
        AutoCastExclusion.BelowStartThreshold => BelowStartThreshold,
        AutoCastExclusion.OutsideRitual => OutsideRitual,
        AutoCastExclusion.RitualChannel => RitualChannel,
        AutoCastExclusion.BackingOff => BackingOff,
        _ => 0,
    };
}

internal readonly struct AutoCastDecisionMetrics
{
    internal AutoCastDecisionMetrics(
        int capturedSlots,
        int eligibleSlots,
        int plannedActions,
        bool holdingCharge,
        bool channelBlocked)
        : this(capturedSlots, eligibleSlots, plannedActions, holdingCharge, channelBlocked, default)
    {
    }

    internal AutoCastDecisionMetrics(
        int capturedSlots,
        int eligibleSlots,
        int plannedActions,
        bool holdingCharge,
        bool channelBlocked,
        in AutoCastExclusionHistogram exclusions,
        bool loadoutOpen = false,
        Guid heldSpellId = default)
    {
        if (capturedSlots < 0) throw new ArgumentOutOfRangeException(nameof(capturedSlots));
        if (eligibleSlots < 0) throw new ArgumentOutOfRangeException(nameof(eligibleSlots));
        if (plannedActions < 0) throw new ArgumentOutOfRangeException(nameof(plannedActions));
        CapturedSlots = capturedSlots;
        EligibleSlots = eligibleSlots;
        PlannedActions = plannedActions;
        HoldingCharge = holdingCharge;
        ChannelBlocked = channelBlocked;
        Exclusions = exclusions;
        LoadoutOpen = loadoutOpen;
        HeldSpellId = heldSpellId;
    }

    public int CapturedSlots { get; }
    public int EligibleSlots { get; }
    public int PlannedActions { get; }

    /// <summary>Whether a full-charge hold was live at the end of the cycle.</summary>
    public bool HoldingCharge { get; }

    /// <summary>Whether a channel in progress paused the whole rotation.</summary>
    public bool ChannelBlocked { get; }

    /// <summary>Whether the player had the spell loadout on screen, so the rotation stood aside.</summary>
    public bool LoadoutOpen { get; }

    /// <summary>The first spell a ritual rule held back in a cycle that cast nothing, or empty.</summary>
    public Guid HeldSpellId { get; }

    /// <summary>
    /// The ritual rule behind <see cref="HeldSpellId"/>. One cycle can only hit one of the two: one needs
    /// no ritual running, the other needs one running.
    /// </summary>
    public AutoCastExclusion HeldFor => HeldReason(HeldSpellId, Exclusions.OutsideRitual);

    internal static AutoCastExclusion HeldReason(Guid heldSpellId, long outsideRitual) =>
        heldSpellId == Guid.Empty ? AutoCastExclusion.None
        : outsideRitual > 0 ? AutoCastExclusion.OutsideRitual
        : AutoCastExclusion.RitualChannel;

    /// <summary>Why the equipped slots that did not reach the plan did not reach it.</summary>
    public AutoCastExclusionHistogram Exclusions { get; }
}
