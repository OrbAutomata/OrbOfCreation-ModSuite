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


using static OrbAutomata.GameMcp.GameMcpGenericLevelProjection;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpResourceProjection
{

    /// <summary>
    /// One resource row: which way its counter runs, and then the numbers the screen shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers are the screen's, both ways round, and they are not touched here. What
    /// <c>meter</c> adds is the word for which reading they take: an inverted counter renders
    /// <c>GetMissing() / maxQuantity</c>, so Glyph Upgrades at 50/80 is fifty left to invest of
    /// eighty earned, with thirty already committed — and published as a bare pair it read, to any
    /// consumer not told otherwise, as fifty held with room for thirty more. That reading plans
    /// backwards: the amount falls as the player progresses and rises only when more is earned.
    /// </para>
    /// <para>
    /// <c>atCapacity</c> says the same thing twice on those rows and said it in the wrong words.
    /// It compares the displayed number with the ceiling, so on an inverted counter it is true
    /// exactly when nothing has been used — the plain <c>yes</c> read as "stuck at the ceiling"
    /// and meant its precise opposite. Those rows answer with the used-ness word instead, which
    /// cannot be read either way but the one it means.
    /// </para>
    /// </remarks>
    internal static GameMcpValue ProjectResource(GameWorldState world, in WorldResource resource)
    {
        var amount = WorldResourceCoordinate.DisplayAmount(in resource);
        var left = WorldResourceCoordinate.DisplaysWhatIsLeft(in resource);
        var result = new JObject
        {
            ["entityId"] = resource.EntityId.ToString("D"),
            ["category"] = "resources",
            ["meter"] = left
                ? GameMcpListColumns.MeterLeft
                : GameMcpListColumns.MeterHeld,
            ["amount"] = new GameMcpDomainValue(amount),

            // A resource with no storage ceiling says so under both keys. Publishing the native
            // capacity would state a limit the game does not apply, and a bare `atCapacity: no`
            // would answer "is it full" about a thing that cannot fill.
            ["capacity"] = resource.IsCapped
                ? new GameMcpDomainValue(resource.Reading.Capacity)
                : (object)GameMcpListColumns.Uncapped,
            ["netRatePerSecond"] = new GameMcpDomainValue(resource.TrueRate),
            ["atCapacity"] = AtCapacityCell(in resource, left),
            ["inLedger"] = LedgerCell(world, in resource),
        };
        return result.Freeze();
    }

    /// <summary>
    /// Whether the game counts this resource in its own resource list yet, which is the whole of
    /// what <c>ResourceSO.visible</c> means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ResourceSO.CheckVisibility()</c> sets it from <c>startVisible &amp;&amp;
    /// visiblePrerequisites.Check()</c> and it gates ledger membership
    /// (<c>ResourceTypeSO.GetVisibleResources</c>), whether a price prints as a known resource
    /// (<c>ResourceCostList.AllVisible</c>), and the rarity and global-progress math. A page-owned
    /// bar is bound to its resource and drawn by the page, filtered through none of that.
    /// </para>
    /// <para>
    /// The game's own name for the bit is <c>ResourceSO.IsDiscovered()</c>, a one-line call to
    /// <c>IsVisible()</c>; the word is not reused here because on every other row of this surface
    /// <c>discovered</c> means a discovery button was pressed, and no resource has one. The game's
    /// resource list has no label of its own to borrow — the only callers of
    /// <c>GetVisibleResources</c> are tooltip builders.
    /// </para>
    /// </remarks>
    internal static object LedgerCell(GameWorldState world, in WorldResource resource)
    {
        if (resource.Reading.Visible) return new JObject { ["available"] = true }.Freeze();

        // So a resource with no captured container says what actually adds it, and points nowhere.
        // The one that does have conditions names the read that prints them, because a list row
        // renders no such block.
        return new JObject
        {
            ["available"] = false,
            ["reason"] = GameMcpRequirementProjection.HasUnlockConditions(world, resource.EntityId)
                ? "the game has not counted it yet; world_get prints what would add it under " +
                    "\"unlocksWhen\""
                : "the game counts it the first time you gain any",
        }.Freeze();
    }

    /// <summary>
    /// Full, in the words the row's own meter reads in. The used-ness pair belongs to a
    /// <see cref="GameMcpListColumns.MeterLeft"/> row and only there: on a meter that fills, full
    /// means blocked, and <c>nothing_used</c> would have said the exact opposite of it.
    /// </summary>
    internal static object AtCapacityCell(in WorldResource resource, bool left)
    {
        if (!resource.IsCapped) return GameMcpListColumns.Uncapped;
        if (!left) return resource.IsAtCapacity;
        return resource.IsAtCapacity
            ? GameMcpListColumns.NothingUsed
            : GameMcpListColumns.SomeUsed;
    }

    internal static GameMcpValue ProjectResourceType(
        GameWorldState world,
        in WorldResourceType resourceType)
    {
        var result = new JObject
        {
            ["entityId"] = resourceType.EntityId.ToString("D"),
            ["category"] = "resource-types",
            ["hidden"] = resourceType.SpecialHidden,
        };
        AddLevelDecision(world, result, resourceType.LevelDecision,
            !resourceType.SpecialHidden, "hidden");
        return result.Freeze();
    }
    internal static GameMcpValue List(
        GameWorldState world,
        in WorldResourceType resourceType,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = resourceType.EntityId.ToString("D"),
                ["totalLevel"] = resourceType.LevelDecision.TotalLevel,
                ["hidden"] = resourceType.SpecialHidden,
            }.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldResource resource,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectResource(world, in resource);
    }

}
#endif
