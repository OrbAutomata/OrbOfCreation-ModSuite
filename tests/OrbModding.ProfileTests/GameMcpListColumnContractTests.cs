using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpUpgradePageTestSupport;

namespace OrbModding.ProfileTests;

/// <summary>
/// A category's list columns are declared and total, so the header a page shows is a fact about the
/// category rather than about which rows that page happened to hold.
/// </summary>
public sealed class GameMcpListColumnContractTests
{
    /// <summary>
    /// The round-8 defect: reading the upgrades page before a prestige listed the ceiling, and
    /// reading it after — when every upgrade was uncapped — dropped the column, so the one page
    /// that most needed to say caps exist was the page that said nothing about them.
    /// </summary>
    [Fact]
    public void An_all_uncapped_upgrades_page_shows_the_columns_a_capped_page_shows()
    {
        var mixed = Columns(Page(Upgrade(Capped, bounded: true), Upgrade(Uncapped, bounded: false)));
        var allUncapped = Columns(Page(
            Upgrade(Uncapped, bounded: false),
            Upgrade(Capped, bounded: false)));

        Assert.Equal(mixed, allUncapped);
        Assert.Contains("maximum", mixed);
        Assert.Contains("state", mixed);
        Assert.Contains("affordable", mixed);
    }

    /// <summary>
    /// The ceiling a page has none of is spelled, not omitted and not invented: <c>0</c> would read
    /// as a cap of zero and as nothing left to buy, which is the opposite of what it means. The
    /// column reads <c>1</c>, the finite count, or the word — nothing else.
    /// </summary>
    [Fact]
    public void An_upgrade_with_no_ceiling_says_so_instead_of_publishing_a_number()
    {
        Assert.Equal(
            "uncapped",
            (string?)Rows(Page(Upgrade(Uncapped, bounded: false))).Single()["maximum"]);
        Assert.Equal(
            10,
            (int?)Rows(Page(Upgrade(Capped, bounded: true))).Single()["maximum"]);
    }

    /// <summary>
    /// A finished upgrade has no next level, so there is no price for one — the same absence a row
    /// the world published no cost for has. It used to answer <c>already_maxed</c> here, which was
    /// the completed state said a second time in a column that asks about money.
    /// </summary>
    [Fact]
    public void A_row_with_no_price_names_which_kind_of_no_price_it_is()
    {
        var rows = Rows(Page(
            Upgrade(Exhausted, bounded: true, exhausted: true),
            Upgrade(Uncapped, bounded: false)));

        Assert.Equal("unpriced", (string?)rows[0]["affordable"]);
        Assert.Equal("completed", (string?)rows[0]["state"]);
        Assert.Equal("unpriced", (string?)rows[1]["affordable"]);
        Assert.Equal("available", (string?)rows[1]["state"]);
    }

