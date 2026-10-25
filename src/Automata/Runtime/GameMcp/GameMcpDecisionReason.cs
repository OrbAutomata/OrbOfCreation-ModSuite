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


using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The one sentence generator. A blocked read-side decision and the refusal of the mutation that
/// decision guards both write through here, so the two cannot answer the same code two ways.
/// </summary>
/// <remarks>
/// <para>
/// Where a site holds the numbers — a shortfall, a ceiling, a count — it writes its own sentence
/// from them and this table is not consulted. What lives here is the sentence for a code that
/// stands on its own.
/// </para>
/// </remarks>
internal static class GameMcpDecisionReason
{
    /// <summary>
    /// The one have/need sentence on every surface. Each entry names a resource that is genuinely
    /// short, the price it asks, and what the player holds, both in the player's own units. A
    /// caller that fixes the first named resource is not blocked by a second one nobody mentioned.
    /// </summary>
    internal static string Shortfall(
        IEnumerable<(string Resource, BigDouble Needed, BigDouble Held)> rows)
    {
        var text = new StringBuilder("Needs ");
        var written = 0;
        foreach (var row in rows)
        {
            if (written > 0) text.Append("; ");
            written++;
            var (needed, held) = GameMcpNumberFormatter.FormatApart(row.Needed, row.Held);
            text.Append(needed)
                .Append(' ')
                .Append(row.Resource)
                .Append(" (have ")
                .Append(held)
                .Append(')');
        }
        return written == 0 ? string.Empty : text.Append('.').ToString();
    }

    /// <summary>
    /// The eight generic classes that reach the wire. The class says which kind of no this is; the
    /// sentence beside it says everything else.
    /// </summary>
    /// <remarks>
    /// The producer vocabulary survives inside the suite, where it selects the sentence; only the
    /// class crosses the wire.
    /// </remarks>
    internal const string ClassNotFound = "ERR_NOT_FOUND";
    internal const string ClassState = "ERR_STATE";
    internal const string ClassLimit = "ERR_LIMIT";
    internal const string ClassUnaffordable = "ERR_UNAFFORDABLE";
    internal const string ClassLocked = "ERR_LOCKED";
    internal const string ClassUnavailable = "ERR_UNAVAILABLE";
    internal const string ClassInput = "ERR_INPUT";
    internal const string ClassRefused = "ERR_REFUSED";

    /// <summary>Every class the wire may carry, for documentation and closed-world tests.</summary>
    internal static readonly string[] Classes =
    {
        ClassNotFound, ClassState, ClassLimit, ClassUnaffordable,
        ClassLocked, ClassUnavailable, ClassInput, ClassRefused,
    };

    /// <summary>
    /// What one running ritual battle holds shut, in one sentence, said wherever the fact that a
    /// battle is running is published.
    /// </summary>
    /// <remarks>
    /// "Activate a ritual" answers in battle vocabulary — <c>activeBattle: no -&gt; yes</c> — and
    /// nothing on the wire said what that blocked. The gate is small and closed, so it is stated
    /// outright: the enumeration is the whole set of decisions this surface conditions on a running
    /// battle, and a test holds it to that.
    /// </remarks>
    internal const string RitualBattleGate =
        "While a ritual battle runs no ritual can be activated and no ritual's starting level can " +
        "be set; no other decision on this surface is gated on it.";

    /// <summary>
    /// A check that answered yes needs no code at all.
    /// </summary>
    /// <remarks>
    /// Every affirmative half of a producer pair belongs here, not only the ones spelled
    /// <c>passed</c>. A met requirement leaf carries <c>requirement_met</c>, and while that code
    /// sat outside this list every satisfied leaf in <c>world_get</c> — the detail read's primary
    /// content — rendered as a refusal, which is the exact confusion this method exists to end.
    /// </remarks>
    internal static bool IsPassing(string reasonCode) =>
        Rows.TryGetValue(reasonCode, out var row) && row.Passing;

    /// <summary>
    /// Whether the suite's own machinery is what stopped the call, with the game never asked and
    /// nothing the caller passed at fault. It is what separates the wire's <c>failed</c> from its
    /// <c>refused</c>, and it is a short closed list on purpose: everything absent from it is a no
    /// somebody other than the suite gave.
    /// </summary>
    /// <remarks>
    /// Contention for the mutation permit is deliberately not here. That is the suite working as
    /// designed — another service holds the family this instant — and it clears on its own, so it
    /// is a refusal with a reason rather than a defect to report.
    /// </remarks>
    internal static bool IsSuiteDefect(string reasonCode) =>
        Rows.TryGetValue(reasonCode, out var row) && row.SuiteDefect;

    /// <summary>
    /// The generic class for one producer code. The default is <see cref="ClassRefused"/>: a code
    /// this table has not met is a no the suite cannot classify further, which is exactly what
    /// "the game refused and the published world does not explain why" already meant.
    /// </summary>
    internal static string Class(string reasonCode) =>
        Rows.TryGetValue(reasonCode, out var row) ? row.Class : ClassRefused;

    /// <summary>
    /// The player sentence for one decision code. Unknown codes are rendered rather than dropped:
    /// the world publishes a few reasons as free text of its own, and a plain restatement is still
    /// an answer, where silence is the defect this exists to end.
    /// </summary>
    internal static string For(string reasonCode) =>
        Rows.TryGetValue(reasonCode, out var row) && row.Sentence is not null
            ? row.Sentence
            : Restate(reasonCode);

    /// <summary>
    /// The locked-screen answer with the door named.
    /// </summary>
    /// <remarks>
    /// Every producer holds the view's identity, so the path is always available; a view the suite
    /// has not pinned a route for keeps the unnamed sentence rather than a guessed route, because a
    /// reader sent to the wrong page cannot tell which half was wrong.
    /// </remarks>
    /// <summary>
    /// Where an undiscovered thing comes from: the roll on its tree's screen, and whether that roll
    /// can give it yet.
    /// </summary>
    internal static string RollOnly(Guid screenId, bool inPool, string missingBook, bool offered)
    {
        var path = screenId == Guid.Empty ? string.Empty : GameScreenPath.For(screenId);
        var roll = path.Length == 0 ? "a discovery tree's roll" : "the roll on " + path;
        var pool = offered
            ? "it is on offer there now; pick it with game_discover mode=offer_select on that tree"
            : inPool
                ? "it is in that roll's pool"
                : missingBook.Length > 0
                    ? "it is not in that roll's pool until you own the " + missingBook +
                      " recipe book"
                    : "it is not in that roll's pool yet";
        return "Only " + roll + " gives this, and " + pool + ".";
    }

