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


using static OrbAutomata.GameMcp.GameMcpChallengeProjection;
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWorldRead
{

    internal static JObject NotAvailableWithoutWorld(GameMcpFrameContext state, string code, string reason)
    {
        var result = new JObject
        {
            ["status"] = "not_available",
            ["code"] = code,
            ["reason"] = reason,
            ["lifecycleState"] = state.LifecycleState.ToString(),
        };
        return result;
    }

    /// <summary>
    /// The code and sentence for a world read with no live world behind it. A lifecycle that is not
    /// playing is the answer whenever it applies: it names the state <c>game_probe</c> reports, so a
    /// caller comparing the two tools sees one fact rather than two beliefs.
    /// </summary>
    internal static void WorldUnavailable(
        GameMcpFrameContext state,
        out string code,
        out string reason)
    {
        if (GameLifecycleUnavailability.TryDescribe(state.LifecycleState, out code, out reason))
            return;
        code = "world_not_published";
        reason = state.RuntimeNotAvailableReason.Length == 0
            ? "the suite has not finished its first read of the game; try again in a moment."
            : state.RuntimeNotAvailableReason;
    }

    /// <summary>
    /// Whether the pinned publication is a reading of a live run. Generation 1 with no collection
    /// timestamp is the flushed state a lifecycle boundary leaves behind, and it is the same shape
    /// the publisher is constructed in, so "before the first run" and "after the run ended" answer
    /// identically.
    /// </summary>
    internal static bool IsWorldPublished(GameMcpFrameContext state) =>
        state.World is not null &&
        state.World.Generation.Value > 1 &&
        state.World.Snapshot.CollectedAtUtcTicks > 0;

    internal static bool TryWorld(
        GameMcpFrameContext state,
        out WorldPublication<GameWorldState> publication,
        out JObject unavailable)
    {
        if (IsWorldPublished(state))
        {
            publication = state.World!;
            unavailable = null!;
            return true;
        }

        publication = null!;
        WorldUnavailable(state, out var code, out var reason);
        unavailable = NotAvailableWithoutWorld(state, code, reason);
        return false;
    }

    internal static JObject NotAvailable(
        WorldPublication<GameWorldState> publication,
        string code,
        string reason)
    {
        var result = new JObject();
        result["status"] = "not_available";
        result["code"] = code;
        result["reason"] = reason;
        return result;
    }

    internal static JArray LocalizedRequirementImplications(
        GameWorldState world,
        HashSet<Guid> touchedIdentities)
    {
        var result = new JArray();
        if (touchedIdentities.Count == 0 ||
            !TryLocalizedRequirementFailures(world, out var failures))
        {
            return result;
        }

        for (var index = 0; index < failures.Length; index++)
        {
            var failure = failures[index];
            if (!touchedIdentities.Contains(failure.OwnerId)) continue;
            // No `ownerKind`, no `conditionTypeName`, and no `collectorReason`. All three put the
            // game's own class names into wire columns of a row a caller reads: the first says
            // which registry the owner is in, which its uuid already answers, and the other two
            // name the C# class the suite failed to model. `unmodeled_requirement_leaf` carries an
            // authored sentence that says the same thing in the reader's words.
            result.Add(new JObject
            {
                ["category"] = "entity-requirements",
                ["reasonCode"] = "unmodeled_requirement_leaf",
                ["ownerUuid"] = failure.OwnerId.ToString("D"),
                ["containerIndex"] = failure.ContainerIndex,
                ["ordinal"] = failure.Ordinal,
            });
        }
        return result;
    }

    internal static JArray LocalizedDiscoveryOfferImplications(
        GameWorldState world,
        HashSet<Guid> touchedIdentities)
    {
        var result = new JArray();
        if (touchedIdentities.Count == 0) return result;
        for (var treeIndex = 0; treeIndex < world.DiscoveryTrees.Count; treeIndex++)
        {
            var tree = world.DiscoveryTrees[treeIndex];
            if (!touchedIdentities.Contains(tree.EntityId)) continue;
            for (var offerIndex = 0; offerIndex < tree.CurrentOfferIds.Count; offerIndex++)
            {
                var offerId = tree.CurrentOfferIds[offerIndex];
                if (GameMcpEntityIdentity.TryDescribePublishedEntity(
                        world, offerId, out _, out _, out _))
                {
                    continue;
                }
                result.Add(new JObject
                {
                    ["treeUuid"] = tree.EntityId.ToString("D"),
                    ["ordinal"] = offerIndex,
                    ["offerUuid"] = offerId.ToString("D"),
                    ["reasonCode"] = "offer_not_in_explainable_world",
                });
            }
        }
        return result;
    }

    /// <summary>
    /// The entity-requirements reader deliberately publishes every unmodeled leaf with its owner.
    /// It is safe to localize the category's skipped count only when those published rows account
    /// for the entire count. A thrown read has no owner row and therefore remains category-global.
    /// </summary>
    internal static bool TryLocalizedRequirementFailures(
        GameWorldState world,
        out WorldEntityRequirement[] failures)
    {
        var skipped = 0;
        var reportFound = false;
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var report = world.CollectionCategories[index];
            if (!string.Equals(
                    GameMcpWorldCategories.Normalize(report.Category),
                    "entity-requirements",
                    StringComparison.Ordinal))
            {
                continue;
            }
            reportFound = true;
            if (report.Outcome != WorldCategoryOutcome.Collected || report.Skipped <= 0)
            {
                failures = Array.Empty<WorldEntityRequirement>();
                return false;
            }
            skipped = report.Skipped;
            break;
        }
        if (!reportFound)
        {
            failures = Array.Empty<WorldEntityRequirement>();
            return false;
        }

        var localized = new List<WorldEntityRequirement>();
        for (var index = 0; index < world.EntityRequirements.Count; index++)
        {
            var row = world.EntityRequirements[index];
            if (row.NodeKind == WorldRequirementNodeKind.Leaf &&
                row.Kind == WorldRequirementConditionKind.Unknown)
            {
                localized.Add(row);
            }
        }
        if (localized.Count != skipped)
        {
            failures = Array.Empty<WorldEntityRequirement>();
            return false;
        }
        failures = localized.ToArray();
        return true;
    }

    /// <summary>
    /// The refusal a name that is not a listable page earns. A name the world collects is told where
    /// its rows are read; only a name nothing collects is called unknown.
    /// </summary>
    /// <remarks>
    /// The collector runs more categories than this surface pages, and <c>world_categories</c> prints
    /// every one of them — so a caller reading that page and asking for a name on it was answered
    /// "unknown category", which contradicts the page they read it from. The vocabulary is not
    /// authored here: a report feeding a listable page is named by the categories themselves, and
    /// anything else is matched against the collection report the published world carries.
    /// </remarks>
    internal static JObject NoSuchCategory(
        WorldPublication<GameWorldState> publication,
        string name,
        string unknownReason)
    {
        var normalized = GameMcpWorldCategories.Normalize(name);
        if (GameMcpWorldCategories.ListedReportPageNames.TryGetValue(normalized, out var pages))
        {
            return NotAvailable(
                publication,
                "category_not_listable",
                "'" + normalized + "' is one of the collection reports the world runs, not a page " +
                "of its own; its rows are read into " + string.Join(", ", pages) +
                ", so list " + pages[0] + " instead");
        }

        for (var index = 0; index < publication.Snapshot.CollectionCategories.Count; index++)
        {
            var report = publication.Snapshot.CollectionCategories[index];
            if (!string.Equals(
                    GameMcpWorldCategories.Normalize(report.Category),
                    normalized,
                    StringComparison.Ordinal))
            {
                continue;
            }
            return NotAvailable(
                publication,
                "category_not_listable",
                "'" + normalized + "' is a category the world collects, and world_categories gives " +
                "it a row with its row count, but " +
                GameMcpDecisionReason.CollectorNotListable);
        }

        return NotAvailable(publication, "unknown_category", unknownReason);
    }
}
#endif
