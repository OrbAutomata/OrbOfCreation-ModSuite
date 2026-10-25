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

internal static class GameMcpEntityBlockers
{

    internal static JObject? Purchase(GameWorldState world, Guid id, EntityKind kind)
    {
        if (kind is not EntityKind.Structure and not EntityKind.Upgrade)
            return null;
        if (kind == EntityKind.Structure &&
            (!WorldLookup.TryFind(world.Structures, id, out var structure) ||
             !structure.Reading.Unlocked))
            return null;
        if (kind == EntityKind.Upgrade &&
            (!WorldLookup.TryFind(world.Upgrades, id, out var upgrade) ||
             !GameMcpScreenState.IsUpgradeOffered(world, in upgrade) || upgrade.IsExhausted))
            return null;
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, id, out var start, out var count))
        {
            return new JObject
            {
                ["evaluated"] = false,
                ["reasonCode"] = "exact_cost_unavailable",
            };
        }
        var rows = new JArray();
        for (var index = 0; index < count; index++)
        {
            var row = world.PurchaseCosts[start + index];
            rows.Add(GameMcpDecisionReason.ProjectPurchaseCost(world, in row));
        }

        // No outer status around the array: each row already carries its own verdict, and a row
        // without one was not evaluated.
        return new JObject
        {
            ["rows"] = rows,
        };
    }

    internal static JObject Blockers(GameWorldState world, Guid id, EntityKind kind)
    {
        var result = new JObject();

        if (kind is EntityKind.Structure or EntityKind.Upgrade)
        {
            JObject queue;
            if (WorldLookup.TryFind(
                    world.ActionQueues, KnownEntities.ActiveActionables.Uuid, out var actionQueue))
            {
                queue = Blocker(
                    !actionQueue.Consistent || !actionQueue.HasEmptySlot,
                    !actionQueue.Consistent
                        ? "queue_reading_inconsistent"
                        : actionQueue.HasEmptySlot ? "queue_room_available" : "queue_full");
            }
            else
            {
                queue = Blocker(true, "queue_not_published");
            }
            result["queue"] = queue;
        }
        if (kind == EntityKind.Upgrade && WorldLookup.TryFind(world.Upgrades, id, out var upgrade))
        {
            result["cap"] = Blocker(upgrade.IsExhausted, upgrade.IsExhausted
                ? "level_cap_reached"
                : "below_level_cap");
        }
        else if (kind == EntityKind.Research &&
                 WorldLookup.TryFind(world.Research, id, out var research))
        {
            var slack = research.Modifiers.LeewayPoints.ToInt();
            // Leeway blocks only together with the caps: native develops on leeway OR on being
            // below both caps, so an exhausted leeway beside an open cap is not a blocker. The
            // reason has to say the same thing the verdict does. Three states, three answers.
            var capsOpen =
                research.BelowArtificialMaxLevel && research.BelowMaxInvestmentLevel;
            var leewayBlocks = !research.StillHasLeeway && !capsOpen;
            var leeway = Blocker(
                leewayBlocks,
                research.StillHasLeeway
                    ? "native_leeway_available"
                    : leewayBlocks
                        ? "native_leeway_exhausted"
                        : "native_develops_below_caps");
            // The four numbers behind the verdict ride only where the verdict is a no. Every one of
            // them is published one block down in `researchThresholds` — `current`, `leeway`,
            // `effectiveThreshold`, `nativeMeetsLevelRequirements` — which is always beside this on
            // a research page, so an open axis was spending them on a reader who already had them.
            if (leewayBlocks)
            {
                leeway["currentTotalLevel"] = research.TotalLevel;
                leeway["leeway"] = slack;
                leeway["effectiveRequirement"] = research.EffectiveRequirementLevel;
                leeway["nativeMeetsLevelRequirements"] = research.MeetsLevelRequirements;
            }
            result["leeway"] = leeway;
            var cap = Blocker(
                research.Complete || !research.BelowArtificialMaxLevel ||
                    !research.BelowMaxInvestmentLevel,
                research.Complete
                    ? "already_maxed"
                    : !research.BelowArtificialMaxLevel
                        ? "artificial_research_cap_reached"
                        : !research.BelowMaxInvestmentLevel
                            ? "research_investment_cap_reached"
                            : "below_research_cap");
            // The four level readings the row above already prints under these same words ride only
            // where the cap is what refused; the cap's own facts ride always. An open axis restating
            // the row was 943 bytes of one round on a block whose name the caller never once wrote.
            if ((bool)cap["blocked"]!)
            {
                cap["queuedLevels"] = GameMcpResearchProjection.ResearchQueuedLevels(in research);
                cap["purchasedLevel"] = research.PurchasedLevels;
                cap["bonusLevel"] = research.BonusLevel;
                cap["totalLevel"] = research.TotalLevel;
            }
            cap["baseLevelExcludingBonus"] = research.BaseLevel;
            if (research.MaxLevel >= 0) cap["effectiveCap"] = research.MaxLevel;
            if (research.ArtificialMaxLevel >= 0)
                cap["artificialCap"] = research.ArtificialMaxLevel;
            cap["nativeComplete"] = research.Complete;
            result["cap"] = cap;
        }
        if (kind == EntityKind.SpellRecipe &&
            WorldLookup.TryFind(world.SpellRecipes, id, out var spell))
        {
            result["recipeDiscovery"] = Blocker(
                !spell.Discovered,
                spell.Discovered ? "recipe_discovered" : "recipe_not_discovered");
            var (bandwidth, drain) = SpellResourceBlockers(world, id);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }
        else if (kind == EntityKind.AlchemyRecipe &&
                 WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy))
        {
            result["recipeDiscovery"] = Blocker(
                !alchemy.Discovered,
                alchemy.Discovered ? "recipe_discovered" : "recipe_not_discovered");
            var (bandwidth, drain) = AlchemyResourceBlockers(world, id);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }
        else if (kind == EntityKind.CraftingRecipe &&
                 WorldLookup.TryFind(world.CraftingRecipes, id, out var crafting))
        {
            result["recipeDiscovery"] = Blocker(
                !crafting.Reading.Visible,
                crafting.Reading.VisibilityReasonCode);
            var bandwidth = CraftingBandwidthBlocker(world, in crafting);
            var drain = CraftingDrainBlocker(in crafting);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }

        return result;
    }

    internal static (JObject? Bandwidth, JObject? Drain) SpellResourceBlockers(
        GameWorldState world,
        Guid recipeId)
    {
        var bandwidthRows = new JArray();
        var drainRows = new JArray();
        var bandwidthBlocked = false;
        var drainBlocked = false;
        for (var slotIndex = 0; slotIndex < world.SpellSlots.Count; slotIndex++)
        {
            var slot = world.SpellSlots[slotIndex];
            if (!slot.Occupied || slot.SpellRecipeId != recipeId) continue;
            CollectSpellCosts(
                world, slot.SlotIndex, WorldSpellCostKind.Immediate,
                bandwidthRows, ref bandwidthBlocked);
            CollectSpellCosts(
                world, slot.SlotIndex, WorldSpellCostKind.Drain,
                drainRows, ref drainBlocked);
        }
        return (
            ResourceBlocker(bandwidthRows, bandwidthBlocked, reasonCode: "bandwidth_blocked"),
            ResourceBlocker(drainRows, drainBlocked, reasonCode: "drain_blocked"));
    }

    internal static void CollectSpellCosts(
        GameWorldState world,
        int slotIndex,
        WorldSpellCostKind kind,
        JArray rows,
        ref bool blocked)
    {
        if (!WorldSpellCostLookup.TryFindRange(
                world.SpellCosts, slotIndex, kind, out var start, out var count)) return;
        for (var index = 0; index < count; index++)
        {
            var cost = world.SpellCosts[start + index];
            var row = ResourceCostEvidence(world, cost.ResourceId, cost.Amount, out var oneBlocked);
            row["slot"] = GameMcpSlotNumbering.Wire(slotIndex);
            row["costKind"] = kind.ToString();
            rows.Add(row);
            blocked |= oneBlocked;
        }
    }

    internal static (JObject? Bandwidth, JObject? Drain) AlchemyResourceBlockers(
        GameWorldState world,
        Guid recipeId)
    {
        var bandwidthRows = ResourceCosts(
            world, recipeId, WorldAlchemyCostKind.RecipeDrain, out var bandwidthBlocked);
        var drainRows = ResourceCosts(
            world, recipeId, WorldAlchemyCostKind.CurrentDrain, out var drainBlocked);
        return (
            ResourceBlocker(bandwidthRows, bandwidthBlocked, reasonCode: "bandwidth_blocked"),
            ResourceBlocker(drainRows, drainBlocked, reasonCode: "drain_blocked"));
    }

    internal static JArray ResourceCosts(
        GameWorldState world,
        Guid recipeId,
        WorldAlchemyCostKind kind,
        out bool blocked)
    {
        blocked = false;
        var rows = new JArray();
        if (!WorldAlchemyCostLookup.TryFindRange(
                world.AlchemyCosts, recipeId, kind, out var start, out var count)) return rows;
        for (var index = 0; index < count; index++)
        {
            var cost = world.AlchemyCosts[start + index];
            var row = ResourceCostEvidence(world, cost.ResourceId, cost.Amount, out var oneBlocked);
            row["costKind"] = kind.ToString();
            rows.Add(row);
            blocked |= oneBlocked;
        }
        return rows;
    }

    internal static JObject ResourceCostEvidence(
        GameWorldState world,
        Guid resourceId,
        BigDouble amount,
        out bool blocked)
    {
        var available = BigDouble.Zero;
        var playerCost = amount;
        var bandwidth = false;
        if (WorldLookup.TryFind(world.Resources, resourceId, out var resource))
        {
            bandwidth = resource.Reading.Traits.BandwidthResource;
            available = WorldResourceCoordinate.SpendableAmount(in resource);
            playerCost = WorldResourceCoordinate.PlayerFacingCost(in resource, amount);
            blocked = !WorldResourceCoordinate.HasAmount(in resource, amount);
        }
        else
        {
            blocked = true;
        }
        var result = new JObject
        {
            ["resourceUuid"] = resourceId.ToString("D"),
            ["cost"] = ProjectNumber(playerCost),
            ["spendableAmount"] = ProjectNumber(available),
            ["affordable"] = !blocked,
            ["blocked"] = blocked,
        };
        if (bandwidth) result["bandwidth"] = true;
        if (blocked) result["reasonCode"] = "resource_or_headroom_insufficient";
        return result;
    }

    internal static JObject? CraftingBandwidthBlocker(
        GameWorldState world,
        in WorldCraftingRecipe recipe)
    {
        var rows = new JArray();
        var blocked = false;
        for (var index = 0; index < recipe.Resources.Count; index++)
        {
            var resource = recipe.Resources[index];
            if (resource.Kind != WorldCraftingRecipeResourceKind.AuthoredInput ||
                !resource.BandwidthResource) continue;
            var available = WorldLookup.TryFind(
                world.Resources, resource.ResourceId, out var resourceState);
            var oneBlocked = !available ||
                !WorldResourceCoordinate.HasAmount(in resourceState, resource.Amount);
            var spendable = available
                ? WorldResourceCoordinate.SpendableAmount(in resourceState)
                : BigDouble.Zero;
            blocked |= oneBlocked;
            rows.Add(new JObject
            {
                ["resourceUuid"] = resource.ResourceId.ToString("D"),
                ["cost"] = ProjectNumber(resource.Amount),
                ["spendableAmount"] = ProjectNumber(spendable),
                ["affordable"] = !oneBlocked,
                ["bandwidth"] = true,
                ["blocked"] = oneBlocked,
            });
        }
        return ResourceBlocker(rows, blocked, reasonCode: "bandwidth_blocked");
    }

    internal static JObject? CraftingDrainBlocker(in WorldCraftingRecipe recipe)
    {
        var rows = new JArray();
        var blocked = false;
        for (var index = 0; index < recipe.DrainBlocks.Count; index++)
        {
            var row = recipe.DrainBlocks[index];
            rows.Add(new GameMcpDomainValue(row));
            blocked |= row.Blocked;
        }
        return ResourceBlocker(rows, blocked, reasonCode: "drain_blocked");
    }

    /// <summary>
    /// The blocked half of one resource axis, under the axis's own reason code.
    /// </summary>
    /// <remarks>
    /// A third axis added tomorrow would have inherited the same silence with nothing to signal it;
    /// a whole code passed in cannot.
    /// </remarks>
    internal static JObject? ResourceBlocker(JArray rows, bool blocked, string reasonCode)
    {
        if (rows.Count == 0) return null;
        var result = new JObject
        {
            ["blocked"] = blocked,
            ["rows"] = rows,
        };
        if (blocked) result["reasonCode"] = reasonCode;
        return result;
    }

    internal static JObject Blocker(bool blocked, string reasonCode) => new()
    {
        ["blocked"] = blocked,
        ["reasonCode"] = reasonCode,
    };
}
#endif