    internal static string ScreenLocked(Guid viewId)
    {
        var path = GameScreenPath.For(viewId);
        return path.Length == 0
            ? For("screen_locked")
            : path + " is not unlocked yet, so the game draws no row for this.";
    }

    /// <summary>
    /// Whether this table has a sentence of its own for the code, as opposed to falling back to
    /// restating it. A caller that has somewhere better to go when the answer is "no" needs to be
    /// able to ask, rather than comparing against the fallback's own output.
    /// </summary>
    internal static bool Knows(string reasonCode) =>
        Rows.TryGetValue(reasonCode, out var row) && row.Sentence is not null;

    /// <summary>
    /// The answer for a stop no producer accounted for.
    /// </summary>
    /// <remarks>
    /// Every boundary above answers with its own sentence; reaching here means one stopped a call
    /// without supplying it, which is a defect in that producer rather than a kind of no. It used
    /// to name the suite's own machinery — "the spell workbench boundary refused and gave no reason
    /// of its own", in eleven spellings — and call it a refusal, which told a caller the game had
    /// said no and sent them looking for a game state to change that does not exist. Nothing was
    /// applied is the one thing the disposition does guarantee, so that is what it says.
    /// </remarks>
    internal const string NoAccount =
        "Nothing was applied, and the suite has no account of what stopped it. This is a defect " +
        "in the suite rather than a state you can change.";

    /// <summary>
    /// The sentence <c>world_categories</c> prints once under <c>unlistable:</c>, which the rows
    /// that cannot be paged say back. Never a <c>reasonCode</c>, and so never a class.
    /// </summary>
    internal const string CollectorNotListable =
        "this collector publishes no table of its own, so world_list cannot page it; its rows " +
        "reach the wire inside the reads that carry them";

    /// <summary>Every code this surface answers with, so tests can hold the whole table.</summary>
    internal static IEnumerable<string> Codes => Rows.Keys;

    private static readonly Row Affirmative = new(ClassRefused, passing: true);

