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

public sealed class GameMcpPurchaseAnswerTests
{
    [Fact]
    public void PlayerFacingAttributePurchaseUsesTheStructureCapabilityAndAnswersQueued()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-000000000001");
        var before = new GameWorldState
        {
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(attributeId, 632),
            }),
        };
        Assert.True(GameMcpEntityCapabilityMap.Contains(
            before,
            attributeId,
            GameMcpCommandKind.Purchase,
            out var reason), reason);

        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Purchase,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "structure",
            targetId: attributeId,
            secondaryId: Guid.Empty,
            derivedNativeType: "StructureSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false,
            frameContext: GameMcpTestHarness.Context(before));

        var delta = GameMcpTestHarness.Json(
            GameMcpWorldQuery.QueuedMutation(attributeId, asked: 1, queued: 1));

        Assert.Equal(GameMcpTestHarness.Handle(attributeId), (string?)delta["uuid"]);
        Assert.Equal(1, (int)delta["queued"]!);
        Assert.NotNull(delta["name"]);
        Assert.Equal(3, delta.Count);
        Assert.Equal(GameMcpCommandKind.Purchase, command.Kind);
    }

    /// <summary>
    /// A purchase does not apply when it is pressed — the game queues the levels and drains them
    /// afterwards — so no world captured after it describes the press. The answer is the press, and
    /// it needs no settled world at all, which is why a purchase does not wait for one.
    /// </summary>
    [Fact]
    public void APurchaseAnswersFromItsOwnSentinelAndNeverWaitsForASettledWorld()
    {
        Assert.False(GameMcpCommandKinds.RequiresPostStateSettlement(
            GameMcpCommandKind.Purchase));

        var attributeId = Guid.Parse("f2000000-0000-0000-0000-00000000000d");
        var delta = GameMcpTestHarness.Json(
            GameMcpWorldQuery.QueuedMutation(attributeId, asked: 3, queued: 3));

        Assert.Equal(3, (int)delta["queued"]!);
        Assert.Null(delta["level"]);
        Assert.Null(delta["queuedLevels"]);
    }

    /// <summary>
    /// The game's own purchase loop stops as soon as it cannot pay for the next level, so a caller
    /// has to be able to tell a partial delivery from a satisfied ask. Round 9 could not: an
    /// <c>amount=1000</c> that delivered one level was byte-identical to <c>amount=1</c>.
    /// </summary>
    [Fact]
    public void APartialPurchaseSaysDeliveredAgainstAskedOnOneLine()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-00000000000e");
        var delta = GameMcpTestHarness.Json(
            GameMcpWorldQuery.QueuedMutation(attributeId, asked: 1000, queued: 1));

        var queued = (string?)delta["queued"];
        Assert.NotNull(queued);
        Assert.StartsWith("1 of 1000 asked;", queued);
        Assert.DoesNotContain("\n", queued);
    }

    /// <summary>
    /// The suite's own half of a shortfall is explained on the same line as the delivery. Round 13
    /// asked for ten levels against nine of room and got a refusal that delivered nothing; the press
    /// now fills and says, in the sentence it already had, how many it kept back and why.
    /// </summary>
    [Fact]
    public void AnOverAskTheQueueCannotHoldFillsAndNamesWhatTheSuiteWithheld()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-000000000010");
        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.QueuedMutation(
            attributeId,
            asked: 10,
            queued: 9,
            remainder: GameMcpWorldQuery.PurchaseShortfallReason(
                ActionQueueWorld(capacity: 10),
                withheld: 1,
                offeredToTheGame: 9,
                queued: 9,
                gameStopped: null)));

        Assert.Equal(
            "9 of 10 asked; 1 was not taken because the action queue is full " +
            "(10 of 10 slots used).",
            (string?)delta["queued"]);
        Assert.DoesNotContain("\n", (string?)delta["queued"]);
    }

    /// <summary>
    /// Where the game stopped early as well, the suite still speaks only for its own half and gives
    /// the game's half the gate it actually watched shut — never one it inferred.
    /// </summary>
    [Fact]
    public void AShortfallBothSidesCausedKeepsTheTwoHalvesApart()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-000000000011");
        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.QueuedMutation(
            attributeId,
            asked: 10,
            queued: 4,
            remainder: GameMcpWorldQuery.PurchaseShortfallReason(
                ActionQueueWorld(capacity: 10),
                withheld: 1,
                offeredToTheGame: 9,
                queued: 4,
                GameMcpWorldQuery.PurchaseStopClause(
                    AutoBuyGroupStop.NextLevelUnaffordable))));

        Assert.Equal(
            "4 of 10 asked; the game took no more this press: the next level's cost is not met, " +
            "and 1 was never offered because the action queue had room for only 9.",
            (string?)delta["queued"]);
    }

    /// <summary>
    /// An attribute group is driven one level at a time behind the game's own gates, so the suite
    /// watched which one shut and says it. The gate it watched is the only one it may name.
    /// </summary>
    [Fact]
    public void AnAttributeGroupNamesTheGameGateTheSuiteWatchedShut()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-000000000012");
        var stopped = GameMcpTestHarness.Json(GameMcpWorldQuery.QueuedMutation(
            attributeId,
            asked: 5,
            queued: 3,
            remainder: GameMcpWorldQuery.PurchaseShortfallReason(
                ActionQueueWorld(capacity: 10),
                withheld: 0,
                offeredToTheGame: 5,
                queued: 3,
                GameMcpWorldQuery.PurchaseStopClause(
                    AutoBuyGroupStop.NextLevelUnaffordable))));

        Assert.Equal(
            "3 of 5 asked; the game took no more this press: the next level's cost is not met.",
            (string?)stopped["queued"]);

        var shut = GameMcpTestHarness.Json(GameMcpWorldQuery.QueuedMutation(
            attributeId,
            asked: 5,
            queued: 1,
            remainder: GameMcpWorldQuery.PurchaseShortfallReason(
                ActionQueueWorld(capacity: 10),
                withheld: 0,
                offeredToTheGame: 5,
                queued: 1,
                GameMcpWorldQuery.PurchaseStopClause(AutoBuyGroupStop.NotAdmitted))));

        Assert.Equal(
            "1 of 5 asked; the game took no more this press: it no longer admits this purchase.",
            (string?)shut["queued"]);
    }

    /// <summary>
    /// An upgrade multi-buy breaks inside the game's own loop, so the suite has no gate to name and
    /// invents none. It offers the one thing it can still read — what the next level costs.
    /// </summary>
    [Fact]
    public void AnUpgradeGroupOffersTheNextLevelsPriceInsteadOfAGuessedReason()
    {
        Assert.Null(GameMcpWorldQuery.PurchaseStopClause(AutoBuyGroupStop.None));

        var upgradeId = Guid.Parse("f2000000-0000-0000-0000-000000000013");
        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.QueuedMutation(
            upgradeId,
            asked: 5,
            queued: 2,
            remainder: GameMcpWorldQuery.PurchaseShortfallReason(
                ActionQueueWorld(capacity: 10),
                withheld: 0,
                offeredToTheGame: 5,
                queued: 2,
                GameMcpWorldQuery.NextLevelPriceClause(new[]
                {
                    ("Mana", new BigDouble(1200)),
                    ("Insight", new BigDouble(300)),
                }))));

        Assert.Equal(
            "2 of 5 asked; the game took no more this press: the next level costs 1.20e3 Mana and " +
            "300 Insight.",
            (string?)delta["queued"]);

        Assert.Null(GameMcpWorldQuery.NextLevelPriceClause(
            Array.Empty<(string, BigDouble)>()));
    }

    /// <summary>
    /// A price the boundary could not read leaves the sentence exactly as it was: what happened,
    /// with nothing appended that the suite cannot stand behind.
    /// </summary>
    [Fact]
    public void AnUnreadableNextLevelPriceLeavesTheSentenceUnchanged()
    {
        var upgradeId = Guid.Parse("f2000000-0000-0000-0000-000000000014");
        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.QueuedMutation(
            upgradeId,
            asked: 5,
            queued: 2,
            remainder: GameMcpWorldQuery.PurchaseShortfallReason(
                ActionQueueWorld(capacity: 10),
                withheld: 0,
                offeredToTheGame: 5,
                queued: 2,
                gameStopped: null)));

        Assert.Equal(
            "2 of 5 asked; the game took no more this press.",
            (string?)delta["queued"]);
    }

    /// <summary>
    /// The one purchase refusal left. It used to answer "The game refused, and nothing it reports
    /// explains why" — or worse, blame the caller's resources for a queue that had no room — which
    /// was the worst answer in the verb.
    /// </summary>
    [Fact]
    public void AFullActionQueueRefusesNamingTheQueueAndNotTheCallersPockets()
    {
        Assert.Equal(
            "The game's action queue is full (10 of 10 slots used); nothing can be queued until " +
            "something in it settles.",
            GameMcpWorldQuery.ActionQueueFullReason(ActionQueueWorld(capacity: 10)));

        var name = AutoBuyActionResultCodes.ActionQueueFull.Name;
        Assert.Equal("queue_full", name);
        Assert.Equal("ERR_LIMIT", GameMcpDecisionReason.Class(name));
    }

    private static GameWorldState ActionQueueWorld(int capacity)
    {
        var maximumId = Guid.Parse("f2000000-0000-0000-0000-0000000000aa");
        return new GameWorldState
        {
            CollectedAtEpoch = 51,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            ActionQueues = PublicationTable<WorldActionQueue>.Create(new[]
            {
                new WorldActionQueue(
                    KnownEntities.ActiveActionables.Uuid, maximumId,
                    slotCount: capacity, usedSlots: capacity, emptySlots: 0,
                    hasEmptySlot: false, consistent: true),
            }),
            IntVariables = PublicationTable<WorldNumberVariable>.Create(new[]
            {
                new WorldNumberVariable(maximumId, new BigDouble(capacity), isPercent: false),
            }),
        };
    }

    /// <summary>
    /// These verbs promise "it queued and how many", and the commonest press of all — one level —
    /// used to answer a bare <c>yes</c>: the observation the sentinel had already made was spent on
    /// a word that says nothing, and a live round had to re-read the entity to learn what its own
    /// commit did. An observed count is always the number. Only a count the evidence does not carry
    /// stays a bare yes, because inventing one would be the worse defect.
    /// </summary>
    [Fact]
    public void AQueuedMutationSaysTheCountItObservedIncludingTheCountOfOne()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-00000000000f");

        var one = GameMcpTestHarness.Json(
            GameMcpWorldQuery.QueuedMutation(attributeId, asked: 1, queued: 1));
        Assert.Equal(1, (int)one["queued"]!);

        var unknown = GameMcpTestHarness.Json(
            GameMcpWorldQuery.QueuedMutation(attributeId, asked: 1, queued: null));
        Assert.True((bool)unknown["queued"]!);
    }

    /// <summary>
    /// A committed purchase answers with what moved. It used to answer with what it charged as
    /// well, and on a multi-level call that figure priced one level of a rising ladder while
    /// reading like the whole charge — always short, always in the direction of believing there is
    /// more left to spend. The cost curves live in the world publication, where Auto Buy plans off
    /// them; a caller that could not afford something still reads what it needs and what it holds
    /// in the refusal's own sentence.
    /// </summary>
    [Fact]
    public void ACommittedPurchaseReportsTheLevelsItBoughtAndNoPaymentRows()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-000000000003");
        var resourceId = Guid.Parse("f2000000-0000-0000-0000-000000000004");
        var identities = EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(resourceId, "ResourceSO", "Glyph Upgrades", "GlyphUpgrades"),
        });
        var before = new GameWorldState
        {
            EntityIdentities = identities,
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(attributeId, 100),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                Stock(resourceId, 1000),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                new WorldPurchaseCost(attributeId, resourceId, new BigDouble(2)),
            }),
        };
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Purchase,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "structure",
            targetId: attributeId,
            secondaryId: Guid.Empty,
            derivedNativeType: "StructureSO",
            amount: 25,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false,
            frameContext: GameMcpTestHarness.Context(before));
        var delta = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.QueuedMutation(
                attributeId, asked: command.Amount, queued: 25),
            identities));

        Assert.Equal(25, (int)delta["queued"]!);
        Assert.Null(delta["paid"]);
        Assert.Null(delta["costPerLevel"]);
        Assert.Null(delta["spendableAmount"]);
    }

    private static WorldResource Stock(Guid id, double quantity)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var held = new BigDouble(quantity);
        var reading = new RawResourceSample(
            id, held, new BigDouble(-1), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        return new WorldResource(
            in reading, true, held, 0d, false, held, BigDouble.Zero);
    }

    [Fact]
    public void AnAttributeRowPublishesTheBadgeLevelAndNamesWorkInFlightSeparately()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-000000000002");
        var world = new GameWorldState
        {
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                // Four significant digits on purpose: the badge draws BeautifyInt, so a level the
                // large-magnitude renderer would round to 2.14e3 has to reach the wire as 2136.
                Structure(attributeId, 2136, queued: 3),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "attributes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, 2101),
            "attributes",
            attributeId.ToString("D")));
        var row = response["row"]!;
        var listed = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, 2101), "attributes", 0, 10))["rows"]![0]!;

        Assert.Equal(2136, (int)row["level"]!);
        Assert.Equal(3, (int)row["queuedLevels"]!);
        Assert.Null(row["committedLevel"]);
        Assert.Equal(2136, (int)listed["level"]!);
        Assert.Equal(3, (int)listed["queuedLevels"]!);
        Assert.Null(listed["committedLevel"]);
    }

    [Fact]
    public void AnAttributeWithNoWorkInFlightSaysSoInsteadOfDroppingTheKey()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-000000000009");
        var world = new GameWorldState
        {
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(attributeId, 12, queued: 0),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "attributes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, 2102),
            "attributes",
            attributeId.ToString("D")))["row"]!;
        var listed = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, 2102), "attributes", 0, 10))["rows"]![0]!;

        Assert.Equal(0, (int)row["queuedLevels"]!);
        Assert.Equal(0, (int)listed["queuedLevels"]!);
    }

    [Fact]
    public void AnUpgradeWithoutACeilingNamesThatInsteadOfReportingNoLevelsLeft()
    {
        var unboundedId = Guid.Parse("f2000000-0000-0000-0000-000000000003");
        var exhaustedId = Guid.Parse("f2000000-0000-0000-0000-000000000004");
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                Upgrade(unboundedId, level: 7, maxLevel: -1),
                Upgrade(exhaustedId, level: 10, maxLevel: 10),
            }),
            UpgradeListMemberships = GameMcpTestHarness.OnTheUpgradesList(unboundedId, exhaustedId),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "upgrades", WorldCategoryOutcome.Collected, 2, 0, string.Empty),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var context = GameMcpTestHarness.Context(world, 2102);

        var unbounded = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "upgrades", unboundedId.ToString("D")))["row"]!;
        Assert.Equal(7, (int)unbounded["level"]!);
        Assert.Equal("uncapped", (string?)unbounded["maximum"]);
        Assert.Equal("available", (string?)unbounded["state"]);
        Assert.Null(unbounded["reasonCode"]);

        var exhausted = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "upgrades", exhaustedId.ToString("D")))["row"]!;
        Assert.Equal(10, (int)exhausted["maximum"]!);

        // One word, where a code, a boolean and a levels-left count used to divide the same fact
        // between them. `get` says the lifecycle in the page's vocabulary, not a second one.
        Assert.Equal("completed", (string?)exhausted["state"]);
        Assert.Null(exhausted["reasonCode"]);
        Assert.Null(exhausted["available"]);
        Assert.Null(exhausted["remainingLevels"]);

        var listed = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "upgrades", 0, 10));
        var rows = listed["rows"]!.Values<JObject>().ToArray();
        Assert.Equal("uncapped", (string?)rows[0]!["maximum"]);
        Assert.Equal("available", (string?)rows[0]!["state"]);
        Assert.Equal(10, (int)rows[1]!["maximum"]!);
        Assert.Equal("completed", (string?)rows[1]!["state"]);

        // A finished upgrade has no next level to price, and that is the whole of what the money
        // column says about it. The state is what says it is finished, and it says it once.
        Assert.Equal("unpriced", (string?)rows[1]!["affordable"]);
        Assert.Null(rows[1]!["reasonCode"]);
        Assert.Null(rows[1]!["reason"]);

        // A caller paging the list must read the ceiling the same way a get would: both surfaces
        // publish it on every row, so neither can be read as the leaner one having dropped it.
        Assert.Equal(0, (int)rows[0]!["queuedLevels"]!);
        Assert.Equal((int?)exhausted["maximum"], (int?)rows[1]!["maximum"]);
    }

    [Fact]
    public void ActionFailureProjectionCarriesOnlyStableCodeAndActionableReason()
    {
        var context = GameMcpTestHarness.Context(
            GameWorldStateDefaults.Empty,
            generation: 77);
        var operation = new GameMcpFrameOperation(
            1,
            new GameMcpOperationRequestBuilder
            {
                ToolName = "game_purchase",
                Route = GameMcpTestHarness.Route("game_purchase"),
            }.Freeze());
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Purchase,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "purchase",
            targetId: Guid.Parse("01234567-89ab-4cde-8f01-23456789abcd"),
            secondaryId: Guid.Empty,
            derivedNativeType: "StructureSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false,
            sourceOperation: operation,
            frameContext: context);

        var response = GameMcpTestHarness.Json(GameMcpCommandResult.Rejected(
            "native_rejected",
            "live native admission refused",
            observedLifecycleGeneration: 0,
            observedConfigurationGeneration: 0).Project(command));

        Assert.Equal("refused", (string?)response["status"]);
        Assert.Equal("ERR_REFUSED", (string?)response["reasonCode"]);
        Assert.Equal("live native admission refused", (string?)response["reason"]);
        Assert.Equal(GameMcpTestHarness.Handle(command.TargetId), (string?)response["uuid"]);
        // The refusal names what it refused about. Nothing in this fixture's catalog answers for
        // that id, and a refusal that shows a bare id reads as a refusal about a row named in hex.
        Assert.Equal(
            "(unnamed " + GameMcpTestHarness.Handle(command.TargetId) + ")",
            (string?)response["name"]);
        Assert.Equal(5, response.Count);
        Assert.Null(response["worldGeneration"]);
        Assert.Null(response["readWith"]);
        Assert.Null(response["lifecycleGenerationMismatch"]);
        Assert.Null(response["configurationGenerationMismatch"]);
    }

    private static WorldUpgrade Upgrade(Guid id, int level, int maxLevel) =>
        GameWorldStateDeriver.Derive(new RawUpgradeSample(
            id,
            level,
            maxLevel,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 0d,
            cachedCostLevel: level));

    private static WorldStructure Structure(Guid id, int level, int queued = 0)
    {
        var modifiers = new RawStructureModifiers(
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero);
        var reading = new RawStructureSample(
            id,
            Guid.Empty,
            new BigDouble(level),
            new BigDouble(queued),
            unlocked: true,
            queuedEchos: 0,
            completedEchos: 0,
            selfBonusLevels: 0,
            queueTimeLeft: BigDouble.Zero,
            currentBuildTime: BigDouble.Zero,
            flagged: false,
            baseLevel: 0,
            queueTimeTotal: 0,
            debugStructure: false,
            disabled: false,
            observableId: 0,
            insufficientReqPenaltyActive: false,
            bufferDevelopedQuantity: 0,
            costPerQuantityId: Guid.Empty,
            in modifiers);
        return new WorldStructure(
            in reading,
            new BigDouble(level + queued),
            hasWorkInFlight: queued != 0,
            new BigDouble(level),
            developmentProgress: 0);
    }
}