    /// <summary>
    /// A page whose every row says the same word names the word once beside the count instead of on
    /// every row — and still names the columns, because a reader who has only ever seen this page
    /// has to be able to learn from it that ceilings exist. The share line and the header together
    /// are the declared set, in declaration order; neither on its own is.
    /// </summary>
    [Fact]
    public void A_page_of_uncapped_upgrades_says_uncapped_once_and_still_names_both_ceiling_columns()
    {
        var page = GameMcpTextPage.Render(Page(
            Upgrade(Uncapped, bounded: false),
            Upgrade(Capped, bounded: false),
            Upgrade(Exhausted, bounded: false),
            Upgrade(Uncapped, bounded: false),
            Upgrade(Capped, bounded: false),
            Upgrade(Exhausted, bounded: false)));

        Assert.Contains(
            "these 6 share: level=3, queuedLevels=0, screen=Magic, state=available, " +
            "maximum=uncapped, nextLevelRequirements=met, affordable=unpriced",
            page,
            StringComparison.Ordinal);

        // Every column the share line settled leaves the rows: a page that printed `uncapped` six
        // more times under a line that just said all six share it is the same fact said seven times.
        Assert.Equal("[id | name]", Bracket(page));
        Assert.DoesNotContain("| uncapped |", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Most categories render straight from their declared field list, and that list is what the
    /// header promises. Skipping a declared field the row happened to carry nothing under made the
    /// header a fact about the page's rows instead — the same defect, one layer down and across
    /// every category that has no hand-written projection.
    /// </summary>
    [Fact]
    public void A_declared_field_the_row_carries_nothing_under_says_so()
    {
        var selected = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var rows = Rows(AlchemyTypes(selected, Guid.Empty));

        Assert.Equal("-", (string?)rows[1]["selectedLevel"]);
        Assert.NotNull(rows[0]["selectedLevel"]);
        Assert.Equal(
            Columns(AlchemyTypes(selected, selected)),
            Columns(AlchemyTypes(Guid.Empty, Guid.Empty)));
    }

    /// <summary>
    /// The declaration is the contract, so it answers for categories that exist. A stale name would
    /// be a column set nothing is held to, which reads like enforcement and is not.
    /// </summary>
    [Fact]
    public void Every_declared_column_set_belongs_to_a_registered_category()
    {
        var registered = new HashSet<string>(
            GameMcpWorldCategories.RegisteredCategoryNames(), StringComparer.Ordinal);

        Assert.NotEmpty(GameMcpListColumns.Categories);
        Assert.All(GameMcpListColumns.Categories, name => Assert.Contains(name, registered));
    }

    /// <summary>
    /// Every declaring category is listed once here, from a world holding one row of each, so a
    /// column set is held to its own rows rather than only to the categories some other test
    /// happens to read. Each row has a real catalog identity and zero-valued readings, where a
    /// conditional field is most likely to disappear.
    /// </summary>
    [Fact]
    public void Every_declaring_category_builds_rows_its_declaration_accepts()
    {
        var context = GameMcpTestHarness.Context(OneRowOfEach(), generation: 4242);
        var world = Assert.IsType<WorldPublication<GameWorldState>>(context.World).Snapshot;

        Assert.All(GameMcpListColumns.Categories, category =>
        {
            var page = GameMcpTestHarness.Json(
                GameMcpWorldList.ListRows(context, category, 0, 50).Freeze(), world);
            Assert.True(
                page["rows"] is not null,
                category + ": " + (string?)page["status"] + " " + (string?)page["reason"]);
            Assert.NotEmpty(page["rows"]!.Values<JObject>());
        });
    }

    /// <summary>
    /// An empty page of a category names the same columns a full page of it shows, for every
    /// category the surface lists. That header is the only thing an empty page has instead of a
    /// row to read the shape off, so a declaration that drifted from the rows would put its worst
    /// header on the one read with nothing on it to say so.
    /// </summary>
    [Fact]
    public void An_empty_page_of_a_category_names_the_columns_a_full_page_shows()
    {
        var context = GameMcpTestHarness.Context(OneRowOfEach(), generation: 4242);
        var world = Assert.IsType<WorldPublication<GameWorldState>>(context.World).Snapshot;

        Assert.All(GameMcpWorldCategories.RegisteredCategoryNames(), category =>
        {
            var page = GameMcpTestHarness.Json(
                GameMcpWorldList.ListRows(context, category, 0, 50).Freeze(), world);
            var rows = Assert.IsType<JArray>(page["rows"]);
            Assert.True(rows.Count > 0, category + " has no fixture row");
            var empty = GameMcpTestHarness.Json(
                GameMcpWorldList.ListRows(context, category, rows.Count, 50).Freeze(), world);
            var emptyPage = GameMcpTextPage.Render(empty);

            Assert.Empty(empty["rows"]!.Values<JObject>());
            Assert.Contains(
                "rows 0/" + rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                emptyPage.Split('\n'));
            var declared = Bracket(emptyPage)
                .Trim('[', ']')
                .Split(" | ", StringSplitOptions.None);
            var fullPage = GameMcpTextPage.Render(page);
            string[] shown;
            if (declared.SequenceEqual(new[] { "id", "name" }))
            {
                // Marker types carry only identity. The shipping surface prints their names and
                // handles as an identity list, while an empty page still declares the same schema.
                var identity = Assert.IsType<JObject>(Assert.Single(rows));
                Assert.Equal(new[] { "uuid", "name" },
                    identity.Properties().Select(property => property.Name));
                Assert.Equal("rows: " + (string?)identity["name"] + " " + (string?)identity["uuid"],
                    fullPage);
                shown = identity.Properties()
                    .Select(property => property.Name == "uuid" ? "id" : property.Name).ToArray();
            }
            else
            {
                shown = Bracket(fullPage)
                    .Trim('[', ']')
                    .Split(" | ", StringSplitOptions.None);
            }

            Assert.Equal(declared, shown);
        });
    }

    /// <summary>Brew Mana Potion: an ordinary Alchemy recipe the Scholar screen never draws.</summary>
    private static readonly Guid OrdinaryRecipe =
        Guid.Parse("ef29f6df-4660-4d83-bf62-1230cf1a23de");

    /// <summary>Study Mind: one of the forty-six Concepts, on the Scholar screen.</summary>
    private static readonly Guid ConceptRecipe =
        Guid.Parse("fa165240-42a6-447d-9d3b-f6fa2865dbf9");

    private static WorldAlchemyRecipe ZeroRecipe(Guid recipeId, Guid coreTypeId) => new(
        recipeId, coreTypeId, discovered: false, maxLevel: 0, advancementLevel: 0,
        discoveryRarityLevel: 0, masteryXp: BigDouble.Zero, masteryLevel: 0,
        recipeTime: BigDouble.Zero, isRequiredDiscovery: false, isCompletionRecipe: false,
        isAdvancementRecipe: false, completionTime: 0, isDebugAlchemy: false,
        power: BigDouble.Zero, speed: BigDouble.Zero, drainCostMod: BigDouble.Zero,
        special: BigDouble.Zero, timeReqMod: BigDouble.Zero, timeScalingMod: BigDouble.Zero,
        masteryXpRate: BigDouble.Zero, effectLevels: BigDouble.Zero,
        overdrivePower: BigDouble.Zero, overdriveSpeed: BigDouble.Zero,
        overdriveDrainCostMod: BigDouble.Zero, overdriveXpRate: BigDouble.Zero,
        freeUsageSlots: BigDouble.Zero, maxUsageSlots: BigDouble.Zero,
        cachedCompletionTime: BigDouble.Zero, requiredExperience: BigDouble.Zero);

    private static GameWorldState OneRowOfEach()
    {
        var world = new GameWorldState
        {
            Structures = One(nameof(GameWorldState.Structures), id => new WorldStructure(
                reading: new RawStructureSample(
                    structureId: id,
                    structureTypeId: FixtureId(nameof(GameWorldState.StructureTypes)),
                    level: default,
                    queuedLevels: default,
                    unlocked: default,
                    queuedEchos: default,
                    completedEchos: default,
                    selfBonusLevels: default,
                    queueTimeLeft: default,
                    currentBuildTime: default,
                    flagged: default,
                    baseLevel: default,
                    queueTimeTotal: default,
                    debugStructure: default,
                    disabled: default,
                    observableId: default,
                    insufficientReqPenaltyActive: default,
                    bufferDevelopedQuantity: default,
                    costPerQuantityId: FixtureId(nameof(GameWorldState.IntVariables)),
                    modifiers: default),
                committedLevel: default,
                hasWorkInFlight: default,
                effectiveLevel: default,
                developmentProgress: default)),
            Upgrades = One(nameof(GameWorldState.Upgrades), id => new WorldUpgrade(
                reading: new RawUpgradeSample(
                    upgradeId: id,
                    level: default,
                    maxLevel: default,
                    available: default,
                    queuedLevels: default,
                    buildTime: default,
                    developmentTime: default,
                    cachedCostLevel: default),
                isBounded: default,
                isExhausted: default,
                remainingLevels: default,
                committedLevel: default,
                isDeveloping: default,
                developmentProgress: default)),
            Equipment = One(nameof(GameWorldState.Equipment), id => new WorldEquipment(
                equipmentId: id,
                isCreated: default,
                discRarityLevel: default,
                masteryXp: default,
                masteryLevel: default,
                isRequiredDiscovery: default,
                power: default,
                baseLevel: default,
                experienceRateMod: default,
                equippedLevel: default,
                attuningLevel: default,
                attunementTimeLeft: default,
                baseXpRate: default)),
            Rituals = One(nameof(GameWorldState.Rituals), id => new WorldRitual(
                ritualId: id,
                discovered: default,
                inBattle: default,
                activeInstances: default,
                reachedLevel: default,
                lastReachedLevel: default,
                selectedLevel: default,
                wavesCompleted: default,
                discoveryRarityLevel: default,
                critLevel: default,
                echoLevel: default,
                chainLevel: default,
                durationRewardBlocks: default,
                battleTotalWeight: default,
                modifiers: default,
                hideEndScreenResults: default,
                isDiscoverRequired: default,
                forceLevel: default,
                forceLevelValue: default,
                baseWaves: default,
                maxWaves: default,
                requiredWaves: default,
                baseWeight: default,
                minimumEffectLevel: default,
                failedRun: default)),
            Research = One(nameof(GameWorldState.Research), id => new WorldResearch(
                researchId: id,
                level: default,
                queuedLevels: default,
                researchStage: default,
                selfBonusLevels: default,
                maxLevel: default,
                researchTime: default,
                isDeveloping: default,
                isActive: default,
                flagged: default,
                available: default,
                visible: default,
                complete: default,
                canDevelop: default,
                withinDevelopRange: default,
                meetsLevelRequirements: default,
                stillHasLeeway: default,
                belowArtificialMaxLevel: default,
                belowMaxInvestmentLevel: default,
                purchasedLevels: default,
                baseLevel: default,
                bonusLevel: default,
                totalLevel: default,
                artificialMaxLevel: default,
                hiddenLevel: default,
                levelVisibilityRange: default,
                requiredStagesCached: default,
                requiredTimeCached: default,
                baseRequirementLevel: default,
                effectiveRequirementLevel: default,
                requirementAdjustments: PublicationTable<WorldResearchRequirementAdjustment>.Empty,
                modifiers: default)),
            ResourceTypes = One(nameof(GameWorldState.ResourceTypes), id => new WorldResourceType(
                resourceTypeId: id,
                level: default,
                freeLevels: default,
                specialHidden: default,
                ignoreAudit: default,
                ignoreEffects: default,
                auditHasMaxQuantity: default)),
            EquipmentTypes = One(nameof(GameWorldState.EquipmentTypes), id => new WorldEquipmentType(
                equipmentTypeId: id,
                level: default,
                freeLevels: default,
                baseUsage: default,
                masteryLevel: default,
                maxTypeSlots: default)),
            AugmentGlyphs = One(nameof(GameWorldState.AugmentGlyphs), id => new WorldGlyph(
                glyphId: id,
                level: 2,
                freeLevels: 1,
                discoveryRarityLevel: default,
                learned: true,
                discoverable: true,
                discoveryRequired: default,
                augmentsSpells: true,
                requiresDuration: default,
                requiresToggleable: default,
                masteryReqCount: default,
                freeUsages: default,
                freeLoadoutUsages: default,
                maxUsages: new BigDouble(3),
                maximumUsages: 3,
                maximumFreeUsages: 1,
                levelDecision: new WorldLevelableDecision(
                    totalLevel: 3,
                    bonusLevels: 1,
                    canPurchase: true,
                    purchaseAffordable: false,
                    paidCosts: PublicationTable<WorldLevelableCost>.Create(new[]
                    {
                        new WorldLevelableCost(FixtureId(nameof(GameWorldState.Resources)), BigDouble.One),
                    }),
                    supportsBonus: true,
                    bonusResourcesVisible: false,
                    bonusAffordable: false,
                    bonusCosts: PublicationTable<WorldLevelableCost>.Create(new[]
                    {
                        new WorldLevelableCost(FixtureId(nameof(GameWorldState.Resources)), BigDouble.One),
                    })))),
            // `alchemy-recipes` and `concepts` are one native class split by the recipe's own
            // core alchemy type, so one row of each category is two rows here. Everything but
            // that type stays at the game's zero values, like every other row in this fixture.
            AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(new[]
            {
                ZeroRecipe(OrdinaryRecipe, KnownEntities.Brewing.Uuid),
                ZeroRecipe(ConceptRecipe, KnownEntities.Reductive.Uuid),
            }.OrderBy(recipe => recipe.EntityId).ToArray()),
            PlotNodes = One(nameof(GameWorldState.PlotNodes), id => new WorldPlotNode(
                reading: new RawPlotNodeSample(
                    plotNodeId: id,
                    visible: default,
                    currentTime: default,
                    nextErraticTime: default,
                    sizeLevel: default,
                    masteryXp: default,
                    masteryLevel: default,
                    noMastery: default,
                    noSizeDisplay: default,
                    useVisibilityPrereq: default,
                    hasErraticGrowth: default,
                    debugMode: default,
                    erraticQuantity: default,
                    actionQuantityUsageMain: default,
                    actionQuantityUsageAny: default,
                    actionXpRate: default,
                    yieldMod: default,
                    specialMod: default,
                    actionSpeed: default,
                    actionCostMod: default,
                    growingSpeed: default,
                    restingSpeed: default,
                    sizeMod: default,
                    qualityMod: default,
                    recoverySizeMod: default,
                    naturalGrowth: default,
                    naturalGrowthPower: default,
                    lastQuantity: default,
                    idleQuantity: default,
                    totalQuantity: default),
                remainingQuantity: default,
                remainingTotalQuantity: default)),
            PurchaseCosts = One(nameof(GameWorldState.PurchaseCosts), id => new WorldPurchaseCost(entityId: FixtureId(nameof(GameWorldState.Structures)), resourceId: FixtureId(nameof(GameWorldState.Resources)), amount: default)),
            Challenges = One(nameof(GameWorldState.Challenges), id => new WorldChallenge(
                challengeId: id,
                level: default,
                state: default,
                seen: default,
                rewardQueued: default,
                maxLevel: default,
                weight: default,
                difficulty: default,
                baseReward: default)),
            CraftingRecipes = One(nameof(GameWorldState.CraftingRecipes), id => new WorldCraftingRecipe(
                reading: new RawCraftingRecipeSample(
                    recipeId: id,
                    visible: default,
                    canBuyAtStartingQuantity: default,
                    startingQuantity: default,
                    useQuantityAsLevel: default,
                    timeToComplete: default,
                    outputWithinCapacity: default,
                    completionEffectCount: default),
                types: PublicationTable<WorldCraftingRecipeTypeLink>.Empty,
                resources: PublicationTable<WorldCraftingRecipeResource>.Empty,
                consumableOutputs: PublicationTable<WorldCraftingRecipeConsumableOutput>.Empty,
                drainBlocks: PublicationTable<WorldCraftingRecipeDrainBlock>.Empty)),
            DiscoveryTrees = One(nameof(GameWorldState.DiscoveryTrees), id => new WorldDiscoveryTree(
                treeId: id,
                visible: default,
                actionMode: default,
                actionTime: default,
                rerollsLeft: default,
                usedRerollsLastDiscover: default,
                selectedChoiceId: Guid.Empty,
                currentOfferIds: Array.Empty<Guid>(),
                hasImmediateRequiredDiscovery: default,
                nextItemAffordable: default,
                nextItemCosts: Array.Empty<WorldResourceCost>(),
                overrideRerollsId: Guid.Empty,
                overrideChoicesId: Guid.Empty,
                additionalDiscoveryChoices: default,
                discoveryBonusLevelCost: default,
                debugMode: default,
                totalDiscoveredCount: default,
                poolDiscoveredCount: default,
                totalDiscoverableCount: default,
                poolDiscoverableCount: default,
                hasRequiredDiscovery: default,
                hasRemainingDiscovery: default,
                hasCompletedAllDiscoveries: default)),
            RecipeBooks = One(nameof(GameWorldState.RecipeBooks), id => new WorldRecipeBook(recipeBookId: id, available: default)),
            Resources = One(nameof(GameWorldState.Resources), id => new WorldResource(
                reading: new RawResourceSample(
                    resourceId: id,
                    quantity: default,
                    capacity: default,
                    visible: default,
                    lifetimeQuantity: default,
                    discoveryTime: default,
                    quality: default,
                    gainRate: default,
                    drain: default,
                    reservation: default,
                    usage: default,
                    inLossMode: default,
                    inRestMode: default,
                    inRallyMode: default,
                    appliedLevels: default,
                    levelVariableId: FixtureId(nameof(GameWorldState.IntVariables)),
                    rateInputs: default,
                    traits: default,
                    modifiers: default),
                isCapped: default,
                headroom: default,
                fillFraction: default,
                isAtCapacity: default,
                trueQuantity: default,
                trueRate: default)),
            PlayerLoadouts = One(nameof(GameWorldState.PlayerLoadouts), id => new WorldPlayerLoadout(
                id: id,
                name: "Column fixture loadout",
                selected: default,
                savesEquipment: default,
                savesAlchemy: default,
                icon: default,
                color: default,
                canSwitchNow: default)),
            SnapshotLoadouts = One(nameof(GameWorldState.SnapshotLoadouts), id => new WorldSnapshotLoadout(id: id, kind: WorldSnapshotLoadoutKind.Alchemy, slots: 1)),
            SnapshotSlots = One(nameof(GameWorldState.SnapshotSlots), id => new WorldSnapshotSlot(ownerId: FixtureId(nameof(GameWorldState.SnapshotLoadouts)), slot: default, populated: true)),
            SnapshotEntries = One(nameof(GameWorldState.SnapshotEntries), id => new WorldSnapshotEntry(ownerId: FixtureId(nameof(GameWorldState.SnapshotLoadouts)), slot: default, entryId: OrdinaryRecipe, quantity: 1)),
            CraftingQueueEntries = One(nameof(GameWorldState.CraftingQueueEntries), id => new WorldCraftingQueueEntry(
                queueId: KnownEntities.ActivePlotNodeActions.Uuid,
                slot: default,
                recipeId: FixtureId(nameof(GameWorldState.CraftingRecipes)),
                amount: default,
                automatic: default,
                repetitions: default)),
            SpellSlots = One(nameof(GameWorldState.SpellSlots), id => new WorldSpellSlot(
                slotIndex: default,
                spellRecipeId: FixtureId(nameof(GameWorldState.SpellRecipes)),
                occupied: default,
                casting: default,
                readyingCast: default,
                attuning: default,
                channeled: default,
                toggled: default,
                chargeable: default,
                castReady: default,
                chargeAvailable: default,
                resourcesCovered: default,
                currentCharges: default,
                maximumCharges: default,
                cooldownRemaining: default)),
            SpellCosts = One(nameof(GameWorldState.SpellCosts), id => new WorldSpellCost(slotIndex: default, kind: default, resourceId: FixtureId(nameof(GameWorldState.Resources)), amount: default)),
            AlchemyInstances = One(nameof(GameWorldState.AlchemyInstances), id => new WorldAlchemyInstance(recipeId: OrdinaryRecipe, quantity: default, queuedQuantity: default, drainReadable: default, drainRatio: default)),
            AlchemyLoadout = One(nameof(GameWorldState.AlchemyLoadout), id =>
                new WorldAlchemyLoadoutDecision(OrdinaryRecipe, 0, 0, 0, 0, 0, 0, false, false)),
            ActionQueueSlots = One(nameof(GameWorldState.ActionQueueSlots), id => new WorldActionQueueSlot(
                queueId: KnownEntities.ActivePlotNodeActions.Uuid,
                index: default,
                empty: default,
                plotNodeId: FixtureId(nameof(GameWorldState.PlotNodes)),
                plotNodeActionId: FixtureId(nameof(GameWorldState.PlotNodeActions)),
                quantity: default,
                engaged: default)),
            PlotActions = One(nameof(GameWorldState.PlotActions), id => new WorldPlotAction(reading: new RawPlotAction(plotNodeId: FixtureId(nameof(GameWorldState.PlotNodes)), plotNodeActionId: FixtureId(nameof(GameWorldState.PlotNodeActions)), offeredCount: default, instanceCount: default, prerequisiteEvidence: default), elementCost: default, elementCostKnown: default, hasEnoughForOneInstance: default, maximumRemainingInstances: default)),
            // A request the game never made is not a default struct: the constructor is what
            // guarantees its three names are non-null, so the fixture goes through it.
            Targeting = PublicationTable<WorldTargetingRequest>.Create(new[]
            {
                new WorldTargetingRequest(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    cancelAvailable: false,
                    PublicationTable<WorldTargetingCandidate>.Empty),
            }),
            DoubleVariables = One(nameof(GameWorldState.DoubleVariables), id => new WorldNumberVariable(variableId: id, value: default, isPercent: default)),
            IntVariables = One(nameof(GameWorldState.IntVariables), id => new WorldNumberVariable(variableId: id, value: default, isPercent: default)),
            BoolVariables = One(nameof(GameWorldState.BoolVariables), id => new WorldBoolVariable(variableId: id, value: default, initialValue: default, isSaved: default, observerId: default)),
            ModifierVariables = One(nameof(GameWorldState.ModifierVariables), id => new WorldModifierVariable(variableId: id, modifierType: default, amount: default, order: default)),
            Statistics = One(nameof(GameWorldState.Statistics), id => new WorldStatistic(statisticId: id, displayType: string.Empty, isPercent: default, description: string.Empty, globalDefinition: string.Empty)),
            AttributeGroups = One(nameof(GameWorldState.AttributeGroups), id => new WorldAttributeGroup(attributeGroupId: id, description: string.Empty)),
            StatusEffects = One(nameof(GameWorldState.StatusEffects), id => new WorldStatusEffect(
                statusEffectId: id,
                isBuff: default,
                maxDuration: default,
                stacksSeparately: default,
                resetDurationOnApplication: default,
                effectTimer: default,
                description: string.Empty)),
            CharacterAttributes = One(nameof(GameWorldState.CharacterAttributes), id => new WorldCharacterAttribute(characterAttributeId: id, damageTypeId: FixtureId(nameof(GameWorldState.DamageTypes)), description: string.Empty)),
            DamageTypes = One(nameof(GameWorldState.DamageTypes), id => new WorldDamageType(damageTypeId: id, damageReductionRate: default, ignoreEntrenched: default, description: string.Empty)),
            CharacterModifiers = One(nameof(GameWorldState.CharacterModifiers), id => new WorldCharacterModifier(characterModifierId: id, weightChance: default, description: string.Empty)),
            CharacterActions = One(nameof(GameWorldState.CharacterActions), id => new WorldCharacterAction(characterActionId: id, prepTime: default, actTime: default, speedMod: default, description: string.Empty)),
            CharacterTypes = One(nameof(GameWorldState.CharacterTypes), id => new WorldCharacterType(characterTypeId: id, description: string.Empty)),
            Enchantments = One(nameof(GameWorldState.Enchantments), id => new WorldEnchantment(enchantmentId: id, description: string.Empty)),
            GlyphTypes = One(nameof(GameWorldState.GlyphTypes), id => new WorldGlyphType(glyphTypeId: id, description: string.Empty)),
            RuneStones = One(nameof(GameWorldState.RuneStones), id => new WorldRuneStone(runeStoneId: id, description: string.Empty)),
            DisplayTypes = One(nameof(GameWorldState.DisplayTypes), id => new WorldDisplayType(displayTypeId: id, description: string.Empty)),
            AlchemyTypes = One(nameof(GameWorldState.AlchemyTypes), id => new WorldAlchemyType(alchemyTypeId: id, selectedLevelId: FixtureId(nameof(GameWorldState.IntVariables)), maxUsageByMastery: default, level: default)),
            SpellRecipes = One(nameof(GameWorldState.SpellRecipes), id => new WorldSpellRecipe(
                spellRecipeId: id,
                discovered: default,
                discRarityLevel: default,
                masteryXp: default,
                masteryLevel: default,
                masteryLevelReady: default,
                hiddenDiscovery: default,
                isRequiredDiscovery: default,
                penaltyUsageCost: default,
                castSpeed: default,
                baseCharges: default,
                repeatInstantEffects: default,
                spellPowerMod: default,
                spellCostMod: default,
                spellCdSpeedMod: default,
                spellDurationMod: default,
                spellSpecialMod: default,
                spellXpMod: default,
                hasAlertedThisMastery: default)),
            SpellTypes = One(nameof(GameWorldState.SpellTypes), id => new WorldSpellType(
                spellTypeId: id,
                typeLevel: default,
                typeXp: default,
                typeXpRequiredBase: default,
                augmentPowerMod: default,
                hasNoLevels: default,
                isElemental: default,
                isLoadoutUnique: default,
                hasNotTypeSignificance: default,
                isVisible: default,
                debugMode: default,
                typeXpMod: default,
                power: default,
                cooldownSpeed: default,
                cooldownTime: default,
                costMod: default,
                drainCostMod: default,
                durationMod: default,
                elementalResonance: default,
                augmentResonance: default,
                maxStacksMod: default,
                scalingMod: default,
                usageCostReduction: default,
                bonusCritRate: default,
                critEffectMod: default,
                critDurationMod: default,
                bonusDoubleCastRate: default,
                doubleCastEffectMod: default,
                chargeTimeMod: default,
                chargeEffectMod: default,
                chargeSpecialMod: default,
                bonusFlashRate: default,
                flashEffectMod: default)),
            CraftingRecipeTypes = One(nameof(GameWorldState.CraftingRecipeTypes), id => new WorldCraftingRecipeType(
                craftingRecipeTypeId: id,
                startingLevel: default,
                maxStartingLevel: default,
                craftVerb: string.Empty,
                isLevelType: default,
                initiated: default,
                magnitudeLoss: default,
                magnitudeTime: default,
                magnitudeIncrement: default)),
            StructureTypes = One(nameof(GameWorldState.StructureTypes), id => new WorldStructureType(structureTypeId: id, baseEffectLevel: default, baseBuildTime: default, overrideRankDefault: default, overrideRank: default)),
            RitualTypes = One(nameof(GameWorldState.RitualTypes), id => new WorldRitualType(ritualTypeId: id, initiated: default, activeRituals: default)),
            HarvestTypes = One(nameof(GameWorldState.HarvestTypes), id => new WorldHarvestType(harvestTypeId: id, level: default)),
            PlotNodeTypes = One(nameof(GameWorldState.PlotNodeTypes), id => new WorldPlotNodeType(plotNodeTypeId: id, totalLevel: default)),
            ResearchTypes = One(nameof(GameWorldState.ResearchTypes), id => new WorldResearchType(
                researchTypeId: id,
                linkedDevelopCost: default,
                linkedResourceCost: default,
                linkedResearchTime: default,
                ignoreWhenLevelDependent: default,
                persistThroughReset: default,
                cachedTotalLevel: default,
                cachedPeakLevel: default,
                cachedQueuedLevel: default,
                cachedDevelopingLevel: default,
                cachedQueuedValue: default,
                cachedInvestmentLevel: default,
                cachedPurchasedLevel: default,
                freeBonusLevels: default,
                usedBonusLevels: default,
                maxInvestmentLevel: default)),
            ConsumableFamilies = One(nameof(GameWorldState.ConsumableFamilies), id => new WorldConsumableFamily(consumableFamilyId: id, hidden: default, sortOrder: default)),
            HarvestActionTypes = One(nameof(GameWorldState.HarvestActionTypes), id => new WorldHarvestActionType(harvestActionTypeId: id)),
            PassiveAbilityTypes = One(nameof(GameWorldState.PassiveAbilityTypes), id => new WorldPassiveAbilityType(passiveAbilityTypeId: id)),
            TimeRuneTypes = One(nameof(GameWorldState.TimeRuneTypes), id => new WorldTimeRuneType(timeRuneTypeId: id, initialized: default, totalLevel: default)),
            PlayerLoadoutEntries = One(nameof(GameWorldState.PlayerLoadoutEntries), id => new WorldLoadoutEntry(ownerId: FixtureId(nameof(GameWorldState.PlayerLoadouts)), kind: WorldLoadoutEntryKind.Equipment, entryId: FixtureId(nameof(GameWorldState.Equipment)), referenceId: FixtureId(nameof(GameWorldState.Equipment)), quantity: 1)),
            HarvestElements = One(nameof(GameWorldState.HarvestElements), id => new WorldHarvestElement(
                harvestElementId: id,
                masteryXp: default,
                masteryLevel: default,
                harvestTime: default,
                growthTime: default,
                rarityValue: default,
                initialMaxQuantity: default,
                requiredXpToLevel: default,
                instances: default,
                power: default,
                harvestSpeedMod: default,
                drainCostMod: default,
                autoGenerationMod: default,
                experienceRateMod: default,
                actionXpRate: default,
                actionPower: default,
                actionSpeed: default,
                actionCostMod: default,
                harvestRate: default,
                lastOutputRate: default)),
            HarvestActions = One(nameof(GameWorldState.HarvestActions), id => new WorldHarvestAction(harvestActionId: id, power: default, speed: default, costMod: default)),
            TimeRunes = One(nameof(GameWorldState.TimeRunes), id => new WorldTimeRune(
                timeRuneId: id,
                discovered: default,
                level: default,
                discRarityLevel: default,
                masteryXp: default,
                masteryLevel: default,
                isDiscoverRequired: default,
                seen: default,
                freeUsages: default,
                power: default,
                powerScalingMod: default,
                masteryXpMod: default)),
            Consumables = One(nameof(GameWorldState.Consumables), id => new WorldConsumable(
                consumableId: id,
                visible: default,
                randomized: default,
                quantity: default,
                queuedQuantity: default,
                maximumCarryLoad: default,
                gainedSince: default,
                maxCreatedLevel: default,
                currentPrepTime: default,
                currentCooldown: default,
                currentCooldownTime: default,
                modifiers: default,
                preparationTime: default,
                canBeRandomized: default,
                hasDuration: default,
                durationBase: default,
                queueOnStart: default)),
            Achievements = One(nameof(GameWorldState.Achievements), id => new WorldAchievement(
                achievementId: id,
                level: default,
                seen: default,
                logProgress: default,
                steamApiName: string.Empty,
                maxLevels: default,
                achievementStrength: default)),
            Advancements = One(nameof(GameWorldState.Advancements), id => new WorldAdvancement(
                advancementId: id,
                levels: default,
                xp: default,
                isPersistent: default,
                baseRequiredXp: default,
                power: default)),
            ThoughtStreams = One(nameof(GameWorldState.ThoughtStreams), id => new WorldThoughtStream(thoughtStreamId: id, state: default)),
            Tutorials = One(nameof(GameWorldState.Tutorials), id => new WorldTutorial(tutorialId: id, isCompleted: default)),
            Views = PublicationTable<WorldView>.Create(new[]
            {
                new WorldView(KnownEntities.MagicGlyphsUpgrade.Uuid,
                    active: false, alwaysActive: false, available: true),
            }),
            PlotNodeActions = One(nameof(GameWorldState.PlotNodeActions), id => new WorldPlotNodeAction(
                plotNodeActionId: id,
                hasBeenUsed: default,
                isGrowingAction: default,
                showExitTooltip: default,
                elementCost: default,
                useSizeModForCost: default,
                useAnyStateForCost: default,
                parallelAction: default,
                baseTime: default,
                useSpaceUsageForTimeMult: default,
                ignoreNodeYield: default,
                useParentSize: default,
                useParentQuality: default,
                power: default,
                speed: default,
                costMod: default,
                growthSizeMod: default,
                refundRating: default,
                sizeModNodeCount: default,
                elementCostType: default,
                elementCostExitPhase: default,
                prerequisiteCount: default,
                resourceCostCount: default,
                persistentEffectCount: default,
                completionEffectCount: default)),
            PassiveAbilities = One(nameof(GameWorldState.PassiveAbilities), id => new WorldPassiveAbility(
                passiveAbilityId: id,
                muted: default,
                touched: default,
                hidden: default,
                silent: default,
                global: default,
                startOnCooldown: default,
                ignoreReactionCooldown: default,
                reactionTokenCost: default,
                maxTokens: default,
                minTokenForEffect: default,
                tokenIndividuateDuration: default,
                applyWhileRecharging: default,
                expireAttachedStatusEffect: default,
                tokenRate: default)),
            Characters = One(nameof(GameWorldState.Characters), id => new WorldCharacter(characterId: id, discovered: default, numberSlain: default, floats: default)),
            MasteryExperience = One(nameof(GameWorldState.MasteryExperience), id => new WorldMasteryExperience(
                sequence: 1,
                domain: default,
                sourceId: FixtureId(nameof(GameWorldState.SpellRecipes)),
                sourceMastery: default,
                sourceEligible: default,
                amount: default)),
            AlchemyCosts = One(nameof(GameWorldState.AlchemyCosts), id => new WorldAlchemyCost(recipeId: OrdinaryRecipe, kind: default, resourceId: FixtureId(nameof(GameWorldState.Resources)), amount: default)),
            AlchemyUsageCosts = One(nameof(GameWorldState.AlchemyUsageCosts), id => new WorldAlchemyUsageCost(recipeId: OrdinaryRecipe, resourceId: FixtureId(nameof(GameWorldState.Resources)), amount: default)),
            PlotAuthoring = One(nameof(GameWorldState.PlotAuthoring), id => new WorldPlotAuthoring(plotNodeId: FixtureId(nameof(GameWorldState.PlotNodes)), autoActionId: FixtureId(nameof(GameWorldState.PlotNodeActions)), phaseCount: default)),
            PlotPhaseDescriptors = One(nameof(GameWorldState.PlotPhaseDescriptors), id => new WorldPlotPhaseDescriptor(
                plotNodeId: FixtureId(nameof(GameWorldState.PlotNodes)),
                ordinal: default,
                phase: default,
                phaseTimeSeconds: default,
                processType: default,
                exitPhase: default)),
            EffectBlocks = One(nameof(GameWorldState.EffectBlocks), id => new WorldEffectBlock(
                ownerId: FixtureId(nameof(GameWorldState.Structures)),
                ordinal: default,
                blockTypeName: string.Empty,
                prerequisiteCount: default,
                modCount: default,
                scriptCount: default,
                modTypeName: string.Empty,
                scriptTypeName: string.Empty,
                scalingWeightId: FixtureId(nameof(GameWorldState.ModifierVariables)),
                treasurePoolId: FixtureId(nameof(GameWorldState.TreasurePools)),
                effectTypeName: string.Empty,
                effectValue: default,
                filterListType: default,
                filterContentCount: default)),
            EntityRequirements = One(nameof(GameWorldState.EntityRequirements), id => new WorldEntityRequirement(
                ownerId: FixtureId(nameof(GameWorldState.Upgrades)),
                ownerKind: default,
                containerIndex: default,
                ordinal: default,
                parentOrdinal: -1,
                depth: default,
                nodeKind: default,
                @operator: default,
                kind: default,
                conditionTypeName: string.Empty,
                targetId: FixtureId(nameof(GameWorldState.Research)),
                reqType: default,
                baseValue: default,
                perLevel: default,
                modPerLevel: default)),
            TreasurePools = One(nameof(GameWorldState.TreasurePools), id => new WorldTreasurePool(
                poolId: id,
                treasuresFound: default,
                partialReward: default,
                forceLevel: default,
                treasureLevel: default,
                calculatedTreasureLevel: default)),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(
                    GameMcpTestHarness.AllCategoriesCollected()),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var identities = new List<EntityIdentityName>();
        foreach (var category in GameMcpWorldCategories.Categories)
        {
            if (category.IdentityMode != "stable_entity_uuid") continue;
            for (var index = 0; index < category.Count(world); index++)
            {
                Assert.True(category.TryIdentity(category.Row(world, index), out var identity),
                    category.Name + " requires a real fixture identity");
                identities.Add(new EntityIdentityName(identity,
                    category.ExpectedNativeType.Split('|')[0],
                    category.Name + " fixture", category.Name + "Fixture"));
            }
        }
        return world with { EntityIdentities = GameMcpTestHarness.CatalogWith(identities.ToArray()) };
    }

    private static PublicationTable<TRow> One<TRow>(string table, Func<Guid, TRow> create)
        where TRow : struct =>
        PublicationTable<TRow>.Create(new[] { create(FixtureId(table)) });

    private static Guid FixtureId(string table) =>
        new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(table))
            .AsSpan(0, 16));

    /// <summary>
    /// A category that builds its own rows is held to its declaration on every row it builds, so a
    /// projection edit that made one column conditional again cannot reach a page.
    /// </summary>
    [Fact]
    public void A_row_that_drops_a_declared_column_is_refused_rather_than_published()
    {
        var partial = new GameMcpObjectBuilder
        {
            ["entityId"] = Guid.Empty.ToString("D"),
            ["level"] = 1,
            ["queuedLevels"] = 0,
            ["state"] = "available",
            ["enabled"] = true,
        }.Freeze();

        var refusal = Assert.Throws<InvalidOperationException>(
            () => GameMcpListColumns.Verify("attributes", partial));

        Assert.Contains("affordable", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_that_invents_a_column_is_refused_rather_than_published()
    {
        var extra = new GameMcpObjectBuilder
        {
            ["entityId"] = Guid.Empty.ToString("D"),
            ["totalLevel"] = 1,
            ["hidden"] = false,
            ["mood"] = "curious",
        }.Freeze();

        var refusal = Assert.Throws<InvalidOperationException>(
            () => GameMcpListColumns.Verify("resource-types", extra));

        Assert.Contains("mood", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A list read answers a planning question, and the test for a column is whether two reads
    /// seconds apart with nobody playing between them would agree. These five did not: each named
    /// what the game was doing at the instant it was asked, so a page of them was stale on arrival
    /// and — as a live round caught with spell-slots' <c>casting</c> — could change the header
    /// between two pages of one scan. They are named here so a later edit cannot bring one back by
    /// only looking at the row in front of it.
    /// </summary>
    [Fact]
    public void No_declared_column_names_what_the_game_is_doing_this_instant()
    {
        var transient = new[]
        {
            ("spell-slots", "casting"),
            ("agromancy-processing", "processing"),
            ("alchemy-instances", "settled"),
            ("plot-nodes", "idleQuantity"),
            ("research", "development"),
        };

        Assert.All(transient, pair =>
        {
            Assert.True(
                GameMcpListColumns.TryDeclared(pair.Item1, out var declared),
                pair.Item1 + " stopped declaring its columns");
            Assert.DoesNotContain(pair.Item2, declared);
        });

        // The durable half of the one column that had both keeps its own name: a pause is the
        // player's saved switch, and it is the fact a planner scans this category for.
        Assert.True(GameMcpListColumns.TryDeclared("research", out var research));
        Assert.Contains("paused", research);
    }

    /// <summary>
    /// A category small enough to read in one call is read in one call. Paging it charges a second
    /// request, and charges every reader the thought a <c>next=</c> demands, to discover that there
    /// was never a second page. The count line still says how many rows there are, because that is
    /// a fact whether or not any were withheld.
    /// </summary>
    [Fact]
    public void A_category_small_enough_to_read_whole_is_not_paged()
    {
        var context = GameMcpTestHarness.Context(
            UpgradesWorld(
                Upgrade(Capped, bounded: true),
                Upgrade(Uncapped, bounded: false),
                Upgrade(Exhausted, bounded: true, exhausted: true)),
            generation: 735);

        var whole = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            context, "upgrades", 0, GameMcpWorldList.DefaultLimit, limitFromCaller: false));

        Assert.Equal(3, Rows(whole).Length);
        Assert.Equal(3, (int)whole["total"]!);
        Assert.Null(whole["nextOffset"]);
        Assert.Equal("rows 3/3", GameMcpTextPage.Render(whole).Split('\n')[0]);

        // A caller that named a smaller page gets the page it named, and is told where to resume.
        // The rule raises no page above what was asked for; it only stops lowering one below the
        // whole of a category nobody asked to have cut up.
        var asked = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            context, "upgrades", 0, 2, limitFromCaller: true));

        Assert.Equal(2, Rows(asked).Length);
        Assert.Equal(2, (int)asked["nextOffset"]!);
    }

    /// <summary>
    /// Absence says one thing per surface, in one mark. In a table it is printed, because the
    /// header promised a column and a page that dropped it would be a header about its rows.
    /// Outside a table there is no header to keep and no siblings to line up with, so absence is
    /// silence — the same answer a spell with nothing to toggle already gives, and the same one the
    /// wire normalizer already gives for the zero identity on every projection that declares no
    /// paths.
    /// </summary>
    [Fact]
    public void A_member_the_row_carries_nothing_under_is_marked_in_a_table_and_silent_outside_one()
    {
        var identity = Guid.Parse("45000000-0000-4000-8000-000000000000");
        var context = GameMcpTestHarness.Context(
            AlchemyTypesWorld(Guid.Empty), generation: 736);

        var row = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            context, "alchemy-types", 0, 50))["rows"]!.Values<JObject>());
        Assert.Equal("-", (string?)row!["selectedLevel"]);

        var detail = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            context, "alchemy-types", new[] { identity.ToString("D") }));
        var got = Assert.Single(detail["results"]!.Values<JObject>())!["row"]!;

        Assert.Null(got["selectedLevel"]);
        Assert.DoesNotContain(
            "selectedLevel",
            GameMcpTextPage.Render(detail),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// One response, one notation. A type's level reached its row as a plain <c>10300</c> beside
    /// the same quantity written <c>1.03e4</c> in the worth block below it, because the wire
    /// rewrote the screen's own spelling back into an integer for every field named like a count.
    /// The rewrite also invented precision it could not have: <c>1.03e4</c> is a rounded reading,
    /// so the exact-looking <c>10300</c> was a number the game never held. A count small enough
    /// that the screen writes it plainly still ships as the integer a caller can hand back.
    /// </summary>
    [Fact]
    public void A_level_the_screen_writes_as_an_exponent_stays_the_way_the_screen_writes_it()
    {
        var identity = Guid.Parse("45000000-0000-4000-8000-000000000000");
        var big = GameMcpTestHarness.Context(
            AlchemyTypesWorld(new BigDouble(10300), Guid.Empty), generation: 737);
        var small = GameMcpTestHarness.Context(
            AlchemyTypesWorld(new BigDouble(7), Guid.Empty), generation: 738);

        Assert.Equal("1.03e4", (string?)TypeRow(big, identity)["level"]);
        Assert.Equal(7, (int?)TypeRow(small, identity)["level"]);
    }

    private static JObject TypeRow(GameMcpFrameContext context, Guid identity) =>
        (JObject)Assert.Single(GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            context, "alchemy-types", new[] { identity.ToString("D") }))["results"]!
            .Values<JObject>())!["row"]!;

    private static GameWorldState AlchemyTypesWorld(params Guid[] selectedLevels) =>
        AlchemyTypesWorld(BigDouble.Zero, selectedLevels);

    private static GameWorldState AlchemyTypesWorld(
        BigDouble level,
        params Guid[] selectedLevels)
    {
        var types = new WorldAlchemyType[selectedLevels.Length];
        for (var index = 0; index < selectedLevels.Length; index++)
        {
            types[index] = new WorldAlchemyType(
                Guid.Parse("4500000" + index + "-0000-4000-8000-000000000000"),
                selectedLevels[index],
                maxUsageByMastery: false,
                level: level);
        }
        return new GameWorldState
        {
            AlchemyTypes = PublicationTable<WorldAlchemyType>.Create(types),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "alchemy-types", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
            }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

    private static JObject AlchemyTypes(params Guid[] selectedLevels)
    {
        return GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(AlchemyTypesWorld(selectedLevels)), "alchemy-types", 0, 50));
    }

    /// <summary>Every column the page's rows carry, which is every column it declares.</summary>
    private static IReadOnlyList<string> Columns(JObject page)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows(page))
            foreach (var property in row.Properties())
                names.Add(property.Name);
        return names.ToArray();
    }
}
