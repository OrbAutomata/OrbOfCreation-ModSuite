#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

using static OrbAutomata.GameMcp.GameMcpEntityBlockers;
using static OrbAutomata.GameMcp.GameMcpEntityIdentity;
using static OrbAutomata.GameMcp.GameMcpEntityPredicates;
using static OrbAutomata.GameMcp.GameMcpRequirementProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpEntityExplainer
{

    /// <summary>
    /// Everything a detail read says beyond identity and the published row: the decisions the game
    /// will accept, the requirement graph behind them, the exact price and what is holding it.
    /// </summary>
    /// <remarks>
    /// Silent for an id whose category this build evaluates no verdicts for. That id still answers
    /// — with its identity and its row — because "there is nothing more to say about this one" and
    /// "this one could not be read" are different answers and a missing block said both.
    /// </remarks>
    internal static void AddDetail(JObject result, GameWorldState world, Guid uuid)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!TryResolve(world, uuid, out var kind, out _, out _)) return;

        // A Concept recipe is an alchemy recipe the game also lets you assign to a slot. Answering
        // it under the one kind and dropping the other half answered a question the caller did not
        // ask, and the assignment state was reachable only from a second read.
        if (WorldConceptRecipeLookup.TryFind(world.ConceptRecipes, uuid, out var conceptRecipe))
        {
            result["concept"] = new JObject
            {
                ["assignedCount"] = WorldAlchemyInstanceLookup.TryFind(
                    world.AlchemyInstances, uuid, out var assignment)
                    ? assignment.Quantity
                    : 0,
                ["usedSlots"] = world.AlchemyInstances.Count,
                ["maximumSlots"] = conceptRecipe.SlotCount,
                ["canAdd"] = GameMcpAlchemyLoadoutProjection.ConceptAddDecision(world, in conceptRecipe),
            };
        }
        // Both blocks are always present. An entity with no applicable predicate and one whose
        // predicates were never evaluated are different answers, and an omitted key said both.
        var requirements = GameMcpRequirementProjection.Requirements(world, uuid, kind);

        // The requirement block is built first so the predicates beside it can read it. A predicate
        // that answered "the game says nothing about what would unlock this" while the block under
        // it listed exactly that sent a caller looking for an explanation that was one line away.
        result["predicates"] = Predicates(
            world, uuid, kind, requirements?["unlocksWhen"] is JObject);
        if (requirements is not null) result["requirements"] = requirements;
        var researchThresholds = ResearchThresholds(world, uuid, kind);
        if (researchThresholds is not null) result["researchThresholds"] = researchThresholds;
        var purchase = Purchase(world, uuid, kind);
        if (purchase is not null) result["purchase"] = purchase;
        result["blockers"] = Blockers(world, uuid, kind);
    }

    /// <summary>
    /// Whether the published world marks this entity as one the game is not drawing for the
    /// player: the detail read's own <c>visible</c> predicate, read where that predicate exists.
    /// </summary>
    internal static bool IsHidden(GameWorldState world, Guid uuid)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!TryResolve(world, uuid, out var kind, out _, out _)) return false;
        var visible = Predicates(world, uuid, kind, unlockConditionsPublished: false)["visible"];
        return visible is JObject verdict && verdict["available"] is false;
    }
}
#endif
