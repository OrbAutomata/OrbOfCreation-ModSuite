using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// Every row has its name. Where the game authors a player-facing word the row prints that word;
/// where it authors none, the asset's own name is the only word there is and the same one column
/// prints it. The <c>int-variables</c> page holds one of each, so both halves of the rule are read
/// off one page: <c>SummonedLevel</c> is authored no word, <c>World Resets</c> is.
/// </summary>
public sealed class GameMcpUnwordedNameTests
{
    /// <summary>SummonedLevel, an IntVariable the game authors no display name for.</summary>
    private static readonly Guid SummonedLevel =
        Guid.Parse("18c498f5-e4a7-4549-b093-117e206cc043");

    /// <summary>World Resets, an IntVariable on the same page that has one.</summary>
    private static readonly Guid WorldResets =
        Guid.Parse("56039067-0092-49c0-a7bd-3c43eb119d37");

    /// <summary>
    /// The pass that names a row is the wire normalizer, so the rule is pinned there directly as
    /// well as through the verbs: it is the pass that used to staple the asset id on as a second
    /// column, and the one that must now put it in the first.
    /// </summary>
    [Fact]
    public void The_normalizer_names_an_unworded_row_and_leaves_a_worded_one_its_word()
    {
        var page = (JObject)GameMcpEntityWireNormalizer.Normalize(
            new JObject
            {
                ["rows"] = new JArray
                {
                    new JObject { ["entityId"] = SummonedLevel.ToString("D"), ["value"] = 4 },
                    new JObject { ["entityId"] = WorldResets.ToString("D"), ["value"] = 1 },
                },
            },
            GameMcpTestHarness.EntityCatalog);
        var rows = page["rows"]!.Values<JObject>().ToArray();

        Assert.Equal("SummonedLevel", (string?)rows[0]!["name"]);
        Assert.Null(rows[0]!["internalName"]);
        Assert.Equal("World Resets", (string?)rows[1]!["name"]);
        Assert.Null(rows[1]!["internalName"]);
    }

    [Fact]
    public void The_list_page_names_every_row_and_never_prints_a_worded_rows_asset_name()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5201);

        var byUuid = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "int-variables", 0, 10))["rows"]!
            .Values<JObject>()
            .ToDictionary(row => (string?)row!["uuid"] ?? string.Empty, row => row!);

        Assert.Equal(
            "SummonedLevel",
            (string?)byUuid[GameMcpTestHarness.Handle(SummonedLevel)]["name"]);
        Assert.Equal(
            "World Resets",
            (string?)byUuid[GameMcpTestHarness.Handle(WorldResets)]["name"]);
        foreach (var row in byUuid.Values) Assert.Null(row["internalName"]);
    }

    /// <summary>
    /// Search files the same two rows under the same two words, and says the hit was on
    /// <c>name</c> — because on an unworded row that is the cell the query matched. Saying
    /// <c>internalName</c> would name a column the row does not print.
    /// </summary>
    [Fact]
    public void Search_names_an_unworded_row_the_same_way_and_says_the_hit_was_on_its_name()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5202);

        var unworded = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, "SummonedLevel", 0, 10, limitFromCaller: false))["rows"]!
            .Values<JObject>())!;
        var worded = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, "World Resets", 0, 10, limitFromCaller: false))["rows"]!
            .Values<JObject>())!;

        Assert.Equal("SummonedLevel", (string?)unworded["name"]);
        Assert.Equal("name", (string?)unworded["matchedOn"]);
        Assert.Null(unworded["internalName"]);
        Assert.Equal("World Resets", (string?)worded["name"]);
        Assert.Equal("name", (string?)worded["matchedOn"]);
        Assert.Null(worded["internalName"]);
    }

    /// <summary>
    /// A row whose asset name really is a different string from its word still finds that string,
    /// and says so: <c>matchedOn: internalName</c> is the answer for a hit on a field the row does
    /// not print, which is exactly the case it was made for.
    /// </summary>
    [Fact]
    public void A_worded_rows_asset_name_still_finds_it_and_is_still_called_out_as_internal()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5203);

        var hit = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, "WorldResets", 0, 10, limitFromCaller: false))["rows"]!
            .Values<JObject>())!;

        Assert.Equal("World Resets", (string?)hit["name"]);
        Assert.Equal("internalName", (string?)hit["matchedOn"]);
        Assert.Null(hit["internalName"]);
    }

    private static GameWorldState World() => new()
    {
        CollectedAtEpoch = 52,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        IntVariables = PublicationTable<WorldNumberVariable>.Create(
            new[]
            {
                new WorldNumberVariable(SummonedLevel, new BigDouble(4), isPercent: false),
                new WorldNumberVariable(WorldResets, new BigDouble(1), isPercent: false),
            }.OrderBy(variable => variable.EntityId).ToArray()),
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "int-variables", WorldCategoryOutcome.Collected, 2, 0, string.Empty),
        }),
    };
}
