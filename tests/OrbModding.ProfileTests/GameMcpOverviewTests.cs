using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.TestSupport;
using Xunit;
using static OrbModding.ProfileTests.GameMcpTestHarness;

namespace OrbModding.ProfileTests;

public sealed class GameMcpOverviewTests
{
    /// <summary>
    /// Every queue answers for itself, with the capacity it is measured against. The overview used
    /// to print one unnamed occupancy beside a queue count of two: it covered the walked plot-action
    /// queue only, and a live round read it five times while asking about the attribute queue a
    /// purchase's ceiling comes from.
    /// </summary>
    [Fact]
    public void WorldOverviewPublishesEachActionQueuesOccupancyUnderItsOwnName()
    {
        var plotQueue = KnownEntities.ActivePlotNodeActions.Uuid;
        var maximumId = Guid.NewGuid();
        var slots = new[]
        {
            new WorldActionQueueSlot(plotQueue, 0, empty: false, Guid.NewGuid(), Guid.NewGuid(), 1, engaged: true),
            new WorldActionQueueSlot(plotQueue, 1, empty: true, Guid.Empty, Guid.Empty, 0, engaged: false),
        };
        var world = new GameWorldState
        {
            ActionQueueSlots = PublicationTable<WorldActionQueueSlot>.Create(slots, slots.Length),
            ActionQueues = PublicationTable<WorldActionQueue>.Create(new[]
            {
                new WorldActionQueue(
                    plotQueue, Guid.Empty,
                    slotCount: 2, usedSlots: 1, emptySlots: 1,
                    hasEmptySlot: true, consistent: true),
                new WorldActionQueue(
                    KnownEntities.ActiveActionables.Uuid, maximumId,
                    slotCount: 4, usedSlots: 4, emptySlots: 0,
                    hasEmptySlot: false, consistent: true),
            }),
            IntVariables = PublicationTable<WorldNumberVariable>.Create(new[]
            {
                new WorldNumberVariable(maximumId, new BigDouble(10), isPercent: false),
            }),
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var overview = GameMcpTestHarness.Json(
            GameMcpWorldQuery.Overview(GameMcpTestHarness.Context(world)));

        Assert.Null(overview["running"]?["occupiedActionQueueSlots"]);
        var queues = (JArray?)overview["running"]?["actionQueues"];
        Assert.NotNull(queues);
        Assert.Equal(2, queues!.Count);
        Assert.Equal(GameMcpTestHarness.Handle(plotQueue), (string?)queues[0]["uuid"]);
        Assert.Equal("Plot actions", (string?)queues[0]["name"]);
        // The asset id the normalizer used to staple beside that authored word, on the one row in
        // the suite that had to author one.
        Assert.Null(queues[0]["internalName"]);
        Assert.Equal(1, (int?)queues[0]["usedSlots"]);
        Assert.Equal(2, (int?)queues[0]["capacity"]);
        Assert.Equal(
            GameMcpTestHarness.Handle(KnownEntities.ActiveActionables.Uuid),
            (string?)queues[1]["uuid"]);
        Assert.Equal("Active Attributes", (string?)queues[1]["name"]);
        Assert.Equal(4, (int?)queues[1]["usedSlots"]);
        Assert.Equal(10, (int?)queues[1]["capacity"]);
        Assert.Equal(0, (int?)overview["running"]?["activeConceptAssignments"]);
    }

    /// <summary>
    /// Every queue the world publishes prints a word a player would recognise, not the internal
    /// spelling of the list variable behind it.
    /// </summary>
    /// <remarks>
    /// The Agromancy queue's row read <c>ActivePlotNodeActions</c> in a column of its own, beside
    /// the word the collector had authored: the producer wrote only <c>name</c>, and the wire
    /// normalizer stapled the Unity asset id on afterwards because the game authors no display name
    /// for that list. So the sweep runs the normalizer over the row, which is the pass that added
    /// the column, rather than the producer that never wrote it. It starts from the collector
    /// rather than from a list of two, so a third queue collected tomorrow is held to the same rule
    /// instead of quietly inheriting the old fallback.
    /// </remarks>
    [Fact]
    public void NoQueueTheWorldPublishesPrintsItsInternalName()
    {
        var collector = File.ReadAllText(Path.Combine(
            RepositorySources.Root,
            "src", "Common", "Runtime", "World", "Categories", "WorldActionQueue.cs"));
        var published = Regex
            .Matches(collector, @"registry\[KnownEntities\.([A-Za-z0-9_]+)\.Uuid\]")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(published);
        foreach (var member in published)
        {
            var field = typeof(KnownEntities).GetField(
                member, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            var entity = field!.GetValue(null)!;
            var uuid = (Guid)entity.GetType().GetProperty("Uuid")!.GetValue(entity)!;
            var internalName = (string)entity.GetType()
                .GetProperty("DiagnosticName")!.GetValue(entity)!;

            var printed = GameMcpWorldQuery.QueueName(uuid, GameMcpTestHarness.EntityCatalog);

            Assert.NotEqual(string.Empty, printed);
            Assert.NotEqual(internalName, printed);
            Assert.NotEqual(uuid.ToString("D"), printed);

            var row = (JObject)GameMcpEntityWireNormalizer.Normalize(
                new JObject
                {
                    ["uuid"] = uuid.ToString("D"),
                    ["name"] = printed,
                    ["usedSlots"] = 0,
                    ["capacity"] = 1,
                },
                GameMcpTestHarness.EntityCatalog);

            Assert.Equal(printed, (string?)row["name"]);
            Assert.Null(row["internalName"]);
        }
    }

    /// <summary>
    /// The overview says how long the run has taken, in the exact form the game prints its own
    /// clock in.
    /// </summary>
    /// <remarks>
    /// No surface answered "how long has this taken me" at all, and a round wanting it diffed
    /// wall-clock stamps between its own calls — which measures the session, not the run. The game
    /// keeps the number: <c>Time Played</c> is a saved <c>DoubleVariable</c> it accumulates every
    /// frame, and its screen form is <c>Utils.BeautifyTimeUltraPrecise</c> — seconds under a
    /// minute, years over one, and otherwise two-digit units joined by colons with leading empty
    /// units dropped.
    /// </remarks>
    [Theory]
    [InlineData(0d, "0s")]
    [InlineData(45d, "45s")]
    [InlineData(60d, "01:00")]
    [InlineData(461d, "07:41")]
    [InlineData(7661d, "02:07:41")]
    [InlineData(359999d, "99:59:59")]
    [InlineData(31557601d, "1.00y")]
    public void The_overview_prints_the_runs_own_clock_the_way_the_game_does(
        double seconds, string expected)
    {
        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(Clock(
            new WorldNumberVariable(
                KnownEntities.TimePlayed.Uuid,
                new BigDouble(seconds),
                isPercent: false,
                isTime: true,
                isTimeAccurate: true))));

        Assert.Equal(expected, (string?)overview["timePlayed"]);
    }

    /// <summary>
    /// Every variable the game marks as a duration prints as one, in whichever of its two time
    /// formats the second flag selects — not only the one the suite had hard-coded.
    /// </summary>
    /// <remarks>
    /// <c>NumberVariable.GetValueDisplay()</c> tests <c>isTimeVariable</c> first and
    /// <c>isTimeAccurateVariable</c> inside it: both set is <c>BeautifyTimeUltraPrecise</c>
    /// (<c>hh:mm:ss</c>), the flag alone is <c>BeautifyTimeAccurate</c>, whose unit thresholds are
    /// the game's own — seconds up to 1000, then minutes up to 60000, then hours.
    /// A caller reading 7661 could not tell a duration from a quantity.
    /// </remarks>
    [Theory]
    [InlineData(true, 7661d, "02:07:41")]
    [InlineData(true, 45d, "45s")]
    [InlineData(false, 7661d, "128m")]
    [InlineData(false, 461d, "461s")]
    [InlineData(false, 45.678d, "45.7s")]
    [InlineData(false, 4.5678d, "4.57s")]
    public void A_variable_the_game_draws_as_a_duration_prints_as_one(
        bool ultraPrecise,
        double seconds,
        string expected)
    {
        var id = Guid.Parse("18c498f5-e4a7-4549-b093-117e206cc043");
        var world = new GameWorldState
        {
            DoubleVariables = PublicationTable<WorldNumberVariable>.Create(new[]
            {
                new WorldNumberVariable(
                    id,
                    new BigDouble(seconds),
                    isPercent: false,
                    isTime: true,
                    isTimeAccurate: ultraPrecise),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                Clean("double-variables"),
            }),
            CollectedAtEpoch = 51,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var context = GameMcpTestHarness.Context(world);

        var listed = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "double-variables", 0, 10));
        Assert.Equal(expected, (string?)listed["rows"]![0]!["value"]);

        var block = GameMcpTestHarness.Json(
            GameMcpWorldQuery.GetRow(context, "double-variables", id.ToString("D")));
        Assert.Equal(expected, (string?)block["row"]!["value"]);
    }

