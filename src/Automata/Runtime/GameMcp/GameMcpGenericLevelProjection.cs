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
using static OrbAutomata.GameMcp.GameMcpDiscoveryTreeOfferProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;

namespace OrbAutomata.GameMcp;

internal static class GameMcpGenericLevelProjection
{
    /// <summary>
    /// What the press bought and what the game asked for it.
    /// </summary>
    /// <remarks>
    /// A ×5 rune buy off a ladder whose first level is free answered <c>free: yes</c> while Time
    /// Advancements fell 94 → 84. The press is the only thing that saw all five prices, so the
    /// press is what answers: <c>free</c> when every level it bought asked for nothing, and
    /// otherwise what it was charged, for how many levels.
    /// </remarks>
    internal static GameMcpValue Project(in GenericLevelSubmission submission, GameWorldState world)
    {
        var result = new GameMcpObjectBuilder();
        if (!submission.Outcome.Verified)
        {
            if (submission.Outcome.CallOutcome.MutationAttempts > 0)
                result["missingOutcome"] = "requested level increase";
            return result.Freeze();
        }
        if (submission.LevelsCharged == 0) return result.Freeze();
        GameMcpChargeProjection.Add(result, world, submission.Charges);
        if (!submission.ChargedNothing) result["chargedLevels"] = submission.LevelsCharged;
        return result.Freeze();
    }

    internal static GameMcpValue ProjectGenericLevelDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        if (!TryFindLevelDecision(
                state.World.Snapshot, command.TargetId, command.DerivedNativeType,
                out var current))
            return PostStateNotPublished("the thing you levelled");
        var oldWorld = Before(command);
        WorldLevelableDecision previous = default;
        var hadBefore = oldWorld is not null && TryFindLevelDecision(
            oldWorld, command.TargetId, command.DerivedNativeType, out previous);
        var currentPaid = current.TotalLevel - current.BonusLevels;
        var previousPaid = hadBefore ? previous.TotalLevel - previous.BonusLevels : (int?)null;
        var result = new JObject { ["uuid"] = command.TargetId.ToString("D") };
        if (command.Mode == "bonus")
        {
            if (hadBefore && current.BonusLevels <= previous.BonusLevels)
                return PostStateUnavailable("requested_state_not_reached",
                    "the settled world does not show a higher bonus level");
            result["bonusLevel"] = new JObject
            {
                ["before"] = hadBefore ? previous.BonusLevels : (int?)null,
                ["after"] = current.BonusLevels,
            };
        }
        else
        {
            if (hadBefore && currentPaid <= previousPaid)
                return PostStateUnavailable("requested_state_not_reached",
                    "the settled world does not show a higher paid level");
            result["paidLevel"] = new JObject
            {
                ["before"] = previousPaid,
                ["after"] = currentPaid,
            };
            // The game's headroom can be below the ask, and one level bought against an ask of two
            // settled into an answer indistinguishable from a satisfied amount=1. What was asked is
            // said only when it differs from what arrived.
            if (hadBefore && command.Amount > currentPaid - previousPaid!.Value)
            {
                result["requestedAmount"] = command.Amount;
                result["deliveredAmount"] = currentPaid - previousPaid.Value;
            }
        }
        result["totalLevel"] = new JObject
        {
            ["before"] = hadBefore ? previous.TotalLevel : (int?)null,
            ["after"] = current.TotalLevel,
        };

        // What the press cost is not answered here. This projector sees two worlds, and a ladder
        // prices every rung on its own, so the world standing before the press knows only the
        // first one's price — which is how a ×5 rune buy off a free first rung read `free: yes`
        // while Time Advancements fell. The press saw all five prices and says so itself; see
        // GameMcpGenericLevelProjection.

