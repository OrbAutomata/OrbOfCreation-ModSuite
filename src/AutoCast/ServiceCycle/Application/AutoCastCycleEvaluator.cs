using System;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The pure Auto Cast worker policy: given the pinned world and the pinned configuration it plans at
/// most one cast — or one charge release — and returns the wake that paces the rotation.
/// </summary>
/// <remarks>
/// <para>
/// The admission ladder is the legacy engine's, term for term and in its order. Occupancy first, then
/// the game's manager-wide and per-spell readiness answers, then the reserve floor, then the start threshold. The order is
/// load-bearing rather than incidental: the resource terms run before anything expensive so that a
/// spell nobody can pay for is cheap to reject, and the one term that is missing here — the target
/// preflight — is missing because it is a live graph walk the boundary owns (W60).
/// </para>
/// <para>
/// Selection is round-robin and first-fit, exactly as before. There is no ranking and no score: the
/// feature's promise is that the equipped loadout takes turns, and a rotation that reordered itself by
/// some notion of value would be a different feature. The cursor lives in worker state and the scan
/// starts where the last cast left off.
/// </para>
/// <para>
/// The two ritual terms run last, so a slot is held for a ritual reason only when it would otherwise
/// have gone. While no ritual battle runs, an attack spell waits: the game drops combat effects outside
/// one. While one runs, a channel waits if an attack spell is equipped: a running channel pauses this
/// rotation, attacks included.
/// </para>
/// <para>
/// A spell whose last cast faulted waits out its back-off on the retry gate while the rest of the
/// loadout keeps its turns. A charge release that faulted is still owed: the input stays pressed, which
/// can keep the game from starting any cast, the player's included, so the release is planned again
/// whenever it comes due. Either episode ends when its action commits or when no occupied slot
/// holds the spell any more.
/// </para>
/// <para>
/// The reserve floor is read from configuration, not from the strategy bulletin. That is the standing
/// interim rule for every migration before the strategist exists, and it is the same arithmetic Auto
/// Buy already applies to the same two settings.
/// </para>
/// </remarks>
internal static class AutoCastCycleEvaluator
{
    public static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        ref AutoCastCycleState state,
        ServiceRetryGateSnapshot retryGate,
        MonotonicTimestamp decisionAt,
        ServiceActionWriter<AutoCastCycleAction> actions,
        out AutoCastDecisionMetrics metrics)
    {
        if (retryGate is null) throw new ArgumentNullException(nameof(retryGate));
        var wake = WakePolicy.OnPublication;
        var slots = world.SpellSlots;
        metrics = new AutoCastDecisionMetrics(
            slots.Count,
            eligibleSlots: 0,
            plannedActions: 0,
            holdingCharge: state.HeldChargeSlot != AutoCastCycleState.NoHeldSlot,
            channelBlocked: false);

        // A disabled service plans nothing but still reschedules, so it resumes the moment the
        // operator turns it back on. It does not release a hold on the way out: the start gate opens
        // no cycle while disabled, and the feature runtime lets go of the hold on the main thread
        // once the start gate has read the feature off.
        if (!AutoCastConfigurationPolicy.IsOperational(config)) return wake;

        var rows = slots.AsSpan();
        var owedRelease = SettleRetries(retryGate, rows, decisionAt, actions);

        // A live hold is the whole cycle. The legacy engine froze everything while charging — no
        // evaluation, no other slot, not even its interval clock — because a second cast during a
        // charge is exactly what the setting exists to prevent.
        if (state.HeldChargeSlot != AutoCastCycleState.NoHeldSlot)
            return EvaluateHeldCharge(world, in config, ref state, actions, rows, ref metrics);

        if (owedRelease >= 0)
        {
            actions.Add(new AutoCastCycleAction(
                AutoCastActionKind.ReleaseCharge,
                rows[owedRelease].SlotIndex,
                rows[owedRelease].SpellRecipeId,
                world.CollectedAtEpoch));
            metrics = new AutoCastDecisionMetrics(
                rows.Length, 0, 1, holdingCharge: false, channelBlocked: false);
            return wake;
        }

        // The game removes an equipped spell only at full charges and idle, and a refused removal
        // also switches that spell to a time-based cooldown. A rotation that kept casting would make
        // every loadout edit fail, so while the player has the loadout on screen it waits.
        if (LoadoutOnScreen(world))
        {
            metrics = new AutoCastDecisionMetrics(
                slots.Count, 0, 0, holdingCharge: false, channelBlocked: false, default,
                loadoutOpen: true);
            return wake;
        }

        // A channel in progress pauses the rotation wholesale rather than skipping its slot: the
        // caster is occupied, so nothing else can go either.
        for (var index = 0; index < rows.Length; index++)
        {
            ref readonly var row = ref rows[index];
            if (!row.Occupied || !row.Channeled || !row.Casting) continue;
            metrics = new AutoCastDecisionMetrics(
                slots.Count, 0, 0, holdingCharge: false, channelBlocked: true);
            return wake;
        }

        var absoluteReserve = ResolveAbsoluteReserve(config.Reserves.AbsoluteReserve);
        var relativeMultiplier = Math.Max(0.0, config.Reserves.RelativeReserveMultiplier);
        var startFraction = AutoCastConfigurationPolicy.StartResourceFraction(config);
        var ritual = RitualPolicy.From(world, rows, AutoCastConfigurationPolicy.IsRitualAware(config));

        var empty = 0;
        var busy = 0;
        var notReady = 0;
        var reserveFloor = 0;
        var belowThreshold = 0;
        var outsideRitual = 0;
        var ritualChannel = 0;
        var backingOff = 0;
        var eligible = 0;
        var chosen = -1;
        var heldSpellId = Guid.Empty;

        var start = NormalizeCursor(state.NextSlotIndex, rows);
        for (var offset = 0; offset < rows.Length; offset++)
        {
            var index = (start + offset) % rows.Length;
            ref readonly var row = ref rows[index];

            var exclusion = Admit(
                in row,
                world,
                in absoluteReserve,
                relativeMultiplier,
                startFraction,
                in ritual);

            switch (exclusion)
            {
                case AutoCastExclusion.Empty: empty++; continue;
                case AutoCastExclusion.Busy: busy++; continue;
                case AutoCastExclusion.NotReady: notReady++; continue;
                case AutoCastExclusion.ReserveFloor: reserveFloor++; continue;
                case AutoCastExclusion.BelowStartThreshold: belowThreshold++; continue;
                case AutoCastExclusion.OutsideRitual: outsideRitual++; Hold(in row); continue;
                case AutoCastExclusion.RitualChannel: ritualChannel++; Hold(in row); continue;
            }

            if (row.SpellRecipeId != Guid.Empty &&
                retryGate.IsWaiting(
                    AutoCastServicePolicies.RetryTarget(AutoCastActionKind.Fire, row.SpellRecipeId), decisionAt))
            {
                backingOff++;
                continue;
            }

            eligible++;

            // First fit wins the turn, and the scan continues rather than breaking so the histogram
            // is whole.
            if (chosen < 0) chosen = index;
        }

        var histogram = new AutoCastExclusionHistogram(
            empty, busy, notReady, reserveFloor, belowThreshold,
            outsideRitual,
            ritualChannel,
            backingOff);

        if (chosen < 0)
        {
            metrics = new AutoCastDecisionMetrics(
                slots.Count, eligible, 0, holdingCharge: false, channelBlocked: false, in histogram,
                heldSpellId: heldSpellId);
            return wake;
        }

        ref readonly var pick = ref rows[chosen];
        var holdsCharge = pick.Chargeable && AutoCastConfigurationPolicy.HoldsFullCharge(config);

        actions.Add(new AutoCastCycleAction(
            AutoCastActionKind.Fire,
            pick.SlotIndex,
            pick.SpellRecipeId,
            world.CollectedAtEpoch,
            pick.Chargeable,
            ritual: ritual.ConditionFor(in pick, world)));

        state.RecordPlannedCast(pick.SlotIndex, pick.SpellRecipeId, holdsCharge);

        metrics = new AutoCastDecisionMetrics(
            slots.Count, eligible, 1, holdsCharge, channelBlocked: false, in histogram);

        // A cast that took a hold wakes on the next world rather than on the interval, so the release
        // lands as close to the charge finishing as a generation-gated service can put it.
        return wake;

        void Hold(in WorldSpellSlot row)
        {
            if (heldSpellId == Guid.Empty) heldSpellId = row.SpellRecipeId;
        }
    }

    /// <summary>
    /// Ends every retry episode for a spell no occupied slot holds, and answers the row of a faulted
    /// charge release that is due again, or -1 for none.
    /// </summary>
    private static int SettleRetries(
        ServiceRetryGateSnapshot retryGate,
        ReadOnlySpan<WorldSpellSlot> rows,
        MonotonicTimestamp decisionAt,
        ServiceActionWriter<AutoCastCycleAction> actions)
    {
        var owed = -1;
        for (var index = 0; index < retryGate.Count; index++)
        {
            var entry = retryGate[index];
            var row = Equipped(rows, entry.Target.Id);
            if (row < 0)
                actions.EndRetry(entry.Target);
            else if (owed < 0 &&
                entry.Target.ActionKind == (byte)AutoCastActionKind.ReleaseCharge &&
                !retryGate.IsWaiting(entry.Target, decisionAt))
                owed = row;
        }
        return owed;
    }

    private static int Equipped(ReadOnlySpan<WorldSpellSlot> rows, Guid spellId)
    {
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].Occupied && rows[index].SpellRecipeId == spellId) return index;
        return -1;
    }

    /// <summary>
    /// Decides whether a live hold keeps holding or is let go, from the snapshot's own reading of the
    /// slot it is holding.
    /// </summary>
    /// <remarks>
    /// The four conditions are the legacy engine's: still in gameplay and enabled (checked by the
    /// caller), the setting still on, and the game still reporting the spell as charging. The fourth —
    /// that the position still holds the same spell — is new only in spelling: the legacy engine held
    /// a native reference, and a reference cannot survive a rearranged loadout either.
    /// </remarks>
    private static WakePolicy EvaluateHeldCharge(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        ref AutoCastCycleState state,
        ServiceActionWriter<AutoCastCycleAction> actions,
        ReadOnlySpan<WorldSpellSlot> rows,
        ref AutoCastDecisionMetrics metrics)
    {
        var held = state.HeldChargeSlot;
        var stillCharging =
            AutoCastConfigurationPolicy.HoldsFullCharge(config) &&
            TryFind(rows, held, out var row) &&
            row.Occupied &&
            row.SpellRecipeId == state.HeldChargeSpellId &&
            row.ReadyingCast;

        if (stillCharging)
        {
            // Keep holding, and look again on the next world rather than on the interval. The
            // release is one generation behind the game at worst, which is the price of deciding it
            // off the main thread; waking on the interval instead would make it several.
            metrics = new AutoCastDecisionMetrics(
                rows.Length, 0, 0, holdingCharge: true, channelBlocked: false);
            return WakePolicy.OnPublication;
        }

        actions.Add(new AutoCastCycleAction(
            AutoCastActionKind.ReleaseCharge,
            held,
            state.HeldChargeSpellId,
            world.CollectedAtEpoch));
        state.ReleaseHeldCharge();

        metrics = new AutoCastDecisionMetrics(
            rows.Length, 0, 1, holdingCharge: false, channelBlocked: false);
        return WakePolicy.OnPublication;
    }

    /// <summary>
    /// The admission ladder for one slot, in the legacy engine's order, returning the first term that
    /// refused it.
    /// </summary>
    private static AutoCastExclusion Admit(
        in WorldSpellSlot row,
        GameWorldState world,
        in BigDouble absoluteReserve,
        double relativeMultiplier,
        double startFraction,
        in RitualPolicy ritual)
    {
        if (!row.Occupied) return AutoCastExclusion.Empty;

        // CanCastASpell is a manager-wide ordinary wait state. It was previously asked only at the
        // boundary, which made every publication submit one cast solely to have the live gate bounce
        // it. The published answer keeps that backpressure in planning; the boundary still repeats
        // the same native question immediately before firing (M3).
        if (!row.CasterAvailable) return AutoCastExclusion.Busy;

        // An aura that is up and a spell mid-cast are the same refusal to the rotation: the slot is
        // doing something, so it is not this slot's turn.
        if (row.Casting) return AutoCastExclusion.Busy;

        // The game's own composite answer. Everything under it — cooldown, charges, attunement,
        // affordability by the game's reckoning — is the game's to decide, not this planner's.
        if (!row.CastReady) return AutoCastExclusion.NotReady;

        if (!ClearsReserveFloor(in row, world, in absoluteReserve, relativeMultiplier))
            return AutoCastExclusion.ReserveFloor;

        if (!ClearsStartThreshold(in row, world, startFraction))
            return AutoCastExclusion.BelowStartThreshold;

        return ritual.ConditionFor(in row, world) switch
        {
            AutoCastRitualCondition.InRitual when !ritual.Running => AutoCastExclusion.OutsideRitual,
            AutoCastRitualCondition.OutsideRitual when ritual.Running => AutoCastExclusion.RitualChannel,
            _ => AutoCastExclusion.None,
        };
    }

    /// <summary>The ritual rules for one cycle: off, or on with the world's answer to whether a ritual battle runs.</summary>
    private readonly struct RitualPolicy
    {
        private readonly bool _aware;
        private readonly bool _attackEquipped;

        private RitualPolicy(bool aware, bool running, bool attackEquipped)
        {
            _aware = aware;
            Running = running;
            _attackEquipped = attackEquipped;
        }

        internal bool Running { get; }

        internal static RitualPolicy From(GameWorldState world, ReadOnlySpan<WorldSpellSlot> rows, bool aware)
        {
            if (!aware) return default;

            var running = false;
            var rituals = world.Rituals.AsSpan();
            for (var index = 0; index < rituals.Length && !running; index++)
                running = rituals[index].InBattle;

            var attackEquipped = false;
            for (var index = 0; index < rows.Length && !attackEquipped; index++)
                attackEquipped = rows[index].Occupied && IsAttack(world, rows[index].SpellRecipeId);

            return new RitualPolicy(aware: true, running, attackEquipped);
        }

        /// <summary>What a cast of this slot needs to stay wanted, given which slots are equipped.</summary>
        internal AutoCastRitualCondition ConditionFor(in WorldSpellSlot row, GameWorldState world)
        {
            if (!_aware) return AutoCastRitualCondition.None;
            if (IsAttack(world, row.SpellRecipeId)) return AutoCastRitualCondition.InRitual;
            return row.Channeled && _attackEquipped
                ? AutoCastRitualCondition.OutsideRitual
                : AutoCastRitualCondition.None;
        }

        private static bool IsAttack(GameWorldState world, Guid recipeId) =>
            WorldSpellGraphLookup.TryFindAuthoring(world.SpellRecipeAuthoring, recipeId, out var authoring) &&
            authoring.CombatOnly;
    }

    /// <summary>
    /// Whether casting leaves every resource it charges above the operator's floor.
    /// </summary>
    /// <remarks>
    /// Immediate costs only, and only the ones that are not zero, which is what the legacy policy
    /// tested. Drain is upkeep rather than a spend and has never been reserve-checked: a floor is
    /// about what remains after paying, and an ongoing cost never finishes being paid.
    /// </remarks>
    private static bool ClearsReserveFloor(
        in WorldSpellSlot row,
        GameWorldState world,
        in BigDouble absoluteReserve,
        double relativeMultiplier)
    {
        if (!WorldSpellCostLookup.TryFindRange(
                world.SpellCosts, row.SlotIndex, WorldSpellCostKind.Immediate, out var start, out var count))
        {
            return true; // costs nothing on any resource, so nothing to reserve against
        }

        for (var offset = 0; offset < count; offset++)
        {
            var cost = world.SpellCosts[start + offset];
            if (IsNegative(cost.Amount)) return false; // an unreadable price is not a free one
            if (IsZero(cost.Amount)) continue;

            if (!WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource)) return false;

            var available = resource.TrueQuantity;
            if (IsNegative(available)) return false;

            var floor = BigDouble.Max(absoluteReserve, cost.Amount * relativeMultiplier);
            if (available.CompareTo(cost.Amount + floor) < 0) return false;
        }

        return true;
    }

    /// <summary>
    /// Whether every resource the spell touches is full enough to start.
    /// </summary>
    /// <remarks>
    /// Both cost kinds count here, and that asymmetry with the reserve is the legacy policy's, kept
    /// on purpose: the threshold is about whether the stockpile is healthy enough to run a spell at
    /// all, and a spell that drains a resource is exactly the kind that should wait for it to fill.
    /// A resource with no ceiling is exempt, because a share of an unbounded pool means nothing.
    /// </remarks>
    private static bool ClearsStartThreshold(
        in WorldSpellSlot row,
        GameWorldState world,
        double startFraction)
    {
        var slotIndex = row.SlotIndex;
        return Clears(WorldSpellCostKind.Immediate) && Clears(WorldSpellCostKind.Drain);

        bool Clears(WorldSpellCostKind kind)
        {
            if (!WorldSpellCostLookup.TryFindRange(
                    world.SpellCosts, slotIndex, kind, out var start, out var count))
            {
                return true;
            }

            for (var offset = 0; offset < count; offset++)
            {
                var cost = world.SpellCosts[start + offset];
                if (IsNegative(cost.Amount)) return false;
                if (!WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource)) return false;
                if (IsNegative(resource.TrueQuantity)) return false;
                if (!resource.IsCapped) continue; // unbounded: no share to be below
                if (resource.FillFraction < startFraction) return false;
            }

            return true;
        }
    }

    private static bool IsZero(BigDouble value) => value.Mantissa == 0.0;

    private static bool IsNegative(BigDouble value) => value.Mantissa < 0.0;

    private static bool TryFind(ReadOnlySpan<WorldSpellSlot> rows, int slotIndex, out WorldSpellSlot row)
    {
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].SlotIndex != slotIndex) continue;
            row = rows[index];
            return true;
        }

        row = default;
        return false;
    }

    /// <summary>
    /// Where this cycle's scan begins. The cursor is a slot index rather than a row position, and the
    /// table is sparse, so it is mapped onto the rows by counting rather than by indexing.
    /// </summary>
    private static int NormalizeCursor(int cursor, ReadOnlySpan<WorldSpellSlot> rows)
    {
        if (rows.Length == 0) return 0;
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].SlotIndex >= cursor) return index;
        }

        return 0;
    }

    /// <summary>
    /// The configured absolute floor, or none at all when the setting cannot be read as a number.
    /// </summary>
    /// <remarks>
    /// Auto Buy's reading, adopted rather than re-decided. The legacy cast policy rejected every
    /// candidate on an unparseable reserve, which turns one bad character in a settings file into a
    /// feature that silently does nothing; falling back to the setting's own default of no reserve is
    /// what the operator asked for in every case production can actually produce.
    /// </remarks>
    private static BigDouble ResolveAbsoluteReserve(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            !double.IsNaN(parsed) && !double.IsInfinity(parsed) &&
            parsed > 0.0)
        {
            return new BigDouble(parsed, 0);
        }

        return default;
    }

    // View activity latches per sibling list, so a page is on screen only when every view on its
    // route is the active one.
    private static bool LoadoutOnScreen(GameWorldState world) =>
        ViewActive(world, KnownEntities.ScreenMagic.Uuid) &&
        ViewActive(world, KnownEntities.MagicSpellbook.Uuid) &&
        ViewActive(world, KnownEntities.MagicSpellbookLoadout.Uuid);

    private static bool ViewActive(GameWorldState world, Guid viewId) =>
        WorldLookup.TryFind(world.Views, viewId, out var view) && view.Active;
}