    /// <summary>
    /// One row per code: its class, whether it is the suite's own failure, whether it answers yes,
    /// and the sentence it reads as on its own. A code with no sentence is restated on the read
    /// side and answers <see cref="NoAccount"/> on a mutation.
    /// </summary>
    private static readonly Dictionary<string, Row> Rows = new(StringComparer.Ordinal)
    {
        // A check that answered yes.
        ["passed"] = Affirmative,
        ["committed"] = Affirmative,
        ["queue_room_available"] = Affirmative,
        ["below_level_cap"] = Affirmative,
        ["requirement_met"] = Affirmative,
        ["recipe_discovered"] = Affirmative,
        ["native_leeway_available"] = Affirmative,
        ["native_develops_below_caps"] = Affirmative,
        ["below_research_cap"] = Affirmative,
        ["visible"] = Affirmative,
        ["ready"] = Affirmative,
        ["can_buy"] = Affirmative,
        ["drain_available"] = Affirmative,
        ["output_capacity_available"] = Affirmative,

        // The caller's own argument is what is wrong.
        ["invalid_uuid"] = new(ClassInput),
        ["invalid_offset"] = new(ClassInput),
        ["invalid_limit"] = new(ClassInput),
        ["unknown_category"] = new(ClassInput),
        ["unknown_discovery_surface"] = new(ClassInput),
        // A configuration section nothing is filed under is the same kind of no as a world
        // category nothing is filed under: the caller named a grouping word this surface has none
        // of, and the sentence beside it names the ones it does have.
        ["unknown_section"] = new(ClassInput),
        ["query_required"] = new(ClassInput),
        ["argument_validation_failed"] = new(ClassInput),
        ["filter_not_supported"] = new(ClassInput),
        // The word the caller passed is not one this filter takes. Named apart from
        // `invalid_state`, which is the target being in the wrong state for a verb: one producer
        // code cannot mean both, and while they shared a spelling this table answered the first
        // arm it met, so every already-run challenge and every wrong-state research refused as
        // though the caller's own argument had been malformed.
        ["invalid_state_filter"] = new(ClassInput),
        ["category_not_searchable"] = new(ClassInput),
        // A name the world collects under but pages nothing under. It is the caller's argument that
        // is wrong for this verb, exactly as an unsearchable category is, and saying
        // `unknown_category` for it contradicted the world_categories page the name was read from.
        ["category_not_listable"] = new(ClassInput),
        ["run_filter_out_of_scope"] = new(ClassInput),
        ["discovered_filter_out_of_scope"] = new(ClassInput),
        ["keyword_not_worn"] = new(ClassInput),
        ["keyword_unknown"] = new(ClassInput),
        ["unsupported_type"] = new(ClassInput),
        ["composition_unsupported"] = new(ClassInput),
        ["wrong_mode"] = new(ClassInput),
        ["wrong_alchemy_surface"] = new(ClassInput),
        ["wrong_level_surface"] = new(ClassInput),
        ["wrong_loadout_surface"] = new(ClassInput),
        ["wrong_selection"] = new(ClassInput),
        // The breaker is the door; the settings pen keeps publishing the value and stops writing
        // it, so what is readable does not shrink and what is writable is one.
        ["wrong_configuration_surface"] = new(
            ClassInput,
            "This setting is one of the seven breakers, and suite_breakers is the one door that " +
            "flips it; suite_configuration still reads its value."),
        ["ambiguous_offer"] = new(
            ClassInput,
            "The game offers this action more than once, so the target is unclear."),
        ["ambiguous_handle"] = new(ClassInput),
        ["screen_match_failed"] = new(ClassInput),
        ["subtab_match_failed"] = new(ClassInput),
        ["page_relation_ambiguous"] = new(ClassInput),
        // One entity drawn by several elements at once. The caller's uuid is a fine id and the
        // screen is in a fine state; what is unanswerable is which of the buttons showing that
        // entity they meant, which is the same kind of no an ambiguous handle is.
        ["ambiguous_element"] = new(ClassInput),
        ["ambiguous_modal"] = new(ClassInput),
        ["level_out_of_range"] = new(ClassInput),
        ["slot_out_of_range"] = new(ClassInput),
        ["destination_out_of_range"] = new(ClassInput),
        ["name_out_of_range"] = new(ClassInput),
        // A value outside the range its setting accepts is the same kind of no as a dial value
        // outside the range the game allows.
        ["configuration_write_rejected"] = new(ClassInput),
        // The caller quoted a page marker or a destination the screen catalog never printed.
        ["tooltip_offset_invalid"] = new(ClassInput),
        ["tile_destination_mismatch"] = new(ClassInput),
        ["composite_identity_required"] = new(ClassInput),

        // The named thing is not there.
        ["unknown_uuid"] = new(ClassNotFound),
        ["not_world_projected"] = new(ClassNotFound),
        // A glyph the game draws no Recipe Book for. It answered `world_not_published` — the suite
        // having read nothing at all — for a perfectly healthy read of a published world.
        ["offer_not_in_explainable_world"] = new(
            ClassNotFound,
            "This offer is on the screen but the world published no row for it, so nothing " +
            "further can be said about it."),
        ["resource_not_published"] = new(ClassNotFound),
        ["automation_entry_not_published"] = new(ClassNotFound),
        ["not_created"] = new(ClassNotFound, "This artifact has not been crafted yet."),
        // Two things are equipped in this game and they are equipped in different places, so the
        // sentence has to name which one it is talking about. One code covered both, and a live
        // round read "None of this artifact is equipped." off a spell recipe's `canUse` — the
        // artifact loadout is not where spells live, so the reader was sent to the wrong screen.
        ["not_equipped"] = new(ClassNotFound, "None of this artifact is equipped."),
        ["spell_not_equipped"] = new(ClassNotFound, "This spell is not in any spell slot."),
        ["none_owned"] = new(ClassNotFound, "None of this is owned."),
        ["not_active"] = new(ClassNotFound, "This is not active, so there is nothing to act on."),
        ["not_a_duration_ritual"] = new(
            ClassNotFound,
            "This ritual grants no duration reward to cancel."),
        ["no_cancellable_usage"] = new(ClassNotFound, "Nothing is queued that could be cancelled."),
        ["nothing_to_discard"] = new(ClassNotFound),
        ["no_pending_request"] = new(ClassNotFound),
        ["no_pending_target"] = new(ClassNotFound),
        ["no_valid_target"] = new(
            ClassNotFound,
            "This spell has nothing valid to aim at right now."),
        ["no_open_modal"] = new(ClassNotFound),
        ["no_other_slot"] = new(ClassNotFound, "There is no other spell slot to move to."),
        ["single_slot"] = new(ClassNotFound, "There is no other slot to move to."),
        // "Nothing left to discover" was printed beside `discoveredCount: 12` and
        // `discoverableCount: 65` on the same row, and only one of the two conditions the game folds
        // into this code makes it true. The read row now says which, with those numbers; this is the
        // sentence the verbs that have no row to point at say instead, and it claims neither.
        ["no_discoveries"] = new(
            ClassNotFound,
            "Nothing in this tree can be discovered right now; its row says how many of its " +
            "discoveries are made and how many are still out of reach."),
        ["no_ritual_battle_active"] = new(ClassNotFound),
        ["slot_empty"] = new(ClassNotFound),
        ["not_on_screen"] = new(ClassNotFound),
        // Offers, challenges and prestige
        ["not_offered"] = new(ClassNotFound, "This is not among the offers the game is showing."),
        ["offer_unavailable"] = new(ClassNotFound, "Select an offer before confirming it."),
        ["item_unavailable"] = new(ClassNotFound),
        ["instance_unavailable"] = new(ClassNotFound),
        ["target_unavailable"] = new(ClassNotFound),
        ["recipe_unavailable"] = new(ClassNotFound),
        ["element_unavailable"] = new(ClassNotFound, "The game is not offering this element."),
        ["source_unavailable"] = new(ClassNotFound),
        ["no_current_offers"] = new(ClassNotFound, "This tree is showing no offers to reroll."),
        ["active_section_empty"] = new(ClassNotFound),
        ["control_unavailable"] = new(ClassNotFound),
        ["list_unavailable"] = new(ClassNotFound),
        // The UI gadgets.
        ["tooltip_match_failed"] = new(ClassNotFound),
        ["tooltip_content_unavailable"] = new(ClassNotFound),
        ["native_plot_not_resolved"] = new(ClassNotFound),
        ["native_spell_not_resolved"] = new(ClassNotFound),
        ["no_modal_named"] = new(ClassNotFound),
        ["selection_unavailable"] = new(ClassNotFound),

        // The target exists and is in the wrong state for this verb.
        ["invalid_state"] = new(
            ClassState,
            "This is not in a state where that action does anything."),
        // The rule, not this press's effect.
        ["already_ran"] = new(
            ClassState,
            "A challenge that has already run cannot be queued again until the next reset."),
        ["no_active_duration_reward"] = new(
            ClassState,
            "No duration reward from this ritual is running."),
        // Progress and state
        ["already_active"] = new(ClassState, "This is already running."),
        ["already_stopped"] = new(ClassState, "This is already stopped."),
        ["already_developing"] = new(
            ClassState,
            "A development is already running on this research."),
        ["already_discovered"] = new(ClassState, "This is already discovered."),
        ["already_in_requested_state"] = new(
            ClassState,
            "This is already in the state the call asked for."),
        ["already_maxed"] = new(ClassState, "This is already at its maximum level."),
        ["cooldown_active"] = new(ClassState, "This is still on cooldown."),
        ["inventory_busy"] = new(
            ClassState,
            "The inventory is busy, so nothing can be used right now."),
        ["native_caster_busy"] = new(
            ClassState,
            "Another spell is still casting, and the game casts one spell at a time. This " +
            "press started nothing; try it again once that cast ends."),
        // The call named no argument at all: what blocks it is that the screen has two modals open
        // right now, which is a state and moves on its own.
        ["multiple_modals_open"] = new(ClassState),
        // A panel is already covering the board, which is the screen's state and moves the moment
        // it is dismissed; nothing about the call was wrong.
        ["modal_already_open"] = new(ClassState),
        ["targeting_in_progress"] = new(
            ClassState,
            "the spell slot is already in a native targeting interaction"),
        ["transition_in_progress"] = new(ClassState),
        ["manual_pause"] = new(
            ClassState,
            "the spell slot is under the player's manual-pause authority"),
        // A discovery tree is holding this entity as the offer the player picked. The tree's own
        // confirm press clears it, so it is the tree's state rather than a fact about the row.
        ["tree_holds_this_offer"] = new(ClassState),
        // Continue exists only on the title screen; being in a run is a state, and it moves.
        ["continue_wrong_scene"] = new(ClassState),
        ["ritual_battle_active"] = new(
            ClassState,
            "A ritual battle is running. " + RitualBattleGate),
        ["wrong_active_ritual"] = new(ClassState),
        ["wrong_scene"] = new(ClassState),
        ["spell_already_inactive"] = new(ClassState, "This toggle spell is already off."),
        ["spell_already_casting"] = new(
            ClassState,
            "This spell is already running, so a fire press starts no cast; " +
            "toggle_off ends a running toggle spell."),
        ["spell_not_ready"] = new(
            ClassState,
            "The game refused the cast on its own readiness terms."),
        ["spell_not_toggleable"] = new(ClassState, "This equipped spell is not a toggle spell."),
        ["spell_not_chargeable"] = new(
            ClassState,
            "The game offers this spell no charged cast, so it can only be fired outright."),
        ["unique_spell_conflict"] = new(ClassState),
        ["recipe_incomplete"] = new(ClassState, "The station has no complete recipe loaded."),
        ["slot_occupied"] = new(ClassState),
        ["recipe_identity_changed"] = new(ClassState),
        ["slot_identity_changed"] = new(
            ClassState,
            "The spell slot no longer holds the spell this call named."),
        ["ownership_changed"] = new(ClassState),
        ["mastery_limit_changed"] = new(ClassState),
        ["assignment_unsettled"] = new(ClassState),
        ["challenges_not_fetched"] = new(
            ClassState,
            "The challenge offers for this cycle have not been fetched yet."),
        ["world_cycle_incomplete"] = new(ClassState, "The world cycle is not complete yet."),
        ["level_locked"] = new(
            ClassState,
            "The game fixes this ritual's starting level, so it cannot be set."),
        ["cancellable_spells_disabled"] = new(
            ClassState,
            "Cancellable spells are switched off, so this cast cannot be toggled off."),
        ["service_disabled"] = new(
            ClassState,
            "The suite feature that performs this is switched off."),
        ["emergency_stop"] = new(
            ClassState,
            "The suite's emergency stop is on, so nothing was sent to the game."),
        ["modal_already_closing"] = new(ClassState),
        ["modal_close_not_ready"] = new(ClassState),
        ["requested_state_not_reached"] = new(ClassState),
        ["randomization_unavailable"] = new(ClassState),
        ["cancel_unavailable"] = new(ClassState),
        ["fetch_unavailable"] = new(ClassState),
        ["bonus_unavailable"] = new(ClassState),
        ["develop_unavailable"] = new(ClassState),
        ["reroll_already_used"] = new(
            ClassState,
            "A reroll was already spent on this discovery, so no further reroll is offered."),
        ["switch_blocked"] = new(ClassState, "The game refuses a loadout swap right now."),
        ["cast_in_progress"] = new(
            ClassState,
            "This spell is mid-cast, so it cannot be removed until the cast finishes."),
        ["charge_unavailable"] = new(ClassState),
        // The game's own words for the one gate SpellManager.RemoveSpell applies to itself.
        ["spell_recharging"] = new(
            ClassState,
            "The game only removes a spell at full charges, and this one is still recharging."),
        ["batch_spend_drift"] = new(
            ClassState,
            "Earlier purchases in this batch spent the margin this one was planned against."),
        ["resources_uncovered"] = new(ClassState),
        ["attuning"] = new(ClassState),

        // A ceiling, a capacity, or a budget is reached; a smaller ask or a later call may work.
        ["amount_unavailable"] = new(
            ClassLimit,
            "The game's own headroom for this is below what the call asked for."),
        // Capacity and lists
        ["loadout_full"] = new(ClassLimit, "Every slot in this loadout is in use."),
        ["automation_full"] = new(ClassLimit, "Every automation slot on this queue is in use."),
        ["harvest_list_full"] = new(ClassLimit, "The harvest list has no empty slot."),
        ["plot_action_list_full"] = new(ClassLimit, "The plot-action list has no empty slot."),
        ["research_queue_full"] = new(
            ClassLimit,
            "The research queue has no room for another level."),
        ["queue_full"] = new(ClassLimit),
        ["equipment_type_full"] = new(
            ClassLimit,
            "Every slot this artifact type may occupy is in use."),
        ["maximum_stacks"] = new(
            ClassLimit,
            "This artifact is already equipped to its stack limit."),
        ["selection_full"] = new(
            ClassLimit,
            "Every challenge selection this cycle allows is already taken."),
        ["capacity_exhausted"] = new(ClassLimit, "The element's capacity is already full."),
        // One glyph asked for more uses than its own GlyphSO.GetMaxUsages() allows. A ceiling on
        // one row of a layout, which a smaller count on that row fixes.
        ["glyph_usages_exceeded"] = new(ClassLimit),
        ["no_rerolls"] = new(ClassLimit, "No rerolls are left this cycle."),
        ["reroll_unavailable"] = new(ClassLimit, "No rerolls are left."),
        ["element_capacity_unavailable"] = new(ClassLimit),
        // Structural gaps the game itself declares
        ["plot_quantity_insufficient"] = new(
            ClassLimit,
            "The plot has no room or not enough of what this action consumes."),
        ["resource_or_headroom_insufficient"] = new(
            ClassLimit,
            "This resource falls short, or its capacity leaves no room for what the action adds."),
        ["usage_budget_unavailable"] = new(ClassLimit),
        ["multi_buy_unavailable"] = new(
            ClassLimit,
            "The global multi-buy target is zero, so a queued develop would take no levels."),
        ["augment_slots_exceeded"] = new(ClassLimit),
        // Both resource axes of a recipe. Their rows already answer
        // resource_or_headroom_insufficient one by one, and the axis that aggregates them says the
        // same thing about the recipe as a whole. The two resource axes a recipe can be held on.
        ["bandwidth_blocked"] = new(
            ClassLimit,
            "One of the resources this consumes has no bandwidth left."),
        ["drain_blocked"] = new(
            ClassLimit,
            "One of the resources this drains is already at its limit."),
        ["engagement_drain_limited"] = new(
            ClassLimit,
            "This recipe would drain a resource past what the game allows; the ratio it will " +
            "allow is beside this."),
        ["screenshot_budget_reached"] = new(ClassLimit),
        ["destination_full"] = new(
            ClassLimit,
            "Every slot this upgrade would fill is already occupied."),
        ["slot_unavailable"] = new(ClassLimit),
        ["level_cap_reached"] = new(ClassLimit),
        ["artificial_research_cap_reached"] = new(ClassLimit),
        ["research_investment_cap_reached"] = new(ClassLimit),

        // Named resources fall short.
        // Price
        ["unaffordable"] = new(ClassUnaffordable, "The named resources fall short of the price."),
        ["usage_unaffordable"] = new(
            ClassUnaffordable,
            "The ongoing usage cost is more than is held."),
        ["level_not_affordable"] = new(
            ClassUnaffordable,
            "No spell has a mastery level ready whose cost you can afford."),
        ["insufficient_quantity"] = new(ClassUnaffordable),
        ["insufficient_bandwidth"] = new(ClassUnaffordable),

        // Progression, visibility, or authored requirements are not reached yet.
        // Nothing to act on yet
        ["not_available"] = new(ClassLocked, "The game has not unlocked this yet."),
        ["action_not_available"] = new(ClassLocked),
        ["not_visible"] = new(ClassLocked, "The game is not showing this yet."),
        ["hidden"] = new(ClassLocked),
        ["hidden_or_undiscovered"] = new(
            ClassLocked,
            "This is not discovered, so the game offers no action on it."),
        ["undiscovered"] = new(ClassLocked, "This has not been discovered yet."),
        ["not_discovered"] = new(ClassLocked, "This has not been discovered yet."),
        ["progression_locked"] = new(
            ClassLocked,
            "The progression that unlocks this is not reached yet."),
        ["level_all_locked"] = new(
            ClassLocked,
            "Level All Spells is locked until Unlock Level All Spells is bought."),
        ["requirements_unmet"] = new(ClassLocked, "This does not meet its level requirements yet."),
        ["usage_requirements_unmet"] = new(
            ClassLocked,
            "This does not meet its usage requirements yet."),
        ["native_prerequisites_currently_unmet"] = new(
            ClassLocked,
            "native prerequisites are currently unmet according to one fresh action-boundary check"),
        ["develop_range_refused"] = new(
            ClassLocked,
            "The game's own develop gate is shut on this research."),
        ["native_not_discoverable"] = new(
            ClassLocked,
            "The game never offers a discovery action for this."),
        ["discovery_unavailable"] = new(
            ClassLocked,
            "The game does not offer a discovery for this recipe."),
        // No screen discovers one thing; the tree's roll is the only way in, and its own row is
        // where a caller acts.
        ["discovered_by_roll_only"] = new(
            ClassLocked,
            "Only a discovery tree's roll gives this; no screen discovers it directly."),
        ["selection_restricted"] = new(
            ClassLocked,
            "This challenge cannot be selected in the offer set it came from."),
        ["selection_hidden"] = new(ClassLocked),
        ["cannot_level"] = new(ClassLocked),
        ["resources_hidden"] = new(
            ClassLocked,
            "The resources this bonus is priced in are not visible yet."),
        ["recipe_not_discovered"] = new(ClassLocked),
        ["prerequisites_unmet"] = new(ClassLocked),
        ["not_discovered_or_offered"] = new(
            ClassLocked,
            "The game draws this once it is discovered, or while a discovery tree offers it."),
        ["native_hidden"] = new(ClassLocked),
        // One sentence, because they are one fact: the game draws this once it is discovered, and a
        // discovery tree holding it as its current offer draws it early.
        ["hidden_discovery"] = new(
            ClassLocked,
            "The game draws this once it is discovered, or while a discovery tree offers it."),
        ["requirement_unmet"] = new(ClassLocked),
        // A tree with discoveries left that none of its gates has put in reach is locked, not empty:
        // the caller's next move is to open a Recipe Book, which is a different move from giving up
        // on the tree — and while both wore ERR_NOT_FOUND, nothing on the wire told them apart.
        ["no_discoveries_in_reach"] = new(
            ClassLocked,
            "The discoveries this tree has left are not in reach yet: a tree's pool is widened by " +
            "its Recipe Books, and an item in the pool is offered only once the game shows it."),
        // Every producer of this code has the tree in hand and finds `IsVisible(tree)` false — the
        // read side off the published row, the action off its own preflight, which answers
        // `identity_unavailable` when the tree genuinely is not there. A tree whose screen is shut
        // is locked, and the sentence has always said so.
        ["tree_unavailable"] = new(ClassLocked, "The game is not showing this discovery tree."),
        // Not the same as unaffordable and not the same as full: the screen this action lives on
        // is not unlocked, so the game draws no button at all.
        ["screen_locked"] = new(
            ClassLocked,
            "The screen this action lives on is not unlocked yet."),
        // The lock and the next level's requirements are two different authored lists, and this
        // block once carried only the second.
        ["unlock_conditions_unmet"] = new(
            ClassLocked,
            "The game keeps this locked. What would unlock it is under \"unlocksWhen\"; the " +
            "requirements beside it are what its next level needs, which is a separate list."),
        ["research_locked"] = new(
            ClassLocked,
            "This research is locked, so the Research grid draws no tile for it."),
        // GlyphSO.IsAvailable() is false: undiscovered, or its prerequisites are not met. The glyph
        // list draws no copy to socket, which is a gate rather than a missing entity — the suite
        // resolved the glyph before it asked.
        ["glyph_unavailable"] = new(ClassLocked),
        // ResourceSO.visible is false: the game does not count this resource in its own resource
        // list yet. A gate the unlock container beside it names, not a thing that is missing and
        // not a claim about what any screen draws. ResourceSO.visible, in the one thing it actually
        // governs. The lock the game states and does not explain. It is the honest floor under
        // every other sentence here: those name a condition because the world published one, and
        // this one is what is left to say when it published none. Restating the code produced
        // "Native unavailable." — the suite's own word for the reading, in a sentence meant for a
        // player, that named neither a gate nor a limit. Saying the limit out loud is the answer: a
        // caller learns there is nothing further to look up, which is a decision it can act on.
        ["native_unavailable"] = new(
            ClassLocked,
            "The game keeps this locked, and says nothing about what would unlock it."),
        ["research_leeway_exhausted"] = new(
            ClassLocked,
            "This research has no leeway left and is at one of its caps."),
        ["native_leeway_exhausted"] = new(ClassLocked),

        // The suite or the game could not read or serve the fact.
        // The binding set the boundary needs was never assembled, so nothing was submitted.
        ["contract_unavailable"] = new(ClassUnavailable, suiteDefect: true),
        ["feature_contract_unavailable"] = new(
            ClassUnavailable,
            "the native harvest feature contract is unavailable",
            suiteDefect: true),
        ["pair_contract_unavailable"] = new(
            ClassUnavailable,
            "The current plot and harvest action cannot be matched safely.",
            suiteDefect: true),
        // The submission reached the boundary off Unity's thread and was stopped there.
        ["wrong_thread"] = new(ClassUnavailable, suiteDefect: true),
        ["identity_unavailable"] = new(ClassUnavailable),
        ["entity_name_unavailable"] = new(ClassUnavailable),
        // The suite has no world and no identity catalog to answer from.
        ["world_not_published"] = new(ClassUnavailable, suiteDefect: true),
        ["lifecycle_no_game"] = new(ClassUnavailable),
        ["lifecycle_initializing"] = new(ClassUnavailable),
        ["lifecycle_resetting"] = new(ClassUnavailable),
        ["lifecycle_scene_exit"] = new(ClassUnavailable),
        ["lifecycle_not_available"] = new(ClassUnavailable),
        ["lifecycle_replaced"] = new(
            ClassUnavailable,
            "The run changed underneath this call — a save load, reset, or new game plus — " +
            "so nothing was sent to the game."),
        // Said once here because two producers said it in two different sets of words.
        ["stale_configuration_generation"] = new(
            ClassUnavailable,
            "The suite's settings changed after this call was accepted and before the game ran it, " +
            "so nothing was done; make the call again against the settings now in force."),
        ["post_state_timeout"] = new(ClassUnavailable),
        ["post_state_not_published"] = new(ClassUnavailable),
        ["entity_data_incomplete"] = new(ClassUnavailable),
        ["discovery_offer_read_incomplete"] = new(ClassUnavailable),
        ["unmodeled_requirement_leaf"] = new(
            ClassUnavailable,
            "One of this entity's requirements uses a condition this build does not model, so " +
            "its graph is incomplete."),
        ["unsupported_requirement_value"] = new(
            ClassUnavailable,
            "This requirement compares something this build does not model; read it on the " +
            "game's own tooltip."),
        ["requirement_class_unread"] = new(ClassUnavailable),
        ["navigation_unavailable"] = new(ClassUnavailable),
        ["equipment_loadout_unavailable"] = new(ClassUnavailable),
        ["equipment_manager_unavailable"] = new(ClassUnavailable),
        ["equipment_type_unavailable"] = new(ClassUnavailable),
        ["equipment_stack_identity_inconsistent"] = new(ClassUnavailable),
        ["research_decision_unavailable"] = new(
            ClassUnavailable,
            "The game's research state was not readable in this world, so no develop decision was formed."),
        ["challenge_state_unavailable"] = new(
            ClassUnavailable,
            "The game's challenge state was not readable in this world."),
        ["prestige_state_unavailable"] = new(
            ClassUnavailable,
            "The game's prestige state was not readable in this world."),
        ["loadout_unavailable"] = new(
            ClassUnavailable,
            "The game's equipment loadout was not readable in this world."),
        ["glyph_requirements_unavailable"] = new(ClassUnavailable),
        ["usage_requirements_unavailable"] = new(ClassUnavailable),
        // The suite staged a layout into the game's own selection lists and read back something
        // else. Nothing the caller passed is wrong and nothing in the game refused: the write the
        // suite performs did not land, which is the suite's defect to answer for.
        ["staged_write_failed"] = new(ClassUnavailable, suiteDefect: true),
        // The suite could not pin the game's multi-buy multiplier for the press, so the press was
        // never made. This answered `native_rejected` — the game refusing — for a call the game
        // never saw.
        ["single_buy_unavailable"] = new(
            ClassUnavailable,
            "The suite could not set the game's multi-buy multiplier, so no purchase was " +
            "attempted.",
            suiteDefect: true),
        // The suite asked for a control the screen has no button for. The router validates
        // every mode before a boundary is reached, so arriving here is the suite contradicting
        // its own router rather than the caller passing a bad word.
        ["unsupported_control"] = new(ClassUnavailable, suiteDefect: true),
        // The UI gadgets again: a shell that is not drawing tabs, a build that exposes no plot
        // or spell list or no queue-room reading, and the two guards the router already makes
        // unreachable.
        ["native_navigation_unavailable"] = new(ClassUnavailable),
        ["native_plot_navigation_unavailable"] = new(ClassUnavailable),
        ["native_plot_list_unavailable"] = new(ClassUnavailable),
        ["native_spell_navigation_unavailable"] = new(ClassUnavailable),
        ["native_spell_list_unavailable"] = new(ClassUnavailable),
        ["native_probe_unavailable"] = new(ClassUnavailable),
        ["tooltip_contract_unavailable"] = new(ClassUnavailable, suiteDefect: true),
        ["tooltip_read_faulted"] = new(ClassUnavailable),
        // The tooltip's graph goes further than the projector walks, so the read was abandoned
        // rather than answered with the part of it that had been collected. Not a ceiling the
        // caller can ask under — the tool takes no size — and not the game refusing: the fact is
        // simply not one this suite will serve, which is what this class says. The same spelling as
        // `requirement_depth_exceeded`, which is the same concept one graph over.
        ["tooltip_depth_exceeded"] = new(ClassUnavailable),
        // A panel the suite will not press. Nothing about the call was wrong and the game refused
        // nothing: opening it would run scene-wired handlers no read of the build can follow, and
        // the three the game draws beside the player's own chrome are a world reset, an ending
        // screen and a developer console.
        ["modal_not_offered"] = new(ClassUnavailable),
        // The router validates every screen and every probe name before a gadget is reached, so
        // these two are the suite contradicting its own router rather than a caller's mistake.
        ["navigation_request_invalid"] = new(ClassUnavailable, suiteDefect: true),
        ["unsupported_probe"] = new(ClassUnavailable, suiteDefect: true),
        ["continue_contract_unavailable"] = new(ClassUnavailable, suiteDefect: true),
        ["cost_unavailable"] = new(
            ClassUnavailable,
            "The game does not publish a price for this, so affordability cannot be read."),
        ["exact_cost_unavailable"] = new(
            ClassUnavailable,
            "The game's exact price for this is still refreshing; open its screen and read again."),
        ["usage_cost_unavailable"] = new(ClassUnavailable),
        ["action_family_unavailable"] = new(
            ClassUnavailable,
            "the suite does not own the requested native action family"),
        ["screenshot_budget_unavailable"] = new(ClassUnavailable),
        ["inline_screenshot_failed"] = new(ClassUnavailable),
        ["request_canceled_before_claim"] = new(ClassUnavailable),
        ["operation_dispatch_fault"] = new(ClassUnavailable),
        ["adapter_fault"] = new(
            ClassUnavailable,
            "The suite could not prove the game had taken this safely, so it stopped rather " +
            "than report a change it had not seen."),
        ["pair_faulted"] = new(
            ClassUnavailable,
            "the native harvest pair faulted before a verified mutation"),
        ["post_commit_fault"] = new(ClassUnavailable),
        ["verification_failed"] = new(ClassUnavailable),
        ["prerequisite_unverified"] = new(ClassUnavailable),
        ["requirement_cycle"] = new(ClassUnavailable),
        ["requirement_depth_exceeded"] = new(ClassUnavailable),
        ["requirement_unevaluable"] = new(
            ClassUnavailable,
            "The game's own answer for this requirement could not be read, so whether it is " +
            "met is unknown."),
        // The requirement graph. The unmet leaf writes its own sentence from the numbers already
        // beside it and never reaches here.
        ["tier_not_published"] = new(
            ClassUnavailable,
            "The game publishes no such tier on this gate, so what it asks for could not be read."),
        ["threshold_scaling_unavailable"] = new(
            ClassUnavailable,
            "This requirement's threshold changes with level and the game did not publish the " +
            "scaled number, so it cannot be compared."),
        ["category_not_collected"] = new(ClassUnavailable),
        // Not the same no as a locked screen: which screen this is drawn on could not be told at
        // all, so whether it is unlocked was never asked. Restating the code said "Owning screen
        // unknown." — the suite's own words for its own gap, in a sentence meant for a player.
        ["owning_screen_unknown"] = new(
            ClassUnavailable,
            "Which screen the game draws this on could not be told, so whether that screen is " +
            "unlocked is unknown."),
        ["owning_screen_unreadable"] = new(
            ClassUnavailable,
            "The game's purchase-screen chain for this target could not be read when this " +
            "run's topology was captured."),
        ["owning_screen_contradictory"] = new(
            ClassUnavailable,
            "This target's captured purchase-screen relation contradicts itself."),
        ["topology_not_captured"] = new(
            ClassUnavailable,
            "The purchase-screen topology holds no admission evidence for this run."),
        ["owning_screen_status_unmodelled"] = new(
            ClassUnavailable,
            "This target's captured purchase-screen relation carries a status this build " +
            "does not model."),
        ["owning_screen_availability_unreadable"] = new(
            ClassUnavailable,
            "The game did not answer whether this target's purchase screen is available " +
            "right now."),
        ["configuration_unpublished"] = new(ClassUnavailable),
        ["configuration_not_available"] = new(
            ClassUnavailable,
            "The suite has not published its settings yet, so there is nothing to write against."),
        // The caller's value was accepted and the suite's own publication of it did not happen.
        // Nothing in the game was asked and no argument would have changed the outcome.
        // The half of a write only the suite owns. The value was accepted and then the suite's own
        // publication of it did not happen, which shipped as `configuration_write_rejected` — the
        // caller's own argument is what is wrong — for a call whose argument was fine. Whether the
        // setting took is genuinely unknown from here, so the sentence says that and names the one
        // move that settles it.
        ["configuration_write_unconfirmed"] = new(
            ClassUnavailable,
            "The setting was accepted but the suite could not confirm it took; read it back and, " +
            "if it is unchanged, report this.",
            suiteDefect: true),
        // The suite is not running, or could not attach to the game's tooltips or its Continue
        // button. The game is never asked in any of the three.
        ["runtime_not_available"] = new(ClassUnavailable, suiteDefect: true),
        // Price and room, on the read side, where the mutation's own sentences do not reach.
        ["price_unavailable"] = new(
            ClassUnavailable,
            "The game has not published a price for this yet; open its screen and read again."),
        ["affordability_unavailable"] = new(
            ClassUnavailable,
            "The game published a price but not whether you can pay it; open its screen and read " +
            "again."),
        ["entity_catalog_unavailable"] = new(ClassUnavailable, suiteDefect: true),
        ["queue_not_published"] = new(
            ClassUnavailable,
            "The action queue was not readable in this world, so queue room is unknown."),
        ["queue_reading_inconsistent"] = new(ClassUnavailable),

        // A no the game gave and the suite cannot classify further.
        ["native_discovery_refused"] = new(
            ClassRefused,
            "The game refuses to discover this right now."),
        ["native_use_refused"] = new(ClassRefused, "The game refuses to use this right now."),
        ["native_level_refused"] = new(ClassRefused, "The game refuses to level this right now."),
        ["native_develop_refused"] = new(
            ClassRefused,
            "The game refuses to develop this right now."),
        ["native_rejected"] = new(
            ClassRefused,
            "The game refused this at the moment it was asked, and gave no reason of its own."),
        ["projection_refused"] = new(
            ClassRefused,
            "The suite's own resource-rate policy refuses this assignment."),
        ["policy_rejected"] = new(
            ClassRefused,
            "The suite's own policy refused this before the game was asked."),
        ["skipped"] = new(
            ClassRefused,
            "Nothing changed: by the time this was asked, there was nothing left for it to do."),
        ["charge_hold_refused"] = new(
            ClassRefused,
            "A charged-cast hold could not be established, so no cast was submitted."),
        ["native_prerequisite_validation_unavailable"] = new(
            ClassRefused,
            "the exact native harvest prerequisite validation was unreadable, so no quantity " +
            "mutation was attempted"),
        ["can_fire_refused"] = new(ClassRefused),
        ["native_target_refused"] = new(ClassRefused),
        ["saved_entry_unavailable"] = new(ClassRefused),

        // Background automation stops, which no command answers with.
        ["family_changed"] = new(ClassRefused),
        ["native_busy"] = new(ClassRefused),
        ["use_not_verified"] = new(ClassRefused),
        ["temporary_duration_changed"] = new(ClassRefused),
        ["temporary_cost_changed"] = new(ClassRefused),
        ["temporary_effect_present"] = new(ClassRefused),
        ["relationship_mismatch"] = new(ClassRefused),
        ["competing_supply"] = new(ClassRefused),
        ["post_payment_fault"] = new(ClassRefused),
    };

