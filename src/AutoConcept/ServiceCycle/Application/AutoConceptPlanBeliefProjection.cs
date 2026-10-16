using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Projects the immutable world facts that the native Concept boundary revalidates.
/// Worker-planned and explicitly MCP-submitted actions must carry the same belief shape.
/// </summary>
internal static class AutoConceptPlanBeliefProjection
{
    internal static bool TryCreate(
        GameWorldState world,
        Guid recipeId,
        out AutoConceptPlanBelief belief,
        out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        belief = default;
        if (!WorldConceptRecipeLookup.TryFind(
                world.ConceptRecipes,
                recipeId,
                out var concept))
        {
            reason =
                "published concept-recipes has no AlchemyRecipeSO row for " +
                EntityIdentityFormatter.Format(recipeId);
            return false;
        }
        if (!WorldLookup.TryFind(world.AlchemyRecipes, recipeId, out var recipe))
        {
            reason =
                "published alchemy-recipes has no AlchemyRecipeSO row for " +
                EntityIdentityFormatter.Format(recipeId);
            return false;
        }
        if (!recipe.Discovered)
        {
            reason =
                "published alchemy recipe " + EntityIdentityFormatter.Format(recipeId) +
                " is not discovered";
            return false;
        }

        var maximum = recipe.ResolvedMaxUsageSlots.ToDouble();
        if (!double.IsFinite(maximum)) maximum = 0;
        var maximumQuantity =
            Math.Max(0, (int)Math.Min(int.MaxValue, Math.Floor(maximum)));
        var instanceFound = WorldAlchemyInstanceLookup.TryFind(
            world.AlchemyInstances,
            recipeId,
            out var instance);
        WorldAlchemyCostLookup.TryFindRange(
            world.AlchemyCosts,
            recipeId,
            WorldAlchemyCostKind.RecipeDrain,
            out _,
            out var authoredDrainResources);
        belief = new AutoConceptPlanBelief(
            instanceFound ? instance.Quantity : 0,
            instanceFound ? instance.QueuedQuantity : 0,
            maximumQuantity,
            concept.CoreTypeId,
            authoredDrainResources);
        reason = string.Empty;
        return true;
    }

#if SERVICE_CYCLE_PROFILE
    internal static bool TryResolveGameMcpTarget(
        AutoConceptActionKind kind,
        int requestedAmount,
        in AutoConceptPlanBelief belief,
        out int targetOrDelta,
        out string code,
        out string reason)
    {
        targetOrDelta = 0;
        if (belief.Quantity != belief.QueuedQuantity)
        {
            code = "concept_assignment_unsettled";
            reason =
                "published concept quantity " + belief.Quantity +
                " does not equal queued quantity " + belief.QueuedQuantity;
            return false;
        }
        try
        {
            switch (kind)
            {
                case AutoConceptActionKind.Add:
                    targetOrDelta = checked(belief.Quantity + requestedAmount);
                    break;
                case AutoConceptActionKind.RemoveOwned:
                    targetOrDelta = requestedAmount;
                    break;
                case AutoConceptActionKind.RotateOut
                    when requestedAmount == belief.Quantity:
                    targetOrDelta = belief.Quantity;
                    break;
                case AutoConceptActionKind.RotateOut:
                    code = "concept_rotation_quantity_mismatch";
                    reason =
                        "rotate_out must name the exact published active quantity " +
                        belief.Quantity + ", not " + requestedAmount;
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        catch (OverflowException)
        {
            code = "concept_amount_overflow";
            reason = "the requested concept amount overflows the native integer quantity";
            return false;
        }
        code = string.Empty;
        reason = string.Empty;
        return true;
    }
#endif
}
