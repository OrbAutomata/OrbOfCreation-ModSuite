using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameMcpNativeRegistryCollection
{
    public const string Name = "Game MCP native registries";
}

[Collection(GameMcpNativeRegistryCollection.Name)]
public sealed class GameMcpEntityExplainerTests : IDisposable
{
    private static readonly Guid ReadySpellId =
        Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid WaitingSpellId =
        Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid ReadyResearchId =
        Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid BlockedResearchId =
        Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid ReadyCraftingId =
        Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid BlockedCraftingId =
        Guid.Parse("30000000-0000-4000-8000-000000000002");

    public GameMcpEntityExplainerTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void ExplanationStaysPinnedAndEveryPlayerPredicateHasTrueAndFalseEvidence()
    {
        var original = new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                Spell(ReadySpellId, discovered: true, hidden: false, masteryLevel: 3),
                Spell(WaitingSpellId, discovered: false, hidden: false, masteryLevel: 1),
            }),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    slotIndex: 0,
                    ReadySpellId,
                    occupied: true,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: true,
                    castReady: true,
                    chargeAvailable: true,
                    resourcesCovered: true,
                    currentCharges: 1,
                    maximumCharges: 1,
                    cooldownRemaining: BigDouble.Zero),
            }),
            Research = PublicationTable<WorldResearch>.Create(new[]
            {
                Research(ReadyResearchId, available: true, level: 6, maxLevel: 20,
                    baseRequirement: 5, effectiveRequirement: 5, leeway: 0),
                Research(BlockedResearchId, available: false, level: 1, maxLevel: 20,
                    baseRequirement: 5, effectiveRequirement: 5, leeway: 0),
            }),
            CraftingRecipes = PublicationTable<WorldCraftingRecipe>.Create(new[]
            {
                Crafting(ReadyCraftingId, visible: true, canBuy: true),
                Crafting(BlockedCraftingId, visible: false, canBuy: false),
            }),
            CollectedAtEpoch = 31,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(original, new WorldGeneration(909));
        var pinned = Snapshot(publisher.ReadLatest());
        publisher.Publish(new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                Spell(ReadySpellId, discovered: false, hidden: true, masteryLevel: 99),
            }),
            CollectedAtEpoch = 32,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        }, new WorldGeneration(910));

        var readySpell = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(pinned, ReadySpellId.ToString("D")));
        var waitingSpell = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(pinned, WaitingSpellId.ToString("D")));
        var readyResearch = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(pinned, ReadyResearchId.ToString("D")));
        var blockedResearch = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(pinned, BlockedResearchId.ToString("D")));
        var readyCrafting = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(pinned, ReadyCraftingId.ToString("D")));
        var blockedCrafting = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(pinned, BlockedCraftingId.ToString("D")));

        Assert.Null(readySpell["worldGeneration"]);
        Assert.Null(readySpell["lifecycleGeneration"]);
        Assert.Equal(3, (int)readySpell["state"]!["masteryLevel"]!);
        // A predicate that holds is published holding. Dropping the passing ones made absence mean
        // "true" on one key and "this entity has no such predicate" on the next.
        Assert.True(Predicate(readySpell, "visible"));
        Assert.True(Predicate(readySpell, "available"));
        Assert.False(Predicate(readySpell, "canDiscover"));
        Assert.True(Predicate(readySpell, "canUse"));
        Assert.True(Predicate(waitingSpell, "canDiscover"));
        Assert.False(Predicate(waitingSpell, "canUse"));
        Assert.True(Predicate(readyResearch, "available"));
        Assert.False(Predicate(blockedResearch, "available"));
        Assert.False(Predicate(blockedResearch, "canDevelop"));
        Assert.True(Predicate(readyCrafting, "visible"));
        Assert.False(Predicate(blockedCrafting, "visible"));
        Assert.False(Predicate(blockedCrafting, "available"));
        Assert.False(Predicate(blockedCrafting, "canPurchase"));
        Assert.Null(readySpell["predicates"]!["canDevelop"]);
        Assert.Null(readySpell["predicates"]!["canPurchase"]);
        Assert.Null(readySpell["requirements"]);
        Assert.Null(readySpell["purchase"]);

        foreach (var explanation in new[]
                 {
                     readySpell, waitingSpell, readyResearch, blockedResearch,
                     readyCrafting, blockedCrafting,
                 })
        {
            if (explanation["predicates"] is not JObject predicateObject) continue;
            foreach (var predicate in predicateObject.Properties())
            {
                var value = Assert.IsType<JObject>(predicate.Value);
                if (!(bool)value["value"]!)
                    Assert.False(string.IsNullOrWhiteSpace((string?)value["reasonCode"]));
            }
        }
    }

    [Fact]
    public void ExplanationSeparatesUnknownFromKnownButUnprojectedIdentity()
    {
        var known = Guid.Parse("b4505524-ad2f-4a5a-9d28-df0c30937748");
        var unknown = Guid.Parse("00000000-0000-4000-8000-000000000099");
        var world = new GameWorldState
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var context = GameMcpTestHarness.Context(world, generation: 911);

        var knownResult = GameMcpTestHarness.Json(GameMcpEntityExplainer.Explain(
            context,
            known.ToString("D")));
        var unknownResult = GameMcpTestHarness.Json(GameMcpEntityExplainer.Explain(
            context,
            unknown.ToString("D")));

        Assert.Equal("ERR_NOT_FOUND", (string?)knownResult["reasonCode"]);
        Assert.Equal("InventoryUnlocked", (string?)knownResult["name"]);
        Assert.Equal("entity_catalog", (string?)knownResult["readWith"]!["tool"]);
        Assert.Null(knownResult["nameEvidence"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)unknownResult["reasonCode"]);
        Assert.Equal("entity_catalog", (string?)unknownResult["readWith"]!["tool"]);
        Assert.Equal(
            "(unnamed " + unknown.ToString("D").Substring(0, 6) + ")",
            (string?)unknownResult["name"]);
        Assert.Null(unknownResult["nameEvidence"]);
    }

    /// <remarks>
    /// An equipped spell instance is a runtime object, not a loaded asset, so the asset catalog
    /// does not know it — and the explanation answered "nothing in this process knows this UUID"
    /// and pointed at that same catalog. The world had published the UUID inside a spell-slot row,
    /// so both the claim and the remedy were wrong.
    /// </remarks>
    [Fact]
    public void A_runtime_member_of_a_published_row_is_answered_with_the_row_that_owns_it()
    {
        var instance = Guid.Parse("4e551da3-262b-4bee-9f92-904cb81bf25a");
        var world = new GameWorldState
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    0, instance, Guid.Empty, true, false, false, false, false,
                    false, false, false, false, false, 0, 0, BigDouble.Zero),
            }),
        };

        var result = GameMcpTestHarness.Json(GameMcpEntityExplainer.Explain(
            GameMcpTestHarness.Context(world, generation: 913),
            instance.ToString("D")));

        Assert.Equal("ERR_NOT_FOUND", (string?)result["reasonCode"]);
        Assert.Equal("world_list", (string?)result["readWith"]!["tool"]);
        Assert.Equal("spell-slots", (string?)result["readWith"]!["category"]);
    }

    [Fact]
    public void RequirementsExpandOrderedLinkTiersAndPreserveAndOrGroups()
    {
        var owner = Upgrade();
        var research = ResearchStub(level: 0);
        var either = new Requirements.OrRequirement();
        either.orConditions.Add(Require(research, 5));
        var all = new Requirements.AndRequirement();
        all.andConditions.Add(Require(research, 6));
        either.orConditions.Add(all);
        owner.prerequisitesPerLevel.prerequisites.Add(either);

        var link = new global::PrerequisiteLinkSO();
        global::PrerequisiteLinkSO.All.Add(link);
        link.linkTiers.Add(Tier(Require(research, 1)));
        link.linkTiers.Add(Tier(Require(research, 2)));
        owner.prerequisitesPerLevel.prerequisites.Add(
            new Requirements.PrerequisiteLinkRequirement
            {
                item = link,
                reqType = Requirements.PrerequisiteLinkType.Tier,
                value = new Requirements.LeveledValue { baseValue = 1d },
            });

        var result = Explain(Collect(), owner.GetGuid(), 920);

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)result["reasonCode"]);
        var requirements = Assert.IsType<JObject>(result["requirements"]);
        var root = Assert.IsType<JObject>(requirements["root"]);
        Assert.Equal("AND", (string?)root["operator"]);
        var top = root["children"]!.OfType<JObject>().ToArray();
        Assert.Equal(2, top.Length);
        Assert.Equal("OR", (string?)top[0]["operator"]);
        var orChildren = top[0]["children"]!.OfType<JObject>().ToArray();
        Assert.Equal("AndRequirement", (string?)orChildren[1]["conditionType"]);
        Assert.Equal("Unevaluable", (string?)orChildren[1]["verdict"]);

        var firstLeaf = orChildren[0];
        Assert.Equal(GameMcpTestHarness.Handle(research.GetGuid()),
            (string?)firstLeaf["requirement"]!["uuid"]);
        Assert.Equal("ResearchSO", (string?)firstLeaf["requirementNativeType"]);
        Assert.Equal("total_level", (string?)firstLeaf["selectedValueKind"]);
        Assert.NotNull(firstLeaf["current"]);
        Assert.NotNull(firstLeaf["required"]);
        Assert.False((bool)firstLeaf["met"]!);

        var tiers = top[1]["prerequisiteLinkTiers"]!.OfType<JObject>().ToArray();
        Assert.Equal(new[] { 0, 1 }, tiers.Select(tier => (int)tier["tierIndex"]!).ToArray());
        Assert.False((bool)tiers[0]["selected"]!);
        Assert.True((bool)tiers[1]["selected"]!);
        Assert.All(tiers, tier =>
        {
            var tierRequirements = Assert.IsType<JObject>(tier["requirements"]);
            Assert.Equal("AND", (string?)tierRequirements["operator"]);
        });
    }

    [Fact]
    public void ImprovedCastingExpandsItsOrPrerequisiteAndUsesNativeCompletionTruth()
    {
        var improvedId = Guid.Parse("21628be0-4377-4b13-b28c-171ab29324bf");
        var expansionId = Guid.Parse("779fcab3-7ac8-4b7c-a96b-fed313a4fa51");
        var wizardryId = Guid.Parse("fcd15239-47d1-41b9-bad0-59826fb41ba4");
        var improved = ResearchStub(level: 1, id: improvedId, maxLevel: 1);
        var expansion = ResearchStub(level: 0, id: expansionId, maxLevel: 20);
        var wizardry = ResearchStub(level: 5, id: wizardryId, maxLevel: 20);
        var either = new Requirements.OrRequirement();
        either.orConditions.Add(Require(wizardry, 5));
        either.orConditions.Add(Require(expansion, 15));
        improved.levelPrerequisites.prerequisites.Add(either);
        improved.levelPrerequisites.ParameterizedCheckResult = true;

        var result = Explain(Collect(), improvedId, 923);

        var responseBytes = System.Text.Encoding.UTF8.GetByteCount(
            result.ToString(Newtonsoft.Json.Formatting.None));
        Assert.True(responseBytes < 3_402, "explanation was " + responseBytes + " bytes");

        Assert.Equal("available", (string?)result["status"]);
        var requirements = Assert.IsType<JObject>(result["requirements"]);
        Assert.Null(requirements["applicable"]);
        Assert.Equal(1, (long)requirements["checkLevel"]!);
        var root = Assert.IsType<JObject>(requirements["root"]);
        var orGroup = Assert.Single(root["children"]!.OfType<JObject>());
        Assert.Equal("OR", (string?)orGroup["operator"]);
        var leaves = orGroup["children"]!.OfType<JObject>().ToArray();
        Assert.Equal(2, leaves.Length);
        Assert.Equal(GameMcpTestHarness.Handle(wizardryId), (string?)leaves[0]["requirement"]!["uuid"]);
        Assert.Equal("total_level", (string?)leaves[0]["selectedValueKind"]);
        Assert.Equal("5", (string?)leaves[0]["current"]);
        Assert.Equal("5", (string?)leaves[0]["required"]);
        Assert.True((bool)leaves[0]["met"]!);
        Assert.Equal(GameMcpTestHarness.Handle(expansionId), (string?)leaves[1]["requirement"]!["uuid"]);
        Assert.Equal("0", (string?)leaves[1]["current"]);
        Assert.Equal("15", (string?)leaves[1]["required"]);
        Assert.False((bool)leaves[1]["met"]!);
        Assert.Null(requirements["nativeParity"]);

        var predicates = result["predicates"]!;
        Assert.False((bool)predicates["available"]!["value"]!);
        Assert.Equal("ERR_STATE", (string?)predicates["available"]!["reasonCode"]);
        Assert.False((bool)predicates["canDevelop"]!["value"]!);
        Assert.Equal("ERR_STATE", (string?)predicates["canDevelop"]!["reasonCode"]);
        var cap = result["blockers"]!["cap"]!;
        Assert.True((bool)cap["blocked"]!);
        Assert.Equal("ERR_STATE", (string?)cap["reasonCode"]);
        Assert.Equal(1, (int)cap["purchasedLevel"]!);
        Assert.Equal(1, (int)cap["baseLevelExcludingBonus"]!);
        Assert.Equal(0, (int)cap["bonusLevel"]!);
        Assert.Equal(1, (int)cap["totalLevel"]!);
        Assert.Equal(1, (int)cap["effectiveCap"]!);
        Assert.True((bool)cap["nativeComplete"]!);

        // Levels waiting are the research row's own number; their sum with the built ones is not.
        Assert.Equal(0, (int)cap["queuedLevels"]!);
        Assert.Null(cap["committedLevel"]);
    }

    [Fact]
    public void AuthoredTooltipDescriptionLeadsDiscoverableExplanationWhenNativelyAvailable()
    {
        var glyphId = Guid.Parse("168e3734-1ecb-4938-bd4a-d011ff13e201");
        var native = new global::GlyphSO
        {
            DisplayName = "Weak",
            Description = "Reduces the strength of a spell glyph effect.",
        };
        native.SetGuid(glyphId);
        global::IdScriptableObject.RuntimeLookup[glyphId] = native;
        var world = new GameWorldState
        {
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            CollectedAtEpoch = 38,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = Explain(world, glyphId, 924);

        Assert.Equal("Weak", (string?)result["name"]);
        Assert.Equal(
            "Reduces the strength of a spell glyph effect.",
            (string?)result["description"]);
        Assert.True(
            result.Properties().TakeWhile(property => property.Name != "description")
                .All(property => property.Name is "worldGeneration" or "status" or "uuid" or
                    "name" or "category" or "nativeType"));
        // Every predicate that applies ships, passing or not. Asserting only that each carries a
        // boolean would hold just as well if the passing ones went back to being stripped, so the
        // one that passes here is named: an undiscovered discoverable glyph can be discovered.
        var predicates = Assert.IsType<JObject>(result["predicates"]);
        Assert.All(
            predicates.Properties(),
            predicate => Assert.Equal(
                JTokenType.Boolean, predicate.Value["value"]?.Type));
        Assert.True((bool)predicates["canDiscover"]!["value"]!);
        Assert.False((bool)predicates["visible"]!["value"]!);
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
                new BigDouble(1d), BigDouble.Zero));
        var world = new GameWorldState
        {
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            CollectedAtEpoch = 77,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = Explain(world, id, 947);

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.False((bool)result["state"]!["complete"]!);
        Assert.False((bool)result["predicates"]!["canDevelop"]!["value"]!);
        Assert.Equal("ERR_LIMIT", (string?)result["predicates"]!["canDevelop"]!["reasonCode"]);
        var cap = result["blockers"]!["cap"]!;
        Assert.Equal(1, (int)cap["artificialCap"]!);
        Assert.Null(cap["effectiveCap"]);
        Assert.False((bool)cap["nativeComplete"]!);
    }

    [Fact]
    public void NativeRequirementDisagreementFailsLoudWithBothVerdicts()
    {
        var owner = Upgrade();
        var research = ResearchStub(level: 6);
        owner.prerequisitesPerLevel.prerequisites.Add(Require(research, 6));

        var collected = Collect();
        var result = Explain(collected, owner.GetGuid(), 921);

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)result["reasonCode"]);
        var parity = result["requirements"]!["nativeParity"]!;
        Assert.Equal("Met", (string?)parity["suiteVerdict"]);
        Assert.Equal("Unmet", (string?)parity["nativeVerdict"]);

        // One condition, one name: the parity block and the envelope answered the same failure
        // with two different codes, and the sentence was computed for the envelope only.
        Assert.Equal("ERR_UNAVAILABLE", (string?)parity["reasonCode"]);
        Assert.Equal(
            "The suite reads this requirement as Met where the game reads it as Unmet.",
            (string?)parity["reason"]);
        Assert.Equal((string?)result["reason"], (string?)parity["reason"]);
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
            "ChallengeSO",
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
        var state = Snapshot(world, 930);

        var upgradeResult = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(state, upgradeId.ToString("D")));
        var researchResult = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(state, ReadyResearchId.ToString("D")));
        var craftingResult = GameMcpTestHarness.Json(
            GameMcpEntityExplainer.Explain(state, ReadyCraftingId.ToString("D")));

        // This world is assembled by hand, so no live entity carries the identity and the game has
        // no answer to compare against. The parity block says which, rather than going missing.
        Assert.Equal("ERR_UNAVAILABLE", (string?)upgradeResult["requirements"]!["nativeParity"]!["reasonCode"]);
        Assert.False((bool)upgradeResult["predicates"]!["canPurchase"]!["value"]!);
        Assert.Equal("ERR_STATE", (string?)upgradeResult["predicates"]!["canPurchase"]!["reasonCode"]);
        Assert.Null(upgradeResult["purchase"]);
        Assert.True((bool)upgradeResult["blockers"]!["queue"]!["blocked"]!);
        Assert.Null(upgradeResult["blockers"]!["queue"]!["evidence"]);
        Assert.True((bool)upgradeResult["blockers"]!["cap"]!["blocked"]!);

        var thresholds = researchResult["researchThresholds"]!;
        Assert.Equal(10, (int)thresholds["baseThreshold"]!);
        Assert.Equal(10, (int)thresholds["scaledThreshold"]!);
        Assert.Equal(5, (int)thresholds["effectiveThreshold"]!);
        var adjustment = Assert.Single(thresholds["activeAdjustments"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(challengeId), (string?)adjustment["source"]!["uuid"]);
        Assert.Equal("ChallengeSO", (string?)adjustment["sourceNativeType"]);
        Assert.Null(researchResult["blockers"]!["leeway"]!["applicable"]);
        Assert.True((bool)researchResult["blockers"]!["cap"]!["blocked"]!);
        Assert.Null(researchResult["blockers"]!["bandwidth"]);

        Assert.Null(craftingResult["blockers"]!["recipeDiscovery"]!["applicable"]);
        Assert.True((bool)craftingResult["blockers"]!["bandwidth"]!["blocked"]!);
        var bandwidthRow = Assert.Single(
            craftingResult["blockers"]!["bandwidth"]!["rows"]!.Values<JObject>())!;
        Assert.Equal("25", (string?)bandwidthRow["cost"]);
        Assert.Equal("10", (string?)bandwidthRow["amount"]);
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
        Assert.Equal("ERR_REFUSED", (string?)rows[1]!["reasonCode"]);
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

    private static bool Predicate(JObject explanation, string name) =>
        (bool)explanation["predicates"]![name]!["value"]!;

    private static JObject Explain(GameWorldState world, Guid id, ulong generation) =>
        GameMcpTestHarness.Json(GameMcpEntityExplainer.Explain(
            Snapshot(world, generation),
            id.ToString("D")));

    private static GameMcpFrameContext Snapshot(GameWorldState world, ulong generation)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            world with { EntityIdentities = GameMcpTestHarness.EntityCatalog },
            new WorldGeneration(generation));
        return Snapshot(publisher.ReadLatest());
    }

    private static GameMcpFrameContext Snapshot(
        WorldPublication<GameWorldState> publication)
    {
        if (publication.Snapshot.EntityIdentities.IsBound)
            return GameMcpTestHarness.Context(publication);
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            publication.Snapshot with
            {
                EntityIdentities = GameMcpTestHarness.EntityCatalog,
            },
            publication.Generation);
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static WorldSpellRecipe Spell(
        Guid id,
        bool discovered,
        bool hidden,
        int masteryLevel) => new(
            id,
            discovered,
            discRarityLevel: 0,
            masteryXp: BigDouble.Zero,
            masteryLevel,
            masteryLevelReady: false,
            hiddenDiscovery: hidden,
            isRequiredDiscovery: true,
            penaltyUsageCost: 1,
            castSpeed: 1,
            baseCharges: 1,
            repeatInstantEffects: false,
            spellPowerMod: BigDouble.One,
            spellCostMod: BigDouble.One,
            spellCdSpeedMod: BigDouble.One,
            spellDurationMod: BigDouble.One,
            spellSpecialMod: BigDouble.One,
            spellXpMod: BigDouble.One,
            hasAlertedThisMastery: false);

    private static WorldResearch Research(
        Guid id,
        bool available,
        int level,
        int maxLevel,
        int baseRequirement,
        int effectiveRequirement,
        int leeway,
        PublicationTable<WorldResearchRequirementAdjustment>? adjustments = null) => new(
            id,
            level,
            queuedLevels: 0,
            researchStage: 0,
            selfBonusLevels: 0,
            maxLevel,
            researchTime: 60,
            isDeveloping: false,
            isActive: false,
            flagged: false,
            available,
            visible: available,
            complete: maxLevel > 0 && level >= maxLevel,
            canDevelop: available && (maxLevel <= 0 || level < maxLevel),
            withinDevelopRange: available && (maxLevel <= 0 || level < maxLevel),
            meetsLevelRequirements: level + leeway >= effectiveRequirement,
            stillHasLeeway: true,
            belowArtificialMaxLevel: true,
            belowMaxInvestmentLevel: maxLevel <= 0 || level < maxLevel,
            purchasedLevels: level,
            baseLevel: level,
            bonusLevel: 0,
            totalLevel: level,
            artificialMaxLevel: 0,
            hiddenLevel: false,
            levelVisibilityRange: 2,
            requiredStagesCached: 0,
            requiredTimeCached: BigDouble.Zero,
            baseRequirement,
            effectiveRequirement,
            adjustments ?? PublicationTable<WorldResearchRequirementAdjustment>.Empty,
            new RawResearchModifiers(
                bonusLevels: BigDouble.Zero,
                baseLevels: BigDouble.Zero,
                power: new BigDouble(100d),
                maxLevelCap: BigDouble.Zero,
                leewayPoints: new BigDouble(leeway)));

    private static WorldCraftingRecipe Crafting(
        Guid id,
        bool visible,
        bool canBuy,
        PublicationTable<WorldCraftingRecipeResource>? resources = null,
        PublicationTable<WorldCraftingRecipeDrainBlock>? drains = null)
    {
        var reading = new RawCraftingRecipeSample(
            id,
            visible,
            canBuy,
            startingQuantity: BigDouble.One,
            useQuantityAsLevel: false,
            timeToComplete: 1,
            outputWithinCapacity: true,
            typeCount: 0,
            authoredInputCount: resources?.Count ?? 0,
            generatedOutputCount: 0,
            consumableOutputCount: 0,
            engagementEffectCount: drains?.Count ?? 0,
            completionEffectCount: 0);
        return new WorldCraftingRecipe(
            in reading,
            PublicationTable<WorldCraftingRecipeTypeLink>.Empty,
            resources ?? PublicationTable<WorldCraftingRecipeResource>.Empty,
            PublicationTable<WorldCraftingRecipeConsumableOutput>.Empty,
            drains ?? PublicationTable<WorldCraftingRecipeDrainBlock>.Empty);
    }

    private static global::UpgradeSO Upgrade()
    {
        var upgrade = new global::UpgradeSO { maxLevel = -1 };
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    private static global::ResearchSO ResearchStub(
        int level,
        Guid? id = null,
        int maxLevel = 20)
    {
        var research = new global::ResearchSO
        {
            uuid = (id ?? Guid.NewGuid()).ToString("D"),
            level = level,
            maxLevel = maxLevel,
        };
        global::ResearchSO.All.Add(research);
        return research;
    }

    private static Requirements.ResearchRequirement Require(
        global::ResearchSO target,
        double value) => new()
        {
            item = target,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = value },
        };

    private static global::PrerequisiteLinkSO.LinkDefinition Tier(
        params Requirements.IRequirementCondition[] requirements)
    {
        var tier = new global::PrerequisiteLinkSO.LinkDefinition();
        foreach (var requirement in requirements)
            tier.prerequisites.prerequisites.Add(requirement);
        return tier;
    }

    private static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }

    private static void ClearRegistries()
    {
        global::UpgradeSO.All.Clear();
        global::StructureSO.All.Clear();
        global::ResearchSO.All.Clear();
        global::SpellRecipeSO.All.Clear();
        global::AlchemyRecipeSO.All.Clear();
        global::RitualSO.All.Clear();
        global::GlyphSO.All.Clear();
        global::IntVariable.All.Clear();
        global::PrerequisiteLinkSO.All.Clear();
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::GameManager.currentFrame = 0;
    }
}
