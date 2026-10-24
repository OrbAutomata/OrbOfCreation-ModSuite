using System;
using System.Collections.Generic;
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
    private static readonly Guid ScrollId =
        Guid.Parse("fd000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_requires_the_exact_plot_action_pair_and_visible_ui_modes()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_agromancy");

        Assert.Equal(new[] { "mode" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Contains("add_plot_action",
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        var operation = GameMcpToolCatalog.Shipping.BuildOperation("game_agromancy", new JObject
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
        var response = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(world, generation: 911),
            "agromancy-plot-actions", 0, 10).Freeze(), world);

        var row = Assert.Single(response["rows"]!.Values<JObject>());
        Assert.Equal("Moon Garden", (string?)row["plot"]!["name"]);
        Assert.Equal("Plant Moondust", (string?)row["action"]!["name"]);
        Assert.Equal(2, (int)row["active"]!);

        // A row answers each decision with the one word a cell has room for. What a press would
        // cost belongs to the detail projection, which is where a caller who asked about this one
        // pair still finds it.
        Assert.Equal("yes", (string?)row["add"]);
        Assert.Equal("yes", (string?)row["remove"]);
        Assert.Equal(3, (int)AddDecision(world, active: 2)["plotQuantityCost"]!);

        var processing = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(world, generation: 911),
            "agromancy-processing", 0, 10).Freeze(), world)["rows"]!.Values<JObject>());
        Assert.Equal("Moon Garden", (string?)processing["plot"]!["name"]);
        Assert.Equal("Plant Moondust", (string?)processing["action"]!["name"]);
        Assert.Equal(2, (int)processing["amount"]!);
        Assert.Equal(4, (int)processing["capacity"]!);
        Assert.Equal(1, (int)processing["used"]!);

        // Whether the occupant is under way rather than merely present is the game's `IsEngaged()`,
        // and it turns over between two reads with nobody playing. What the slot holds and how much
        // of it are what a plan is made of, and they are the whole row.
        Assert.Equal(
            new[] { "slot", "capacity", "used", "plot", "action", "amount" },
            processing.Children<JProperty>().Select(property => property.Name));

        var blockedWorld = World(prerequisitesReady: false, active: 0);
        var blocked = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(blockedWorld, generation: 912),
            "agromancy-plot-actions", 0, 10).Freeze(), blockedWorld)["rows"]!.Values<JObject>());
        // The prerequisite is not readable, which is a missing read rather than a refusal, so the
        // cell says so in one word and never claims false. Nothing is running, which the row names
        // on its own axis rather than leaving bare for the backstop to read as a refusal the game
        // never issued.
        Assert.Equal("unverified", (string?)blocked["add"]);
        Assert.Equal("inactive", (string?)blocked["remove"]);

        // The sentence and the repair are not lost, only left to the projection that has room for
        // them — the one a mutation answers with and an explanation reads from.
        var blockedAdd = AddDecision(blockedWorld, active: 0);
        Assert.Equal("unavailable", (string?)blockedAdd!["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)blockedAdd!["reasonCode"]);
        Assert.Equal(
            "game_agromancy add_plot_action", (string?)blockedAdd!["checkWith"]);
        Assert.Equal(
            "This is not active, so there is nothing to act on.",
            (string?)GameMcpTestHarness.Json(
                GameMcpHarvestLifecycleProjection.PlotActionRemoveDecision(0))["reason"]);
    }

    /// <summary>
    /// Create Spring drained Alchemic Scroll at a rate no read published: the pair, the action's
    /// page and the add reply said what the plot paid and nothing about what the action takes each
    /// second. All three now carry the drain the diamond's hover prints, in the screen's words.
    /// </summary>
    [Fact]
    public void A_plot_action_names_what_it_drains_each_second_where_it_is_read_and_started()
    {
        var world = DrainingWorld(active: 1);
        var context = GameMcpTestHarness.Context(world, generation: 916);

        var read = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            context, "plot-node-actions", new[] { ActionId.ToString("D") }).Freeze(), world);
        var results = Assert.IsType<JArray>(read["results"]);
        var detail = Assert.IsType<JObject>(Assert.Single(results));
        var detailRow = Assert.IsType<JObject>(detail["row"]);
        var drains = Assert.IsType<JArray>(detailRow["drainCostsPerSecond"]);
        var drain = Assert.IsType<JObject>(Assert.Single(drains));
        Assert.Equal("Alchemic Scroll", (string?)drain["resource"]!["name"]);
        Assert.Equal("0.500", (string?)drain["cost"]);
        Assert.Equal("20", (string?)drain["spendableAmount"]);
        Assert.True((bool)drain["affordable"]!);

        var row = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            context, "agromancy-plot-actions", 0, 10).Freeze(), world)["rows"]!.Values<JObject>())!;
        Assert.Null(row["drainCostsPerSecond"]);

        var page = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            context, "plot-node-actions", new[] { ActionId.ToString("D") }).Freeze(),
            world)["results"]!.Values<JObject>())!;
        Assert.True(JToken.DeepEquals(
            drain, Assert.Single(page["row"]!["drainCostsPerSecond"]!.Values<JObject>())));

        var added = GameMcpTestHarness.Json(GameMcpPostStateSettlement.Project(
            context, PlotCommand("add_plot_action", DrainingWorld(active: 0)),
            GameMcpCommandResult.Committed("committed", 9, 3)), world);
        Assert.Equal(1, (int)added["active"]!["after"]!);
        Assert.True(JToken.DeepEquals(
            drain, Assert.Single(added["drainCostsPerSecond"]!.Values<JObject>())));

        var stopped = DrainingWorld(active: 0);
        var removed = GameMcpTestHarness.Json(GameMcpPostStateSettlement.Project(
            GameMcpTestHarness.Context(stopped, generation: 917),
            PlotCommand("remove_plot_action", world),
            GameMcpCommandResult.Committed("committed", 9, 3)), stopped);
        Assert.Equal(0, (int)removed["active"]!["after"]!);
        Assert.Null(removed["drainCostsPerSecond"]);
    }

    private static GameWorldState DrainingWorld(int active)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(ScrollId, new BigDouble(20), new BigDouble(100),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var world = World(prerequisitesReady: true, active: active);
        return world with
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, world.EntityIdentities.Rows
                .AsSpan().ToArray()
                .Append(new EntityIdentityName(ScrollId, "ResourceSO", "Alchemic Scroll", "alchemicScroll"))
                .OrderBy(row => row.EntityId).ToArray()),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                new WorldResource(in reading, true, new BigDouble(80), 0.2, false,
                    new BigDouble(20), BigDouble.Zero),
            }),
            PlotNodeActions = PublicationTable<WorldPlotNodeAction>.Create(new[]
            {
                new WorldPlotNodeAction(ActionId, false, false, false, 3, false, false, false,
                    120d, false, false, false, false, BigDouble.Zero, BigDouble.Zero,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, 0, 0, 0, 0, 1, 0, 0),
            }),
            PlotActionDrains = PublicationTable<WorldPlotActionDrain>.Create(new[]
            {
                new WorldPlotActionDrain(PlotId, ActionId, 0, ScrollId, new BigDouble(0.5)),
            }),
        };
    }

    private static GameMcpCommand PlotCommand(string mode, GameWorldState before) =>
        new(
            1,
            GameMcpCommandKind.Harvest,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: mode,
            targetId: PlotId,
            secondaryId: ActionId,
            derivedNativeType: "PlotNodeSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false,
            frameContext: GameMcpTestHarness.Context(before));

    /// <summary>
    /// The page this lane was called for. Twenty pairs whose prerequisite the game will not
    /// evaluate until the action starts used to repeat a 133-character sentence and its
    /// <c>ERR_</c> class in two columns of every row — 6,710 bytes for twenty rows of five facts.
    /// Words carry the same two decisions, and what a reader has to hold in their head is a
    /// vocabulary instead of a paragraph. The three the whole page agrees on are said once above
    /// the rows and then not said again: a share line that named them and left them on every row
    /// under it was the same fact printed twenty-one times.
    /// </summary>
    [Fact]
    public void A_page_of_blocked_pairs_says_the_block_in_one_word_per_cell()
    {
        var world = ManyPairs(20);
        var page = GameMcpTextPage.Render(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(world, generation: 913),
            "agromancy-plot-actions", 0, 20).Freeze(), world));
        var lines = page.Split('\n');

        Assert.Equal("rows 20/20", lines[0]);
        Assert.Equal("these 20 share: active=0, add=unverified, remove=inactive", lines[1]);
        Assert.Equal("[plot | action]", lines[2]);
        Assert.Equal("Moon Garden 0 fd0000 | Plant Moondust 0 fe0000", lines[3]);
        Assert.Equal(23, lines.Length);

        // The regression this retires, in the units it was reported in.
        Assert.DoesNotContain("ERR_", page, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot be read ahead of time", page, StringComparison.Ordinal);
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(page), 1, 2000);
    }

    private static GameWorldState ManyPairs(int count)
    {
        var pairs = new List<WorldPlotAction>();
        var instances = new List<WorldPlotActionInstance>();
        var identities = new List<EntityIdentityName>();
        for (var index = 0; index < count; index++)
        {
            var plot = Guid.Parse("fd" + index.ToString("D2") + "0000-0000-0000-0000-000000000001");
            var action =
                Guid.Parse("fe" + index.ToString("D2") + "0000-0000-0000-0000-000000000002");
            pairs.Add(new WorldPlotAction(
                new RawPlotAction(plot, action, 1, 0,
                    PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation),
                elementCost: 3,
                elementCostKnown: true,
                hasEnoughForOneInstance: true,
                maximumRemainingInstances: 8));
            instances.Add(new WorldPlotActionInstance(plot, action, 0, 0, false, false, true));
            identities.Add(
                new EntityIdentityName(plot, "PlotNodeSO", "Moon Garden " + index, "moonGarden"));
            identities.Add(new EntityIdentityName(
                action, "PlotNodeActionSO", "Plant Moondust " + index, "plantMoondust"));
        }

        var world = World(prerequisitesReady: false, active: 0);
        return world with
        {
            EntityIdentities = GameMcpTestHarness.CatalogWith(identities.ToArray()),
            PlotActions = PublicationTable<WorldPlotAction>.Create(pairs.ToArray()),
            PlotActionInstances = PublicationTable<WorldPlotActionInstance>.Create(
                instances.ToArray()),
        };
    }

    /// <summary>
    /// A harvest node's own page says what it is, where it stands and what it holds, in the same
    /// words every other category's page uses. It used to be the reflective dump of the scan's
    /// field list — no `category`, no `state` — so a live round read `locked` off the node's list
    /// row, opened its page, and found no lifecycle word anywhere on it.
    /// </summary>
    /// <remarks>
    /// Every fact here is one the list row already computes. Nothing new is read from the game;
    /// what changed is that the block says the same things under the same names.
    /// </remarks>
    [Fact]
    public void A_harvest_node_page_says_its_lifecycle_in_the_word_its_own_list_row_says()
    {
        var world = PlotNodeWorld(visible: false);
        var context = GameMcpTestHarness.Context(world, generation: 914);
        var listed = Assert.Single(
            GameMcpTestHarness.Json(GameMcpWorldList.ListRows(context, "plot-nodes", 0, 10).Freeze(), world)["rows"]!
                .Values<JObject>())!;
        var block = Assert.Single(GameMcpTestHarness.Json(
            GameMcpWorldDetail.GetRows(context, "plot-nodes", new[] { PlotId.ToString("D") })
                .Freeze(),
            world)["results"]!.Values<JObject>())!;

        Assert.Equal("locked", (string?)listed["state"]);

        // Same words, same values: every fact the page adds is one the list row did not carry, and
        // not one is a second spelling of a fact it did.
        Assert.Equal(
            "uuid, name, state, masteryLevel, availableQuantity, amount",
            string.Join(", ", listed.Children<JProperty>().Select(property => property.Name)));

        // The one skeleton: identity at the top, the row under `row:`. `nativeType` is absent
        // because `plot-nodes` is one native class and `world_categories` publishes which.
        Assert.Equal(
            new[] { "uuid", "name", "internalName", "category", "row" },
            block.Children<JProperty>().Select(property => property.Name));
        Assert.Equal("Moon Garden", (string?)block["name"]);
        Assert.Equal("plot-nodes", (string?)block["category"]);
        Assert.Equal(
            "state, masteryLevel, masteryXp, idleQuantity, availableQuantity, " +
            "availableIdleQuantity, currentTime, amount",
            string.Join(", ", block["row"]!.Children<JProperty>().Select(p => p.Name)));
        Assert.Equal((string?)listed["state"], (string?)block["row"]!["state"]);

        var open = PlotNodeWorld(visible: true);
        Assert.Equal(
            "available",
            (string?)Assert.Single(GameMcpTestHarness.Json(
                GameMcpWorldDetail.GetRows(
                    GameMcpTestHarness.Context(open, generation: 915),
                    "plot-nodes",
                    new[] { PlotId.ToString("D") }).Freeze(),
                open)["results"]!.Values<JObject>())!["row"]!["state"]);
    }

    private static GameWorldState PlotNodeWorld(bool visible)
    {
        var reading = new RawPlotNodeSample(
            PlotId, visible, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            masteryLevel: 2, noMastery: false, noSizeDisplay: false, useVisibilityPrereq: true,
            hasErraticGrowth: false, debugMode: false, erraticQuantity: 0,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            lastQuantity: 0, idleQuantity: 3, totalQuantity: 5);
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, new[]
            {
                new EntityIdentityName(PlotId, "PlotNodeSO", "Moon Garden", "moonGarden"),
            }),
            PlotNodes = PublicationTable<WorldPlotNode>.Create(new[]
            {
                new WorldPlotNode(in reading, remainingQuantity: 3, remainingTotalQuantity: 5),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "plot-nodes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }

    /// <summary>The full verdict consumed by the shipping list's decision cell.</summary>
    private static JObject AddDecision(GameWorldState world, int active) =>
        GameMcpTestHarness.Json(GameMcpHarvestLifecycleProjection.ProjectPlotActionDecision(
            world, world.PlotActions[0], active).Freeze(), world);

    /// <summary>
    /// An Agromancy queue the world did not publish is a read that failed, not a list with no room.
    /// Saying "full" sent a caller off to free a slot that was never the problem.
    /// </summary>
    [Fact]
    public void An_unpublished_plot_action_queue_is_unreadable_rather_than_full()
    {
        var world = World(prerequisitesReady: true, active: 0, queuePublished: false);

        var row = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(world, generation: 913),
            "agromancy-plot-actions", 0, 10).Freeze(), world)["rows"]!.Values<JObject>());
        Assert.Equal("unreadable", (string?)row!["add"]);
        Assert.Equal(
            "The action queue was not readable in this world, so queue room is unknown.",
            (string?)AddDecision(world, active: 0)["reason"]);
    }

    private static GameWorldState World(bool prerequisitesReady, int active, bool queuePublished = true)
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
        var identities = GameMcpTestHarness.CatalogWith(
            new EntityIdentityName(PlotId, "PlotNodeSO", "Moon Garden", "moonGarden"),
            new EntityIdentityName(ActionId, "PlotNodeActionSO", "Plant Moondust", "plantMoondust"));
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = identities,
            PlotActions = PublicationTable<WorldPlotAction>.Create(new[] { pair }),
            PlotActionInstances = instances,
            ActionQueueSlots = active > 0
                ? PublicationTable<WorldActionQueueSlot>.Create(new[]
                {
                    new WorldActionQueueSlot(PlotLifecycleNativeBindings.ActiveActionsId,
                        0, false, PlotId, ActionId, active, true),
                })
                : PublicationTable<WorldActionQueueSlot>.Empty,
            ActionQueues = queuePublished
                ? PublicationTable<WorldActionQueue>.Create(new[]
                {
                    new WorldActionQueue(PlotLifecycleNativeBindings.ActiveActionsId,
                        Guid.Empty, 4, active > 0 ? 1 : 0, active > 0 ? 3 : 4,
                        hasEmptySlot: true, consistent: true),
                })
                : PublicationTable<WorldActionQueue>.Empty,
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
}