        // What a glyph level buys is slots, and the game's own level panel says so in those words:
        // `[N] Slot` off GetMaxUsages() and `[M] Free Slot` off GetFreeUsages(). Those are the two
        // numbers the player watches move, so they are the two the post-state names.
        if (command.DerivedNativeType == "GlyphSO" &&
            WorldLookup.TryFind(state.World.Snapshot.AugmentGlyphs, command.TargetId, out var glyph))
        {
            WorldGlyph priorGlyph = default;
            var hadGlyph = oldWorld is not null &&
                WorldLookup.TryFind(oldWorld.AugmentGlyphs, command.TargetId, out priorGlyph);
            result["slots"] = new JObject
            {
                ["before"] = hadGlyph ? priorGlyph.MaximumUsages : (int?)null,
                ["after"] = glyph.MaximumUsages,
            };
            result["freeSlots"] = new JObject
            {
                ["before"] = hadGlyph ? priorGlyph.MaximumFreeUsages : (int?)null,
                ["after"] = glyph.MaximumFreeUsages,
            };
        }
        return result.Freeze();
    }

    internal static bool TryFindLevelDecision(
        GameWorldState world,
        Guid target,
        string nativeType,
        out WorldLevelableDecision decision)
    {
        if (nativeType == "EquipmentTypeSO" &&
            WorldLookup.TryFind(world.EquipmentTypes, target, out var equipment))
        {
            decision = equipment.LevelDecision;
            return true;
        }
        if (nativeType == "GlyphSO" &&
            WorldLookup.TryFind(world.AugmentGlyphs, target, out var glyph))
        {
            decision = glyph.LevelDecision;
            return true;
        }
        if (nativeType == "ResourceTypeSO" &&
            WorldLookup.TryFind(world.ResourceTypes, target, out var resourceType))
        {
            decision = resourceType.LevelDecision;
            return true;
        }
        if (nativeType == "TimeRuneSO" &&
            WorldLookup.TryFind(world.TimeRunes, target, out var timeRune))
        {
            decision = timeRune.LevelDecision;
            return true;
        }
        decision = default;
        return false;
    }

    internal static GameMcpValue ProjectTimeRune(GameWorldState world, in WorldTimeRune rune)
    {
        var result = new JObject
        {
            ["entityId"] = rune.EntityId.ToString("D"),
            ["category"] = "time-runes",
            ["discovered"] = rune.Discovered,
            ["masteryLevel"] = rune.MasteryLevel,
            ["masteryXp"] = new GameMcpDomainValue(rune.MasteryXp),
            ["masteryXpNeeded"] = new GameMcpDomainValue(rune.RequiredMasteryXp),
            ["seen"] = rune.Seen,
        };
        AddLevelDecision(world, result, rune.LevelDecision,
            rune.Discovered, "undiscovered");
        AddDiscoveryDecision(
            world,
            result,
            rune.Discovery,
            KnownEntities.TimeTimeRuneCreate.Uuid,
            rune.EntityId);
        return result.Freeze();
    }

    internal static void AddLevelDecision(
        GameWorldState world,
        JObject result,
        WorldLevelableDecision decision,
        bool targetAvailable = true,
        string targetReasonCode = "not_available",
        string targetReason = "",
        bool drawsLevel = true)
    {
        // A detail block is not a table, so a level the game draws nowhere says nothing at all
        // rather than printing a zero. `purchase` still prints and still names the screen that
        // would buy one, which is the whole of what a caller can act on.
        if (drawsLevel)
        {
            result["paidLevel"] = decision.TotalLevel - decision.BonusLevels;
            if (decision.SupportsBonus) result["bonusLevel"] = decision.BonusLevels;
            result["totalLevel"] = decision.TotalLevel;
        }

        var purchase = new JObject
        {
            ["available"] = targetAvailable && decision.CanPurchase && decision.PurchaseAffordable,
        };
        if (!targetAvailable)
        {
            purchase["reasonCode"] = targetReasonCode;
            // A screen word is the whole answer where the generic code says only that some screen
            // is locked; the caller needs to know which one and what buys it.
            if (targetReason.Length > 0) purchase["reason"] = targetReason;
        }
        else if (!decision.CanPurchase) purchase["reasonCode"] = "native_level_refused";
        else
        {
            purchase["affordable"] = decision.PurchaseAffordable;
            if (!decision.PurchaseAffordable) purchase["reasonCode"] = "unaffordable";
            purchase["costs"] = ProjectLevelCosts(world, decision.PaidCosts);
            if (AsksForNothing(decision.PaidCosts)) purchase["free"] = true;
        }
        result["purchase"] = purchase;

        if (!decision.SupportsBonus) return;
        var bonus = new JObject
        {
            ["available"] = targetAvailable &&
                decision.BonusResourcesVisible && decision.BonusAffordable,
        };
        if (!targetAvailable)
        {
            bonus["reasonCode"] = targetReasonCode;
            if (targetReason.Length > 0) bonus["reason"] = targetReason;
        }
        else if (!decision.BonusResourcesVisible) bonus["reasonCode"] = "resources_hidden";
        else
        {
            bonus["affordable"] = decision.BonusAffordable;
            if (!decision.BonusAffordable) bonus["reasonCode"] = "unaffordable";
            bonus["costs"] = ProjectLevelCosts(world, decision.BonusCosts);
            if (AsksForNothing(decision.BonusCosts)) bonus["free"] = true;
        }
        result["bonus"] = bonus;
    }

    /// <summary>
    /// Whether a level asks for nothing — either the game names no price at all, or it names one
    /// and every line of it is zero.
    /// </summary>
    /// <remarks>
    /// A round bought a glyph level and read <c>free: yes</c>, then bought a time-rune level whose
    /// own row said <c>cost: 0 of 94 Time Advancement</c> and got no <c>free</c> at all, because
    /// the test was "the game names no price" rather than "nothing is owed". Those are one fact to
    /// a caller and were wearing two shapes, one of them silence — and an absent field reads as a
    /// negative claim, which here would have been "you paid".
    /// </remarks>
    internal static bool AsksForNothing(PublicationTable<WorldLevelableCost> costs)
    {
        for (var index = 0; index < costs.Count; index++)
            if (costs[index].Amount > BigDouble.Zero) return false;
        return true;
    }

    /// <summary>
    /// A level's price, in the one price shape.
    /// </summary>
    /// <remarks>
    /// The sibling key is a different fact and stays — it answers whether the whole purchase is
    /// payable — while the column answers it per resource, which is what names the one that is
    /// short.
    /// </remarks>
    internal static JArray ProjectLevelCosts(
        GameWorldState world,
        PublicationTable<WorldLevelableCost> costs)
    {
        var result = new JArray();
        for (var index = 0; index < costs.Count; index++)
        {
            var cost = costs[index];
            var price = PlayerFacingCost(world, cost.ResourceId, cost.Amount);
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(price),
            };

            // What is held and whether it covers the price are the same reading, so they are
            // published together or not at all: a resource the world carries no row for has no
            // holding to state, and standing zero in for it would say the player has none.
            if (WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
            {
                var held = resource.Reading.Quantity;
                (row["cost"], row["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(
                    price, SpendableAmount(world, cost.ResourceId, held));
                row["affordable"] = CanAfford(world, cost.ResourceId, cost.Amount, held);
            }
            result.Add(row);
        }
        return result;
    }

}
#endif
