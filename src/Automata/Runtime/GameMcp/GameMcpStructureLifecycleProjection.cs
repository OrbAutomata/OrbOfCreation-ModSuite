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

using static OrbAutomata.GameMcp.GameMcpPurchaseProjection;
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpStructureLifecycleProjection
{
    internal static GameMcpValue Project(in NativeActionOutcome submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested enabled state";
        return result.Freeze();
    }

    /// <summary>
    /// A structure's whole lifecycle, which is two words. <c>StructureSO</c> carries no
    /// <c>maxLevel</c> field at all, so there is no level at which one is finished and no third
    /// word to reach. Its soft prerequisites are a development penalty rather than a gate — a
    /// structure builds worse with them unmet, not never — so they belong to the can-purchase
    /// axis and never to this column.
    /// </summary>
    internal static string StructureState(in WorldStructure structure) =>
        structure.Reading.Unlocked
            ? GameMcpListColumns.Available
            : GameMcpListColumns.Locked;

    internal static GameMcpValue ProjectStructureLifecycleDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = state.World.Snapshot;
        if (!WorldLookup.TryFind(after.Structures, command.TargetId, out var current))
            return PostStateNotPublished("this structure");
        var before = Before(command);
        WorldStructure previous = default;
        var hadBefore = before is not null &&
            WorldLookup.TryFind(before.Structures, command.TargetId, out previous);
        return Change(command.TargetId,
            hadBefore ? !previous.Reading.Disabled : (bool?)null,
            !current.Reading.Disabled,
            "enabled");
    }

    internal static GameMcpValue ProjectStructure(in WorldStructure structure)
    {
        var enabled = !structure.Reading.Disabled;
        var toggle = new JObject
        {
            ["available"] = structure.Reading.Unlocked,
        };
        if (structure.Reading.Unlocked)
            toggle["next"] = enabled ? "disable" : "enable";
        else
            toggle["reasonCode"] = "not_available";
        var result = new JObject
        {
            ["entityId"] = structure.EntityId.ToString("D"),
            ["category"] = "attributes",

            // The badge UIStructureItem renders is Utils.BeautifyInt(StructureSO.GetBaseLevel()),
            // which is what Reading.Level captures through GetPurchaseLevel; while levels are
            // developing the same badge switches to "+N" from GetQueuedQuantity(). Both are counts,
            // not magnitudes: routing them through the game's large-number renderer rounded a
            // 2,136-level attribute to 2.14e3 and made the wire disagree with the screen by up to
            // five levels. Publishing level plus queued under one name would put a number on the
            // wire that no screen shows.
            ["level"] = structure.Reading.Level.ToInt(),
            ["queuedLevels"] = structure.Reading.QueuedLevels.ToInt(),

            // Two words, and never a third: a structure has no ceiling to reach.
            ["state"] = StructureState(in structure),
            ["enabled"] = enabled,
            ["toggle"] = toggle,
        };
        return result.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldStructure structure,
        int nestedOffset,
        int nestedLimit)
    {
        var projected = new JObject
        {
            ["entityId"] = structure.EntityId.ToString("D"),
            ["level"] = structure.Reading.Level.ToInt(),
            ["queuedLevels"] = structure.Reading.QueuedLevels.ToInt(),
            ["state"] = StructureState(in structure),
            ["enabled"] = !structure.Reading.Disabled,
            ["affordable"] =
                TryPurchaseAffordability(world, structure.EntityId, out var affordable)
                    ? affordable
                    : (object)GameMcpListColumns.Unpriced,
        };
        return projected.Freeze();
    }

}
#endif
