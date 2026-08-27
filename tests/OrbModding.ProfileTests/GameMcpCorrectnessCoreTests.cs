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

public sealed class GameMcpCorrectnessCoreTests
{
    [Fact]
    public void Player_facing_costs_apply_the_resources_quality_discount()
    {
        Assert.Equal("1.88e491", PlayerFacingCost(
            new BigDouble(3.1d, 523), new BigDouble(1.65d, 34)));
        Assert.Equal("1.56e508", PlayerFacingCost(
            new BigDouble(1.82d, 529), new BigDouble(1.17d, 23)));
        Assert.Equal("9.03e169", PlayerFacingCost(
            new BigDouble(9.96d, 205), new BigDouble(1.103d, 38)));
    }

    /// <summary>
    /// The four bandwidth/inverted quadrants, plus the live rows that exposed the old rule:
    /// <c>atCapacity</c> answers in the coordinate <c>amount</c> is published in, so an inverted
    /// counter is full when its displayed number reaches the ceiling. Potion Toxicity showing 0 of
    /// its tolerance is not at capacity; Stability showing its whole pool is.
    /// </summary>
    /// <remarks>
    /// The same rows pin what the reading is called. An inverted counter says <c>meter: left</c>,
    /// because its <c>amount</c> is what is left of its <c>capacity</c> and falls as the total is
    /// used; every other row says <c>held</c>. And on a <c>left</c> row the full/not-full bit is
    /// said in used-ness words — Stability's whole pool showing means <c>nothing_used</c>, and a
    /// plain <c>yes</c> there read as stuck while meaning the opposite.
    /// </remarks>
    [Fact]
    public void ResourceCoordinatesCoverEveryBandwidthAndInvertedQuadrant()
    {
        var ordinaryId = Guid.Parse("36666666-6666-4666-8666-666666666666");
        var spellCapacityId = Guid.Parse("37777777-7777-4777-8777-777777777777");
        var potionToxicityId = Guid.Parse("38888888-8888-4888-8888-888888888888");
        var glyphUpgradesId = Guid.Parse("39999999-9999-4999-8999-999999999999");
        var stabilityId = Guid.Parse("3aaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var timeAdvancementId = Guid.Parse("3bbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var arcanumId = Guid.Parse("3ccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(ordinaryId, "ResourceSO", "Knowledge", "knowledge"),
                new EntityIdentityName(spellCapacityId, "ResourceSO", "Spell Capacity", "weightSpell"),
                new EntityIdentityName(potionToxicityId, "ResourceSO", "Potion Toxicity", "potionToxicity"),
                new EntityIdentityName(glyphUpgradesId, "ResourceSO", "Glyph Upgrades", "glyphUpgrades"),
                new EntityIdentityName(stabilityId, "ResourceSO", "Stability", "stability"),
                new EntityIdentityName(timeAdvancementId, "ResourceSO", "Time Advancement", "timeAdvancement"),
                new EntityIdentityName(arcanumId, "ResourceSO", "Arcanum", "arcanum"),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                Derived(ordinaryId, held: 3, bandwidth: false, inverted: false),
                Derived(spellCapacityId, held: 3, bandwidth: true, inverted: false),
                Derived(potionToxicityId, held: 10, bandwidth: false, inverted: true),
                Derived(glyphUpgradesId, held: 10, bandwidth: true, inverted: true),
                Derived(stabilityId, held: 0, bandwidth: false, inverted: true),
                Derived(timeAdvancementId, held: 4, bandwidth: false, inverted: true),
                Derived(arcanumId, held: 10, bandwidth: false, inverted: false),
            }),
            SpellCosts = PublicationTable<WorldSpellCost>.Create(new[]
            {
                new WorldSpellCost(0, WorldSpellCostKind.Immediate,
                    ordinaryId, new BigDouble(100)),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "resources", WorldCategoryOutcome.Collected, 7, 0, string.Empty),
            }),
        };

        AssertCoordinates(
            world, 0, ordinaryId, display: "3", spendable: 3, cost: 50,
            meter: "held", atCapacity: false);
        AssertCoordinates(
            world, 1, spellCapacityId, display: "3", spendable: 7, cost: 100,
            meter: "held", atCapacity: false);
        AssertCoordinates(
            world, 2, potionToxicityId, display: "0", spendable: 10, cost: 50,
            meter: "left", atCapacity: "some_used");
        AssertCoordinates(
            world, 3, glyphUpgradesId, display: "0", spendable: 0, cost: 100,
            meter: "left", atCapacity: "some_used");
        AssertCoordinates(
            world, 4, stabilityId, display: "10", spendable: 0, cost: 50,
            meter: "left", atCapacity: "nothing_used");
        AssertCoordinates(
            world, 5, timeAdvancementId, display: "6", spendable: 4, cost: 50,
            meter: "left", atCapacity: "some_used");
        AssertCoordinates(
            world, 6, arcanumId, display: "10", spendable: 10, cost: 50,
            meter: "held", atCapacity: true);

        var costs = Assert.IsType<JArray>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.ProjectEquippedSpellCosts(
                world, 0, WorldSpellCostKind.Immediate).Freeze(),
            world.EntityIdentities));
        var cost = Assert.Single(costs.Values<JObject>());
        Assert.Equal("50", (string?)cost["cost"]);
        Assert.Equal("3", (string?)cost["spendableAmount"]);
        Assert.False((bool)cost["affordable"]!);
    }

    private static void AssertCoordinates(
        GameWorldState world,
        int index,
        Guid resourceId,
        string display,
        int spendable,
        int cost,
        string meter,
        object atCapacity)
    {
        var row = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.ProjectResource(world, world.Resources[index]),
            world.EntityIdentities));
        Assert.Equal(meter, (string?)row["meter"]);
        Assert.Equal(display, (string?)row["amount"]);
        Assert.Equal("10", (string?)row["capacity"]);
        Assert.Equal(atCapacity, ((JValue)row["atCapacity"]!).Value);
        Assert.Equal(new BigDouble(spendable),
            GameMcpWorldQuery.SpendableAmount(world, resourceId, BigDouble.Zero));
        Assert.Equal(new BigDouble(cost),
            GameMcpWorldQuery.PlayerFacingCost(world, resourceId, new BigDouble(100)));
    }

    [Fact]
    public void Skipped_unaffordable_purchase_names_every_short_resource_and_amounts()
    {
        var target = Guid.Parse("31111111-1111-4111-8111-111111111111");
        var resource = Guid.Parse("32222222-2222-4222-8222-222222222222");
        var second = Guid.Parse("32222222-2222-4222-8222-222222222223");
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resource, new BigDouble(2), new BigDouble(10), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(200),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var secondReading = new RawResourceSample(
            second, new BigDouble(1), new BigDouble(10), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(200),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(resource, "ResourceSO", "Knowledge", "knowledge"),
                new EntityIdentityName(second, "ResourceSO", "Mana", "mana"),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                new WorldPurchaseCost(
                    target, resource, new BigDouble(100), new BigDouble(100), 1,
                    new BigDouble(100),
                    PublicationTable<WorldPurchaseCostModifierSource>.Empty,
                    affordabilityEvaluated: true,
                    availableAmount: new BigDouble(2),
                    combinedEffectiveAmount: new BigDouble(100),
                    resourceAffordable: false,
                    resourceAffordabilityReasonCode: "insufficient_resource",
                    affordable: false,
                    affordabilityReasonCode: "unaffordable"),
                new WorldPurchaseCost(
                    target, second, new BigDouble(100), new BigDouble(100), 1,
                    new BigDouble(100),
                    PublicationTable<WorldPurchaseCostModifierSource>.Empty,
                    affordabilityEvaluated: true,
                    availableAmount: new BigDouble(1),
                    combinedEffectiveAmount: new BigDouble(100),
                    resourceAffordable: false,
                    resourceAffordabilityReasonCode: "insufficient_resource",
                    affordable: false,
                    affordabilityReasonCode: "unaffordable"),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                new WorldResource(in reading, true, new BigDouble(8), 0.2d, false,
                    new BigDouble(4), BigDouble.Zero),
                new WorldResource(in secondReading, true, new BigDouble(9), 0.2d, false,
                    new BigDouble(4), BigDouble.Zero),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);
        var action = ServiceActionResult.Skipped(CommonActionResultCodes.Skipped);

        var result = AutomataServiceCycleRuntime.ProjectPurchaseRefusal(
            command, world, in action, 1, 1);

        Assert.NotNull(result);
        Assert.Equal("unaffordable", result!.Code);
        Assert.Equal(
            "Needs 50 Knowledge (have 2); 50 Mana (have 1).",
            result.Reason);
    }

    /// <summary>
    /// No result code's own sentence ever prints a native result number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A feature result number names no axis a caller can act on, and round 8 shipped it as prose
    /// nine separate ways — "the native action boundary returned Rejected with exact result code
    /// 2051" was the whole answer a caller got for a purchase that had stopped working. The sweep
    /// covers the entire code space rather than the codes that leak today, because the leak was a
    /// fallback: every code a producer forgets to map lands there next.
    /// </para>
    /// <para>
    /// Its scope is the channel it names and no more: the sentence a result <em>code</em> answers
    /// with. A boundary's own <c>exactReason</c> overrides that sentence without passing through
    /// here, and it carries digits on purpose — the epoch a purchase topology was stamped at, the
    /// slot number a spell moved to. A blanket digit assert on that channel would delete facts a
    /// caller acts on, so the honest scope is this one, stated in the name rather than implied.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_result_code_sentence_prints_a_native_result_number()
    {
        var reserved = new[]
        {
            CommonActionResultCodes.Committed,
            CommonActionResultCodes.EmergencyStop,
            CommonActionResultCodes.LifecycleReplaced,
            CommonActionResultCodes.ServiceDisabled,
            CommonActionResultCodes.NativeRejected,
            CommonActionResultCodes.PolicyRejected,
            CommonActionResultCodes.AdapterFault,
            CommonActionResultCodes.Skipped,
        };

        foreach (GameMcpCommandKind kind in Enum.GetValues(typeof(GameMcpCommandKind)))
        {
            foreach (var code in reserved)
                Assert.DoesNotContain(GameMcpActionResultCodeNames.Reason(code, kind), char.IsDigit);

            for (var code = ServiceActionResultCode.FirstFeatureCode; code <= 4200; code++)
            {
                var reason = GameMcpActionResultCodeNames.Reason(
                    new ServiceActionResultCode(code), kind);
                Assert.DoesNotContain(reason, char.IsDigit);
            }
        }
    }

    /// <summary>
    /// The five ways the purchase-screen gate says no are five answers, not one number.
    /// </summary>
    [Fact]
    public void Every_purchase_screen_refusal_answers_in_its_own_words()
    {
        var codes = new[]
        {
            AutoBuyActionResultCodes.OwningViewRelationUnreadable,
            AutoBuyActionResultCodes.OwningViewTopologyUnbound,
            AutoBuyActionResultCodes.OwningViewTopologyUncaptured,
            AutoBuyActionResultCodes.OwningViewRelationStatusUnmodeled,
            AutoBuyActionResultCodes.OwningViewAvailabilityUnreadable,
        };

        var names = codes
            .Select(code => GameMcpActionResultCodeNames.Name(code, GameMcpCommandKind.Purchase))
            .ToArray();
        var reasons = codes
            .Select(code => GameMcpActionResultCodeNames.Reason(code, GameMcpCommandKind.Purchase))
            .ToArray();

        Assert.Equal(codes.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(codes.Length, reasons.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("native_rejected", names);
    }

    [Fact]
    public void Native_rejection_the_read_side_can_explain_never_answers_native_rejected()
    {
        var target = Guid.Parse("34444444-4444-4444-8444-444444444444");
        var reading = new RawUpgradeSample(
            target, level: 10, maxLevel: 10, available: true, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 1d, cachedCostLevel: 10);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in reading, true, true, 0, 10, false, 0d),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);

        var result = AutomataServiceCycleRuntime.ProjectPurchaseRefusal(
            command,
            world,
            ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected),
            1,
            1);

        Assert.NotNull(result);
        Assert.Equal("already_maxed", result!.Code);
    }

    [Fact]
    public void Native_rejection_the_read_side_cannot_explain_stays_native_rejected()
    {
        var target = Guid.Parse("34444444-4444-4444-8444-444444444445");
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);

        Assert.Null(AutomataServiceCycleRuntime.ProjectPurchaseRefusal(
            command,
            new GameWorldState(),
            ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected),
            1,
            1));
    }

    [Fact]
    public void Maxed_purchase_refusal_uses_the_same_semantic_reason_as_the_read()
    {
        var target = Guid.Parse("34444444-4444-4444-8444-444444444444");
        var reading = new RawUpgradeSample(
            target, level: 10, maxLevel: 10, available: true, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 1d, cachedCostLevel: 10);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in reading, true, true, 0, 10, false, 0d),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);

        var result = AutomataServiceCycleRuntime.ProjectPurchaseRefusal(
            command, world, ServiceActionResult.Skipped(CommonActionResultCodes.Skipped),
            1, 1);

        Assert.NotNull(result);
        Assert.Equal("already_maxed", result!.Code);
        Assert.Contains("maximum level", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A live round read this refusal as `Aspect: Workshop [AspectWorkshop]
    /// (d9f1a5c3-d7de-4819-b234-a15781190650) is already at its maximum level.` — a log line served
    /// as player prose. The sentence says the player's word for the thing; the asset name and the
    /// id are identity and ride as the response's own fields.
    /// </summary>
    [Fact]
    public void Maxed_purchase_refusal_names_the_player_word_and_leaks_no_asset_name_or_uuid()
    {
        var target = Guid.Parse("d9f1a5c3-d7de-4819-b234-a15781190650");
        var reading = new RawUpgradeSample(
            target, level: 10, maxLevel: 10, available: true, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 1d, cachedCostLevel: 10);
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(
                    target, "UpgradeSO", "Aspect: Workshop", "AspectWorkshop"),
            }),
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in reading, true, true, 0, 10, false, 0d),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);

        var result = AutomataServiceCycleRuntime.ProjectPurchaseRefusal(
            command, world, ServiceActionResult.Skipped(CommonActionResultCodes.Skipped),
            1, 1);

        Assert.NotNull(result);
        Assert.Equal(
            "Aspect: Workshop is already at its maximum level.", result!.Reason);
    }

    [Fact]
    public void Wrong_tool_redirect_uses_the_created_equipments_player_action()
    {
        var target = Guid.Parse("35555555-5555-4555-8555-555555555555");
        var equipment = new WorldEquipment(
            target, isCreated: true, discRarityLevel: 0, masteryXp: BigDouble.Zero,
            masteryLevel: 0, isRequiredDiscovery: false, power: BigDouble.One,
            baseLevel: BigDouble.One, experienceRateMod: BigDouble.One,
            equippedLevel: 0, attuningLevel: 0, attunementTimeLeft: 0d,
            baseXpRate: BigDouble.Zero);
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(target, "EquipmentSO", "Static Gauntlets", "static"),
            }),
            Equipment = PublicationTable<WorldEquipment>.Create(new[] { equipment }),
        };

        Assert.True(GameMcpEntityCapabilityMap.TryOwningTool(
            world, target, out var category, out var nativeType, out var tool));
        Assert.Equal("equipment", category);
        Assert.Equal("EquipmentSO", nativeType);
        Assert.Equal("game_equipment", tool);
    }

    private static string PlayerFacingCost(BigDouble nominal, BigDouble quality)
    {
        var resource = Guid.NewGuid();
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resource, BigDouble.Zero, new BigDouble(-1), true,
            BigDouble.Zero, BigDouble.Zero, quality,
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var world = new GameWorldState
        {
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                new WorldResource(in reading, true, BigDouble.Zero, 0d, false,
                    BigDouble.Zero, BigDouble.Zero),
            }),
        };

        return GameMcpNumberFormatter.Format(GameMcpWorldQuery.PlayerFacingCost(
            world, resource, nominal));
    }

    private static WorldResource Derived(Guid resourceId, int held, bool bandwidth, bool inverted)
    {
        var rateInputs = default(RawResourceRateInputs);
        var modifiers = default(RawResourceModifiers);
        var traits = Traits(bandwidth, inverted);
        var reading = new RawResourceSample(
            resourceId, new BigDouble(held), new BigDouble(10), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(200),
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty,
            in rateInputs, in traits, in modifiers);
        return GameWorldStateDeriver.Derive(in reading, default(WorldFrameGlobals));
    }

    private static RawResourceTraits Traits(bool bandwidth, bool inverted) => new(
        0d, 0d, 0d,
        false, false, false,
        bandwidth, inverted, false, false,
        BigDouble.Zero, 0, 0, 0d, false, 0d,
        BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);

    [Fact]
    public void ActionRegistrationDependsOnlyOnRuntimeAdmission()
    {
        Assert.True(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: true));
        Assert.False(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: false));
    }

    [Fact]
    public void PostStateSettlementRejectsAWorldCapturedBeforeTheActionEvenIfPublishedLater()
    {
        Assert.Equal(1f, GameMcpPostStateSettlement.MaximumWaitSeconds);
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(41, 41));
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(40, 41));
        Assert.True(GameMcpPostStateSettlement.IsStrictlyNewer(42, 41));

        var actionCompleted = DateTime.UtcNow.Ticks;
        var staleCapture = GameMcpTestHarness.Context(
            new GameWorldState { CollectedAtUtcTicks = actionCompleted - 1 },
            generation: 42);
        var settledCapture = GameMcpTestHarness.Context(
            new GameWorldState { CollectedAtUtcTicks = actionCompleted + 1 },
            generation: 42);

        Assert.False(GameMcpPostStateSettlement.HasSettledWorld(
            staleCapture, 41, actionCompleted));
        Assert.True(GameMcpPostStateSettlement.HasSettledWorld(
            settledCapture, 41, actionCompleted));
    }

    [Fact]
    public void TimedOutPostStateCarriesExceptionalEvidenceInsteadOfAnEmptyCommit()
    {
        var value = GameMcpPostStateSettlement.TimedOut(
            GameMcpAcceptanceFixture.NativeCommand(),
            latest: null);

        Assert.Equal(
            "{\"postStateUnavailable\":{\"reasonCode\":\"ERR_UNAVAILABLE\",\"reason\":\"no world captured after the action exposed its committed post-state within one second\"}}",
            GameMcpTestHarness.Json(value).ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public void ConceptSettlementWaitsForTheActiveCountItsResponsePublishes()
    {
        var recipe = Guid.Parse("39999999-9999-4999-8999-999999999999");
        var completedAt = DateTime.UtcNow.Ticks;
        var before = new GameWorldState
        {
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 440, 440, true, BigDouble.One),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Concept, 9, 3, "add", recipe, Guid.Empty,
            "AlchemyRecipeSO", 5, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 41));
        // The game takes the queue increase a settlement before it submits it.
        var queuedOnly = new GameWorldState
        {
            CollectedAtUtcTicks = completedAt + 1,
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 440, 445, true, BigDouble.One),
            }),
        };
        var submitted = new GameWorldState
        {
            CollectedAtUtcTicks = completedAt + 1,
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 445, 445, true, BigDouble.One),
            }),
        };

        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(queuedOnly, generation: 42), 41, completedAt, command));
        Assert.True(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(submitted, generation: 42), 41, completedAt, command));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(submitted, generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));
        Assert.Equal(440, (int)delta["activeCount"]!["before"]!);
        Assert.Equal(445, (int)delta["activeCount"]!["after"]!);

        var timeout = GameMcpTestHarness.Json(GameMcpPostStateSettlement.TimedOut(
            command, GameMcpTestHarness.Context(queuedOnly, generation: 42)));
        Assert.Equal("ERR_STATE", (string?)timeout["postStateUnavailable"]!["reasonCode"]);
        Assert.Contains("active count is 440",
            (string?)timeout["postStateUnavailable"]!["reason"]);
    }

    [Fact]
    public void PlotPostStateReportsTheObservedRequestedPairQuantityChange()
    {
        var plotId = KnownEntities.FruitTreePlot.Uuid;
        var actionId = KnownEntities.FruitTreeCollect.Uuid;
        var before = new GameWorldState
        {
            PlotActions = PublicationTable<WorldPlotAction>.Create(new[]
            {
                PlotAction(plotId, actionId, instanceCount: 1,
                    maximumRemainingInstances: 4),
            }),
            ActionQueueSlots = PublicationTable<WorldActionQueueSlot>.Create(new[]
            {
                new WorldActionQueueSlot(PlotLifecycleNativeBindings.ActiveActionsId,
                    0, false, plotId, actionId, 2, true),
            }),
        };
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Harvest,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "add_plot_action",
            targetId: plotId,
            secondaryId: actionId,
            derivedNativeType: "PlotNodeSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false,
            frameContext: GameMcpTestHarness.Context(before));
        var committed = GameMcpCommandResult.Committed("committed", 9, 3);
        var activeWorld = new GameWorldState
        {
            PlotActions = PublicationTable<WorldPlotAction>.Create(new[]
            {
                PlotAction(plotId, actionId, instanceCount: 1,
                    maximumRemainingInstances: 3),
            }),
            ActionQueueSlots = PublicationTable<WorldActionQueueSlot>.Create(new[]
            {
                new WorldActionQueueSlot(PlotLifecycleNativeBindings.ActiveActionsId,
                    0, false, plotId, actionId, 3, true),
            }),
        };

        var active = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(activeWorld), command, committed));
        Assert.Equal(2, (int)active["active"]!["before"]!);
        Assert.Equal(3, (int)active["active"]!["after"]!);
        Assert.Equal(GameMcpTestHarness.Handle(plotId), (string?)active["plot"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)active["plot"]!["name"]));
        Assert.Equal(GameMcpTestHarness.Handle(actionId), (string?)active["action"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)active["action"]!["name"]));

        // A commit answers with what the press changed. Whether another one is possible is a read,
        // and world_get agromancy-plot-actions carries the same decision.
        Assert.Null(active["next"]);

        var fastActionDetails = new GameMcpObjectBuilder
        {
            ["active"] = new GameMcpObjectBuilder
            {
                ["before"] = 0,
                ["after"] = 1,
            }.Freeze(),
        }.Freeze();
        var fastAction = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(new GameWorldState
            {
                PlotActions = activeWorld.PlotActions,
            }),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3, fastActionDetails)));
        Assert.Equal(0, (int)fastAction["active"]!["before"]!);
        Assert.Equal(1, (int)fastAction["active"]!["after"]!);
        Assert.Equal(GameMcpTestHarness.Handle(plotId), (string?)fastAction["plot"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)fastAction["plot"]!["name"]));
        Assert.Equal(GameMcpTestHarness.Handle(actionId), (string?)fastAction["action"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)fastAction["action"]!["name"]));
        Assert.Null(fastAction["postStateUnavailable"]);

        // One request shape, two answers, one identity. The commit's delta names both entities in
        // their own blocks, and the plot's was being promoted on one path and the action's on the
        // other, so a caller correlating by uuid mis-filed half of its calls.
        var commitProjection = GameMcpTestHarness.Json(
            GameMcpCommandResult.Committed("committed", 9, 3)
                .WithDetails(GameMcpWorldQuery.ProjectGameplayPostState(
                    GameMcpTestHarness.Context(activeWorld), command, committed))
                .Project(command));
        var refusalProjection = GameMcpTestHarness.Json(
            GameMcpCommandResult.Rejected("amount_unavailable", "The plot allows fewer than that.")
                .Project(command));

        Assert.Equal(GameMcpTestHarness.Handle(plotId), (string?)commitProjection["uuid"]);
        Assert.Equal(GameMcpTestHarness.Handle(plotId), (string?)refusalProjection["uuid"]);
        Assert.Equal(GameMcpTestHarness.Handle(actionId), (string?)commitProjection["action"]!["uuid"]);
        Assert.Equal(GameMcpTestHarness.Handle(actionId), (string?)refusalProjection["action"]!["uuid"]);
    }

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
            "2 of 5 asked; the game took no more this press: the next level costs 1.2e3 Mana and " +
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

        var name = GameMcpActionResultCodeNames.Name(
            AutoBuyActionResultCodes.ActionQueueFull, GameMcpCommandKind.Purchase);
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
                    "structures", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, 2101),
            "structures",
            attributeId.ToString("D")));
        var row = response["row"]!;
        var listed = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, 2101), "structures", 0, 10))["rows"]![0]!;

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
                    "structures", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, 2102),
            "structures",
            attributeId.ToString("D")))["row"]!;
        var listed = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, 2102), "structures", 0, 10))["rows"]![0]!;

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
                Classification = GameMcpOperationClass.Gameplay,
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

    private static WorldPlotAction PlotAction(
        Guid plotId,
        Guid actionId,
        int instanceCount,
        int maximumRemainingInstances) =>
        new(
            new RawPlotAction(
                plotId,
                actionId,
                offeredCount: 1,
                instanceCount,
                PlotActionPrerequisiteEvidence.NativeLatchedTrue),
            elementCost: 2,
            elementCostKnown: true,
            hasEnoughForOneInstance: true,
            maximumRemainingInstances);
}
