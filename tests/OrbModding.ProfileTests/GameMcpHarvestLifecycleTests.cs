using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpHarvestLifecycleTests
{
    private static readonly Guid ElementId =
        Guid.Parse("fc000000-0000-0000-0000-000000000001");
    private static readonly Guid ActionId =
        Guid.Parse("fc000000-0000-0000-0000-000000000002");
    private static readonly Guid ResourceId =
        Guid.Parse("fc000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_exposes_only_element_and_action_list_controls()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_agromancy");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.Equal(new[] { "mode", "uuid" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(new[]
        {
            "add_plot_action", "remove_plot_action", "add_element", "remove_element",
            "add_element_action", "remove_element_action",
        },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
        var operation = GameMcpProtocolRouter.BuildOperation("game_agromancy", new JObject
        {
            ["mode"] = "add_element_action",
            ["uuid"] = ElementId.ToString("D"),
            ["actionUuid"] = ActionId.ToString("D"),
            ["amount"] = 1,
        });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
    }

    [Fact]
    public void Action_modes_require_action_uuid_and_element_modes_reject_it()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_agromancy",
                ["arguments"] = new JObject
                {
                    ["mode"] = "add_element_action",
                    ["uuid"] = ElementId.ToString("D"),
                    ["amount"] = 1,
                },
            }));
        var extra = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_agromancy",
                ["arguments"] = new JObject
                {
                    ["mode"] = "add_element",
                    ["uuid"] = ElementId.ToString("D"),
                    ["actionUuid"] = ActionId.ToString("D"),
                    ["amount"] = 1,
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'actionUuid' is missing for mode 'add_element_action'",
            GameMcpTestHarness.Page(missing));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'actionUuid' is accepted only for action modes",
            GameMcpTestHarness.Page(extra));
    }

    [Fact]
    public void Harvest_element_detail_joins_current_counts_costs_holdings_and_offered_actions()
    {
        var world = World(elementActive: 2, actionActive: 1);
        var response = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 901),
            "agromancy-elements", ElementId.ToString("D")).Freeze(), world);

        var row = response["row"]!;
        Assert.Equal("Fire", (string?)row["name"]);
        Assert.Equal(2, (int)row["active"]!);
        Assert.True((bool)row["addElement"]!["available"]!);
        var usage = Assert.Single(row["addElement"]!["costs"]!.Values<JObject>());
        Assert.Equal("Mana", (string?)usage["resource"]!["name"]);
        Assert.Equal("4", (string?)usage["cost"]);
        Assert.Equal("20", (string?)usage["spendableAmount"]);
        var action = Assert.Single(row["actions"]!.Values<JObject>());
        Assert.Equal("Grow", (string?)action["name"]);
        Assert.Equal(1, (int)action["active"]!);
        Assert.True((bool)action["add"]!["available"]!);
        Assert.Equal("4.5", (string?)action["add"]!["nextDrain"]![0]!["cost"]);
        Assert.True((bool)action["remove"]!["available"]!);

        var blockedWorld = World(elementActive: 2, actionActive: 4,
            elementAddAvailable: false);
        var blocked = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(blockedWorld, generation: 902),
            "agromancy-elements", ElementId.ToString("D")).Freeze(),
            blockedWorld)["row"]!;
        Assert.False((bool)blocked["addElement"]!["available"]!);
        Assert.Null(blocked["addElement"]!["costs"]);
        Assert.Equal("ERR_UNAFFORDABLE", (string?)blocked["addElement"]!["reasonCode"]);
        var blockedAdd = blocked["actions"]![0]!["add"]!;
        Assert.Null(blockedAdd["nextDrain"]);
        Assert.False((bool)blockedAdd["available"]!);
        Assert.Equal("ERR_LIMIT", (string?)blockedAdd["reasonCode"]);
        Assert.Equal("The element's capacity is already full.", (string?)blockedAdd["reason"]);
    }

    /// <summary>
    /// The count an action commit moved is the action's, so it is said under the action — the same
    /// place the element's own row says it. Flat beside the element handle, `add_element` and
    /// `add_element_action` both answered `active: 0 -&gt; 1`, and the response with two entities on
    /// it never said which one had moved.
    /// </summary>
    [Fact]
    public void Settled_action_delta_uses_the_new_world()
    {
        var before = World(elementActive: 2, actionActive: 1);
        var after = World(elementActive: 2, actionActive: 2);
        var command = new GameMcpCommand(1, GameMcpCommandKind.HarvestLifecycle,
            9, 3, "add_element_action", ElementId, ActionId, "HarvestElementSO",
            1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 91));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 92), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Equal("Fire", (string?)delta["name"]);
        Assert.Equal("Grow", (string?)delta["action"]!["name"]);
        Assert.Null(delta["active"]);
        Assert.Equal(1, (int)delta["action"]!["active"]!["before"]!);
        Assert.Equal(2, (int)delta["action"]!["active"]!["after"]!);
        // A commit answers with what the press changed. What is possible next is the read
        // surface's job, and world_get agromancy-plot-actions already carries the same decision.
        Assert.Null(delta["next"]);
    }

    /// <summary>
    /// The two verbs answer about two different things, so their two answers say two different
    /// things. Both moved a count from 1 to 2 here; the element's is the element's own and the
    /// action's is under the action, which is the only reason a reader can tell the responses apart
    /// without remembering which call produced which.
    /// </summary>
    [Fact]
    public void The_element_verb_and_the_action_verb_do_not_answer_the_same_line()
    {
        var element = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(World(elementActive: 2, actionActive: 1), generation: 92),
            new GameMcpCommand(1, GameMcpCommandKind.HarvestLifecycle,
                9, 3, "add_element", ElementId, Guid.Empty, "HarvestElementSO",
                1, string.Empty, string.Empty, false,
                frameContext: GameMcpTestHarness.Context(
                    World(elementActive: 1, actionActive: 1), generation: 91)),
            GameMcpCommandResult.Committed("committed", 9, 3)),
            World(elementActive: 2, actionActive: 1));
        var action = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(World(elementActive: 1, actionActive: 2), generation: 92),
            new GameMcpCommand(1, GameMcpCommandKind.HarvestLifecycle,
                9, 3, "add_element_action", ElementId, ActionId, "HarvestElementSO",
                1, string.Empty, string.Empty, false,
                frameContext: GameMcpTestHarness.Context(
                    World(elementActive: 1, actionActive: 1), generation: 91)),
            GameMcpCommandResult.Committed("committed", 9, 3)),
            World(elementActive: 1, actionActive: 2));

        Assert.Equal(2, (int)element["active"]!["after"]!);
        Assert.Null(element["action"]);

        Assert.Null(action["active"]);
        Assert.Equal(2, (int)action["action"]!["active"]!["after"]!);
        Assert.NotEqual(element.ToString(), action.ToString());
    }

    private static GameWorldState World(
        int elementActive,
        int actionActive,
        bool elementAddAvailable = true)
    {
        var element = new WorldHarvestElement(ElementId, new BigDouble(3), 4,
            2, 3, 1, 100, 10, BigDouble.One, BigDouble.One, BigDouble.One,
            BigDouble.One, BigDouble.One, BigDouble.One, BigDouble.One,
            BigDouble.One, BigDouble.One, BigDouble.One, BigDouble.One, BigDouble.One);
        var elementControl = new WorldHarvestElementControl(
            ElementId, true, elementActive, 5, true, elementAddAvailable,
            elementAddAvailable, elementActive > 0);
        var actionControl = new WorldHarvestActionControl(
            ElementId, ActionId, true, actionActive, 4, actionActive < 4, actionActive > 0);
        var costs = PublicationTable<WorldHarvestLifecycleCost>.Create(new[]
        {
            new WorldHarvestLifecycleCost(ElementId, Guid.Empty,
                WorldHarvestLifecycleCostKind.ElementUsage, ResourceId, new BigDouble(4)),
            new WorldHarvestLifecycleCost(ElementId, ActionId,
                WorldHarvestLifecycleCostKind.NextActionDrain, ResourceId, new BigDouble(4.5)),
        });
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(ResourceId, new BigDouble(20), new BigDouble(100),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var resource = new WorldResource(in reading, true, new BigDouble(80), 0.2, false,
            new BigDouble(20), BigDouble.Zero);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(ElementId, "HarvestElementSO", "Fire", "fire"),
            new EntityIdentityName(ActionId, "HarvestActionSO", "Grow", "grow"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Mana", "mana"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            HarvestElements = PublicationTable<WorldHarvestElement>.Create(new[] { element }),
            HarvestElementControls = PublicationTable<WorldHarvestElementControl>.Create(
                new[] { elementControl }),
            HarvestActionControls = PublicationTable<WorldHarvestActionControl>.Create(
                new[] { actionControl }),
            HarvestLifecycleCosts = costs,
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("harvest elements", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
                new WorldCollectionCategoryStatus("harvest lifecycle", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
                new WorldCollectionCategoryStatus("resources", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
