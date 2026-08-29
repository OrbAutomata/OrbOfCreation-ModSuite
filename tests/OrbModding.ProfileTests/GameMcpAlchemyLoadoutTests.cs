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

public sealed class GameMcpAlchemyLoadoutTests
{
    private static readonly Guid RecipeId = Guid.Parse("f8000000-0000-0000-0000-000000000001");
    private static readonly Guid TypeId = AlchemyGameplayDomainClassifier.AlchemyTypeUuid;
    private static readonly Guid ResourceId = Guid.Parse("f8000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_exposes_only_the_ui_add_and_remove_surface()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_alchemy");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.Equal(new[] { "mode", "uuid", "amount" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(new[] { "add", "remove" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
        Assert.NotNull(tool["inputSchema"]!["properties"]!["amount"]);
        Assert.Null(tool["inputSchema"]!["properties"]!["level"]);
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_alchemy",
            new JObject
            {
                ["mode"] = "add",
                ["uuid"] = RecipeId.ToString("D"),
                ["amount"] = 2,
            });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
    }

    /// <summary>
    /// Loadout order is cosmetic in this game, so the verb that reordered it is gone: no mode, no
    /// destination argument, and no ceiling for one.
    /// </summary>
    [Fact]
    public void Reordering_the_loadout_is_not_a_capability_this_tool_offers()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_alchemy");
        Assert.Null(tool["inputSchema"]!["properties"]!["destination"]);
        Assert.Null(tool["inputSchema"]!["allOf"]);

        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var move = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_alchemy",
                ["arguments"] = new JObject
                {
                    ["mode"] = "move",
                    ["uuid"] = RecipeId.ToString("D"),
                    ["amount"] = 1,
                },
            }));
        var destination = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_alchemy",
                ["arguments"] = new JObject
                {
                    ["mode"] = "add",
                    ["uuid"] = RecipeId.ToString("D"),
                    ["amount"] = 1,
                    ["destination"] = 2,
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): mode must be one of: add, remove",
            GameMcpTestHarness.Page(move));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'destination' is not accepted by game_alchemy",
            GameMcpTestHarness.Page(destination));
    }

    [Fact]
    public void Recipe_detail_carries_current_holdings_usage_and_each_next_ui_decision()
    {
        var response = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(World(targetAmount: 2, position: 1), generation: 701),
            "alchemy-recipes", RecipeId.ToString("D")).Freeze());

        var row = response["row"]!;
        Assert.Equal("Catalyze", (string?)row["name"]);
        Assert.Equal(2, (int)row["activeCount"]!);
        var loadout = Assert.IsType<JObject>(row["alchemyLoadout"]);
        Assert.Equal(2, (int)loadout["activeCount"]!);
        Assert.Equal(2, (int)loadout["slot"]!);
        Assert.True((bool)loadout["add"]!["available"]!);
        Assert.Equal(4, (int)loadout["add"]!["maximumAmount"]!);
        var add = Assert.IsType<JObject>(loadout["add"]);
        var costs = Assert.IsType<JArray>(add["usageCosts"]);
        var cost = Assert.IsType<JObject>(Assert.Single(costs));
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("5", (string?)cost["cost"]);
        Assert.Equal("80", (string?)cost["spendableAmount"]);
        Assert.True((bool)Assert.IsType<JObject>(loadout["remove"])["available"]!);
        Assert.Null(loadout["move"]);
        Assert.Null(row["level"]);

        var instance = Assert.Single(Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(World(targetAmount: 2, position: 1), generation: 701),
            "alchemy-instances", 0, 10).Freeze())["rows"]!.Values<JObject>());
        // An instance is not its recipe. It publishes no uuid of its own — the absence of a
        // handle is how a row says there is nothing to hand back — so world_get can no longer be
        // handed the recipe's id as though it were the instance's.
        Assert.Null(instance["uuid"]);
        Assert.Null(instance["addressable"]);
        Assert.Null(instance["category"]);
        Assert.Equal(GameMcpTestHarness.Handle(RecipeId), (string?)instance["recipe"]!["uuid"]);
        Assert.Equal("Catalyze", (string?)instance["recipe"]!["name"]);
        Assert.Equal(2, (int)instance["activeCount"]!);
        Assert.Equal(2, (int)instance["queuedCount"]!);

        // `settled` was `activeCount == queuedCount` — the two columns beside it — and the only
        // thing it ever reported was that a change had not landed yet, which the next read resolves
        // without anyone doing anything. Both reasons say the same thing: it is not a column.
        Assert.Equal(
            new[] { "recipe", "activeCount", "queuedCount", "drainRatio" },
            instance.Children<JProperty>().Select(property => property.Name));
    }

    /// <summary>
    /// The loadout list row says the same slot number the recipe detail says. It used to print the
    /// raw array position, one below both of them.
    /// </summary>
    [Fact]
    public void The_loadout_list_row_names_the_same_slot_the_detail_does()
    {
        var listed = Assert.Single(Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(World(targetAmount: 2, position: 1), generation: 702),
            "alchemy-loadout", 0, 10).Freeze())["rows"]!.Values<JObject>());

        Assert.Equal(2, (int)listed["slot"]!);
        Assert.Null(listed["position"]);
        Assert.Equal("Catalyze", (string?)listed["recipe"]!["name"]);
    }

    [Fact]
    public void Settled_delta_comes_from_the_new_world_not_the_action_assertion()
    {
        var before = World(targetAmount: 1, position: 0);
        var after = World(targetAmount: 3, position: 0);
        var command = new GameMcpCommand(1, GameMcpCommandKind.AlchemyLoadout,
            9, 3, "add", RecipeId, Guid.Empty, "AlchemyRecipeSO",
            1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 81));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 82), command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(1, (int)delta["activeCount"]!["before"]!);
        Assert.Equal(3, (int)delta["activeCount"]!["after"]!);
    }

    private static GameWorldState World(int targetAmount, int position)
    {
        var recipe = new WorldAlchemyRecipe(
            RecipeId, TypeId, discovered: true, maxLevel: 1, advancementLevel: 0,
            discoveryRarityLevel: 0, masteryXp: BigDouble.Zero, masteryLevel: 0,
            recipeTime: BigDouble.One, isRequiredDiscovery: false,
            isCompletionRecipe: false, isAdvancementRecipe: false, completionTime: 0,
            isDebugAlchemy: false, power: BigDouble.Zero, speed: BigDouble.Zero,
            drainCostMod: BigDouble.Zero, special: BigDouble.Zero,
            timeReqMod: BigDouble.Zero, timeScalingMod: BigDouble.Zero,
            masteryXpRate: BigDouble.Zero, effectLevels: BigDouble.Zero,
            overdrivePower: BigDouble.Zero, overdriveSpeed: BigDouble.Zero,
            overdriveDrainCostMod: BigDouble.Zero, overdriveXpRate: BigDouble.Zero,
            freeUsageSlots: BigDouble.One, maxUsageSlots: new BigDouble(8),
            cachedCompletionTime: BigDouble.Zero, requiredExperience: BigDouble.One);
        var decision = new WorldAlchemyLoadoutDecision(RecipeId, position, 4,
            targetAmount, targetAmount, 1, 4, true, true);
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(ResourceId, new BigDouble(80), new BigDouble(100),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var resource = new WorldResource(in reading, true, new BigDouble(20), 0.8, false,
            new BigDouble(80), BigDouble.Zero);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(RecipeId, "AlchemyRecipeSO", "Catalyze", "catalyze"),
            new EntityIdentityName(TypeId, "AlchemyTypeSO", "Alchemy", "alchemy"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Knowledge", "knowledge"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(new[] { recipe }),
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(
                    RecipeId, targetAmount, targetAmount, true, BigDouble.One),
            }),
            AlchemyLoadout = PublicationTable<WorldAlchemyLoadoutDecision>.Create(new[] { decision }),
            AlchemyUsageCosts = PublicationTable<WorldAlchemyUsageCost>.Create(new[]
            {
                new WorldAlchemyUsageCost(RecipeId, ResourceId, new BigDouble(5)),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("alchemy recipes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus("concept instances", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus("ordinary alchemy loadout", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus("resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            value,
            World(targetAmount: 0, position: -1).EntityIdentities));
}