    /// <summary>
    /// The run's clock and the reset's clock, side by side and in the same form. After a reset the
    /// two are different answers to "how long has this taken me", and the overview carried only the
    /// one that never resets.
    /// </summary>
    /// <remarks>
    /// <c>TimePlayedThisReset</c> is the game's own second clock — "Time Played this Reset", a
    /// <c>DoubleVariable</c> with both duration flags set, visible behind <c>TimeResetUnlocked</c>
    /// and with <c>dontResetValue: 0</c>, so it starts again at every reset while
    /// <c>TimePlayed</c> keeps counting.
    /// </remarks>
    [Fact]
    public void The_overview_prints_the_resets_clock_beside_the_runs()
    {
        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(Clock(
            new WorldNumberVariable(
                KnownEntities.TimePlayed.Uuid,
                new BigDouble(7661d),
                isPercent: false,
                isTime: true,
                isTimeAccurate: true),
            new WorldNumberVariable(
                KnownEntities.TimePlayedThisReset.Uuid,
                new BigDouble(461d),
                isPercent: false,
                isTime: true,
                isTimeAccurate: true))));

        Assert.Equal("02:07:41", (string?)overview["timePlayed"]);
        Assert.Equal("07:41", (string?)overview["timePlayedThisReset"]);
    }

    /// <summary>
    /// A world that never published the clock says nothing about it rather than printing a zero
    /// that would read as a run that just started. A save that has never reset publishes no reset
    /// clock at all, and a zero there would claim a reset that never happened.
    /// </summary>
    [Fact]
    public void An_unpublished_clock_is_absent_rather_than_zero()
    {
        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(Clock()));
        var neverReset = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(Clock(
            new WorldNumberVariable(
                KnownEntities.TimePlayed.Uuid,
                new BigDouble(7661d),
                isPercent: false,
                isTime: true,
                isTimeAccurate: true))));

        Assert.Equal("available", (string?)overview["status"]);
        Assert.Null(overview["timePlayed"]);
        Assert.Null(overview["timePlayedThisReset"]);
        Assert.Equal("02:07:41", (string?)neverReset["timePlayed"]);
        Assert.Null(neverReset["timePlayedThisReset"]);
    }

    private static GameMcpFrameContext Clock(params WorldNumberVariable[] variables)
    {
        var world = new GameWorldState
        {
            DoubleVariables = PublicationTable<WorldNumberVariable>.Create(variables),
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        return GameMcpTestHarness.Context(world);
    }
}
