using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpEntityDetailFixture;

namespace OrbModding.ProfileTests;

[Collection(NativeRegistryCollection.Name)]
[OrbModding.TestSupport.ResetGameStubs]
public sealed class GameMcpEntityDetailCostAndBlockerTests : IDisposable
{
    public GameMcpEntityDetailCostAndBlockerTests() => OrbModding.GameStubs.GameStubState.Reset();

    public void Dispose() => OrbModding.GameStubs.GameStubState.Reset();

    /// <summary>
    /// The game develops research on leeway **or** on being below both caps, so a spent leeway
    /// under open caps blocks nothing. Three states, three words, and the middle one passes — and a
    /// passing axis is not what `blockers` is for: the block lists what blocks, so an axis with
    /// nothing to report is not listed.
    /// </summary>
    [Fact]
    public void A_spent_leeway_under_open_caps_is_not_among_what_blocks()
    {
        var world = new GameWorldState
        {
            Research = PublicationTable<WorldResearch>.Create(new[]
            {
                Research(
                    ReadyResearchId, available: true, level: 4, maxLevel: 20,
                    baseRequirement: 5, effectiveRequirement: 5, leeway: 0,
                    stillHasLeeway: false),
            }),
            CollectedAtEpoch = 43,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var blockers = Assert.IsType<JObject>(
            Explain(world, ReadyResearchId, generation: 943)["blockers"]);

        // The leeway axis had nothing left but `blocked: no`, so it is gone. The cap axis stays
        // even though it is open, because it carries readings of its own — a drop is per field and
        // only where the field is a restatement.
        Assert.Null(blockers["leeway"]);
        var cap = Assert.IsType<JObject>(blockers["cap"]);
        Assert.False((bool)cap["blocked"]!);
        Assert.Equal(4, (int)cap["baseLevelExcludingBonus"]!);
        Assert.Equal(20, (int)cap["effectiveCap"]!);
        Assert.False((bool)cap["nativeComplete"]!);
        Assert.True(GameMcpDecisionReason.IsPassing("native_develops_below_caps"));
    }

    /// <summary>
    /// The other side of the same rule: an axis that really refuses prints in full, because the
    /// numbers behind a no are what a caller acts on. They ride only here — an open axis restating
    /// `researchThresholds` one block down was spending them on a reader who already had them.
    /// </summary>
    [Fact]
    public void A_leeway_that_blocks_prints_in_full_with_the_numbers_behind_it()
    {
        var world = new GameWorldState
        {
            Research = PublicationTable<WorldResearch>.Create(new[]
            {
                Research(
                    ReadyResearchId, available: true, level: 20, maxLevel: 20,
                    baseRequirement: 5, effectiveRequirement: 5, leeway: 0,
                    stillHasLeeway: false),
            }),
            CollectedAtEpoch = 44,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var leeway = Assert.IsType<JObject>(
            Explain(world, ReadyResearchId, generation: 944)["blockers"]!["leeway"]);

        Assert.True((bool)leeway["blocked"]!);
        Assert.Null(leeway["reasonCode"]);
        Assert.NotNull(leeway["reason"]);
        Assert.Equal(20, (int)leeway["currentTotalLevel"]!);
        Assert.Equal(0, (int)leeway["leeway"]!);
        Assert.Equal(5, (int)leeway["effectiveRequirement"]!);
        Assert.True((bool)leeway["nativeMeetsLevelRequirements"]!);
    }

    [Fact]
    public void UnlimitedAuthoredResearchRetainsLeewayRefusalAndIndependentArtificialCap()
    {
        var id = Guid.Parse("21628be0-4377-4b13-b28c-171ab29324c0");
        var research = new WorldResearch(
            id,
            level: 1,
            queuedLevels: 0,
            researchStage: 0,
            selfBonusLevels: 0,
            maxLevel: -1,
            researchTime: 60,
            isDeveloping: false,
            isActive: false,
            flagged: false,
            available: true,
            visible: true,
            complete: false,
            canDevelop: false,
            withinDevelopRange: false,
            meetsLevelRequirements: true,
            stillHasLeeway: false,
            belowArtificialMaxLevel: false,
            belowMaxInvestmentLevel: true,
            purchasedLevels: 1,
            baseLevel: 1,
            bonusLevel: 0,
            totalLevel: 1,
            artificialMaxLevel: 1,
            hiddenLevel: false,
            levelVisibilityRange: 2,
            requiredStagesCached: 0,
            requiredTimeCached: BigDouble.Zero,
            baseRequirementLevel: 1,
            effectiveRequirementLevel: 1,
            requirementAdjustments: PublicationTable<WorldResearchRequirementAdjustment>.Empty,
            modifiers: new RawResearchModifiers(
                BigDouble.Zero, BigDouble.Zero, new BigDouble(100d),
                new BigDouble(1d), BigDouble.Zero),
            // Every research row the game publishes carries a develop decision; one without it
            // reads as "the research state was not readable", which is a collection fault rather
            // than a capped row.
            decision: new WorldResearchDecision(
                queueMode: false,
                multiBuy: 0,
                queuedLevels: 0,
                levelsAvailable: 0,
                currentInvestmentLevel: 0,
                currentTime: BigDouble.Zero,
                remainingTime: BigDouble.Zero,
                timeRatio: BigDouble.Zero,
                canApplyBonusLevel: false,
                freeBonusLevels: 0,
                developmentCostAffordable: true,
                developmentCosts: PublicationTable<WorldResourceCost>.Empty,
                investment: PublicationTable<WorldResearchInvestment>.Empty,
                researchTypes: PublicationTable<WorldResearchTypeDecision>.Empty));
        var world = new GameWorldState
        {
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            CollectedAtEpoch = 77,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = Explain(world, id, 947);

        Assert.Null(result["status"]);

        // An artificial cap refuses the next develop without finishing anything, so the lifecycle
        // is untouched by it: the row is still available, and the refusal lives on the can-develop
        // axis where it belongs.
        Assert.Equal("available", (string?)result["row"]!["state"]);
        Assert.Null(result["state"]);
        Assert.Null(result["row"]!["complete"]);
        Assert.Null(result["predicates"]!["canDevelop"]);
        Assert.False((bool)result["row"]!["develop"]!["available"]!);
        Assert.Null(result["row"]!["develop"]!["reasonCode"]);
        var cap = result["blockers"]!["cap"]!;
        Assert.Equal(1, (int)cap["artificialCap"]!);
        Assert.Null(cap["effectiveCap"]);
        Assert.False((bool)cap["nativeComplete"]!);
    }

    [Fact]
    public void ThresholdCostAndTypedBlockersCarryCompleteEvidence()
    {
        var upgradeId = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var resourceId = Guid.Parse("40000000-0000-4000-8000-000000000002");
        var modifierId = Guid.Parse("40000000-0000-4000-8000-000000000003");
        var challengeId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        var adjustmentId = Guid.Parse("50000000-0000-4000-8000-000000000002");
        var challengeAdjustment = new WorldResearchRequirementAdjustment(
            adjustmentId,
            challengeId,
            modifierType: 0,
            amount: new BigDouble(-5d),
            order: 0,
            passive: true);
        var rawUpgrade = new RawUpgradeSample(
            upgradeId,
            level: 1,
            maxLevel: 1,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1,
            cachedCostLevel: 1);
        var upgrade = new WorldUpgrade(
            in rawUpgrade,
            isBounded: true,
            isExhausted: true,
            remainingLevels: 0,
            committedLevel: 1,
            isDeveloping: false,
            developmentProgress: 0);
        var cost = new WorldPurchaseCost(
            upgradeId,
            resourceId,
            baseExactAmount: new BigDouble(100d),
            effectiveExactAmount: new BigDouble(250d),
            exactGroupedLevels: 1,
            exactGroupedAmount: new BigDouble(250d),
            modifierSources: PublicationTable<WorldPurchaseCostModifierSource>.Create(new[]
            {
                new WorldPurchaseCostModifierSource(
                    "upgrade.cost_modifier",
                    modifierId,
                    "ValueModifierVariable",
                    "effective cost percent",
                    new BigDouble(150d),
                    hasModifierType: true,
                    modifierType: 3),
            }),
            affordabilityEvaluated: true,
            availableAmount: new BigDouble(200d),
            combinedEffectiveAmount: new BigDouble(250d),
            resourceAffordable: false,
            resourceAffordabilityReasonCode: "insufficient_resource",
            affordable: false,
            affordabilityReasonCode: "unaffordable");
        var research = Research(
            ReadyResearchId,
            available: true,
            level: 4,
            maxLevel: 4,
            baseRequirement: 10,
            effectiveRequirement: 5,
            leeway: 1,
            adjustments: PublicationTable<WorldResearchRequirementAdjustment>.Create(
                new[] { challengeAdjustment }));
        var crafting = Crafting(
            ReadyCraftingId,
            visible: true,
            canBuy: false,
            resources: PublicationTable<WorldCraftingRecipeResource>.Create(new[]
            {
                new WorldCraftingRecipeResource(
                    ReadyCraftingId,
                    WorldCraftingRecipeResourceKind.AuthoredInput,
                    resourceId,
                    new BigDouble(25d),
                    resourceStateAvailable: true,
                    visible: true,
                    bandwidthResource: true,
                    trueQuantity: new BigDouble(100d),
                    isCapped: true,
                    capacity: new BigDouble(100d),
                    usage: new BigDouble(90d),
                    drain: new BigDouble(2d)),
            }),
            drains: PublicationTable<WorldCraftingRecipeDrainBlock>.Create(new[]
            {
                new WorldCraftingRecipeDrainBlock(
                    ReadyCraftingId, blockIndex: 0, necessaryRatio: new BigDouble(0.5d)),
            }));
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[] { upgrade }),
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            CraftingRecipes = PublicationTable<WorldCraftingRecipe>.Create(new[] { crafting }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                GameMcpTestHarness.BandwidthResource(
                    resourceId, new BigDouble(90), new BigDouble(100)),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[] { cost }),
            ActionQueues = PublicationTable<WorldActionQueue>.Create(new[]
            {
                new WorldActionQueue(
                    KnownEntities.ActiveActionables.Uuid,
                    Guid.Empty,
                    slotCount: 1,
                    usedSlots: 1,
                    emptySlots: 0,
                    hasEmptySlot: false,
                    consistent: true),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world, 930);

        var upgradeResult = GameMcpTestHarness.Detail(state, upgradeId);
        var researchResult = GameMcpTestHarness.Detail(state, ReadyResearchId);
        var craftingResult = GameMcpTestHarness.Detail(state, ReadyCraftingId);

        Assert.False((bool)upgradeResult["predicates"]!["canPurchase"]!["available"]!);
        Assert.Null(upgradeResult["predicates"]!["canPurchase"]!["reasonCode"]);
        Assert.Null(upgradeResult["purchase"]);
        Assert.True((bool)upgradeResult["blockers"]!["queue"]!["blocked"]!);
        Assert.Null(upgradeResult["blockers"]!["queue"]!["evidence"]);
        Assert.True((bool)upgradeResult["blockers"]!["cap"]!["blocked"]!);

        var thresholds = researchResult["researchThresholds"]!;
        Assert.Equal(5, (int)thresholds["effectiveThreshold"]!);
        var adjustment = Assert.Single(thresholds["activeAdjustments"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(challengeId), (string?)adjustment["source"]!["uuid"]);
        Assert.Null(adjustment["sourceNativeType"]);
        // One field, printed once: `nativeStillHasLeeway` was `metWithLeeway`'s own value again.
        Assert.NotNull(thresholds["metWithLeeway"]);
        Assert.Null(thresholds["nativeStillHasLeeway"]);
        // This research still has leeway, so that axis is not what refuses and is not listed; the
        // cap is, and it prints in full beside it.
        Assert.Null(researchResult["blockers"]!["leeway"]);
        Assert.True((bool)researchResult["blockers"]!["cap"]!["blocked"]!);
        Assert.Null(researchResult["blockers"]!["bandwidth"]);

        // A discovered recipe's discovery axis had nothing but `blocked: no` to say, so it is not
        // listed; the two axes that do refuse print their rows in full.
        Assert.Null(craftingResult["blockers"]!["recipeDiscovery"]);
        Assert.True((bool)craftingResult["blockers"]!["bandwidth"]!["blocked"]!);
        var bandwidthRow = Assert.Single(
            craftingResult["blockers"]!["bandwidth"]!["rows"]!.Values<JObject>())!;
        Assert.Equal("25", (string?)bandwidthRow["cost"]);
        Assert.Equal("10", (string?)bandwidthRow["spendableAmount"]);
        Assert.Null(bandwidthRow["amount"]);
        Assert.True((bool)bandwidthRow["bandwidth"]!);
        Assert.Null(bandwidthRow["headroom"]);
        Assert.True((bool)craftingResult["blockers"]!["drain"]!["blocked"]!);
    }

    [Fact]
    public void EachPurchaseCostRowAnswersForItsOwnResourceWithNoAggregateBesideThem()
    {
        var upgradeId = Guid.Parse("d5100000-0000-4000-8000-000000000001");
        var heldId = Guid.Parse("d5100000-0000-4000-8000-000000000002");
        var shortId = Guid.Parse("d5100000-0000-4000-8000-000000000003");
        var rawUpgrade = new RawUpgradeSample(
            upgradeId,
            level: 1,
            maxLevel: 10,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1,
            cachedCostLevel: 1);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                GameWorldStateDeriver.Derive(in rawUpgrade),
            }),
            UpgradeListMemberships = GameMcpTestHarness.OnTheUpgradesList(upgradeId),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                PriceLine(upgradeId, heldId, held: 400, resourceAffordable: true),
                PriceLine(upgradeId, shortId, held: 5, resourceAffordable: false),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var purchase = Explain(world, upgradeId, 931)["purchase"]!;
        var rows = purchase["rows"]!.Values<JObject>().ToArray();

        Assert.Null(purchase["affordability"]);
        Assert.Single(purchase.Children());
        Assert.True((bool)rows[0]!["affordable"]!);
        Assert.Null(rows[0]!["reasonCode"]);
        Assert.False((bool)rows[1]!["affordable"]!);
        Assert.Null(rows[1]!["reasonCode"]);
    }

    [Fact]
    public void AnEntityWithNothingToSayPublishesEmptyBlocksRatherThanOmittingThem()
    {
        var upgradeId = Guid.Parse("d5100000-0000-4000-8000-000000000011");
        var rawUpgrade = new RawUpgradeSample(
            upgradeId,
            level: 1,
            maxLevel: 10,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1,
            cachedCostLevel: 1);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                GameWorldStateDeriver.Derive(in rawUpgrade),
            }),
            UpgradeListMemberships = GameMcpTestHarness.OnTheUpgradesList(upgradeId),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var explanation = Explain(world, upgradeId, 932);

        Assert.NotNull(explanation["predicates"]);
        Assert.NotNull(explanation["blockers"]);
        Assert.Empty(explanation["requirements"]!["root"]!["children"]!.Values<JObject>());
    }

    private static WorldPurchaseCost PriceLine(
        Guid ownerId,
        Guid resourceId,
        double held,
        bool resourceAffordable) =>
        new(
            ownerId,
            resourceId,
            baseExactAmount: new BigDouble(100d),
            effectiveExactAmount: new BigDouble(100d),
            exactGroupedLevels: 1,
            exactGroupedAmount: new BigDouble(100d),
            modifierSources: PublicationTable<WorldPurchaseCostModifierSource>.Empty,
            affordabilityEvaluated: true,
            availableAmount: new BigDouble(held),
            combinedEffectiveAmount: new BigDouble(100d),
            resourceAffordable,
            resourceAffordabilityReasonCode:
                resourceAffordable ? string.Empty : "insufficient_resource",
            affordable: false,
            affordabilityReasonCode: "unaffordable");
}
