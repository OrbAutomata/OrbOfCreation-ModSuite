using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpTestHarness;

namespace OrbModding.ProfileTests;

public sealed class GameMcpVariableWordingTests
{
    /// <summary>
    /// The mastery ring is a fixed window the game overwrites, and the sources feeding it repeat on
    /// a short cycle. Paged row by row it cost four full pages to deliver about fifteen distinct
    /// facts, with a monotone counter as the only column that varied down the page.
    /// </summary>
    [Fact]
    public void The_mastery_ring_publishes_each_source_once_with_how_often_it_earned()
    {
        var spell = Guid.Parse("c1000000-0000-0000-0000-000000000001");
        var artifact = Guid.Parse("c1000000-0000-0000-0000-000000000002");
        var samples = new[]
        {
            new WorldMasteryExperience(101, MasteryExperienceDomain.Spell, spell, 331, true,
                new BigDouble(5)),
            new WorldMasteryExperience(102, MasteryExperienceDomain.Artifact, artifact, 12, true,
                new BigDouble(7)),
            new WorldMasteryExperience(103, MasteryExperienceDomain.Spell, spell, 331, true,
                new BigDouble(5)),
            new WorldMasteryExperience(104, MasteryExperienceDomain.Spell, spell, 331, true,
                new BigDouble(6)),
        };
        var world = new GameWorldState
        {
            MasteryExperience = PublicationTable<WorldMasteryExperience>.Create(samples),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[] { Clean("spell-recipes"), Clean("alchemy-recipes"), Clean("equipment") }),
            CollectedAtEpoch = 21,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var page = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(state, "mastery-experience", 0, 50));
        var rows = page["rows"]!.Values<JObject>().ToArray();

        Assert.Equal(2, (int)page["total"]!);
        Assert.Equal(2, rows.Length);
        Assert.Equal(3, (int)rows[0]!["count"]!);
        Assert.Equal("Spell", (string?)rows[0]!["domain"]);
        Assert.Equal(331, (int)rows[0]!["sourceMastery"]!);
        Assert.Equal(1, (int)rows[1]!["count"]!);
        Assert.Equal(4, (int)page["window"]!["samples"]!);
        Assert.Equal(101L, (long)page["window"]!["firstSequence"]!);
        Assert.Equal(104L, (long)page["window"]!["lastSequence"]!);
        Assert.Null(rows[0]!["sequence"]);
    }

    /// <summary>
    /// A variable the game authors no word for is named by the only word there is: its own asset
    /// name. The page row briefly printed `name: -` and a bare id, which named nothing at all —
    /// and a reference to the same id one row over printed `SummonedLevel` all along, so the two
    /// surfaces disagreed about the name of one thing. One column, and it is never empty.
    /// </summary>
    [Fact]
    public void A_variable_the_game_authors_no_word_for_is_named_by_its_asset_name()
    {
        var unworded = Guid.Parse("18c498f5-e4a7-4549-b093-117e206cc043");
        var worded = Guid.Parse("37a84399-98b5-463c-b858-c1ecf2f9bf34");
        var world = new GameWorldState
        {
            IntVariables = PublicationTable<WorldNumberVariable>.Create(new[]
            {
                new WorldNumberVariable(unworded, new BigDouble(4), isPercent: false),
                new WorldNumberVariable(worded, new BigDouble(1), isPercent: false),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                Clean("int-variables"),
            }),
            CollectedAtEpoch = 47,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var rows = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(world), "int-variables", 0, 10))["rows"]!
            .Values<JObject>()
            .ToArray();

        Assert.Equal("SummonedLevel", (string?)rows[0]!["name"]);
        Assert.Null(rows[0]!["internalName"]);
        Assert.Equal("MultiBuy", (string?)rows[1]!["name"]);
        Assert.Null(rows[1]!["internalName"]);
    }

    /// <summary>
    /// A percent variable and a plain one read identically once the flag is gone, so the scan
    /// column carries it: round ten spent 22% of its whole wire on two 200-id detail re-reads whose
    /// only new fact was this one word per row, because the page that listed them did not say it.
    /// The detail block says the same fact by saying nothing — `isPercent` is printed when it is
    /// `yes` and silent when it is `no` — because a documented default spelled out on every block
    /// of a batch is most of that batch.
    /// </summary>
    /// <remarks>
    /// The two rules are one rule seen from both sides: a table's header promised the column, so
    /// the column prints on every row of it including the ones holding the default; a block
    /// promised nothing, so the default is absence. What makes the second readable is that it is
    /// written down where the shape is — <c>docs/development/mcp-tools.md</c> — and a caller who
    /// has read it knows a missing `isPercent` is `no` rather than unknown.
    /// </remarks>
    [Fact]
    public void A_percent_variable_says_so_in_its_row_and_a_plain_one_says_so_by_being_silent()
    {
        var percent = Guid.Parse("18c498f5-e4a7-4549-b093-117e206cc043");
        var plain = Guid.Parse("37a84399-98b5-463c-b858-c1ecf2f9bf34");
        var world = new GameWorldState
        {
            DoubleVariables = PublicationTable<WorldNumberVariable>.Create(new[]
            {
                new WorldNumberVariable(percent, new BigDouble(25), isPercent: true),
                new WorldNumberVariable(plain, new BigDouble(3), isPercent: false),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                Clean("double-variables"),
            }),
            CollectedAtEpoch = 48,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var context = GameMcpTestHarness.Context(world);

        var page = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(context, "double-variables", 0, 10));
        var rows = page["rows"]!.Values<JObject>().ToArray();

        Assert.True((bool)rows[0]!["isPercent"]!);
        Assert.False((bool)rows[1]!["isPercent"]!);
        Assert.Equal(
            "[id | name | value | isPercent]",
            GameMcpTextPage.Render(page).Split('\n')[1]);

        var blocks = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            context,
            "double-variables",
            new[] { percent.ToString("D"), plain.ToString("D") }))["results"]!
            .Values<JObject>()
            .ToArray();

        Assert.True((bool)blocks[0]!["row"]!["isPercent"]!);
        Assert.Null(blocks[1]!["row"]!["isPercent"]);
        Assert.NotNull(blocks[1]!["row"]!["value"]);
    }
}
