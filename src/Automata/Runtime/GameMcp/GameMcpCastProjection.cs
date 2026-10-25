#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;


using static OrbAutomata.GameMcp.GameMcpDecisionReason;
using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpScreenState;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCastProjection
{
    /// <summary>
    /// Which sentence a refused cast carries. The world's wins wherever it has one.
    /// </summary>
    /// <remarks>
    /// The boundary knows only that the game said no. The published slot knows how many charges
    /// this spell is short and when the next one lands, or which other spell is holding the caster.
    /// Both producers run here, in this order, and the live press has no second copy of the rule to
    /// drift from.
    /// </remarks>
    internal static string? CastRefusalReason(
        GameMcpCommand command,
        GameWorldState world,
        in ServiceActionResult result,
        bool boundaryVerified,
        string? boundaryReason)
    {
        var worldReason = ExactGameMcpReason(command, world, in result);
        return !boundaryVerified &&
            string.IsNullOrEmpty(worldReason) &&
            !string.IsNullOrEmpty(boundaryReason)
            ? boundaryReason
            : worldReason;
    }

    internal static string? ExactGameMcpReason(
        GameMcpCommand command,
        GameWorldState world,
        in ServiceActionResult result)
    {
        // The cast boundary asks the game's own CanCast() and binds no charge reader, so its
        // refusal could only report that the game had said no. The published slot holds the two
        // facts that say why, so the sentence is written where they are.
        if (command.Kind == GameMcpCommandKind.Cast &&
            result.Code == AutoCastActionResultCodes.SpellNotReady)
        {
            var sentence = GameMcpCastProjection.CastNotReadyReason(
                world, command.Amount - 1, command.TargetId);
            return sentence.Length == 0 ? null : sentence;
        }

        // SpellManager.CanCastASpell() is activeSpells.Any(s => s.IsReadyingCast()) — one spell
        // mid-cast refuses every cast in the game. The boundary can only report that gate by name;
        // the published loadout holds the spell that is holding it, which is the one thing a caller
        // batching three casts can act on.
        if (command.Kind == GameMcpCommandKind.Cast &&
            result.Code == AutoCastActionResultCodes.NativeCasterBusy)
        {
            var sentence = GameMcpCastProjection.CasterBusyReason(world, command.TargetId);
            return sentence.Length == 0 ? null : sentence;
        }
        if (command.Kind != GameMcpCommandKind.Purchase ||
            !string.Equals(command.Mode, "upgrade", StringComparison.Ordinal) ||
            result.Disposition != ServiceActionDisposition.Skipped ||
            !WorldLookup.TryFind(world.Upgrades, command.TargetId, out var upgrade) ||
            upgrade.Reading.CachedCostLevel == upgrade.Reading.Level)
        {
            return null;
        }

        return "This upgrade's live price is still refreshing; open its game screen and retry.";
    }

    /// <summary>
    /// What the game says about a spell that is not ready, in the game's own word for it.
    /// </summary>
    /// <remarks>
    /// The game's popup for a blocked removal is "Cannot remove a spell that is still recharging.",
    /// and it says that whether the spell is mid-cast or merely below full charges. The game's word
    /// wins, and the numbers the screen does not print ride with it.
    /// </remarks>
    /// <summary>
    /// The two recharge numbers, wherever a spell's charge state reaches the wire.
    /// </summary>
    internal static void AddRechargeFacts(JObject target, in WorldSpellSlot slot)
    {
        if (slot.CurrentCharges >= slot.MaximumCharges) return;
        target["charges"] = slot.CurrentCharges.ToString(CultureInfo.InvariantCulture) +
            " of " + slot.MaximumCharges.ToString(CultureInfo.InvariantCulture);
        if (slot.CooldownRemaining > BigDouble.Zero)
            target["nextChargeIn"] = CoarseClock(slot.CooldownRemaining);
    }

    internal static string RechargeClause(in WorldSpellSlot slot)
    {
        var text = new StringBuilder("it holds ")
            .Append(slot.CurrentCharges.ToString(CultureInfo.InvariantCulture))
            .Append(" of ")
            .Append(slot.MaximumCharges.ToString(CultureInfo.InvariantCulture))
            .Append(" charges");
        if (slot.CooldownRemaining > BigDouble.Zero)
            text.Append(", next in ").Append(CoarseClock(slot.CooldownRemaining));
        return text.ToString();
    }

    /// <summary>
    /// Why the game refused a cast, when the published slot holds the answer.
    /// </summary>
    /// <remarks>
    /// The boundary asks the game's own <c>CanCast()</c> and has no charge reader of its own, so
    /// its refusal could say only that the game had said no on its readiness terms — twenty-five
    /// times in one round, never once with a number. The world publishes the charges and the
    /// countdown for every equipped slot, so the sentence is written here, where they are.
    /// </remarks>
    internal static string CastNotReadyReason(GameWorldState world, int slotIndex, Guid recipeId)
    {
        if (!WorldSpellSlotLookup.TryFind(world.SpellSlots, slotIndex, out var slot) ||
            !slot.Occupied || slot.SpellRecipeId != recipeId ||
            slot.CurrentCharges >= slot.MaximumCharges)
        {
            return string.Empty;
        }
        return EntityIdentityFormatter.PlayerName(recipeId, world.EntityIdentities) +
            " is still recharging: " + RechargeClause(in slot) + ".";
    }

    /// <summary>
    /// Which spell is holding the caster, when the game refuses a cast because one already has it.
    /// </summary>
    /// <remarks>
    /// <c>SpellManager.CanCastASpell()</c> is <c>activeSpells.Any(spell =&gt; spell.IsReadyingCast())</c>:
    /// one spell mid-cast refuses every cast in the game. That is a real game gate, not the suite
    /// serialising anything, and the refusal said only "the native spell system is busy" — a round
    /// firing three spells in one batch could not tell which of its own presses had taken the
    /// caster, or which two were still waiting. The world publishes the same predicate for every
    /// equipped slot, so the sentence names the holder.
    /// </remarks>
    internal static string CasterBusyReason(GameWorldState world, Guid recipeId)
    {
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || !slot.ReadyingCast) continue;
            return EntityIdentityFormatter.PlayerName(
                    slot.SpellRecipeId, world.EntityIdentities) +
                " is still casting; " +
                EntityIdentityFormatter.PlayerName(recipeId, world.EntityIdentities) +
                " waits for it. The game casts one spell at a time.";
        }
        return string.Empty;
    }

    internal static GameMcpValue ProjectSpellSlotSummary(GameWorldState world, in WorldSpellSlot slot)
    {
        var weight = slot.Occupied ? LoadedSpellWeightText(world, slot.SlotIndex) : string.Empty;
        // An empty slot names itself under the column that would name its spell, so `occupied` is
        // not a second column for the same bit — and the id of an empty slot is the game's zero
        // Guid, which the wire drops rather than handing back an address nothing answers to.
        var result = new JObject
        {
            ["slot"] = GameMcpSlotNumbering.Wire(slot.SlotIndex),
            ["spellRecipeId"] = slot.Occupied
                ? slot.SpellRecipeId
                : (object)GameMcpListColumns.Empty,
            ["weight"] = weight.Length > 0 ? weight : GameMcpListColumns.Absent,
        };
        return result.Freeze();
    }

    internal static GameMcpValue ProjectSpellCostRow(in WorldSpellCost cost) =>
        new JObject
        {
            ["slot"] = GameMcpSlotNumbering.Wire(cost.SlotIndex),
            ["kind"] = cost.Kind.ToString(),
            ["resourceId"] = Named(cost.ResourceId),
            ["amount"] = new GameMcpDomainValue(cost.Amount),
        }.Freeze();

    internal static GameMcpValue ProjectCastDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        var slotIndex = command.Amount - 1;
        if (state.World is null ||
            !WorldSpellSlotLookup.TryFind(
                state.World.Snapshot.SpellSlots,
                slotIndex,
                out var after) ||
            !after.Occupied ||
            after.SpellRecipeId != command.TargetId)
        {
            return PostStateNotPublished("that spell in that slot");
        }
        var result = new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["slot"] = GameMcpSlotNumbering.Wire(slotIndex),
        };
        var fire = string.Equals(command.Mode, "fire", StringComparison.Ordinal);
        if (fire)
        {
            // The price of casting this again, against the stock the settled world holds — one
            // instant, both numbers from it. What the press charged is not published here and is
            // not computed: this row is the current price, which is the price then only for a spell
            // whose cost did not move, so publishing it as a payment would be a guess wearing a
            // number — the same defect that retired `paid[]` from game_purchase.
            var costs = ProjectEquippedSpellCosts(
                state.World.Snapshot, slotIndex, WorldSpellCostKind.Immediate);
            if (costs.Count > 0) result["nextCastCosts"] = costs;
            // What a committed fire moved: a cast that was not running is running now. The boundary
            // verifies the press against the game's own fire hook, and it refuses a spell that is
            // already casting, so a commit is a cast this call started. The game increments it when
            // a cast completes, not when one starts, so a cast shorter than the world cadence and a
            // cast that never happened produced the same number — a landed press and a dropped
            // press read byte-identical. Fire is the only mode that carries it, because it is the
            // only mode with a native delta behind it: a release is a native call with nothing to
            // verify a start against, and a toggle-off's own sentinel is a cast ending. Neither may
            // claim one either way.
            result["casting"] = true;
        }
        // Whether this press left a charge held — an input the caller has to pick back up, which no
        // loadout row records. It rides on every mode as yes or no rather than vanishing between
        // them: `release` exists precisely to end a hold, and a response that drops the key instead
        // of saying `no` leaves a caller unable to tell "the hold is over" from "this mode does not
        // speak about holds", which is the same absence-as-value the surface bans everywhere else.
        result["charging"] = fire && HoldsCharge(command);
        // Whether the spell is running, as the boolean the read surface publishes. Every mode says
        // it: the settled slot always knows, and an idle one-shot that dropped the key made its
        // silence carry the answer.
        result["active"] = after.Casting;
        // Named after the term it reads, because that term is not a prediction that the next press
        // lands. The game's own Spell.Fire asks IsCasting() BEFORE it asks CanCast(), so a running
        // spell answers CanCast() with true and answers the press with nothing — which is how a
        // response saying `ready: yes` invited the very press this tool then refused. What a press
        // would do additionally needs the live target selector, which no published world holds, so
        // the field says what it measures and `casting` beside it says the rest.
        result["castReady"] = after.CastReady;

        // The game's own recharge facts, or nothing. `cooldown: 0` rode on all 144 casts of one
        // round while the spell was visibly recharging — the game's charge timer is not what that
        // field reads, and a zero the screen never draws is worse than silence. A charge count
        // rode on two of those 144, because it was published only when the settlement window
        // happened to catch the number moving. Both are now the same rule: when the spell is short
        // of full charges the answer says so, and when it is not there is nothing to say.
        AddRechargeFacts(result, in after);
        return result.Freeze();
    }

    /// <summary>
    /// A fire whose settlement window closed before the world showed the cast. The boundary's own
    /// sentinel already proved the press started it, so the answer is what the press knows; a heavy
    /// save publishing its next world late is not a reason to call a landed cast unknown.
    /// </summary>
    internal static GameMcpValue FireWithoutSettledWorld(GameMcpCommand command) => new JObject
    {
        ["uuid"] = command.TargetId.ToString("D"),
        ["slot"] = GameMcpSlotNumbering.Wire(command.Amount - 1),
        ["casting"] = true,
        ["charging"] = HoldsCharge(command),
    }.Freeze();

    internal static bool HoldsCharge(GameMcpCommand command) =>
        string.Equals(command.PayloadValue, "charge", StringComparison.Ordinal);

    internal static GameMcpValue ProjectSpellSlot(
        GameWorldState world,
        in WorldSpellSlot slot)
    {
        if (!slot.Occupied)
        {
            return new JObject
            {
                ["category"] = "spell-slots",
                ["slot"] = GameMcpSlotNumbering.Wire(slot.SlotIndex),
                ["occupied"] = false,
            }.Freeze();
        }
        var result = ProjectEquippedSpell(world, in slot);
        result["category"] = "spell-slots";
        result["occupied"] = true;
        return result.Freeze();
    }

    /// <summary>
    /// A full recharge, in the screen's own words for this spell.
    /// </summary>
    /// <remarks>
    /// <c>Duration.StylizeAccurateText</c> picks the spelling from the recharge processor's core
    /// type: a time recharge prints through <c>Utils.BeautifyTimeAccurate</c>, and a counted one
    /// prints the count. The core type is the recipe's authored <c>baseRecharge.type</c> — the one
    /// module <c>Duration.Entry.CreateProcessor</c> seeds the processor with, and the one
    /// <c>Duration.Processor.GetCoreType()</c> answers with — so the published authoring says which
    /// of the two this is. What the screen wraps a count in is parentheses; what makes it readable
    /// on the wire is the unit, spelled the way <c>rechargeCountsIn</c> already spells it.
    /// </remarks>
    internal static bool TryRechargeText(
        GameWorldState world,
        in WorldSpellSlot slot,
        out string text)
    {
        text = string.Empty;
        if (!WorldSpellGraphLookup.TryFindAuthoring(
                world.SpellRecipeAuthoring, slot.SpellRecipeId, out var authoring))
        {
            return false;
        }
        var countsIn = GameMcpNativeVocabulary.RechargeProcessorType(
            authoring.RechargeProcessorType);
        text = countsIn == "time"
            ? CoarseClock(slot.Recharge)
            : GameMcpNumberFormatter.Format(slot.Recharge) + " " + countsIn;
        return true;
    }

    internal static JObject ProjectEquippedSpell(
        GameWorldState world,
        in WorldSpellSlot slot)
    {
        // The equipped spell is a runtime instance, so its id is in no catalog and resolves for
        // nobody. Its recipe does, and carries the same name the instance was printed under, so the
        // row names the spell once — by the identity a caller can look up. The slot is the address
        // every spell verb takes, and it is already here.
        var result = new JObject
        {
            ["spellRecipeId"] = slot.SpellRecipeId.ToString("D"),
            ["slot"] = GameMcpSlotNumbering.Wire(slot.SlotIndex),
            ["effectiveLevel"] = slot.EffectiveLevel,
            ["requiredMasteryLevel"] = slot.RequiredMasteryLevel,
            ["recipeMasteryLevel"] = slot.RecipeMasteryLevel,
            ["duration"] = slot.DurationSpell,
            ["toggleable"] = slot.Toggled,
            ["usageRequirementsMet"] = slot.UsageRequirementsMet,
            // The game's own Spell.IsUniqueSpell() answer. True means the game refuses a second
            // spell built from this same recipe while this one is equipped — one instance per
            // recipe, not per slot and not per type — so it is what a caller planning another copy
            // of this recipe has to read. Its home is SpellTypeSO.isLoadoutUnique, which the
            // spell-types rows publish under the same word.
            ["isLoadoutUnique"] = slot.IsLoadoutUnique,
            // The game's own manual-cast counter. It is what a firing loop compares to learn whether
            // anything fired, so it is present whether or not it has ever moved.
            ["casts"] = slot.CastCount,
        };
        // A full recharge, spelled the way the screen spells it. This is the number the bar counts
        // down from and the one the recipe tooltip heads with; the recipe asset's authored
        // baseRecharge.duration is a third quantity that no surface of the game shows, so it is
        // published nowhere a player reads. Seconds or a count is the recipe's own recharge kind,
        // so with that unpublished the row says nothing rather than guessing a unit.
        if (TryRechargeText(world, in slot, out var recharge)) result["recharge"] = recharge;
        // How far off full this spell is, on the row that is the spell. These two numbers reached
        // the wire from one place only — the refusal nested inside this row's own `remove:` block —
        // so a caller asking what a spell's state was had to press a removal it did not want, and a
        // round read a cast refusal with no numbers beside a row that could have printed them.
        AddRechargeFacts(result, in slot);
        if (slot.Casting) result["casting"] = true;
        if (slot.ReadyingCast) result["readyingCast"] = true;
        if (slot.Attuning) result["attuning"] = true;
        if (slot.Toggled && slot.Casting)
        {
            result["toggleOff"] = slot.CancellationEnabled
                ? new JObject { ["available"] = true }
                : new JObject
                {
                    ["available"] = false,
                    ["reasonCode"] = "cancellable_spells_disabled",
                };
        }
        // SpellManager.RemoveSpell gates itself on three facts and nothing else: full charges, not
        // casting, not readying a cast. Spell.CanRemove() is a neighbouring predicate the removal
        // path never consults, and answering from it called slots stuck that the game would have
        // cleared. A blocked row carries the numbers that say how far off it is, because the row
        // itself prints no charge count.
        if (!IsScreenUnlocked(world, KnownEntities.MagicSpellbookLoadout.Uuid))
        {
            result["remove"] = new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "screen_locked",
                ["reason"] = GameMcpDecisionReason.ScreenLocked(
                    KnownEntities.MagicSpellbookLoadout.Uuid),
            };
        }
        else if (slot.Casting || slot.ReadyingCast)
        {
            result["remove"] = new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "cast_in_progress",
                ["reason"] = SpellRemovalRefusal.MidCast(EntityIdentityFormatter.PlayerName(
                    slot.SpellRecipeId, world.EntityIdentities)),
            };
        }
        else if (slot.CurrentCharges < slot.MaximumCharges)
        {
            var recharging = new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "spell_recharging",
                ["reason"] = SpellRemovalRefusal.Recharging(
                    EntityIdentityFormatter.PlayerName(slot.SpellRecipeId, world.EntityIdentities),
                    slot.CurrentCharges,
                    slot.MaximumCharges,
                    slot.CooldownRemaining),
            };
            AddRechargeFacts(recharging, in slot);
            result["remove"] = recharging;
        }
        else
        {
            result["remove"] = new JObject { ["available"] = true };
        }
        // Where a spell can move is the slot list, and the slot list is one read for the whole bar.
        // Inlining it per spell meant explaining eight spells delivered the same eight-slot roster
        // eight times, on the verb that is called most.
        result["move"] = world.SpellSlots.Count > 1
            ? new JObject { ["available"] = true }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "no_other_slot",
            };
        var applied = new JArray();
        for (var index = 0; index < slot.AugmentGlyphs.Count; index++)
        {
            var value = slot.AugmentGlyphs[index];
            applied.Add(new JObject
            {
                ["glyphId"] = value.GlyphId.ToString("D"),
                ["count"] = value.Quantity,
            });
        }
        result["glyphs"] = applied;
        var immediate = ProjectEquippedSpellCosts(world, slot.SlotIndex, WorldSpellCostKind.Immediate);
        var drain = ProjectEquippedSpellCosts(world, slot.SlotIndex, WorldSpellCostKind.Drain);
        if (immediate.Count > 0) result["castCosts"] = immediate;
        if (drain.Count > 0) result["drainCostsPerSecond"] = drain;
        var weight = LoadedSpellWeightText(world, slot.SlotIndex);
        if (weight.Length > 0) result["weight"] = weight;
        return result;
    }

    /// <summary>
    /// What a loaded spell holds against spell capacity, the number its slot's weight badge shows,
    /// so a shortfall's "remove spells weighing N more" is answered by reading the loadout.
    /// </summary>
    internal static string LoadedSpellWeightText(GameWorldState world, int slotIndex)
    {
        if (!WorldSpellCostLookup.TryFindRange(
                world.SpellCosts, slotIndex, WorldSpellCostKind.Weight, out var start, out var count))
            return string.Empty;
        var text = new StringBuilder();
        for (var index = start; index < start + count; index++)
        {
            var row = world.SpellCosts[index];
            if (index > start) text.Append(", ");
            text.Append(GameMcpNumberFormatter.Format(row.Amount)).Append(' ')
                .Append(EntityIdentityFormatter.PlayerName(row.ResourceId, world.EntityIdentities));
        }
        return text.ToString();
    }

    /// <summary>
    /// Whether this recipe's spell is toggled, which is the half of
    /// <c>GlyphSO.MeetsNonLvRequirements</c> the world can answer exactly.
    /// </summary>
    /// <remarks>
    /// <c>Spell.IsToggledSpell()</c> is <c>castType is 1 or 2</c> and nothing else, so the
    /// published cast type settles it. <c>IsDurationSpell()</c> is that same test plus a sweep of
    /// the recipe's own cast effects, so a toggled spell is always a duration spell too, and an
    /// instant one may still be — which is why only the affirmative direction is used here.
    /// </remarks>
    internal static bool SpellIsToggled(GameWorldState world, Guid recipeId) =>
        WorldSpellGraphLookup.TryFindAuthoring(
            world.SpellRecipeAuthoring, recipeId, out var authoring) &&
        authoring.CastType is 1 or 2;


    internal static JArray ProjectEquippedSpellCosts(
        GameWorldState world,
        int slotIndex,
        WorldSpellCostKind kind)
    {
        var result = new JArray();
        if (!WorldSpellCostLookup.TryFindRange(
                world.SpellCosts,
                slotIndex,
                kind,
                out var start,
                out var count))
            return result;
        for (var index = start; index < start + count; index++)
        {
            var value = world.SpellCosts[index];
            var playerCost = PlayerFacingCost(world, value.ResourceId, value.Amount);
            var row = new JObject
            {
                ["resourceId"] = value.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(playerCost),
            };
            if (WorldLookup.TryFind(world.Resources, value.ResourceId, out var resource))
            {
                var amount = SpendableAmount(
                    world, value.ResourceId, resource.Reading.Quantity);
                row["spendableAmount"] = new GameMcpDomainValue(amount);
                row["affordable"] = CanAfford(
                    world, value.ResourceId, value.Amount, resource.Reading.Quantity);
            }
            else row["affordable"] = false;
            result.Add(row);
        }
        return result;
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldSpellSlot listedSpellSlot,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectSpellSlotSummary(world, in listedSpellSlot);
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldSpellCost spellCost,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectSpellCostRow(in spellCost);
    }

}
#endif
