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



namespace OrbAutomata.GameMcp;

internal static class GameMcpPurchaseProjection
{

    internal static bool TryPurchaseAffordability(
        GameWorldState world,
        Guid entityId,
        out bool affordable)
    {
        affordable = false;
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, entityId, out var start, out var count) || count <= 0)
            return false;
        affordable = true;
        for (var index = start; index < start + count; index++)
        {
            var cost = world.PurchaseCosts[index];
            if (!cost.AffordabilityEvaluated) return false;
            if (!cost.Affordable) affordable = false;
        }
        return true;
    }

    /// <summary>
    /// The upgrade's ceiling, or the word for having none. The game marks "no ceiling" with a
    /// negative native maximum, and every candidate number for it either lies or inverts the
    /// meaning — <c>0</c> reads as a cap of zero. The word is the only spelling that is true.
    /// </summary>
    internal static object UpgradeCeiling(in WorldUpgrade upgrade) =>
        upgrade.IsBounded ? upgrade.Reading.MaxLevel : GameMcpListColumns.Uncapped;

    /// <summary>
    /// Whether the next level can be paid for. A completed upgrade has no next level, so the world
    /// publishes no price for one — the same absence <see cref="GameMcpListColumns.Unpriced"/>
    /// already names.
    /// </summary>
    internal static object UpgradeAffordability(GameWorldState world, in WorldUpgrade upgrade)
    {
        if (upgrade.IsExhausted) return GameMcpListColumns.Unpriced;
        return TryPurchaseAffordability(world, upgrade.EntityId, out var affordable)
            ? affordable
            : GameMcpListColumns.Unpriced;
    }

    /// <summary>
    /// The action queue's declared capacity, off the published world: the queue's own maximum, held
    /// as an <c>IntVariable</c> the row carries an edge to. A capacity nobody has published yet — no
    /// save loaded — is no capacity, and the sentences that would have quoted it leave it out rather
    /// than name a number the world does not carry.
    /// </summary>
    internal static bool TryReadActionQueueCapacity(GameWorldState? world, out int capacity)
    {
        capacity = 0;
        if (world is null) return false;
        if (!WorldLookup.TryFind(
                world.ActionQueues, KnownEntities.ActiveActionables.Uuid, out var queue))
            return false;
        if (queue.MaxQueuedItemsId == Guid.Empty ||
            !WorldLookup.TryFind(world.IntVariables, queue.MaxQueuedItemsId, out var maximum))
            return false;
        capacity = maximum.Value.ToInt();
        return capacity > 0;
    }

    /// <summary>
    /// The one purchase refusal a smaller amount does not fix, in the numbers a caller can act on:
    /// the queue is full, this is how full, and it is time rather than a different ask that clears
    /// it.
    /// </summary>
    internal static string ActionQueueFullReason(GameWorldState? world) =>
        TryReadActionQueueCapacity(world, out var capacity)
            ? "The game's action queue is full (" + capacity + " of " + capacity +
              " slots used); nothing can be queued until something in it settles."
            : "The game's action queue is full; nothing can be queued until something in it settles.";

    /// <summary>
    /// The one line a press that delivered fewer levels than it was asked for owes its caller. The
    /// two halves of a shortfall are kept apart on purpose: the suite speaks plainly for the levels
    /// it withheld, and for the levels the game did not take it says only what it watched or read.
    /// </summary>
    internal static string PurchaseShortfallReason(
        GameWorldState? world,
        int withheld,
        int offeredToTheGame,
        int queued,
        string? gameStopped)
    {
        var gameHalf = "the game took no more this press" +
            (string.IsNullOrEmpty(gameStopped) ? string.Empty : ": " + gameStopped);
        if (withheld <= 0) return gameHalf + ".";
        if (queued < offeredToTheGame)
        {
            return gameHalf + ", and " + WithheldCount(withheld) +
                " never offered because the action queue had room for only " +
                offeredToTheGame + ".";
        }
        var full = TryReadActionQueueCapacity(world, out var capacity)
            ? "the action queue is full (" + capacity + " of " + capacity + " slots used)"
            : "the action queue is full";
        return WithheldCount(withheld) + " not taken because " + full + ".";
    }

    /// <summary>
    /// Which of the game's own gates the suite watched shut on a group it drove level by level, in
    /// the words the settled answer uses. A group the suite could not watch has no clause here and
    /// gets none invented for it.
    /// </summary>
    internal static string? PurchaseStopClause(AutoBuyGroupStop stop) => stop switch
    {
        AutoBuyGroupStop.NextLevelUnaffordable => "the next level's cost is not met",
        AutoBuyGroupStop.NotAdmitted => "it no longer admits this purchase",
        _ => null,
    };

    /// <summary>
    /// What the level after a press that stopped short would cost, in the player's own words for the
    /// resources. It is a fact and never a cause: it says what the next press must pay, and nothing
    /// about why this one stopped.
    /// </summary>
    internal static string? NextLevelPriceClause(
        IReadOnlyList<(string Resource, BigDouble Cost)> rows)
    {
        if (rows.Count == 0) return null;
        var text = new StringBuilder("the next level costs ");
        for (var index = 0; index < rows.Count; index++)
        {
            if (index > 0) text.Append(index == rows.Count - 1 ? " and " : ", ");
            text.Append(GameMcpNumberFormatter.Format(rows[index].Cost))
                .Append(' ')
                .Append(rows[index].Resource);
        }
        return text.ToString();
    }

    internal static string WithheldCount(int withheld) =>
        withheld == 1 ? "1 was" : withheld + " were";
}
#endif
