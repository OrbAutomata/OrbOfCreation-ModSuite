using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpPageBudgetTests
{
    [Fact]
    public void A_full_page_really_is_a_twelve_kilobyte_page()
    {
        // The estimate used to be a flat per-field guess about three times the real row, so a full
        // page delivered about a quarter of the bound and a caller paged four times for one page.
        var state = ResourceWorld(200);

        var page = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(state, "resources", 0, 200).Freeze());
        var bytes = Encoding.UTF8.GetByteCount(page.ToString(Formatting.None));

        Assert.NotNull(page["nextOffset"]);
        Assert.True(
            bytes <= GameMcpWorldQuery.MaximumListResponseBytes,
            "a page of " + bytes + " bytes exceeded the published bound");
        Assert.True(
            bytes > GameMcpWorldQuery.MaximumListResponseBytes * 3 / 4,
            "a page of " + bytes + " bytes left most of the published bound unused");
    }

    /// <summary>
    /// A search page mixes categories, so borrowing each category's scan columns unioned them all
    /// and padded the rest with dashes. Every cell here is filled, and `category` — the column that
    /// says which read verb can follow up on the hit — is on every row rather than on the few rows
    /// that happened to have no identity of their own.
    /// </summary>
    [Fact]
    public void A_search_match_names_the_entity_and_the_category_that_reads_the_rest()
    {
        var state = ResourceWorld(200);
        var listed = (JObject)GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(state, "resources", 0, 200).Freeze())["rows"]![0]!;

        var match = (JObject)GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            state,
            (string)listed["name"]!,
            0,
            20).Freeze())["rows"]![0]!;

        Assert.Equal((string?)listed["uuid"], (string?)match["uuid"]);
        Assert.Equal((string?)listed["name"], (string?)match["name"]);
        Assert.Equal("resources", (string?)match["category"]);
        Assert.Equal(new[] { "uuid", "name", "category" }, match.Properties().Select(p => p.Name));
    }

    private static GameMcpFrameContext ResourceWorld(int maximumRows)
    {
        var resources = new List<WorldResource>();
        var catalog = GameMcpTestHarness.EntityCatalog;
        for (var index = 0; index < catalog.Rows.Count && resources.Count < maximumRows; index++)
        {
            if (!string.Equals(
                    catalog.Rows[index].RuntimeType, "ResourceSO", StringComparison.Ordinal))
            {
                continue;
            }
            resources.Add(GameMcpTestHarness.BandwidthResource(
                catalog.Rows[index].EntityId, new BigDouble(1234.5), new BigDouble(10000)));
        }

        Assert.True(resources.Count > 20, "the identity fixture must carry enough resource rows");
        var world = new GameWorldState
        {
            Resources = PublicationTable<WorldResource>.Create(resources.ToArray()),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
                }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = catalog,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(911));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }
}
