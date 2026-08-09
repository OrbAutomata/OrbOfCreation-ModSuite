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
    /// A check that answered yes needs no code at all. Round 7 put <c>passed</c> and friends in the
    /// same field as thirty-nine refusals, which taught a caller that "has a code" means "was
    /// refused" and made a healthy entity read as a blocked one.
    /// </summary>
    internal static bool IsPassing(string reasonCode) => reasonCode switch
    {
        "passed" or "native_verdict_matched" or "committed" or
        "queue_room_available" or "below_level_cap" => true,
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
        "query_required" or "mutually_exclusive" or "missing_required" or "unexpected_field" or
        "unexpected_for_mode" or "argument_validation_failed" or "filter_not_supported" or
        "component_count_too_large" or "unsupported_type" or "composition_unsupported" or
        "wrong_mode" or "wrong_alchemy_surface" or "wrong_level_surface" or
        "wrong_loadout_surface" or "wrong_selection" or
        "discovery_recipe_unresolved" or "discovery_recipe_ambiguous" or "ambiguous_offer" or
        "ambiguous_handle" or "screen_match_failed" or "subtab_match_failed" or
        "multiple_modals_open" or "page_relation_ambiguous" or
        "level_out_of_range" or "slot_out_of_range" or "destination_out_of_range" or
        "name_out_of_range" => ClassInput,

        // The named thing is not there.
        "unknown_uuid" or "not_world_projected" or
        "offer_not_in_explainable_world" or "resource_not_published" or
        "automation_entry_not_published" or "not_created" or "not_equipped" or "none_owned" or
        "not_active" or "no_active_duration_reward" or "not_a_duration_ritual" or
        "no_cancellable_usage" or "nothing_to_discard" or "no_pending_request" or
        "no_pending_target" or "no_valid_target" or "no_open_modal" or "no_other_slot" or
        "single_slot" or "no_discoveries" or "no_ritual_battle_active" or "slot_empty" or
        "not_offered" or "offer_unavailable" or "item_unavailable" or "instance_unavailable" or
        "target_unavailable" or "recipe_unavailable" or "element_unavailable" or
        "tree_unavailable" or "source_unavailable" or "components_unavailable" or
        "no_current_offers" or
        "recipe_has_no_core_glyph" or "core_glyph_not_published" or "active_section_empty" or
        "component_unavailable" or "control_unavailable" or "list_unavailable" or
        "selection_unavailable" => ClassNotFound,

        // The target exists and is in the wrong state for this verb.
        "invalid_state" or "already_ran" or "already_active" or "already_stopped" or "already_developing" or
        "already_discovered" or "already_in_requested_state" or "already_maxed" or
        "cooldown_active" or "inventory_busy" or "native_caster_busy" or
        "targeting_in_progress" or "transition_in_progress" or "manual_pause" or
        "ritual_battle_active" or "wrong_active_ritual" or "not_selected" or "wrong_scene" or
        "spell_already_inactive" or "spell_already_casting" or
        "spell_not_ready" or "spell_not_toggleable" or
        "unique_spell_conflict" or "recipe_incomplete" or "slot_occupied" or
        "composition_changed" or "recipe_identity_changed" or "slot_identity_changed" or
        "ownership_changed" or "mastery_limit_changed" or "assignment_unsettled" or
        "challenges_not_fetched" or "world_cycle_incomplete" or "level_locked" or
        "cancellable_spells_disabled" or "service_disabled" or "emergency_stop" or
        "modal_already_closing" or "modal_close_not_ready" or "requested_state_not_reached" or
        "randomization_unavailable" or "cancel_unavailable" or "fetch_unavailable" or
        "bonus_unavailable" or "develop_unavailable" or
        "immediate_required_discovery" or "reroll_already_used" => ClassState,

        // A ceiling, a capacity, or a budget is reached; a smaller ask or a later call may work.
        "amount_unavailable" or "loadout_full" or "automation_full" or "harvest_list_full" or
        "plot_action_list_full" or "research_queue_full" or "queue_full" or
        "equipment_type_full" or "maximum_stacks" or "selection_full" or "capacity_exhausted" or
        "no_rerolls" or "reroll_unavailable" or "element_capacity_unavailable" or
        "plot_quantity_insufficient" or "resource_or_headroom_insufficient" or
        "usage_budget_unavailable" or "research_leeway_exhausted" or "multi_buy_unavailable" or
        "engagement_drain_limited" or "screenshot_budget_reached" or
        "slot_unavailable" => ClassLimit,

        // Named resources fall short.
        "unaffordable" or "usage_unaffordable" or "level_not_affordable" => ClassUnaffordable,

        // Progression, visibility, or authored requirements are not reached yet.
        "not_available" or "action_not_available" or "not_visible" or "hidden" or
        "hidden_or_undiscovered" or "undiscovered" or "not_discovered" or "progression_locked" or
        "requirements_unmet" or "usage_requirements_unmet" or
        "native_prerequisites_currently_unmet" or "develop_range_refused" or
        "native_not_discoverable" or "discovery_unavailable" or "core_glyph_not_owned" or
        "core_glyph_not_leveled" or "core_glyph_augments_only" or "selection_restricted" or
        "selection_hidden" or "cannot_level" or "resources_hidden" => ClassLocked,

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
        "cost_unavailable" or "exact_cost_unavailable" or "usage_cost_unavailable" or
        "action_family_unavailable" or "screenshot_budget_unavailable" or
        "inline_screenshot_failed" or "request_canceled_before_claim" or
        "operation_dispatch_fault" or "adapter_fault" or "pair_faulted" or "post_commit_fault" or
        "verification_failed" or "prerequisite_unverified" => ClassUnavailable,

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
        "not_equipped" => "None of this artifact is equipped.",
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
        "already_stopped" => "This is already stopped.",
        "already_developing" => "A development is already running on this research.",
        "already_in_requested_state" => "This is already in the state the call asked for.",
        "recipe_incomplete" => "The station has no complete recipe loaded.",
        "cooldown_active" => "This is still on cooldown.",
        "inventory_busy" => "The inventory is busy, so nothing can be used right now.",
        "invalid_state" => "This is not in a state where that action does anything.",
        "already_ran" => "This challenge has already run, so queueing it does nothing.",
        "no_cancellable_usage" => "Nothing is queued that could be cancelled.",
        "level_locked" => "The game fixes this ritual's starting level, so it cannot be set.",
        "not_selected" => "This ritual is not the selected one.",
        "ritual_battle_active" => "A ritual battle is running.",
        "no_active_duration_reward" => "No duration reward from this ritual is running.",
        "not_a_duration_ritual" => "This ritual grants no duration reward to cancel.",
        "usage_requirements_unmet" => "This does not meet its usage requirements yet.",
        "requirements_unmet" => "This does not meet its level requirements yet.",
        "research_leeway_exhausted" =>
            "This research has no leeway left and is at one of its caps.",
        "develop_range_refused" =>
            "The game's own develop gate is shut on this research.",
        "cancellable_spells_disabled" =>
            "Cancellable spells are switched off, so this cast cannot be toggled off.",
        "progression_locked" => "The progression that unlocks this is not reached yet.",

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
        "native_remove_refused" => "The game refuses to clear this slot right now.",
        "native_use_refused" => "The game refuses to use this right now.",
        "native_level_refused" => "The game refuses to level this right now.",
        "native_develop_refused" => "The game refuses to develop this right now.",
        "native_rejected" =>
            "The game refused, and the published world does not explain why.",
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
        "core_glyph_not_published" => "This recipe's core glyph is not in the published world.",
        "core_glyph_not_owned" => "This recipe's core glyph is not owned.",
        "core_glyph_not_leveled" => "This recipe's core glyph has no level yet.",
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
