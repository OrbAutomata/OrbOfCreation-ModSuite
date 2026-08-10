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

public sealed class GameMcpConsumableTests
{
    private static readonly Guid ConsumableId =
        Guid.Parse("f1000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherConsumableId =
        Guid.Parse("f1000000-0000-0000-0000-000000000002");
    private static readonly Guid UsageId =
        Guid.Parse("f1000000-0000-0000-0000-000000000003");
    private static readonly Guid ResourceId = KnownEntities.PotionToxicity.Uuid;

    [Fact]
    public void ToolAdvertisesOneFiveModeMutatingSurfaceWithConditionalInputs()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_consumable");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(
            new[] { "mode", "uuid" },
            schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "use", "cancel", "discard", "set_randomization", "move" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["amount"]);
        Assert.NotNull(schema["properties"]!["enabled"]);
        Assert.NotNull(schema["properties"]!["list"]);
        Assert.NotNull(schema["properties"]!["destination"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
    }

    [Theory]
    [InlineData("discard", "amount")]
    [InlineData("set_randomization", "enabled")]
    [InlineData("move", "list")]
    public void ConditionalInputsNameTheExactMissingArgument(string mode, string field)
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var response = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_consumable",
                ["arguments"] = new JObject
                {
                    ["mode"] = mode,
                    ["uuid"] = ConsumableId.ToString("D"),
                    ["destination"] = mode == "move" ? 1 : null,
                },
            }));

        var page = GameMcpTestHarness.Page(response);
        Assert.StartsWith("refused (ERR_INPUT): ", page, StringComparison.Ordinal);
        Assert.Contains("required field '" + field + "' is missing", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PreDecisionRowCarriesNamedHoldingsCostsAffordabilityAndEveryNextVerb()
    {
        var context = GameMcpTestHarness.Context(World());

        var response = Json(GameMcpWorldQuery.GetRow(
            context,
            "consumables",
            ConsumableId.ToString("D")));
        Assert.True(response["row"] is not null, response.ToString());
        var row = response["row"]!;

        Assert.Equal("Swift Thread", (string?)row["name"]);
        Assert.Equal(GameMcpTestHarness.Handle(ConsumableId), (string?)row["uuid"]);
        Assert.Equal("3", (string?)row["amount"]);
        Assert.Equal(1, (int)row["queued"]!);
        Assert.True((bool)row["use"]!["available"]!);
        var cost = Assert.Single(row["useCosts"]!).Value<JObject>()!;
        Assert.Equal("Toxicity", (string?)cost["resource"]!["name"]);
        Assert.Equal("250", (string?)cost["cost"]);
        Assert.Equal("9e6", (string?)cost["spendableAmount"]);
        Assert.True((bool)row["cancel"]!["available"]!);
        Assert.Equal(3, (int)row["discard"]!["maximumAmount"]!);
        Assert.False((bool)row["randomization"]!["enabled"]!);
        Assert.Equal("inventory", (string?)row["placements"]![0]!["list"]);
        Assert.Null(response["consumableInventory"]);
    }

    [Fact]
    public void CommittedPostStateReturnsOnlyTheNamedAmountDelta()
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Consumable, 9, 3, "discard", ConsumableId,
            Guid.Empty, "ConsumableSO", 1, string.Empty, string.Empty, false, frameContext: GameMcpTestHarness.Context(World(quantity: 3)));
        var committed = GameMcpCommandResult.Committed("committed", 9, 3);
        var terminal = committed
            .WithDetails(GameMcpWorldQuery.ProjectGameplayPostState(
                GameMcpTestHarness.Context(World(quantity: 2)), command,
                committed));
        var postState = Json(terminal.Project(command));

        Assert.Equal("committed", (string?)postState["status"]);
        Assert.Equal("Swift Thread", (string?)postState["name"]);
        Assert.Equal(3, (int)postState["amount"]!["before"]!);
        Assert.Equal(2, (int)postState["amount"]!["after"]!);
        Assert.Null(postState["code"]);
        Assert.Null(postState["inventory"]);
        Assert.Null(postState["receipt"]);

        var randomizationCommand = new GameMcpCommand(
            2, GameMcpCommandKind.Consumable, 9, 3, "set_randomization", ConsumableId,
            Guid.Empty, "ConsumableSO", 1, string.Empty, string.Empty, false, frameContext: GameMcpTestHarness.Context(World()));
        var randomization = committed
            .WithDetails(GameMcpWorldQuery.ProjectGameplayPostState(
                GameMcpTestHarness.Context(World(randomized: true)),
                randomizationCommand,
                committed));
        var randomizationPostState = Json(randomization.Project(randomizationCommand));
        Assert.False((bool)randomizationPostState["randomized"]!["before"]!);
        Assert.True((bool)randomizationPostState["randomized"]!["after"]!);
        Assert.Equal(103, System.Text.Encoding.UTF8.GetByteCount(
            randomizationPostState.ToString(Newtonsoft.Json.Formatting.None)));
    }

    [Fact]
    public void FaultNamesTheMissingOutcomeWhileSuccessIsEmptyForPostStateReplacement()
    {
        var fault = new ConsumablePlayerSubmission(
            ConsumablePlayerPreflight.VerificationFailed,
            ConsumablePlayerNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(2, 1, 0),
            "order did not change");
        var committed = new ConsumablePlayerSubmission(
            ConsumablePlayerPreflight.Proceeded,
            ConsumablePlayerNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(2, 1, 1),
            "order changed");

        var failure = Json(GameMcpConsumableProjection.Project(in fault));
        var success = Json(GameMcpConsumableProjection.Project(in committed));

        Assert.Equal("requested consumable transition", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
        Assert.Empty(success.Properties());
    }

    private static GameWorldState World(int quantity = 3, bool randomized = false)
    {
        var modifiers = default(RawConsumableModifiers);
        var primary = new WorldConsumable(
            ConsumableId,
            visible: true,
            randomized,
            quantity: quantity,
            queuedQuantity: 1,
            maximumCarryLoad: 12,
            gainedSince: 0,
            maxCreatedLevel: 7,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            in modifiers,
            preparationTime: 2,
            canBeRandomized: true,
            hasDuration: true,
            durationBase: 60,
            queueOnStart: false,
            canFire: true,
            immediateCostsAffordable: true,
            usageCostsAffordable: true);
        var other = new WorldConsumable(
            OtherConsumableId, true, false, 1, 0, 4, 0, 1,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, in modifiers,
            0, false, false, 0, false, true, true, true);
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = Identities(),
            Resources = PublicationTable<WorldResource>.Create(new[] { Resource() }),
            Consumables = PublicationTable<WorldConsumable>.Create(new[] { primary, other }),
            ConsumableTypes = PublicationTable<WorldConsumableType>.Create(new[]
            {
                new WorldConsumableType(ConsumableId, KnownEntities.ConsumableThreadType.Uuid),
            }),
            ConsumableCosts = PublicationTable<WorldConsumableCost>.Create(new[]
            {
                new WorldConsumableCost(
                    ConsumableId,
                    WorldConsumableCostKind.Consume,
                    ResourceId,
                    new BigDouble(250)),
            }),
            ConsumableUsages = PublicationTable<WorldConsumableUsage>.Create(new[]
            {
                new WorldConsumableUsage(
                    ConsumableId, UsageId, 7, false, BigDouble.Zero, new BigDouble(60)),
            }),
            ConsumableCounts = PublicationTable<WorldConsumableCount>.Create(new[]
            {
                new WorldConsumableCount(ConsumableId, 7, quantity, 1),
            }),
            ConsumableInventory = new WorldConsumableInventory(
                true,
                12,
                4,
                PublicationTable<WorldConsumableSlot>.Create(new[]
                {
                    new WorldConsumableSlot(
                        WorldConsumableListKind.Inventory, 0, ConsumableId),
                    new WorldConsumableSlot(
                        WorldConsumableListKind.Inventory, 1, OtherConsumableId),
                    new WorldConsumableSlot(
                        WorldConsumableListKind.Hotbar, 0, ConsumableId),
                })),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "consumable-inventory", WorldCategoryOutcome.Collected, 3, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "consumables", WorldCategoryOutcome.Collected, 2, 0, string.Empty),
            }),
        };
    }

    private static WorldResource Resource()
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            ResourceId,
            new BigDouble(9, 6),
            new BigDouble(1, 9),
            true,
            BigDouble.Zero,
            BigDouble.Zero,
            new BigDouble(100),
            new BigDouble(100),
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            false,
            true,
            false,
            0,
            Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in reading,
            true,
            new BigDouble(9.91, 8),
            0.009,
            false,
            new BigDouble(9, 6),
            BigDouble.Zero);
    }

    private static EntityIdentityCatalogSnapshot Identities()
    {
        var rows = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(ConsumableId, "ConsumableSO", "Swift Thread", "swiftThread"),
            new EntityIdentityName(
                OtherConsumableId, "ConsumableSO", "Other Relic", "otherRelic"),
            new EntityIdentityName(UsageId, "ConsumableUsage", "Thread Usage", "threadUsage"),
        }).OrderBy(row => row.EntityId).ToArray();
        return EntityIdentityCatalogSnapshot.Bound(9, rows);
    }

    private static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, Identities()));

    private static JObject Json(GameMcpObjectBuilder value) => Json(value.Freeze());
}
