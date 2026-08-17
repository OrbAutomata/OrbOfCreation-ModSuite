#if SERVICE_CYCLE_PROFILE
using System.Collections.Generic;
using System.Text;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The one sentence generator. A blocked read-side decision and the refusal of the mutation that
/// decision guards both write through here, so the two cannot answer the same code two ways.
/// </summary>
/// <remarks>
/// <para>
/// A read block used to ship <c>reasonCode</c> alone thirty-four times against six that carried
/// prose, which taught callers to fire the mutation just to read the sentence — the exact loop the
/// pre-decision surface exists to spare them, and the one an unattended strategist must not enter
/// on a spending verb.
/// </para>
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
            text.Append(GameMcpNumberFormatter.Format(row.Needed))
                .Append(' ')
                .Append(row.Resource)
                .Append(" (have ")
                .Append(GameMcpNumberFormatter.Format(row.Held))
                .Append(')');
        }
        return written == 0 ? string.Empty : text.Append('.').ToString();
    }

    /// <summary>
    /// The eight generic classes that reach the wire. The class says which kind of no this is; the
    /// sentence beside it says everything else.
    /// </summary>
    /// <remarks>
    /// Round 7 shipped forty distinct reason codes across sixty-nine uses, thirty-four of them used
    /// exactly once — a vocabulary nobody could learn and nobody did, while the prose beside it
    /// already carried the whole meaning. The producer vocabulary survives inside the suite, where
    /// it selects the sentence; only the class crosses the wire.
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
    /// nothing on the wire said what that now blocked. Twenty minutes later a live round wrote "the
    /// battle is currently active, and prestige might refuse to execute while one's ongoing — though
    /// I'll attempt it anyway" and committed the round's one irreversible action while uncertain,
    /// because there was no verb to ask. The gate is small and closed, so it is stated outright: the
    /// enumeration is the whole set of decisions this surface conditions on a running battle, and a
    /// test holds it to that.
    /// </remarks>
    internal const string RitualBattleGate =
        "While a ritual battle runs no ritual can be activated and no ritual's starting level can " +
        "be set; no other decision on this surface is gated on it.";

    /// <summary>
    /// A check that answered yes needs no code at all. Round 7 put <c>passed</c> and friends in the
    /// same field as thirty-nine refusals, which taught a caller that "has a code" means "was
    /// refused" and made a healthy entity read as a blocked one.
    /// </summary>
    /// <remarks>
    /// Every affirmative half of a producer pair belongs here, not only the ones spelled
    /// <c>passed</c>. A met requirement leaf carries <c>requirement_met</c>, and while that code sat
    /// outside this list every satisfied leaf in <c>world_get</c> — the detail read's primary content —
    /// rendered as a refusal, which is the exact confusion this method exists to end.
    /// </remarks>
    internal static bool IsPassing(string reasonCode) => reasonCode switch
    {
        "passed" or "native_verdict_matched" or "committed" or
        "queue_room_available" or "below_level_cap" or
        "requirement_met" or "recipe_discovered" or "native_leeway_available" or
        "native_develops_below_caps" or
        "below_research_cap" or "visible" or "ready" or "can_buy" or
        "drain_available" or "output_capacity_available" => true,
        _ => false,
    };

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
    internal static bool IsSuiteDefect(string reasonCode) => reasonCode switch
    {
        // The binding set the boundary needs was never assembled, so nothing was submitted.
        "contract_unavailable" or "feature_contract_unavailable" or
        "pair_contract_unavailable" or
        // The submission reached the boundary off Unity's thread and was stopped there.
        "wrong_thread" or
        // The suite staged a layout into the game's own selection lists and read back something
        // else. Nothing the caller passed is wrong and nothing in the game refused.
        "staged_write_failed" or
        // The suite has no world and no identity catalog to answer from.
        "world_not_published" or "entity_catalog_unavailable" => true,
        _ => false,
    };

    /// <summary>
    /// The generic class for one producer code. The default is <see cref="ClassRefused"/>: a code
    /// this table has not met is a no the suite cannot classify further, which is exactly what
    /// "the game refused and the published world does not explain why" already meant.
    /// </summary>
    internal static string Class(string reasonCode) => reasonCode switch
    {
        // The caller's own argument is what is wrong.
        "invalid_uuid" or "invalid_offset" or "invalid_limit" or "invalid_mode" or
        "invalid_purchase_amount" or "unknown_category" or "unknown_discovery_surface" or
        // A configuration section nothing is filed under is the same kind of no as a world
        // category nothing is filed under: the caller named a grouping word this surface has none
        // of, and the sentence beside it names the ones it does have.
        "unknown_section" or
        "query_required" or "mutually_exclusive" or "missing_required" or "unexpected_field" or
        "unexpected_for_mode" or "argument_validation_failed" or "filter_not_supported" or
        // The word the caller passed is not one this filter takes. Named apart from
        // `invalid_state`, which is the target being in the wrong state for a verb: one producer
        // code cannot mean both, and while they shared a spelling this table answered the first
        // arm it met, so every already-run challenge and every wrong-state research refused as
        // though the caller's own argument had been malformed.
        "invalid_state_filter" or
        "category_not_searchable" or "run_filter_out_of_scope" or "keyword_not_worn" or
        "component_count_too_large" or "unsupported_type" or "composition_unsupported" or
        "wrong_mode" or "wrong_alchemy_surface" or "wrong_level_surface" or
        "wrong_loadout_surface" or "wrong_selection" or "wrong_configuration_surface" or
        "discovery_recipe_unresolved" or "discovery_recipe_ambiguous" or "ambiguous_offer" or
        "ambiguous_handle" or "screen_match_failed" or "subtab_match_failed" or
        "page_relation_ambiguous" or
        // One entity drawn by several elements at once. The caller's uuid is a fine id and the
        // screen is in a fine state; what is unanswerable is which of the buttons showing that
        // entity they meant, which is the same kind of no an ambiguous handle is.
        "ambiguous_element" or
        "level_out_of_range" or "slot_out_of_range" or "destination_out_of_range" or
        "name_out_of_range" or "discovery_surface_ambiguous" or
        // A value outside the range its setting accepts is the same kind of no as a dial value
        // outside the range the game allows. It used to answer a different class on each verb,
        // which taught a caller that the class described the tool rather than the failure.
        "configuration_write_rejected" or
        "composite_identity_required" => ClassInput,

        // The named thing is not there.
        "unknown_uuid" or "not_world_projected" or
        "offer_not_in_explainable_world" or "resource_not_published" or
        "automation_entry_not_published" or "not_created" or "not_equipped" or
        "spell_not_equipped" or "none_owned" or
        "not_active" or "not_a_duration_ritual" or
        "no_cancellable_usage" or "nothing_to_discard" or "no_pending_request" or
        "no_pending_target" or "no_valid_target" or "no_open_modal" or "no_other_slot" or
        "single_slot" or "no_discoveries" or "no_ritual_battle_active" or "slot_empty" or
        // A real entity that this screen simply does not draw. The named thing is not there is
        // exactly what it means, and the sentence beside it names the screen that does draw it
        // wherever the world publishes one.
        "not_on_screen" or
        "not_offered" or "offer_unavailable" or "item_unavailable" or "instance_unavailable" or
        "target_unavailable" or "recipe_unavailable" or "element_unavailable" or
        "tree_unavailable" or "source_unavailable" or "components_unavailable" or
        "no_current_offers" or
        "recipe_has_no_core_glyph" or "core_glyph_not_published" or "active_section_empty" or
        "component_unavailable" or "control_unavailable" or "list_unavailable" or
        "selection_unavailable" => ClassNotFound,

        // The target exists and is in the wrong state for this verb.
        "invalid_state" or "already_ran" or "no_active_duration_reward" or
        "already_active" or "already_stopped" or "already_developing" or
        "already_discovered" or "already_in_requested_state" or "already_maxed" or
        "cooldown_active" or "inventory_busy" or "native_caster_busy" or
        // The call named no argument at all: what blocks it is that the screen has two modals open
        // right now, which is a state and moves on its own.
        "multiple_modals_open" or
        "targeting_in_progress" or "transition_in_progress" or "manual_pause" or
        "ritual_battle_active" or "wrong_active_ritual" or "wrong_scene" or
        "spell_already_inactive" or "spell_already_casting" or
        "spell_not_ready" or "spell_not_toggleable" or "spell_not_chargeable" or
        "unique_spell_conflict" or "recipe_incomplete" or "slot_occupied" or
        "composition_changed" or "recipe_identity_changed" or "slot_identity_changed" or
        "ownership_changed" or "mastery_limit_changed" or "assignment_unsettled" or
        "challenges_not_fetched" or "world_cycle_incomplete" or "level_locked" or
        "cancellable_spells_disabled" or "service_disabled" or "emergency_stop" or
        "modal_already_closing" or "modal_close_not_ready" or "requested_state_not_reached" or
        "randomization_unavailable" or "cancel_unavailable" or "fetch_unavailable" or
        "bonus_unavailable" or "develop_unavailable" or
        "immediate_required_discovery" or "reroll_already_used" or
        "switch_blocked" or "cast_in_progress" or "charge_unavailable" or
        "spell_recharging" or
        "batch_spend_drift" or
        "resources_uncovered" or "attuning" => ClassState,

        // A ceiling, a capacity, or a budget is reached; a smaller ask or a later call may work.
        "amount_unavailable" or "loadout_full" or "automation_full" or "harvest_list_full" or
        "plot_action_list_full" or "research_queue_full" or "queue_full" or
        "equipment_type_full" or "maximum_stacks" or "selection_full" or "capacity_exhausted" or
        "no_rerolls" or "reroll_unavailable" or "element_capacity_unavailable" or
        "plot_quantity_insufficient" or "resource_or_headroom_insufficient" or
        "usage_budget_unavailable" or "research_leeway_exhausted" or "multi_buy_unavailable" or
        "augment_slots_exceeded" or
        "engagement_drain_limited" or "screenshot_budget_reached" or
        "destination_full" or
        "slot_unavailable" or "level_cap_reached" or
        "artificial_research_cap_reached" or "research_investment_cap_reached" => ClassLimit,

        // Named resources fall short.
        "unaffordable" or "usage_unaffordable" or "level_not_affordable" or
        "insufficient_quantity" or "insufficient_bandwidth" => ClassUnaffordable,

        // Progression, visibility, or authored requirements are not reached yet.
        "not_available" or "action_not_available" or "not_visible" or "hidden" or
        "hidden_or_undiscovered" or "undiscovered" or "not_discovered" or "progression_locked" or
        "requirements_unmet" or "usage_requirements_unmet" or
        "native_prerequisites_currently_unmet" or "develop_range_refused" or
        "native_not_discoverable" or "discovery_unavailable" or
        "core_glyph_augments_only" or "selection_restricted" or
        "selection_hidden" or "cannot_level" or "resources_hidden" or
        "recipe_not_discovered" or "prerequisites_unmet" or "not_discovered_or_offered" or
        "native_hidden" or "hidden_discovery" or "requirement_unmet" or
        "screen_locked" or
        "native_unavailable" or "native_leeway_exhausted" => ClassLocked,

        // The suite or the game could not read or serve the fact.
        "contract_unavailable" or "feature_contract_unavailable" or "pair_contract_unavailable" or
        "wrong_thread" or "identity_unavailable" or "entity_name_unavailable" or
        "world_not_published" or "lifecycle_no_game" or "lifecycle_initializing" or
        "lifecycle_resetting" or "lifecycle_scene_exit" or "lifecycle_not_available" or
        "lifecycle_replaced" or "stale_configuration_generation" or
        "post_state_timeout" or "post_state_not_observed" or "post_state_not_published" or
        "entity_data_incomplete" or "discovery_offer_read_incomplete" or
        "unmodeled_requirement_leaf" or "suite_verdict_unevaluable" or
        "native_verdict_unavailable" or "native_verdict_mismatch" or
        "native_verdict_input_mismatch" or "navigation_unavailable" or
        "equipment_loadout_unavailable" or "equipment_manager_unavailable" or
        "equipment_type_unavailable" or "equipment_stack_identity_inconsistent" or
        "research_decision_unavailable" or "challenge_state_unavailable" or
        "prestige_state_unavailable" or "loadout_unavailable" or
        "glyph_requirements_unavailable" or "usage_requirements_unavailable" or
        // The suite staged a layout into the game's own selection lists and read back something
        // else. Nothing the caller passed is wrong and nothing in the game refused: the write the
        // suite performs did not land, which is the suite's defect to answer for.
        "staged_write_failed" or
        "cost_unavailable" or "exact_cost_unavailable" or "usage_cost_unavailable" or
        "action_family_unavailable" or "screenshot_budget_unavailable" or
        "inline_screenshot_failed" or "request_canceled_before_claim" or
        "operation_dispatch_fault" or "adapter_fault" or "pair_faulted" or "post_commit_fault" or
        "verification_failed" or "prerequisite_unverified" or
        "requirement_cycle" or "requirement_depth_exceeded" or "requirement_unevaluable" or
        "threshold_scaling_unavailable" or "category_not_collected" or
        "owning_screen_unknown" or "owning_screen_unreadable" or
        "owning_screen_contradictory" or "topology_not_captured" or
        "owning_screen_status_unmodelled" or "owning_screen_availability_unreadable" or
        "configuration_unpublished" or "runtime_not_available" or "price_unavailable" or
        "affordability_unavailable" or "entity_catalog_unavailable" or
        "queue_not_published" or "queue_reading_inconsistent" => ClassUnavailable,

        // Everything else is a no the suite cannot classify further, which is what "the game
        // refused and the published world does not account for it" already meant. In practice that
        // is the native_*_refused family: the game's own gate said no and reported nothing else.
        _ => ClassRefused,
    };

    /// <summary>
    /// The player sentence for one decision code. Unknown codes are rendered rather than dropped:
    /// the world publishes a few reasons as free text of its own, and a plain restatement is still
    /// an answer, where silence is the defect this exists to end.
    /// </summary>
    internal static string For(string reasonCode) => reasonCode switch
    {
        // Nothing to act on yet
        "not_available" => "The game has not unlocked this yet.",
        // Not the same as unaffordable and not the same as full: the screen this action lives on
        // is not unlocked, so the game draws no button at all.
        "screen_locked" => "The screen this action lives on is not unlocked yet.",
        "not_visible" => "The game is not showing this yet.",
        "undiscovered" or "not_discovered" =>
            "This has not been discovered yet.",
        "already_discovered" => "This is already discovered.",
        "already_maxed" => "This is already at its maximum level.",
        "hidden_or_undiscovered" =>
            "This is not discovered, so the game offers no action on it.",

        // Price
        "unaffordable" => "The named resources fall short of the price.",
        "usage_unaffordable" => "The ongoing usage cost is more than is held.",
        "cost_unavailable" =>
            "The game does not publish a price for this, so affordability cannot be read.",
        "resources_hidden" =>
            "The resources this bonus is priced in are not visible yet.",
        "exact_cost_unavailable" =>
            "The game's exact price for this is still refreshing; open its screen and read again.",
        "resource_or_headroom_insufficient" =>
            "This resource falls short, or its capacity leaves no room for what the action adds.",

        // Capacity and lists
        "loadout_full" => "Every slot in this loadout is in use.",
        "automation_full" => "Every automation slot on this queue is in use.",
        "harvest_list_full" => "The harvest list has no empty slot.",
        "plot_action_list_full" => "The plot-action list has no empty slot.",
        "research_queue_full" => "The research queue has no room for another level.",
        "capacity_exhausted" => "The element's capacity is already full.",
        "amount_unavailable" =>
            "The game's own headroom for this is below what the call asked for.",
        "equipment_type_full" => "Every slot this artifact type may occupy is in use.",
        "maximum_stacks" => "This artifact is already equipped to its stack limit.",
        // Two things are equipped in this game and they are equipped in different places, so the
        // sentence has to name which one it is talking about. One code covered both, and a live
        // round read "None of this artifact is equipped." off a spell recipe's `canUse` — the
        // artifact loadout is not where spells live, so the reader was sent to the wrong screen.
        "not_equipped" => "None of this artifact is equipped.",
        "spell_not_equipped" => "This spell is not in any spell slot.",
        "not_created" => "This artifact has not been crafted yet.",
        "single_slot" => "There is no other slot to move to.",
        "no_other_slot" => "There is no other spell slot to move to.",
        "not_active" => "This is not active, so there is nothing to act on.",
        "none_owned" => "None of this is owned.",
        "no_discoveries" => "This tree has nothing left to discover.",
        "no_current_offers" => "This tree is showing no offers to reroll.",
        "immediate_required_discovery" =>
            "This tree has a discovery to take first, so its offers cannot be rerolled.",
        "reroll_already_used" =>
            "A reroll was already spent on this discovery, so no further reroll is offered.",

        // Progress and state
        "already_active" => "This is already running.",
        "spell_already_casting" =>
            "This spell is already running, so a fire press starts no cast; " +
            "toggle_off ends a running toggle spell.",
        "spell_not_chargeable" =>
            "The game offers this spell no charged cast, so it can only be fired outright.",
        "already_stopped" => "This is already stopped.",
        "already_developing" => "A development is already running on this research.",
        "already_in_requested_state" => "This is already in the state the call asked for.",
        "recipe_incomplete" => "The station has no complete recipe loaded.",
        "cooldown_active" => "This is still on cooldown.",
        "inventory_busy" => "The inventory is busy, so nothing can be used right now.",
        "invalid_state" => "This is not in a state where that action does anything.",
        // The rule, not this press's effect. "Does nothing" reads as a shrug about the button the
        // caller just pressed, so a live round spent a second mutation finding out whether the
        // next row would behave the same way; the standing rule answers both at once.
        "already_ran" =>
            "A challenge that has already run cannot be queued again until the next reset.",
        "no_cancellable_usage" => "Nothing is queued that could be cancelled.",
        "level_locked" => "The game fixes this ritual's starting level, so it cannot be set.",
        "ritual_battle_active" => "A ritual battle is running. " + RitualBattleGate,
        "no_active_duration_reward" => "No duration reward from this ritual is running.",
        "not_a_duration_ritual" => "This ritual grants no duration reward to cancel.",
        "usage_requirements_unmet" => "This does not meet its usage requirements yet.",
        "requirements_unmet" => "This does not meet its level requirements yet.",
        "research_leeway_exhausted" =>
            "This research has no leeway left and is at one of its caps.",
        "native_develops_below_caps" =>
            "This research has no leeway left, but both its caps are open, " +
            "which is the game's other route to developing it.",
        "develop_range_refused" =>
            "The game's own develop gate is shut on this research.",
        "cancellable_spells_disabled" =>
            "Cancellable spells are switched off, so this cast cannot be toggled off.",
        "progression_locked" => "The progression that unlocks this is not reached yet.",

        // A feature had two doors onto one switch and a live round called both in one breath to be
        // sure they agreed. The breaker is the door; the settings pen keeps publishing the value
        // and stops writing it, so what is readable does not shrink and what is writable is one.
        "wrong_configuration_surface" =>
            "This setting is one of the seven breakers, and suite_breakers is the one door that " +
            "flips it; suite_configuration still reads its value.",

        // Never a `reasonCode`, and so never a class: it is the sentence `world_categories` prints
        // once under `unlistable:`, and the rows that cannot be paged say that one word back.
        "collector_not_listable" =>
            "this collector publishes no table of its own, so world_list cannot page it; its rows " +
            "reach the wire inside the reads that carry them",

        // The lock the game states and does not explain. It is the honest floor under every other
        // sentence here: those name a condition because the world published one, and this one is
        // what is left to say when it published none. Restating the code produced "Native
        // unavailable." — the suite's own word for the reading, in a sentence meant for a player,
        // that named neither a gate nor a limit. Saying the limit out loud is the answer: a caller
        // learns there is nothing further to look up, which is a decision it can act on.
        "native_unavailable" =>
            "The game keeps this locked, and says nothing about what would unlock it.",

        // Offers, challenges and prestige
        "not_offered" => "This is not among the offers the game is showing.",
        "ambiguous_offer" => "The game offers this action more than once, so the target is unclear.",
        "selection_full" => "Every challenge selection this cycle allows is already taken.",
        "selection_restricted" => "This challenge cannot be selected in the offer set it came from.",
        "world_cycle_incomplete" => "The world cycle is not complete yet.",
        "challenges_not_fetched" => "The challenge offers for this cycle have not been fetched yet.",
        "no_rerolls" => "No rerolls are left this cycle.",
        "reroll_unavailable" => "No rerolls are left.",
        "offer_unavailable" => "Select an offer before confirming it.",

        // Structural gaps the game itself declares
        "plot_quantity_insufficient" =>
            "The plot has no room or not enough of what this action consumes.",
        "element_unavailable" => "The game is not offering this element.",
        "tree_unavailable" => "The game is not showing this discovery tree.",
        "components_unavailable" => "This recipe names no core glyph.",
        "discovery_unavailable" => "The game does not offer a discovery for this recipe.",
        "native_not_discoverable" => "The game never offers a discovery action for this.",
        "native_discovery_refused" => "The game refuses to discover this right now.",
        // The game's own words for the one gate SpellManager.RemoveSpell applies to itself.
        "spell_recharging" =>
            "The game only removes a spell at full charges, and this one is still recharging.",
        "native_use_refused" => "The game refuses to use this right now.",
        "native_level_refused" => "The game refuses to level this right now.",
        "native_develop_refused" => "The game refuses to develop this right now.",
        "native_rejected" =>
            "The game refused, and nothing it reports explains why.",
        "multi_buy_unavailable" =>
            "The global multi-buy target is zero, so a queued develop would take no levels.",
        "research_decision_unavailable" =>
            "The game's research state was not readable in this world, so no develop decision was formed.",
        "challenge_state_unavailable" => "The game's challenge state was not readable in this world.",
        "prestige_state_unavailable" => "The game's prestige state was not readable in this world.",
        "loadout_unavailable" => "The game's equipment loadout was not readable in this world.",
        "switch_blocked" => "The game refuses a loadout swap right now.",
        "projection_refused" => "The suite's own resource-rate policy refuses this assignment.",

        // Checks that answered yes
        "passed" => "This check passes.",
        "native_verdict_matched" => "The suite's verdict matches the game's own.",

        // Core-glyph vocabulary
        "recipe_has_no_core_glyph" => "This recipe names no core glyph.",
        "core_glyph_not_published" => "The game did not report this recipe's core glyph.",
        // `core_glyph_not_owned` and `core_glyph_not_leveled` were retired with the predicate that
        // produced them: neither names a gate the game's add path has, and both were being read as
        // the game's own rule.
        "core_glyph_augments_only" => "This recipe's core glyph may only be used as an augment.",

        _ => Restate(reasonCode),
    };

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
}
#endif
