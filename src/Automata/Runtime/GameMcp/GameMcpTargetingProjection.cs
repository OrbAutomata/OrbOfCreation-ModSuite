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


using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpTargetingProjection
{
    internal static GameMcpValue Project(in TargetingSubmission submission)
    {
        var result = new JObject();
        if (submission.Outcome.Verified && submission.SubmittedTarget != Guid.Empty)
            result["submittedTargetUuid"] = submission.SubmittedTarget.ToString("D");
        if (submission.Outcome.Verified) return result.Freeze();
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "target request settlement";
        return result.Freeze();
    }

    internal static Guid SubmittedTarget(GameMcpValue? value)
    {
        if (value is not GameMcpObject obj) return Guid.Empty;
        for (var index = 0; index < obj.Properties.Count; index++)
        {
            var property = obj.Properties[index];
            if (property.Name == "submittedTargetUuid" &&
                property.Value is GameMcpScalar scalar && scalar.Value is string text &&
                Guid.TryParse(text, out var id)) return id;
        }
        return Guid.Empty;
    }

    internal static GameMcpValue ProjectTargetingPostState(GameMcpFrameContext state, Guid submittedTarget)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var result = new JObject();
        if (submittedTarget != Guid.Empty)
            result["submittedTarget"] = ProjectTargetCandidate(world, submittedTarget, -1);

        // A block that would only say nothing is pending does not appear. The game asks for a target
        // by putting a request up, so the block is the request, and no block is the answer that no
        // request is waiting — the same silence a spell with nothing to toggle already answers with.
        if (world.Targeting.Count > 0)
        {
            var request = world.Targeting[0];
            result["targeting"] = ProjectTargeting(world, in request, 0, int.MaxValue);
        }
        return result.Freeze();
    }

    /// <summary>
    /// The request the game is waiting on: who asked, and every structure it will accept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request that is not pending has no row, so a <c>pending</c> column could only ever say
    /// <c>yes</c> — the row's existence already is that fact, and a column with one value in every
    /// world where it can appear costs a reader a column and tells them nothing.
    /// </para>
    /// <para>
    /// <c>ownerNativeType</c> and <c>selectionType</c> were the requesting object's class and the
    /// selection strategy's class, spelled the way the game's own code spells them. The owner is
    /// named beside them in the words the player sees it in, and this verb offers two decisions —
    /// submit one candidate, or let the request pick — neither of which turns on either class. So
    /// they carried no decision fact to rename into player words, and a native type name is not
    /// something this surface says.
    /// </para>
    /// <para>
    /// Whether a random pick would land is whether there is anything to pick: the old
    /// <c>randomize</c> column was <c>candidates</c> being non-empty, restated one column over.
    /// The candidates are the answer, so they are the only place it is said.
    /// </para>
    /// </remarks>
    internal static GameMcpValue ProjectTargeting(
        GameWorldState world,
        in WorldTargetingRequest request,
        int offset,
        int limit)
    {
        var order = CandidatesByEffectiveLevel(world, in request);
        var rows = new JArray();
        for (var index = offset; index < order.Count && rows.Count < limit; index++)
        {
            var candidate = request.Candidates[order[index]];
            rows.Add(ProjectTargetCandidate(world, candidate.StructureId, candidate.Position));
        }
        var candidates = new JObject
        {
            ["rows"] = rows,
            ["total"] = order.Count,
        };
        var end = offset + rows.Count;
        if (end < order.Count) candidates["nextOffset"] = end;
        return new JObject
        {
            ["owner"] = request.OwnerName,
            ["candidates"] = candidates,
        }.Freeze();
    }

    /// <summary>
    /// The candidate order the caller chooses by: strongest effective level first, and the game's
    /// own order among candidates no published structure row covers.
    /// </summary>
    internal static List<int> CandidatesByEffectiveLevel(
        GameWorldState world, in WorldTargetingRequest request)
    {
        var order = new List<int>(request.Candidates.Count);
        var levels = new List<BigDouble>(request.Candidates.Count);
        for (var index = 0; index < request.Candidates.Count; index++)
        {
            var level = BigDouble.Zero;
            var id = request.Candidates[index].StructureId;
            for (var structureIndex = 0; structureIndex < world.Structures.Count; structureIndex++)
            {
                var structure = world.Structures[structureIndex];
                if (structure.EntityId != id) continue;
                level = structure.EffectiveLevel;
                break;
            }
            levels.Add(level);
            // Insertion sort keeps candidates the world publishes no level for in the order the
            // game listed them, and the list is one screen's worth of tiles.
            var slot = order.Count;
            while (slot > 0 && levels[order[slot - 1]] < level) slot--;
            order.Insert(slot, index);
        }
        return order;
    }

    internal static GameMcpValue ProjectTargetCandidate(GameWorldState world, Guid id, int position)
    {
        var identity = EntityIdentityFormatter.Describe(id, world.EntityIdentities);
        var result = new JObject { ["uuid"] = id.ToString("D") };
        if (identity.HasName) result["name"] = identity.Name;
        for (var index = 0; index < world.Structures.Count; index++)
        {
            var structure = world.Structures[index];
            if (structure.EntityId != id) continue;
            // The column the caller ranks by leads: this list is read to pick the strongest
            // candidate, and the number that decides it used to sit last behind three that do not.
            // The two counts under it are the ones the attribute's own badge owns, under the names
            // every other surface uses for them. Their sum has no badge, and a separate
            // work-in-flight flag only restates a queue the caller can already read.
            result["effectiveLevel"] = structure.EffectiveLevel;
            result["level"] = structure.Reading.Level.ToInt();
            result["queuedLevels"] = structure.Reading.QueuedLevels.ToInt();
            result["available"] = structure.Reading.Unlocked;
            break;
        }
        if (position >= 0) result["position"] = GameMcpSlotNumbering.Wire(position);
        return result.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldTargetingRequest targeting,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectTargeting(world, in targeting, nestedOffset, nestedLimit);
    }

}
#endif
