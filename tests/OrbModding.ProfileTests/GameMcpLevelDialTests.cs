using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The Druidry Lv and Alchemy Lv -/+ adjusters live on the Agromancy and Alchemy screens, so each
/// is a mode of that screen's verb, and both reach the one level-dial action the casting dials use.
/// </summary>
public sealed class GameMcpLevelDialTests
{
    [Theory]
    [InlineData("game_agromancy", "set_druidry_level")]
    [InlineData("game_alchemy", "set_alchemy_level")]
    public void EachScreenVerbAdvertisesItsLevelModeAndTheLevelArgument(string tool, string mode)
    {
        var schema = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == tool)["inputSchema"]!;

        Assert.Contains(mode, schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Equal(1, (int)schema["properties"]!["level"]!["minimum"]!);
        var rule = Assert.Single(schema["allOf"]!.Values<JObject>(),
            candidate => (string?)candidate!["if"]!["properties"]!["mode"]!["const"] == mode)!;
        Assert.Equal(new[] { "level" }, rule["then"]!["required"]!.Values<string>());
    }

    [Theory]
    [InlineData("game_agromancy", "set_druidry_level", "druidry")]
    [InlineData("game_alchemy", "set_alchemy_level", "alchemy")]
    public void TheLevelModePreparesTheSharedDialCommandForItsOwnDial(
        string tool,
        string mode,
        string dial)
    {
        var operation = GameMcpProtocolRouter.BuildOperation(
            tool,
            new JObject { ["mode"] = mode, ["level"] = 2 });

        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
        Assert.True(Plugin.TryPrepareGameMcpCommand(
            new GameMcpFrameOperation(1, operation),
            GameMcpTestHarness.Context(World(unlocked: true)),
            out var command,
            out var failure), failure?.Reason);
        Assert.Equal(GameMcpCommandKind.SpellComposition, command.Kind);
        Assert.Equal(mode, command.Mode);
        Assert.Equal(dial, command.PayloadKey);
        Assert.Equal(2, command.Amount);
    }

    [Fact]
    public void TheLevelModeTakesALevelAndNothingTheListModesNeed()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required field " +
            "'level' is missing for mode 'set_druidry_level'",
            Call(router, 1, "game_agromancy", new JObject { ["mode"] = "set_druidry_level" }));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field 'uuid' is not " +
            "accepted for mode 'set_alchemy_level'",
            Call(router, 2, "game_alchemy", new JObject
            {
                ["mode"] = "set_alchemy_level",
                ["level"] = 2,
                ["uuid"] = Guid.NewGuid().ToString("D"),
            }));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field 'level' is " +
            "not accepted for mode 'add_element'",
            Call(router, 3, "game_agromancy", new JObject
            {
                ["mode"] = "add_element",
                ["uuid"] = Guid.NewGuid().ToString("D"),
                ["amount"] = 1,
                ["level"] = 2,
            }));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required field " +
            "'uuid' is missing for mode 'add'",
            Call(router, 4, "game_alchemy", new JObject { ["mode"] = "add", ["amount"] = 1 }));
    }

    [Fact]
    public void TheOverviewPublishesEachDialWhileItsAdjusterIsDrawn()
    {
        var unlocked = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(
            GameMcpTestHarness.Context(World(unlocked: true))));
        var locked = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(
            GameMcpTestHarness.Context(World(unlocked: false))));

        Assert.Equal(2, (int)unlocked["druidryLevel"]!["current"]!);
        Assert.Equal(3, (int)unlocked["druidryLevel"]!["maximum"]!);
        Assert.Equal(4, (int)unlocked["alchemyLevel"]!["current"]!);
        Assert.Equal(5, (int)unlocked["alchemyLevel"]!["maximum"]!);
        Assert.Null(locked["druidryLevel"]);
        Assert.Null(locked["alchemyLevel"]);
    }

    [Fact]
    public void ACommittedDruidryLevelReturnsItsOwnDialDelta()
    {
        var submission = new SpellCompositionSubmission(
            NativeActionOutcome.Commit(
                new NativeMutationCallOutcome(1, 1, 1),
                "The Druidry Lv is now 1."));
        var mapped = submission.Outcome.ToResult();
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.SpellComposition,
            9,
            3,
            "set_druidry_level",
            Guid.Empty,
            Guid.Empty,
            "IntVariable",
            1,
            "druidry",
            string.Empty,
            false,
            frameContext: GameMcpTestHarness.Context(World(unlocked: true)));
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            9,
            3,
            GameMcpSpellCompositionProjection.Project(in submission, "druidry"));
        terminal = terminal.WithSettledPostState(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(World(unlocked: true, druidryLevel: 1)), command, terminal));

        var success = GameMcpTestHarness.Json(terminal.Project(command));

        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal("druidry", (string?)success["dial"]);
        Assert.Equal(2, (int)success["before"]!);
        Assert.Equal(1, (int)success["after"]!);
        Assert.Equal(3, (int)success["maximum"]!);
    }

    private static string Call(GameMcpProtocolRouter router, int id, string tool, JObject arguments) =>
        GameMcpTestHarness.Page(router.Handle(GameMcpAcceptanceFixture.Request(id, "tools/call",
            new JObject { ["name"] = tool, ["arguments"] = arguments })));

    private static GameWorldState World(bool unlocked, int druidryLevel = 2) => new()
    {
        CollectedAtEpoch = 9,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "spell workbench", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
        }),
        Views = PublicationTable<WorldView>.Create(new[]
        {
            new WorldView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, false, false, unlocked),
            new WorldView(KnownEntities.AlchAlchemyLevel.Uuid, false, false, unlocked),
        }.OrderBy(view => view.ViewId).ToArray()),
        SpellWorkbench = new WorldSpellWorkbench(
            1,
            3,
            true,
            4,
            12,
            3,
            9,
            druidryLevel: druidryLevel,
            maximumDruidryLevel: 3,
            alchemyLevel: 4,
            maximumAlchemyLevel: 5),
    };
}
