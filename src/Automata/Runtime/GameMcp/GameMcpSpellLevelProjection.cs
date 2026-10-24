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
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellLevelProjection
{
    /// <summary>What a bought mastery level was charged, from the price its own press paid.</summary>
    internal static GameMcpValue? Project(in SpellLevelSubmission submission, GameWorldState world)
    {
        if (!submission.Outcome.Verified || submission.Charges.Length == 0) return null;
        var result = new GameMcpObjectBuilder();
        GameMcpChargeProjection.Add(result, world, submission.Charges);
        return result.Freeze();
    }

    /// <summary>
    /// What the mastery-experience ring actually says, rather than the ring.
    /// </summary>
    /// <remarks>
    /// The buffer is a fixed-size window the game overwrites, and the sources feeding it repeat on a
    /// short cycle: reading it row by row cost four full pages to deliver about fifteen distinct
    /// facts, and the only column that varied down the page was a monotone counter. The page says
    /// each distinct source once with how many of the window's samples it earned, and names the
    /// window itself so a caller can tell one read's window from the next.
    /// </remarks>
    /// <summary>
    /// The distinct sources in the ring's current window, and how many samples each earned.
    /// </summary>
    internal static void MasteryExperienceSources(
        GameWorldState world,
        List<(MasteryExperienceDomain Domain, Guid SourceId, int SourceMastery)> keys,
        List<int> counts)
    {
        var samples = world.MasteryExperience;
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var key = (sample.Domain, sample.SourceId, sample.SourceMastery);
            var found = false;
            for (var slot = 0; slot < keys.Count; slot++)
            {
                if (!keys[slot].Equals(key)) continue;
                counts[slot]++;
                found = true;
                break;
            }
            if (found) continue;
            keys.Add(key);
            counts.Add(1);
        }
    }

    internal static JObject MasteryExperienceSummary(
        WorldPublication<GameWorldState> publication,
        GameMcpEntityCapabilityDescriptor category,
        int offset,
        int limit)
    {
        var world = publication.Snapshot;
        var samples = world.MasteryExperience;
        var keys = new List<(MasteryExperienceDomain Domain, Guid SourceId, int SourceMastery)>();
        var counts = new List<int>();
        MasteryExperienceSources(world, keys, counts);

        var rows = new JArray();
        var end = Math.Min(keys.Count, checked(offset + limit));
        for (var index = Math.Min(offset, keys.Count); index < end; index++)
        {
            rows.Add(new JObject
            {
                ["count"] = counts[index],
                ["domain"] = keys[index].Domain.ToString(),
                ["sourceMastery"] = keys[index].SourceMastery,
                ["sourceId"] = keys[index].SourceId.ToString("D"),
                ["category"] = "mastery-experience",
                ["addressable"] = false,
            });
        }

        var result = new JObject();
        if (samples.Count > 0)
        {
            result["window"] = new JObject
            {
                ["samples"] = samples.Count,
                ["firstSequence"] = samples[0].Sequence,
                ["lastSequence"] = samples[samples.Count - 1].Sequence,
            };
        }
        if (rows.Count == 0)
        {
            result["columns"] = GameMcpWireColumns.WireColumns(
                category.ListColumns);
        }
        result["rows"] = rows;
        result["total"] = keys.Count;
        if (end < keys.Count) result["nextOffset"] = end;
        return result;
    }

    internal static GameMcpValue ProjectSpellLevelDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null ||
            !WorldLookup.TryFind(state.World.Snapshot.SpellRecipes, command.TargetId, out var current))
            return PostStateNotPublished("this spell");
        var oldWorld = Before(command);
        var before = oldWorld is not null && WorldLookup.TryFind(
            oldWorld.SpellRecipes, command.TargetId, out var previous)
            ? previous.MasteryLevel
            : (int?)null;
        // What the level cost is the press's to say, from the price it paid: two balance readings
        // around a press also move with every other spend and income in the window.
        return new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["mastery"] = new JObject { ["before"] = before, ["after"] = current.MasteryLevel },
        }.Freeze();
    }

    internal static GameMcpValue ProjectSpellLevelAllPostState(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var oldWorld = Before(command);
        var spells = new JArray();
        for (var index = 0; index < world.SpellRecipes.Count; index++)
        {
            var recipe = world.SpellRecipes[index];
            if (!recipe.Discovered) continue;
            var before = oldWorld is not null && WorldLookup.TryFind(
                oldWorld.SpellRecipes, recipe.EntityId, out var previous)
                ? previous.MasteryLevel
                : (int?)null;
            if (before.HasValue && before.Value == recipe.MasteryLevel) continue;
            spells.Add(new JObject
            {
                ["spellRecipeId"] = recipe.EntityId.ToString("D"),
                ["mastery"] = new JObject
                {
                    ["before"] = before,
                    ["after"] = recipe.MasteryLevel,
                },
            });
        }
        return new JObject { ["changedSpells"] = spells }.Freeze();
    }

    /// <summary>
    /// The Confirm Mastery button on the spell's card: its price, whether a press lands, and the
    /// type XP it grants each of the spell's types.
    /// </summary>
    /// <remarks>
    /// The button reads <c>IsReadyToLevelMastery()</c>, which is the mastery bar alone, and is
    /// priced by <c>GetLevelCost()</c>. The spell's leveling requirements are the one gate no
    /// publication carries, so a yes says the press settles them. <c>PurchaseLevel()</c> grants the
    /// authored <c>spellTypes</c>, which is the list the card draws its type tracks from.
    /// </remarks>
    internal static JObject ProjectConfirmMastery(GameWorldState world, in WorldSpellRecipe recipe)
    {
        var available = recipe.MasteryLevelReady && recipe.MasteryLevelAffordable;
        var result = new JObject { ["available"] = available };
        if (available)
            result["notYetChecked"] = "Its leveling requirements, which the press settles.";
        else
            result["reason"] = MasteryRefusalReason(world, recipe.EntityId);
        var grants = ProjectMasteryGrants(world, recipe.EntityId);
        if (grants.Count > 0) result["grants"] = grants;
        if (!OwnedMasteryCostMath.TryFindRange(
                world.MasteryCosts, recipe.EntityId, out var start, out var count) || count <= 0)
            return result;
        var costs = new JArray();
        for (var index = start; index < start + count; index++)
        {
            var cost = world.MasteryCosts[index];
            var price = PlayerFacingCost(world, cost.ResourceId, cost.Amount);
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(price),
            };
            if (TryFindResource(world, cost.ResourceId, out var pool))
            {
                (row["cost"], row["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(
                    price, WorldResourceCoordinate.SpendableAmount(in pool));
            }
            row["affordable"] = cost.Affordable;
            costs.Add(row);
        }
        result["costs"] = costs;
        return result;
    }

    internal static JArray ProjectMasteryGrants(GameWorldState world, Guid recipeId)
    {
        var grants = new JArray();
        if (!WorldSpellGraphLookup.TryFindRelations(
                world.SpellRelations, recipeId, out var start, out var count))
            return grants;
        for (var index = start; index < start + count; index++)
        {
            var relation = world.SpellRelations[index];
            if (relation.Kind != WorldSpellRelationKind.SpellType ||
                !WorldLookup.TryFind(world.SpellTypes, relation.TargetId, out var spellType) ||
                !WorldSpellMasteryGrant.TryTypeXp(world, in spellType, out var typeXp))
                continue;
            grants.Add(new JObject
            {
                ["spellTypeId"] = relation.TargetId.ToString("D"),
                ["typeXp"] = new GameMcpDomainValue(typeXp),
            });
        }
        return grants;
    }

    /// <summary>
    /// Why one spell's next mastery level was refused: no level is ready, or one is and the price
    /// is short by a named amount.
    /// </summary>
    /// <remarks>
    /// The two were one sentence — <c>This spell has no ready mastery level whose cost you can
    /// afford</c> — and a round could not tell which half it had met. It spent a mastery listing,
    /// two navigations, a refused tooltip, a screenshot and a screen read to find out, while the
    /// research verb two calls earlier had answered the same shape of question in eighty-nine
    /// bytes. The game publishes both halves: <c>IsReady()</c> rides on the recipe and
    /// <c>GetLevelCost()</c> rides in the mastery cost table, so the sentence is composed from
    /// them rather than left as the union of two answers.
    /// </remarks>
    internal static string MasteryRefusalReason(GameWorldState world, Guid recipeId)
    {
        if (!WorldLookup.TryFind(world.SpellRecipes, recipeId, out var recipe))
            return string.Empty;
        if (!recipe.MasteryLevelReady)
        {
            return EntityIdentityFormatter.PlayerName(recipeId, world.EntityIdentities) +
                " has no mastery level ready to buy: its mastery bar fills by casting it.";
        }
        if (!OwnedMasteryCostMath.TryFindRange(
                world.MasteryCosts, recipeId, out var start, out var count) || count <= 0)
        {
            return string.Empty;
        }
        var rows = new List<(string, BigDouble, BigDouble)>();
        for (var index = start; index < start + count; index++)
        {
            var cost = world.MasteryCosts[index];

            // What is held and whether it covers the price are the same reading, so a resource the
            // world carries no row for is left out rather than named with a holding of zero.
            if (cost.Affordable ||
                !WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
            {
                continue;
            }
            var identity = EntityIdentityFormatter.Describe(
                cost.ResourceId, world.EntityIdentities);
            rows.Add((
                identity.HasName ? identity.Name : cost.ResourceId.ToString("D"),
                PlayerFacingCost(world, cost.ResourceId, cost.Amount),
                SpendableAmount(world, cost.ResourceId, resource.Reading.Quantity)));
        }
        return GameMcpDecisionReason.Shortfall(rows);
    }

    internal static string LevelAllLockedReason(GameWorldState world)
    {
        var unlock = KnownEntities.UnlockLevelAllSpells.Uuid;
        var name = EntityIdentityFormatter.PlayerName(unlock, world.EntityIdentities);
        if (WorldLookup.TryFind(world.Upgrades, unlock, out var upgrade) &&
            upgrade.Reading.QueuedLevels > 0)
        {
            return "Level All Spells is locked until " + name +
                " finishes developing in the action queue.";
        }
        var text = new StringBuilder("Level All Spells is locked until ")
            .Append(name)
            .Append(" is bought");
        if (WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, unlock, out var start, out var count) && count > 0)
        {
            text.Append(" (");
            for (var index = start; index < start + count; index++)
            {
                var cost = world.PurchaseCosts[index];
                if (index > start) text.Append(", ");
                text.Append(GameMcpNumberFormatter.Format(AdmittedCost(world, in cost)))
                    .Append(' ')
                    .Append(EntityIdentityFormatter.PlayerName(
                        cost.ResourceId, world.EntityIdentities));
            }
            text.Append(')');
        }
        return text.Append('.').ToString();
    }

}
#endif
