using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpStructureLifecycleTests
{
    private static readonly Guid StructureId =
        Guid.Parse("fd000000-0000-0000-0000-000000000001");

    [Fact]
    public void Tool_exposes_only_the_two_player_visible_toggle_states()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_structure");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.Equal(new[] { "mode", "uuid" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(new[] { "enable", "disable" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
        var operation = GameMcpProtocolRouter.BuildOperation("game_structure", new JObject
        {
            ["mode"] = "disable",
            ["uuid"] = StructureId.ToString("D"),
        });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
    }

    [Fact]
    public void Structure_detail_exposes_enabled_state_and_only_the_next_toggle()
    {
        var enabledWorld = World(disabled: false, available: true);
        var unavailableWorld = World(disabled: false, available: false);

        var enabled = Row(enabledWorld);
        var unavailable = Row(unavailableWorld);

        Assert.Equal("Focus", (string?)enabled["name"]);
        Assert.True((bool)enabled["enabled"]!);
        Assert.True((bool)enabled["toggle"]!["available"]!);
        Assert.Equal("disable", (string?)enabled["toggle"]!["next"]);
        Assert.False((bool)unavailable["toggle"]!["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)unavailable["toggle"]!["reasonCode"]);
        Assert.Null(unavailable["toggle"]!["next"]);
    }

    [Fact]
    public void Structure_list_exposes_the_same_enabled_state_as_detail()
    {
        var world = World(disabled: true, available: true);
        var detail = Row(world);
        var list = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 902),
            "structures", 0, 10).Freeze(), world);
        var listed = Assert.Single((JArray)list["rows"]!);

        Assert.Equal((bool)detail["enabled"]!, (bool)listed["enabled"]!);
    }

    [Fact]
    public void Settled_delta_is_observed_from_the_new_world_and_settlement_requires_it()
    {
        var before = World(disabled: false, available: true);
        var after = World(disabled: true, available: true);
        var command = new GameMcpCommand(1, GameMcpCommandKind.StructureLifecycle,
            9, 3, "disable", StructureId, Guid.Empty, "StructureSO",
            1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 91));
        var afterContext = GameMcpTestHarness.Context(after, generation: 92);

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            afterContext, command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Equal("Focus", (string?)delta["name"]);
        Assert.True((bool)delta["enabled"]!["before"]!);
        Assert.False((bool)delta["enabled"]!["after"]!);
        Assert.True(GameMcpPostStateSettlement.IsReady(
            afterContext, 91, 0, command));
        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(before, generation: 92), 91, 0, command));
    }

    private static JObject Row(GameWorldState world) =>
        Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 901),
            "structures", StructureId.ToString("D")).Freeze(), world)["row"]
            as JObject ?? throw new InvalidOperationException("row was unavailable");

    private static GameWorldState World(bool disabled, bool available)
    {
        var modifiers = new RawStructureModifiers(
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero);
        var reading = new RawStructureSample(
            StructureId, Guid.Empty, new BigDouble(2), BigDouble.Zero, available,
            0, 0, 0, BigDouble.Zero, BigDouble.Zero, false, 0, 0f,
            false, disabled, 0, false, 0, Guid.Empty, in modifiers);
        var structure = new WorldStructure(
            in reading, new BigDouble(2), false, new BigDouble(2), 0d);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(StructureId, "StructureSO", "Focus", "focus"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            Structures = PublicationTable<WorldStructure>.Create(new[] { structure }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "structures", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
