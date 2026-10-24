#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

using static OrbAutomata.GameMcp.GameMcpEntityIdentity;
using static OrbAutomata.GameMcp.GameMcpRequirementProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpEntityPredicates
{

    internal static JObject Predicates(
        GameWorldState world,
        Guid id,
        EntityKind kind,
        bool unlockConditionsPublished)
    {
        // What a locked entity says when it is asked why. `native_unavailable` means "the game
        // refuses and names nothing", which is only true while no unlock block stands beside this
        // one; where the block exists it names the conditions, and this points at them.
        var lockedReason = unlockConditionsPublished
            ? "unlock_conditions_unmet"
            : "native_unavailable";
        var result = new JObject();
        switch (kind)
        {
            case EntityKind.Structure:
            {
                WorldLookup.TryFind(world.Structures, id, out var structure);
                result["available"] = Verdict(
                    structure.Reading.Unlocked,
                    lockedReason);
                result["canPurchase"] = PurchaseVerdict(
                    world, id, structure.Reading.Unlocked, lockedReason,
                    WorldRequirementEvaluator.Evaluate(
                        world,
                        id,
                        WorldRequirementEvaluator.StructureCheckLevel(in structure)));
                break;
            }
            case EntityKind.Upgrade:
            {
                WorldLookup.TryFind(world.Upgrades, id, out var upgrade);
                // The game's UpgradeSO.IsAvailable() goes false the moment an upgrade is maxed, so
                // a finished upgrade fell to the locked sentence — "the game keeps this locked, and
                // says nothing about what would unlock it" — for a thing that is done rather than
                // shut. `canPurchase` already knew the difference; both words read it now.
                if (upgrade.Reading.Available &&
                    !WorldUpgradeScreenLists.TryReadScreenGate(world, id, out _))
                {
                    result["available"] = Verdict(false, "owning_screen_unknown");
                    result["canPurchase"] = Verdict(false, "owning_screen_unknown");
                    break;
                }
                if (upgrade.Reading.Available &&
                    GameMcpScreenState.IsUpgradeScreenLocked(world, id, out var screenView))
                {
                    var sentence = GameMcpDecisionReason.ScreenLocked(screenView);
                    result["available"] = Verdict(false, "screen_locked", sentence);
                    result["canPurchase"] = Verdict(false, "screen_locked", sentence);
                    break;
                }
                result["available"] = Verdict(
                    upgrade.Reading.Available,
                    upgrade.IsExhausted ? "already_maxed" : lockedReason);
                result["canPurchase"] = PurchaseVerdict(
                    world, id, upgrade.Reading.Available && !upgrade.IsExhausted,
                    upgrade.IsExhausted ? "already_maxed" : lockedReason,
                    WorldRequirementEvaluator.Evaluate(
                        world,
                        id,
                        WorldRequirementEvaluator.UpgradeCheckLevel(in upgrade)));
                break;
            }
            case EntityKind.Research:
            {
                WorldLookup.TryFind(world.Research, id, out var research);
                result["visible"] = Verdict(
                    research.Visible,
                    "native_hidden");
                result["available"] = Verdict(
                    research.Available,
                    research.Complete ? "already_maxed" : lockedReason);
                // ResearchSO.IsWithinDevelopRange's own gates, in its own order, under the same
                // words and the same codes the research row uses for them. Cost is asked before
                // level requirements because the native method asks it there, and asking it at all
                // is what this predicate was missing: the price is inside IsWithinDevelopRange, so
                // an unaffordable node fell through every named gate and answered with the range
                // itself — one blocked action carrying "Needs 2 Ability Advancement (have 1)." on
                // the row and a range refusal eight lines below it, pointing a reader at a gate
                // that was not the problem. Leeway and the two caps are one gate: native develops
                // on leeway OR on being below both caps, so an exhausted leeway beside an open cap
                // is not what refused. CanDevelop() adds !IsDeveloping() on top of the whole
                // expression, so a running development is asked last.
                var decision = research.Decision;
                var reason = !research.Visible
                    ? "research_locked"
                    : research.Complete
                    ? "already_maxed"
                    : decision.Available && !decision.DevelopmentCostAffordable
                        ? "unaffordable"
                        : !research.MeetsLevelRequirements
                            ? "requirements_unmet"
                            : !research.StillHasLeeway &&
                              !(research.BelowArtificialMaxLevel && research.BelowMaxInvestmentLevel)
                                ? "research_leeway_exhausted"
                                : !research.WithinDevelopRange
                                    ? "develop_range_refused"
                                    : research.IsDeveloping
                                        ? "already_developing"
                                        : research.CanDevelop
                                            ? "can_develop"
                                            : "native_can_develop_refused";

                // The shortfall sentence is the row's own, written from the same costs, so the two
                // are byte-identical and the per-field collapse folds the predicate away instead of
                // printing one refusal twice.
                var canDevelop = research.Visible && research.CanDevelop;
                result["canDevelop"] = reason switch
                {
                    "unaffordable" => Verdict(
                        canDevelop,
                        reason,
                        GameMcpDecisionReason.ShortfallReason(world, decision.DevelopmentCosts)),
                    "research_locked" => Verdict(
                        canDevelop, reason, LockedResearchReason(world, id)),
                    _ => Verdict(canDevelop, reason),
                };
                break;
            }
            case EntityKind.SpellRecipe:
            {
                WorldLookup.TryFind(world.SpellRecipes, id, out var spell);
                var offered = GameMcpDiscoveryTreeOfferProjection.IsCurrentDiscoveryOffer(world, id);
                // A hidden row's whole answer used to be that it was hidden. The book it is behind
                // is the one thing a caller can act on, and the row beside these predicates now
                // names it, so the predicates say the same sentence rather than a shorter one.
                var hidden = GameMcpRecipeBookProjection.MissingRecipeBookReason(world, in spell);
                var visible = hidden.Length > 0
                    ? Verdict(
                        spell.Discovered || !spell.HiddenDiscovery || offered,
                        "hidden_discovery",
                        hidden)
                    : Verdict(
                        spell.Discovered || !spell.HiddenDiscovery || offered,
                        "hidden_discovery");
                // One word per question. `visible` is whether the game draws this at all; it was
                // published a second time as `available`, where the row's own `discover` cell
                // already answers whether the Discover press is live. One entity then read
                // `discover: yes` beside `available: no` — two true answers to two questions
                // wearing one word, which is a flat contradiction to anyone reading it.
                result["visible"] = visible;
                var discoverable = !spell.Discovered && (!spell.HiddenDiscovery || offered);
                var discoverCode = spell.Discovered ? "already_discovered" : "hidden_discovery";
                result["canDiscover"] = !spell.Discovered && hidden.Length > 0
                    ? Verdict(discoverable, discoverCode, hidden)
                    : Verdict(discoverable, discoverCode);
                result["canUse"] = SpellCanUse(world, id);
                break;
            }
            case EntityKind.AlchemyRecipe:
            {
                WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy);
                // No `canAdd` predicate. The same response's `concept:` block publishes that
                // decision with its verdict and its reason, and the block's presence is itself the
                // answer to "is this assignable at all". A row here could only be a second copy of
                // that verdict or a pointer at it, and a pointer is machine vocabulary standing
                // where a player-facing value belongs.
                AddDiscoveryPredicates(result, world, id, alchemy.Discovered, nativeDiscoverable: true);
                break;
            }
            case EntityKind.CraftingRecipe:
            {
                WorldLookup.TryFind(world.CraftingRecipes, id, out var recipe);
                var visible = Verdict(
                    recipe.Reading.Visible,
                    recipe.Reading.VisibilityReasonCode);
                result["visible"] = visible;
                result["canPurchase"] = Verdict(
                    recipe.Reading.CanBuyAtStartingQuantity,
                    recipe.Reading.NativePurchaseReasonCode);
                break;
            }
            case EntityKind.Consumable:
            {
                WorldLookup.TryFind(world.Consumables, id, out var consumable);
                var visible = Verdict(
                    consumable.Visible,
                    "not_visible");
                result["visible"] = visible;
                result["canUse"] = Verdict(
                    consumable.CanFire,
                    consumable.Quantity <= 0 ? "none_owned" : "native_can_fire_refused");
                break;
            }
            // A resource has no predicate of its own. Its one evaluated fact is whether the game
            // counts it in its resource list, and that rides on the row as `inLedger`, where it can
            // carry the sentence that says what the bit means. It used to be published here twice —
            // `visible` and a copy of it under `available` — so a resource with a computed capacity,
            // a bar on the open screen and another entity's price quoted in it read as unavailable.
            case EntityKind.Resource:
                break;
            case EntityKind.Ritual:
            {
                WorldLookup.TryFind(world.Rituals, id, out var ritual);
                AddDiscoveryPredicates(result, world, id, ritual.Discovered, nativeDiscoverable: true);
                break;
            }
            case EntityKind.Glyph:
            {
                WorldLookup.TryFind(world.AugmentGlyphs, id, out var glyph);

                // The game cannot show a glyph it will not offer: GlyphSO.IsVisible() is a call to
                // IsAvailable(), and the picker tile's own IsVisible() calls IsAvailable() too. The
                // two verdicts are one fact, so the discovery-shaped predicate that used to stand in
                // for `visible` contradicted `state` on all 25 unlockers at once — for them the raw
                // discovered field is not what availability reads, and no unlocker is ever a
                // discovery-tree offer, so both of that predicate's inputs were the wrong facts.
                // Whether an undiscovered augment is on offer right now is a real fact and rides on
                // the row's own `discover` block.
                var picker = Verdict(
                    glyph.Learned,
                    glyph.Discoverable ? "not_discovered" : "prerequisites_unmet");
                result["visible"] = picker;
                result["canDiscover"] = Verdict(
                    !glyph.Discovered && glyph.Discoverable,
                    glyph.Discovered
                        ? "already_discovered"
                        : !glyph.Discoverable
                            ? "native_not_discoverable"
                            : "discovery_unavailable");
                break;
            }
            case EntityKind.Equipment:
            {
                WorldLookup.TryFind(world.Equipment, id, out var equipment);
                AddDiscoveryPredicates(result, world, id, equipment.IsCreated, nativeDiscoverable: true);
                break;
            }
            case EntityKind.TimeRune:
            {
                WorldLookup.TryFind(world.TimeRunes, id, out var timeRune);
                AddDiscoveryPredicates(result, world, id, timeRune.Discovered, nativeDiscoverable: true);
                break;
            }
        }
        return result;
    }

    internal static void AddDiscoveryPredicates(
        JObject result,
        GameWorldState world,
        Guid id,
        bool discovered,
        bool nativeDiscoverable)
    {
        var offered = GameMcpDiscoveryTreeOfferProjection.IsCurrentDiscoveryOffer(world, id);
        var visible = discovered || offered;
        result["visible"] = Verdict(
            visible,
            "not_discovered_or_offered");
        result["canDiscover"] = Verdict(
            !discovered && nativeDiscoverable,
            discovered
                ? "already_discovered"
                : !nativeDiscoverable
                    ? "native_not_discoverable"
                    : "discovery_unavailable");
    }

    internal static JObject SpellCanUse(GameWorldState world, Guid recipeId)
    {
        var found = false;
        var ready = false;
        var reason = "spell_not_equipped";
        var slots = new JArray();
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || slot.SpellRecipeId != recipeId) continue;
            found = true;
            ready |= slot.CastReady;
            if (!slot.CastReady)
            {
                reason = !slot.ResourcesCovered
                    ? "resources_uncovered"
                    : !slot.ChargeAvailable
                        ? "charge_unavailable"
                        : slot.Attuning
                            ? "attuning"
                            : slot.Casting || slot.ReadyingCast
                                ? "cast_in_progress"
                                : "native_can_cast_refused";
            }
            // The slot number, not the slot. The same response already carries every equipped
            // instance in full under `equipped`, so reprinting each one here answered a predicate by
            // echoing the block above it; the number is what points at that block and what every
            // spell verb takes.
            slots.Add(GameMcpSlotNumbering.Wire(slot.SlotIndex));
        }
        var result = Verdict(
            found && ready,
            found ? reason : "spell_not_equipped");
        if (slots.Count > 0) result["slots"] = slots;
        return result;
    }

    /// <summary>
    /// One grammar for every yes/no on the page. The explainer's verdicts answered under
    /// <c>value</c> while every decision elsewhere answers under <c>available</c>, so a caller
    /// reading both had to learn that two words mean the same thing.
    /// </summary>
    internal static JObject Verdict(
        bool value,
        string falseReason) => new()
    {
        ["available"] = value,
        ["reasonCode"] = value ? "passed" : falseReason,
    };

    /// <summary>
    /// A verdict that carries the same sentence the row action beside it writes, for a gate they
    /// both judge. The sentence has to be here rather than left to the code's own table because the
    /// row writes its own from the numbers it holds, and a predicate answering the table's generic
    /// line would disagree with its twin word for word and survive the collapse.
    /// </summary>
    internal static JObject Verdict(
        bool value,
        string falseReason,
        string falseSentence)
    {
        var verdict = Verdict(value, falseReason);
        if (!value) verdict["reason"] = falseSentence;
        return verdict;
    }

    /// <remarks>
    /// The purchase verdict is the AND the game's own button is enabled on, and the requirement
    /// leg was missing from it: a row whose screen says "Has Requirements" answered
    /// <c>canPurchase: yes</c> while the requirement verdict two lines below it in the same block
    /// said Unmet. An unevaluable requirement refuses too — never treat this suite's own gap as a
    /// requirement that holds.
    /// </remarks>
    internal static JObject PurchaseVerdict(
        GameWorldState world,
        Guid id,
        bool available,
        string unavailableReason,
        WorldRequirementVerdict requirements)
    {
        if (!available) return Verdict(false, unavailableReason);
        if (requirements != WorldRequirementVerdict.Met)
        {
            return Verdict(
                false,
                requirements == WorldRequirementVerdict.Unmet
                    ? "requirements_unmet"
                    : "requirement_unevaluable");
        }
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, id, out var start, out var count) || count == 0)
            return Verdict(false, "price_unavailable");
        var cost = world.PurchaseCosts[start];
        if (!cost.AffordabilityEvaluated)
            return Verdict(false, "affordability_unavailable");
        return Verdict(cost.Affordable,
            cost.Affordable ? "passed" : "unaffordable");
    }
}
#endif
