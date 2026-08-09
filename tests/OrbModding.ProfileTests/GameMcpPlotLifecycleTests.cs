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

public sealed class GameMcpPlotLifecycleTests
{
    private static readonly Guid PlotId =
        Guid.Parse("fd000000-0000-0000-0000-000000000001");
    private static readonly Guid ActionId =
        Guid.Parse("fd000000-0000-0000-0000-000000000002");

    [Fact]
    public void Tool_requires_the_exact_plot_action_pair_and_visible_ui_modes()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_agromancy");

        Assert.Equal(new[] { "mode", "uuid" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Contains("add_plot_action",
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
        var operation = GameMcpProtocolRouter.BuildOperation("game_agromancy", new JObject
        {
            ["mode"] = "add_plot_action",
            ["uuid"] = PlotId.ToString("D"),
            ["actionUuid"] = ActionId.ToString("D"),
            ["amount"] = 2,
        });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
        Assert.Equal(ActionId, operation.SecondaryUuid);
    }

    [Fact]
    public void Plot_action_rows_name_arbitrary_pairs_and_publish_only_runnable_costs()
    {
        var world = World(prerequisitesReady: true, active: 2);
        var response = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 911),
            "agromancy-plot-actions", 0, 10).Freeze(), world);

        var row = Assert.Single(response["rows"]!.Values<JObject>());
        Assert.Equal("Moon Garden", (string?)row["plot"]!["name"]);
        Assert.Equal("Plant Moondust", (string?)row["action"]!["name"]);
        Assert.Equal(2, (int)row["active"]!);
        Assert.True((bool)row["add"]!["available"]!);
        Assert.Equal(3, (int)row["add"]!["plotQuantityCost"]!);
        Assert.True((bool)row["remove"]!["available"]!);

        var processing = Assert.Single(Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 911),
            "agromancy-processing", 0, 10).Freeze(), world)["rows"]!.Values<JObject>());
        Assert.Equal("Moon Garden", (string?)processing["plot"]!["name"]);
        Assert.Equal("Plant Moondust", (string?)processing["action"]!["name"]);
        Assert.Equal(2, (int)processing["amount"]!);
        Assert.Equal(4, (int)processing["capacity"]!);
        Assert.Equal(1, (int)processing["used"]!);

        var blockedWorld = World(prerequisitesReady: false, active: 0);
        var blocked = Assert.Single(Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(blockedWorld, generation: 912),
            "agromancy-plot-actions", 0, 10).Freeze(), blockedWorld)["rows"]!.Values<JObject>());
        // The prerequisite is not readable, which is a missing read rather than a refusal, so the
        // row says so in the same words every other unreadable fact uses and never claims false.
        Assert.Equal("unavailable", (string?)blocked["add"]!["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)blocked["add"]!["reasonCode"]);
        Assert.Null(blocked["add"]!["available"]);
        Assert.Equal("game_agromancy add_plot_action", (string?)blocked["add"]!["checkWith"]);
        Assert.Null(blocked["add"]!["plotQuantityCost"]);
        // Nothing is running, which the row names on its own axis rather than leaving bare for the
        // backstop to read as a refusal the game never issued.
        Assert.False((bool)blocked["remove"]!["available"]!);
        Assert.Equal("ERR_NOT_FOUND", (string?)blocked["remove"]!["reasonCode"]);
        Assert.Equal(
            "This is not active, so there is nothing to act on.",
            (string?)blocked["remove"]!["reason"]);
    }

    private static GameWorldState World(bool prerequisitesReady, int active)
    {
        var pair = new WorldPlotAction(
            new RawPlotAction(PlotId, ActionId, 1, active > 0 ? 1 : 0,
                prerequisitesReady
                    ? PlotActionPrerequisiteEvidence.NativeLatchedTrue
                    : PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation),
            elementCost: 3,
            elementCostKnown: true,
            hasEnoughForOneInstance: true,
            maximumRemainingInstances: 8);
        var instances = PublicationTable<WorldPlotActionInstance>.Create(new[]
        {
            new WorldPlotActionInstance(PlotId, ActionId, 0, 0, false, false, true),
        });
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(PlotId, "PlotNodeSO", "Moon Garden", "moonGarden"),
            new EntityIdentityName(ActionId, "PlotNodeActionSO", "Plant Moondust", "plantMoondust"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            PlotActions = PublicationTable<WorldPlotAction>.Create(new[] { pair }),
            PlotActionInstances = instances,
            ActionQueueSlots = active > 0
                ? PublicationTable<WorldActionQueueSlot>.Create(new[]
                {
                    new WorldActionQueueSlot(PlotLifecycleNativeBindings.ActiveActionsId,
                        0, false, PlotId, ActionId, active, true),
                })
                : PublicationTable<WorldActionQueueSlot>.Empty,
            ActionQueues = PublicationTable<WorldActionQueue>.Create(new[]
            {
                new WorldActionQueue(PlotLifecycleNativeBindings.ActiveActionsId,
                    Guid.Empty, 4, active > 0 ? 1 : 0, active > 0 ? 3 : 4,
                    hasEmptySlot: true, consistent: true),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("plot nodes", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
                new WorldCollectionCategoryStatus("plot node actions", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
                new WorldCollectionCategoryStatus("plot actions", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
                new WorldCollectionCategoryStatus("action queues", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