    private readonly struct Row
    {
        internal Row(
            string reasonClass,
            string? sentence = null,
            bool suiteDefect = false,
            bool passing = false)
        {
            Class = reasonClass;
            Sentence = sentence;
            SuiteDefect = suiteDefect;
            Passing = passing;
        }

        internal string Class { get; }
        internal string? Sentence { get; }
        internal bool SuiteDefect { get; }
        internal bool Passing { get; }
    }

    /// <summary>
    /// The world publishes a handful of reasons as its own free text rather than as a code from a
    /// closed set. Restating one is honest — it is the game's own words — where dropping it leaves
    /// a caller with a bare code again.
    /// </summary>
    private static string Restate(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            return "The game does not admit this right now.";
        var text = reasonCode.Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text.Substring(1) + ".";
    }

    internal static BigDouble SpendableAmount(
        GameWorldState world,
        Guid resourceId,
        BigDouble fallback) =>
        TryFindResource(world, resourceId, out var resource)
            ? WorldResourceCoordinate.SpendableAmount(in resource)
            : fallback;

    /// <summary>
    /// Converts the game's nominal cost unit to the amount removed from the player's visible pool.
    /// This is the owned equivalent of ResourceSO.GetTrueSpend(amount).
    /// </summary>
    internal static BigDouble PlayerFacingCost(
        GameWorldState world,
        Guid resourceId,
        BigDouble nominalCost)
    {
        if (!TryFindResource(world, resourceId, out var resource))
            return nominalCost;
        return WorldResourceCoordinate.PlayerFacingCost(in resource, nominalCost);
    }

