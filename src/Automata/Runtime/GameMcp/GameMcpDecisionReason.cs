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

        // Progress and state
        "already_active" => "This is already running.",
        "already_stopped" => "This is already stopped.",
        "already_developing" => "A development is already running on this research.",
        "already_in_requested_state" => "This is already in the state the call asked for.",
        "recipe_incomplete" => "The station has no complete recipe loaded.",
        "cooldown_active" => "This is still on cooldown.",
        "inventory_busy" => "The inventory is busy, so nothing can be used right now.",
        "invalid_state" => "This challenge is not in a state that can be activated.",
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
