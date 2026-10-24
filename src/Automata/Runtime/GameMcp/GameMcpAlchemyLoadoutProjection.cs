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
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpAlchemyLoadoutProjection
{
    internal static GameMcpValue Project(in AlchemyLoadoutSubmission submission)
    {
        if (submission.Outcome.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested Alchemy loadout transition";

        // A refusal whose sentence names a ceiling carries that same ceiling as a number, read from
        // the admission capture the sentence was written from.
        if (submission.MaximumAmount >= 0) result["maximumAmount"] = submission.MaximumAmount;
        return result.Freeze();
    }

    /// <summary>
    /// A recipe the loadout does not hold has no position at all — the game stores that as a
    /// negative index, which one-based arithmetic would print as a plausible slot 0, so the column
    /// says which recipes are out rather than going quiet about them.
    /// </summary>
    internal static GameMcpValue ProjectAlchemyLoadoutSummary(
        in WorldAlchemyLoadoutDecision decision)
    {
        var result = new JObject
        {
            ["recipeId"] = Named(decision.RecipeId),
            ["slot"] = decision.Position >= 0
                ? GameMcpSlotNumbering.Wire(decision.Position)
                : (object)GameMcpListColumns.Unslotted,
            ["slotCount"] = decision.SlotCount,
            ["amount"] = decision.Amount,
        };
        return result.Freeze();
    }

    /// <summary>
    /// A recipe the alchemy screen will not show is one the player cannot reach, and that is the
    /// whole of this category's lifecycle: <c>UIAlchemyRecipe.IsVisible()</c> is
    /// <c>AlchemyRecipeSO.IsAvailable()</c>, and a recipe is never finished — <c>maxLevel</c> is the
    /// level it has been taken to, not a ceiling it stops at.
    /// </summary>
    /// <remarks>
    /// <c>IsAvailable()</c> reads <c>discovered</c> for a <c>Discover</c> recipe and
    /// <c>visibilityPrerequisites.Check()</c> for a <c>Prerequisite</c> one, so the published
    /// <c>Discovered</c> is the answer only on the first branch. The gate is captured rather than
    /// assumed, and the second branch is a lock this suite cannot read without making the game latch
    /// a prerequisite container during collection — so it says
    /// <see cref="GameMcpListColumns.Unreadable"/> rather than guessing. All 125 authored recipes on
    /// the pinned build are <c>Discover</c>, so no row reaches that word today; the branch exists so
    /// that a build where one did would say so instead of quietly calling it locked.
    /// </remarks>
    internal static string AlchemyRecipeState(in WorldAlchemyRecipe recipe) =>
        recipe.VisibilityGate != WorldAlchemyRecipe.DiscoverGate
            ? GameMcpListColumns.Unreadable
            : recipe.Discovered
                ? GameMcpListColumns.Available
                : GameMcpListColumns.Locked;

    internal static GameMcpValue ProjectConceptDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = WorldAlchemyInstanceLookup.TryFind(
            state.World.Snapshot.AlchemyInstances, command.TargetId, out var current)
            ? current.Quantity
            : 0;
        var oldWorld = Before(command);
        var before = oldWorld is not null && WorldAlchemyInstanceLookup.TryFind(
            oldWorld.AlchemyInstances, command.TargetId, out var previous)
            ? previous.Quantity
            : 0;
        return Change(command.TargetId, before, after, "activeCount");
    }

    internal static GameMcpValue ProjectAlchemyLoadoutDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        if (!WorldAlchemyLoadoutLookup.TryFind(
                state.World.Snapshot.AlchemyLoadout, command.TargetId, out var current))
            return PostStateNotPublished("this Alchemy recipe");
        var oldWorld = Before(command);
        WorldAlchemyLoadoutDecision previous = default;
        var hadBefore = oldWorld is not null && WorldAlchemyLoadoutLookup.TryFind(
            oldWorld.AlchemyLoadout, command.TargetId, out previous);
        if (command.Mode == "move")
        {
            if (!current.IsActive || current.Position != command.Amount - 1)
                return PostStateUnavailable("requested_state_not_reached",
                    "the settled Alchemy loadout does not show the requested slot");
            return new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                ["slot"] = new JObject
                {
                    ["before"] = hadBefore
                        ? GameMcpSlotNumbering.Wire(previous.Position)
                        : (int?)null,
                    ["after"] = GameMcpSlotNumbering.Wire(current.Position),
                },
            }.Freeze();
        }
        return Change(command.TargetId,
            hadBefore ? (object)previous.TargetAmount : null,
            current.TargetAmount,
            "activeCount");
    }

    /// <summary>
    /// Every alchemy recipe's persistent block carries a <c>ScalingWeightEffectMod</c>, and
    /// <c>AlchemyInstance.ReengageRecipe</c> scales it by the instance's power and speed, so the
    /// authored amount is the card's number only at that one point.
    /// </summary>
    internal const string AlchemyLevelEffectsAt =
        "amounts are authored at Lv 1, 100 power and 100% speed; game_tooltip shows the card's " +
        "scaled number";

    /// <summary>
    /// Whether the game's Active Concepts list takes this recipe now, and when it does not, why.
    /// </summary>
    /// <remarks>
    /// The published assignments are the filled slots only, so a caller counting them cannot tell a
    /// full list from a half-empty one that refuses this particular recipe. The decision carries the
    /// pair, which is also the difference between "swap something out" and "this one, not now".
    /// </remarks>
    internal static JObject ConceptAddDecision(
        GameWorldState world,
        in WorldConceptRecipe recipe)
    {
        var result = new JObject { ["available"] = recipe.CanAddNow };
        if (recipe.CanAddNow) return result;
        var used = world.AlchemyInstances.Count;
        result["reasonCode"] = "slot_unavailable";
        // The pair rides beside this decision on both surfaces that publish it, so the sentence
        // says which of the two situations this is and leaves the counting to those two fields.
        result["reason"] = recipe.SlotCount > 0 && used >= recipe.SlotCount
            ? "Every Concept slot is in use."
            : "Slots are free, and the game still will not take this Concept into one.";
        return result;
    }

    internal static GameMcpValue ProjectAlchemyInstance(
        GameWorldState world,
        in WorldAlchemyInstance instance)
    {
        // `drainReadable` was a column whose only job was to explain the absence of the column
        // beside it. The ratio now carries both answers, so the page says it once.
        //
        // `settled` is gone for two reasons that agree: it is `activeCount == queuedCount`, which
        // the two columns beside it already show, and the only thing it ever reports is that a
        // change is still in flight — which the next read resolves without anyone doing anything.
        var result = new JObject
        {
            ["recipe"] = EntityReference(world, instance.RecipeId),
            ["activeCount"] = instance.Quantity,
            ["queuedCount"] = instance.QueuedQuantity,
            ["drainRatio"] = instance.DrainReadable
                ? new GameMcpDomainValue(instance.DrainRatio)
                : (object)GameMcpListColumns.Unreadable,
        };
        return result.Freeze();
    }

    internal static GameMcpValue ProjectAlchemyRecipe(
        GameWorldState world,
        in WorldAlchemyRecipe recipe)
    {
        var result = new JObject
        {
            ["entityId"] = recipe.EntityId.ToString("D"),
            ["category"] = GameMcpWorldCategories.AlchemyRecipeCategory(recipe.CoreTypeId),

            // `discovered` was this row's lifecycle under another name — it is what
            // AlchemyRecipeSO.IsAvailable() reads for every recipe the pinned build authors — so it
            // says it once, in the word the rest of the surface says it in. The raw field stays on
            // the category's fact scan.
            ["state"] = AlchemyRecipeState(in recipe),
            ["masteryLevel"] = recipe.MasteryLevel,
        };
        if (recipe.MaxLevel >= 0) result["maximumLevel"] = recipe.MaxLevel;
        if (WorldAlchemyInstanceLookup.TryFind(
                world.AlchemyInstances, recipe.RecipeId, out var instance))
        {
            result["activeCount"] = instance.Quantity;
            if (!instance.IsSettled) result["queuedCount"] = instance.QueuedQuantity;
        }
        AddAlchemyLoadoutDecision(world, result, recipe.RecipeId);
        AddUsageRequirementPenalty(world, result, in recipe);
        AddDiscoveryDecision(
            world,
            result,
            recipe.Discovery,
            AlchemyDiscoveryScreen(recipe.CoreTypeId),
            recipe.RecipeId);
        return result.Freeze();
    }

    /// <summary>
    /// What an unmet usage requirement costs while it stays unmet: the core alchemy type's
    /// <c>reqSpeedPenalty</c> and <c>reqCostPenalty</c>, which the recipe applies until
    /// <c>usagePrerequisites.Check()</c> holds. Absent where the type authors no penalty.
    /// </summary>
    internal static void AddUsageRequirementPenalty(
        GameWorldState world,
        JObject result,
        in WorldAlchemyRecipe recipe)
    {
        if (!WorldLookup.TryFind(world.AlchemyTypes, recipe.CoreTypeId, out var core) ||
            (core.RequirementSpeedPenalty.IsEmpty() && core.RequirementCostPenalty.IsEmpty()) ||
            WorldRequirementEvaluator.Evaluate(
                world, recipe.RecipeId, level: 0, WorldRequirementProgramKind.Usage) !=
                WorldRequirementVerdict.Unmet)
        {
            return;
        }

        var needs = GameMcpRequirementProjection.FirstUnmetUsageNeeds(world, recipe.RecipeId);
        var penalties = new List<string>(2);
        if (!core.RequirementSpeedPenalty.IsEmpty())
            penalties.Add("speed " + core.RequirementSpeedPenalty.ToStringValue());
        if (!core.RequirementCostPenalty.IsEmpty())
            penalties.Add("drain cost " + core.RequirementCostPenalty.ToStringValue());
        result["usageRequirement"] = (needs.Length == 0 ? "Usage requirement" : needs) +
            " not met: " + string.Join(", ", penalties) + " until it is";
    }

    /// <summary>Which of the two alchemy discovery screens draws this recipe, or none.</summary>
    internal static Guid AlchemyDiscoveryScreen(Guid coreTypeId) =>
        AlchemyGameplayDomainClassifier.ClassifyTypeUuid(coreTypeId) switch
        {
            AlchemyGameplayDomain.OrdinaryAlchemy => KnownEntities.AlchAlchemyDiscover.Uuid,
            AlchemyGameplayDomain.ScholarConcept => KnownEntities.ScholarConceptDiscover.Uuid,
            _ => Guid.Empty,
        };

    internal static void AddAlchemyLoadoutDecision(
        GameWorldState world,
        JObject result,
        Guid recipeId)
    {
        if (!WorldAlchemyLoadoutLookup.TryFind(world.AlchemyLoadout, recipeId, out var decision))
            return;
        var loadout = new JObject
        {
            ["activeCount"] = decision.Amount,
            ["targetAmount"] = decision.TargetAmount,
        };
        if (decision.IsActive)
            loadout["slot"] = GameMcpSlotNumbering.Wire(decision.Position);
        var addAvailable = decision.Discovered && decision.CanAdd && decision.MaximumAdd > 0;
        var add = new JObject { ["available"] = addAvailable };
        if (addAvailable)
        {
            add["maximumAmount"] = decision.MaximumAdd;
            add["freeUsesRemaining"] = decision.FreeUsesRemaining;
            var costs = ProjectAlchemyUsageCosts(world, recipeId);
            if (costs.Count > 0) add["usageCosts"] = costs;
        }
        else
        {
            add["reasonCode"] = !decision.Discovered
                ? "not_discovered"
                : !decision.CanAdd
                    ? "loadout_full"
                    : "usage_unavailable";
        }
        loadout["add"] = add;
        loadout["remove"] = decision.TargetAmount > 0
            ? new JObject { ["available"] = true, ["maximumAmount"] = decision.TargetAmount }
            : new JObject { ["available"] = false, ["reasonCode"] = "not_active" };
        result["alchemyLoadout"] = loadout;
    }

    internal static JArray ProjectAlchemyUsageCosts(GameWorldState world, Guid recipeId)
    {
        var result = new JArray();
        if (!WorldAlchemyLoadoutLookup.TryFindCostRange(
                world.AlchemyUsageCosts, recipeId, out var start, out var count))
            return result;
        for (var index = start; index < start + count; index++)
        {
            var cost = world.AlchemyUsageCosts[index];
            var amount = PlayerFacingCost(world, cost.ResourceId, cost.Amount);
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(amount),
            };
            if (WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
            {
                (row["cost"], row["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(
                    amount, SpendableAmount(world, cost.ResourceId, resource.Reading.Quantity));
            }
            result.Add(row);
        }
        return result;
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldAlchemyRecipe listedAlchemyRecipe,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = listedAlchemyRecipe.EntityId.ToString("D"),
                ["state"] = AlchemyRecipeState(in listedAlchemyRecipe),
                ["masteryLevel"] = listedAlchemyRecipe.MasteryLevel,
            }.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldAlchemyInstance alchemyInstance,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectAlchemyInstance(world, in alchemyInstance);
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldAlchemyLoadoutDecision alchemyLoadout,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectAlchemyLoadoutSummary(in alchemyLoadout);
    }

}
#endif