    internal static string ShortfallReason(
        GameWorldState world,
        PublicationTable<WorldResourceCost> costs)
    {
        var rows = new List<(string, BigDouble, BigDouble)>();
        for (var index = 0; index < costs.Count; index++)
        {
            var value = costs[index];
            if (CanAfford(world, value.ResourceId, value.Price, value.Held)) continue;
            var identity = EntityIdentityFormatter.Describe(
                value.ResourceId, world.EntityIdentities);
            rows.Add((
                identity.HasName ? identity.Name : value.ResourceId.ToString("D"),
                PlayerFacingCost(world, value.ResourceId, value.Price),
                SpendableAmount(world, value.ResourceId, value.Held)));
        }
        var sentence = GameMcpDecisionReason.Shortfall(rows);
        return sentence.Length == 0
            ? GameMcpDecisionReason.For("unaffordable")
            : sentence;
    }

    internal static bool CanAfford(
        GameWorldState world,
        Guid resourceId,
        BigDouble nominalCost,
        BigDouble fallbackAvailable) =>
        TryFindResource(world, resourceId, out var resource)
            ? WorldResourceCoordinate.HasAmount(in resource, nominalCost)
            : fallbackAvailable.CompareTo(nominalCost) >= 0;

    internal static bool TryFindResource(
        GameWorldState world,
        Guid resourceId,
        out WorldResource resource)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out resource)) return true;
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvest))
        {
            resource = harvest.Resource;
            return true;
        }
        resource = default;
        return false;
    }

    /// <summary>
    /// The price one cost row asks in the player's own units, straight from the capture the action
    /// is admitted against. The published cost row, the unaffordable sentence, and a committed
    /// purchase's <c>paid[]</c> all read this one expression, so they cannot disagree.
    /// </summary>
    internal static BigDouble AdmittedCost(GameWorldState world, in WorldPurchaseCost cost) =>
        PlayerFacingCost(
            world,
            cost.ResourceId,
            cost.AffordabilityEvaluated
                ? cost.CombinedEffectiveAmount
                : cost.EffectiveExactAmount);

    internal static JObject ProjectPurchaseCost(
        GameWorldState world,
        in WorldPurchaseCost cost) =>
        ProjectPurchaseCost(world, in cost, asRow: false);

    internal static JObject ProjectPurchaseCost(
        GameWorldState world,
        in WorldPurchaseCost cost,
        bool asRow)
    {
        var price = AdmittedCost(world, in cost);
        var result = new JObject
        {
            ["resourceId"] = cost.ResourceId.ToString("D"),
            ["cost"] = new GameMcpDomainValue(price),
        };
        // A price the publication could not compare against a same-generation holding says that,
        // rather than dropping the two columns that would have said it.
        if (!cost.AffordabilityEvaluated)
        {
            result["spendableAmount"] = GameMcpListColumns.Unevaluated;
            result["affordable"] = GameMcpListColumns.Unevaluated;
            return result;
        }

        (result["cost"], result["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(
            price, SpendableAmount(world, cost.ResourceId, cost.AvailableAmount));

        // This row answers for its own resource. The whole-price verdict is what the rows fold
        // to, and a row that reported it claimed to be short of a resource it holds plenty of.
        result["affordable"] = cost.ResourceAffordable;

        // `affordable: no` beside the cost and the holding already says "short of this". The
        // plain shortfall code and the generic sentence behind it wrote that same fact a second
        // and a third time on every row of a 744-row category, so only a shortfall that says
        // something else — a bandwidth ceiling rather than a quantity — still names itself.
        //
        // On a row it names itself in the column that asked. `affordable` already answers with a
        // word wherever a plain no would mislead, so the shortfall the cost and holding columns
        // cannot show is one more word in that vocabulary rather than two columns of its own.
        if (!cost.ResourceAffordable &&
            !string.Equals(
                cost.ResourceAffordabilityReasonCode,
                "insufficient_quantity",
                StringComparison.Ordinal))
        {
            if (asRow)
                result["affordable"] =
                    GameMcpListColumns.Word(cost.ResourceAffordabilityReasonCode);
            else result["reasonCode"] = cost.ResourceAffordabilityReasonCode;
        }
        return result;
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldPurchaseCost purchaseCost,
        int nestedOffset,
        int nestedLimit)
    {
        var projected = ProjectPurchaseCost(world, in purchaseCost, asRow: true);
        projected["targetId"] = purchaseCost.EntityId.ToString("D");
        return projected.Freeze();
    }

}
#endif
