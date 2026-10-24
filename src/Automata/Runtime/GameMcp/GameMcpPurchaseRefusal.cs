#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpPurchaseRefusal
{
    internal static GameMcpCommandResult? ProjectPurchaseRefusal(
        GameMcpCommand command,
        GameWorldState world,
        in ServiceActionResult result,
        long lifecycle,
        ulong configurationGeneration,
        AutoBuyAdmissionDiagnosis liveRefusal = default)
    {
        // native_rejected is reserved for a native refusal the published world cannot explain. A
        // rejection whose cause the read side already names answers with that same name.
        if (command.Kind != GameMcpCommandKind.Purchase ||
            result.Disposition is not (ServiceActionDisposition.Skipped or
                ServiceActionDisposition.Rejected))
        {
            return null;
        }
        if (result.Disposition == ServiceActionDisposition.Rejected &&
            result.Code != CommonActionResultCodes.NativeRejected)
        {
            return null;
        }
        if (string.Equals(command.Mode, "upgrade", StringComparison.Ordinal) &&
            WorldLookup.TryFind(world.Upgrades, command.TargetId, out var upgrade) &&
            upgrade.IsExhausted)
        {
            // The player's word for the thing, and only that. This sentence used to spell the
            // target the way a log line does — display name, asset name in brackets, and the whole
            // canonical UUID — inside prose a player reads. The id the caller acts on rides as the
            // response's own `uuid` field, where it can be read without parsing a sentence.
            return GameMcpCommandResult.Rejected(
                "already_maxed",
                EntityIdentityFormatter.PlayerName(command.TargetId, world.EntityIdentities) +
                " is already at its maximum level.",
                lifecycle,
                configurationGeneration);
        }

        // The game re-checks the price when the press arrives, and a press seconds after another
        // spend meets a price the published world still shows as met. The live cost list read at
        // that refusal is the fresher fact, so it names the shortfall.
        if (result.Disposition == ServiceActionDisposition.Skipped &&
            liveRefusal.Classification == AutoBuyRefusalClassification.AffordabilityChanged)
        {
            var live = LiveShortfall(world, liveRefusal.LiveCosts);
            return GameMcpCommandResult.Rejected(
                "unaffordable",
                live.Length == 0 ? GameMcpDecisionReason.For("unaffordable") : live,
                lifecycle,
                configurationGeneration);
        }
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, command.TargetId, out var start, out var count))
            return null;
        var shortfalls = new List<(string Resource, BigDouble Needed, BigDouble Held)>();
        for (var index = start; index < start + count; index++)
        {
            var cost = world.PurchaseCosts[index];
            if (!cost.AffordabilityEvaluated || cost.ResourceAffordable) continue;
            var resource = EntityIdentityFormatter.Describe(
                cost.ResourceId, world.EntityIdentities);
            shortfalls.Add((
                resource.HasName ? resource.Name : cost.ResourceId.ToString("D"),
                GameMcpDecisionReason.AdmittedCost(world, in cost),
                GameMcpDecisionReason.SpendableAmount(
                    world, cost.ResourceId, cost.AvailableAmount)));
        }
        if (shortfalls.Count == 0) return null;
        return GameMcpCommandResult.Rejected(
            "unaffordable",
            GameMcpDecisionReason.Shortfall(shortfalls),
            lifecycle,
            configurationGeneration);
    }

    private static string LiveShortfall(GameWorldState world, AutoBuyLiveCostSnapshot live)
    {
        if (!live.IsComplete) return string.Empty;
        var shortfalls = new List<(string Resource, BigDouble Needed, BigDouble Held)>();
        foreach (var row in live.Rows)
        {
            var covered = row.IsBandwidth
                ? OrbGameMath.SnapFloorToInt(row.Available) >= OrbGameMath.SnapFloorToInt(row.Cost)
                : row.Available.CompareTo(row.Cost) >= 0;
            if (covered) continue;
            var resource = EntityIdentityFormatter.Describe(row.ResourceId, world.EntityIdentities);

            // Both live figures are in the native coordinate, so the one conversion that turns a
            // native cost into the player's units turns the quality-scaled holding back too.
            shortfalls.Add((
                resource.HasName ? resource.Name : row.ResourceId.ToString("D"),
                GameMcpDecisionReason.PlayerFacingCost(world, row.ResourceId, row.Cost),
                GameMcpDecisionReason.PlayerFacingCost(world, row.ResourceId, row.Available)));
        }
        return GameMcpDecisionReason.Shortfall(shortfalls);
    }

    /// <summary>
    /// What the settled answer may say about the levels the game itself did not take. The suite
    /// drives an attribute group one level at a time and re-runs the game's own gates between
    /// levels, so it names the gate it watched shut. An upgrade multi-buy breaks inside the game's
    /// loop, so there is no gate to name and none is guessed: the answer offers the next level's
    /// live price instead, which is a fact rather than a cause.
    /// </summary>
    private static string? PurchaseStopClause(
        GameWorldState world,
        in AutoBuyPurchaseSubmission submission)
    {
        if (submission.CommittedLevels >= submission.RequestedLevels) return null;
        var watched = GameMcpPurchaseProjection.PurchaseStopClause(submission.GroupStop);
        if (watched is not null) return watched;
        if (!submission.NextLevelCosts.IsComplete) return null;
        var rows = new List<(string Resource, BigDouble Cost)>();
        foreach (var row in submission.NextLevelCosts.Rows)
        {
            var resource = EntityIdentityFormatter.Describe(row.ResourceId, world.EntityIdentities);
            rows.Add((
                resource.HasName ? resource.Name : row.ResourceId.ToString("D"),
                row.Cost));
        }
        return GameMcpPurchaseProjection.NextLevelPriceClause(rows);
    }

    internal static void ProjectPurchaseDetails(
        GameMcpCommand command,
        GameWorldState world,
        in AutoBuyPurchaseSubmission submission,
        ref string? exactReason,
        ref GameMcpValue? details)
    {
        if (!submission.Outcome.Verified && !string.IsNullOrEmpty(submission.Outcome.Reason))
            exactReason = submission.Outcome.Reason;

        // The full-queue refusal is written here rather than at the boundary, because the
        // queue's capacity is a published world fact and the boundary reads only the room.
        if (submission.Outcome.Code == AutoBuyActionResultCodes.ActionQueueFull)
            exactReason = GameMcpPurchaseProjection.ActionQueueFullReason(world);

        // A refusal whose sentence names a ceiling carries that ceiling as a number too,
        // read from the same live reading the sentence was written from.
        if (submission.MaximumAmount >= 0)
        {
            details = new GameMcpObjectBuilder
            {
                ["maximumAmount"] = submission.MaximumAmount,
            }.Freeze();
        }
        else if (submission.Outcome.Verified)
        {
            // A purchase is queued, not applied, so the answer is what the press queued and
            // it is complete here: the count is the mutation's own verified queued-level
            // delta, and no later world can add to it without contradicting it. Every level
            // short of the ask is explained on that same line: the suite's own withholding
            // plainly, the game's half with only what the suite watched or can still read.
            details = GameMcpRowKit.QueuedMutation(
                command.TargetId,
                command.Amount,
                submission.CommittedLevels,
                GameMcpPurchaseProjection.PurchaseShortfallReason(
                    world,
                    submission.WithheldBySuite,
                    submission.RequestedLevels,
                    submission.CommittedLevels,
                    PurchaseStopClause(world, in submission)));
        }
    }
}
#endif
