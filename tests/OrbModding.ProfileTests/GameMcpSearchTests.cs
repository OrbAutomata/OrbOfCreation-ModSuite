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

/// <summary>
/// Search answers "I heard a term — what is it?" across every category at once, in one uniform row,
/// most relevant first.
/// </summary>
public sealed class GameMcpSearchTests
{
    private static readonly Guid Concept = Guid.Parse("80a00000-0000-4000-8000-000000000001");
    private static readonly Guid Insight = Guid.Parse("81a00000-0000-4000-8000-000000000001");
    private static readonly Guid Mastery = Guid.Parse("81b00000-0000-4000-8000-000000000001");
    private static readonly Guid Refinement = Guid.Parse("81c00000-0000-4000-8000-000000000001");
    private static readonly Guid Insightful = Guid.Parse("81d00000-0000-4000-8000-000000000001");
    private static readonly Guid Lab = Guid.Parse("82a00000-0000-4000-8000-000000000001");
    private static readonly Guid Warded = Guid.Parse("82b00000-0000-4000-8000-000000000001");
    private static readonly Guid Quiet = Guid.Parse("82c00000-0000-4000-8000-000000000001");
    private static readonly Guid Font = Guid.Parse("82d00000-0000-4000-8000-000000000001");

    private static readonly Guid Building = Guid.Parse("90a00000-0000-4000-8000-000000000001");
    private static readonly Guid Alchemical = Guid.Parse("90b00000-0000-4000-8000-000000000001");
    private static readonly Guid Charm = Guid.Parse("90c00000-0000-4000-8000-000000000001");
    private static readonly Guid Primary = Guid.Parse("90d00000-0000-4000-8000-000000000001");
    private static readonly Guid CharmFocus = Guid.Parse("90e00000-0000-4000-8000-000000000001");
    private static readonly Guid StructureFocus = Guid.Parse("90f00000-0000-4000-8000-000000000001");

    /// <summary>
    /// The page a cross-category search actually returns: four columns whatever category a hit came
    /// from, and an honest empty keywords cell on the classes the game authors no words for. An
    /// upgrade's cell is not filled from its category, its screen, or its name — the game prints no
    /// type line on one, so neither does this.
    /// </summary>
    /// <remarks>
    /// The recipe answers under <c>alchemy-recipes</c> rather than <c>concept-recipes</c>: one
    /// entity is one hit however many categories republish it, and the first category holding it
    /// wins. This row used to read <c>concept-recipes</c> only because the fixture published the
    /// concept republication without the alchemy row the real world always carries beside it.
    /// </remarks>
    [Fact]
    public void A_search_across_categories_says_the_same_four_things_about_every_hit()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 5/5",
                "[id | name | category | keywords]",
                "81a000 | Alchemy Insight | upgrades | -",
                "81b000 | Alchemy Mastery | upgrades | -",
                "81c000 | Alchemy Refinement | upgrades | -",
                "82a000 | Alchemy Lab | structures | Building, Alchemical",
                "80a000 | Concept of Fire | alchemy-recipes | Alchemical, Structure Focus",
            }),
            Render(Search("alchemy")));
    }

    /// <summary>
    /// Relevance is the sort and never a column: the thing called that, then the things the game
    /// files under that word, then the heap the word merely names. The id order inside each band is
    /// what makes two pages of one result agree, and it is deliberately not the order the bands
    /// come in — the concept recipe here has the lowest id of all six and still sorts second.
    /// </summary>
    [Fact]
    public void A_name_match_outranks_a_keyword_match_and_both_outrank_the_category()
    {
        Assert.Equal(
            new[] { "81d000", "80a000", "82a000", "82b000", "82c000", "82d000" },
            Rows(Search("structure")).Select(row => (string?)row["uuid"]).ToArray());
    }

    /// <summary>
    /// The lifecycle filter answers "what have I not unlocked yet" using the same words the pages
    /// say, for every category that says them — which is now every category the player can meet a
    /// locked thing in, alchemy recipes included. The filter reads the row's own word rather than
    /// deriving one here, so its reach follows the column rather than a second list beside it.
    /// </summary>
    [Fact]
    public void A_state_filter_selects_the_rows_whose_own_page_says_that_word()
    {
        // The motivating case: a recipe the alchemy screen shows no row for is locked, and the
        // filter finds it beside the locked upgrade rather than seeing only the purchasables.
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 2/2",
                "[id | name | category | keywords]",
                "81b000 | Alchemy Mastery | upgrades | -",
                "80a000 | Concept of Fire | alchemy-recipes | Alchemical, Structure Focus",
            }),
            Render(Search("alchemy", state: "locked")));

        Assert.Equal(
            new[] { "81a000", "81c000", "82a000" },
            Rows(Search("alchemy", state: "available"))
                .Concat(Rows(Search("alchemy", state: "completed")))
                .Select(row => (string?)row["uuid"])
                .OrderBy(handle => handle, StringComparer.Ordinal)
                .ToArray());

        var refused = Json(GameMcpWorldQuery.Search(
            Context(), "alchemy", 0, 50, string.Empty, "purchasable"));
        Assert.Equal("unavailable", (string?)refused["status"]);
        Assert.Equal("ERR_INPUT", (string?)refused["reasonCode"]);
        Assert.Equal(
            "state must be one of locked, available, completed", (string?)refused["reason"]);
    }

    /// <summary>
    /// The tool description is the only account of the filter a caller reads before using it, and it
    /// went on promising the three purchasables long after the filter reached eight categories — so
    /// a caller who believed it never asked for the locked alchemy recipe the filter would have
    /// found. The description names every category the filter reads a word from, and the two are
    /// held together here.
    /// </summary>
    [Fact]
    public void The_search_tool_describes_the_state_filter_it_actually_has()
    {
        var description = (string?)Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate!["name"] == "world_search")!["description"] ??
            string.Empty;

        Assert.All(
            new[]
            {
                "upgrades", "research", "structures", "alchemy-recipes",
                "glyphs", "rituals", "plot-nodes", "challenges",
            },
            category => Assert.Contains(category, description, StringComparison.Ordinal));
        Assert.DoesNotContain(
            "only upgrades, research and structures", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// One word the player heard can name two families, and which one they meant is the next thing
    /// they need. The line counts only the keywords the query itself hit, so it stays short without
    /// a cap, and it is silent when there was nothing to split.
    /// </summary>
    [Fact]
    public void A_query_that_hits_two_keywords_says_how_the_result_splits_between_them()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "keywordHits: Charm=2, Charm Focus=1",
                "rows 3/3",
                "[id | name | category | keywords]",
                "82b000 | Warded Hall | structures | Charm, Primary",
                "82c000 | Quiet Circle | structures | Charm Focus",
                "82d000 | Still Font | structures | Charm",
            }),
            Render(Search("charm")));

        Assert.Null(Search("structure")["keywordHits"]);
        Assert.Null(Search("alchemy")["keywordHits"]);
    }

    /// <summary>
    /// A search narrowed to one category reads that category alone, and a category whose rows have
    /// no identity of their own is refused by name rather than silently answering nothing.
    /// </summary>
    [Fact]
    public void A_category_filter_narrows_the_search_and_a_composite_one_is_refused()
    {
        Assert.Equal(
            new[] { "82a000" },
            Rows(Search("alchemy", category: "structures"))
                .Select(row => (string?)row["uuid"])
                .ToArray());

        var composite = Json(GameMcpWorldQuery.Search(
            Context(), "alchemy", 0, 50, "targeting"));
        Assert.Equal("unavailable", (string?)composite["status"]);
        Assert.Equal("ERR_INPUT", (string?)composite["reasonCode"]);
        Assert.Contains("read it with world_list", (string?)composite["reason"]);

        var unknown = Json(GameMcpWorldQuery.Search(
            Context(), "alchemy", 0, 50, "no-such-category"));
        Assert.Equal("ERR_INPUT", (string?)unknown["reasonCode"]);
        Assert.Contains("unknown category", (string?)unknown["reason"]);
    }

    /// <summary>
    /// The whole-small-result rule a caller already knows from <c>world_list</c>: a result nobody
    /// asked to have cut up comes back whole, and a caller who names a page size gets the page they
    /// named.
    /// </summary>
    [Fact]
    public void A_result_small_enough_to_read_whole_is_not_paged()
    {
        var whole = Search("alchemy");
        Assert.Equal(5, whole["rows"]!.Count());
        Assert.Null(whole["nextOffset"]);

        var paged = Json(GameMcpWorldQuery.Search(
            Context(), "alchemy", 0, 2, string.Empty, string.Empty, limitFromCaller: true));
        Assert.Equal(2, paged["rows"]!.Count());
        Assert.Equal(2, (int?)paged["nextOffset"]);
        Assert.Equal(5, (int?)paged["total"]);
    }

    /// <summary>
    /// The keyword surface is four published tables, not one, and the words come out in the order
    /// the game prints them. Equipment is the one class that reads its primary type last, and a type
    /// asset the game left nameless contributes no word at all — the asset-name fallback every other
    /// surface may walk would print seven keywords no player has ever seen.
    /// </summary>
    [Fact]
    public void Every_published_keyword_source_resolves_to_the_words_the_game_prints()
    {
        var owner = Guid.Parse("a1000000-0000-4000-8000-000000000001");
        var equipment = Guid.Parse("a2000000-0000-4000-8000-000000000001");
        var research = Guid.Parse("a3000000-0000-4000-8000-000000000001");
        var consumable = Guid.Parse("a4000000-0000-4000-8000-000000000001");
        var spell = Guid.Parse("a5000000-0000-4000-8000-000000000001");
        var wordless = Guid.Parse("a6000000-0000-4000-8000-000000000001");

        var index = GameMcpKeywordIndex.Build(new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(Building, "StructureTypeSO", "Building", "buildingType"),
                new EntityIdentityName(Alchemical, "GlyphTypeSO", "Alchemical", "alchemicalType"),
                new EntityIdentityName(Charm, "SpellTypeSO", "Charm", "charmType"),
                new EntityIdentityName(Primary, "SpellTypeSO", "Primary", "primaryType"),
                new EntityIdentityName(wordless, "ChallengeTypeSO", string.Empty, "wordlessType"),
            }),
            EntityKeywords = PublicationTable<WorldEntityKeyword>.Create(Sorted(
                new WorldEntityKeyword(
                    owner, WorldKeywordOwnerKind.Structure,
                    WorldKeywordSource.PrimaryType, 0, Building),
                new WorldEntityKeyword(
                    owner, WorldKeywordOwnerKind.Structure,
                    WorldKeywordSource.TypeList, 0, Alchemical),
                new WorldEntityKeyword(
                    owner, WorldKeywordOwnerKind.Structure,
                    WorldKeywordSource.TypeList, 1, wordless),
                new WorldEntityKeyword(
                    equipment, WorldKeywordOwnerKind.Equipment,
                    WorldKeywordSource.PrimaryType, 0, Building),
                new WorldEntityKeyword(
                    equipment, WorldKeywordOwnerKind.Equipment,
                    WorldKeywordSource.TypeList, 0, Alchemical))),
            Research = PublicationTable<WorldResearch>.Create(new[]
            {
                ResearchRow(research, Charm, Primary),
            }),
            ConsumableTypes = PublicationTable<WorldConsumableType>.Create(new[]
            {
                new WorldConsumableType(consumable, Charm),
            }),
            SpellRelations = PublicationTable<WorldSpellRelation>.Create(new[]
            {
                new WorldSpellRelation(spell, WorldSpellRelationKind.SpellType, 0, Charm),
                new WorldSpellRelation(spell, WorldSpellRelationKind.CoreGlyph, 0, Building),
            }),
        });

        Assert.Equal("Building, Alchemical", index.Line(owner));
        Assert.Equal("Alchemical, Building", index.Line(equipment));
        Assert.Equal("Charm, Primary", index.Line(research));
        Assert.Equal("Charm", index.Line(consumable));
        Assert.Equal("Charm", index.Line(spell));
        Assert.Equal(string.Empty, index.Line(Guid.NewGuid()));
    }

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject[] Rows(JObject page) =>
        page["rows"]!.Values<JObject>().Select(row => row!).ToArray();

    private static JObject Search(string query, string category = "", string state = "") =>
        Json(GameMcpWorldQuery.Search(
            Context(), query, 0, 50, category, state, limitFromCaller: false));

    private static JObject Json(GameMcpObjectBuilder value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value.Freeze(), Catalog));

    private static readonly EntityIdentityCatalogSnapshot Catalog =
        EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(Concept, "AlchemyRecipeSO", "Concept of Fire", "conceptFire"),
            new EntityIdentityName(Insight, "UpgradeSO", "Alchemy Insight", "alchemyInsight"),
            new EntityIdentityName(Mastery, "UpgradeSO", "Alchemy Mastery", "alchemyMastery"),
            new EntityIdentityName(
                Refinement, "UpgradeSO", "Alchemy Refinement", "alchemyRefinement"),
            new EntityIdentityName(
                Insightful, "UpgradeSO", "Structure Insight", "structureInsight"),
            new EntityIdentityName(Lab, "StructureSO", "Alchemy Lab", "alchemyLab"),
            new EntityIdentityName(Warded, "StructureSO", "Warded Hall", "wardedHall"),
            new EntityIdentityName(Quiet, "StructureSO", "Quiet Circle", "quietCircle"),
            new EntityIdentityName(Font, "StructureSO", "Still Font", "stillFont"),
            new EntityIdentityName(Building, "StructureTypeSO", "Building", "buildingType"),
            new EntityIdentityName(Alchemical, "StructureTypeSO", "Alchemical", "alchemicalType"),
            new EntityIdentityName(Charm, "StructureTypeSO", "Charm", "charmType"),
            new EntityIdentityName(Primary, "StructureTypeSO", "Primary", "primaryType"),
            new EntityIdentityName(
                CharmFocus, "StructureTypeSO", "Charm Focus", "charmFocusType"),
            new EntityIdentityName(
                StructureFocus, "AlchemyTypeSO", "Structure Focus", "structureFocusType"),
        });

    private static GameMcpFrameContext Context()
    {
        var world = new GameWorldState
        {
            EntityIdentities = Catalog,
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                Upgrade(Insight, exhausted: false, locked: false),
                Upgrade(Mastery, exhausted: false, locked: true),
                Upgrade(Refinement, exhausted: true, locked: false),
                Upgrade(Insightful, exhausted: false, locked: false),
            }),
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(Lab, unlocked: true),
                Structure(Warded, unlocked: true),
                Structure(Quiet, unlocked: false),
                Structure(Font, unlocked: true),
            }),
            ConceptRecipes = PublicationTable<WorldConceptRecipe>.Create(new[]
            {
                new WorldConceptRecipe(Concept, Guid.Empty, canAddNow: true, slotCount: 4),
            }),

            // The recipe the concept row republishes, as its own alchemy row and undiscovered: the
            // alchemy screen shows the player no row for it at all, which is exactly what the
            // filter has to be able to find.
            AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(new[]
            {
                Recipe(Concept, discovered: false),
            }),
            EntityKeywords = PublicationTable<WorldEntityKeyword>.Create(Sorted(
                Keyword(Lab, WorldKeywordSource.PrimaryType, 0, Building),
                Keyword(Lab, WorldKeywordSource.TypeList, 0, Alchemical),
                Keyword(Warded, WorldKeywordSource.PrimaryType, 0, Charm),
                Keyword(Warded, WorldKeywordSource.TypeList, 0, Primary),
                Keyword(Quiet, WorldKeywordSource.PrimaryType, 0, CharmFocus),
                Keyword(Font, WorldKeywordSource.PrimaryType, 0, Charm),
                new WorldEntityKeyword(
                    Concept, WorldKeywordOwnerKind.AlchemyRecipe,
                    WorldKeywordSource.TypeList, 0, Alchemical),
                new WorldEntityKeyword(
                    Concept, WorldKeywordOwnerKind.AlchemyRecipe,
                    WorldKeywordSource.TypeList, 1, StructureFocus))),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
            CollectedAtEpoch = 61,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(861));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static WorldEntityKeyword Keyword(
        Guid owner,
        WorldKeywordSource source,
        int ordinal,
        Guid keyword) =>
        new(owner, WorldKeywordOwnerKind.Structure, source, ordinal, keyword);

    /// <summary>The publication order the deriver guarantees: owner, then source, then ordinal.</summary>
    private static WorldEntityKeyword[] Sorted(params WorldEntityKeyword[] rows)
    {
        Array.Sort(rows, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            if (owner != 0) return owner;
            var source = ((int)left.Source).CompareTo((int)right.Source);
            return source != 0 ? source : left.Ordinal.CompareTo(right.Ordinal);
        });
        return rows;
    }

    private static WorldCollectionCategoryStatus[] CleanReports() =>
        GameMcpWorldQuery.RegisteredCategoryNames().Concat(new[]
            {
                "plot-node-actions", "concept-instances", "plot-authoring",
                "crafting-recipe-state", "crafting-decisions", "consumable-inventory",
                "loadouts", "harvest-elements", "plot-actions", "action-queue-slots",
            })
            .Distinct(StringComparer.Ordinal)
            .Select(name => new WorldCollectionCategoryStatus(
                name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
            .ToArray();

    /// <summary>
    /// One alchemy recipe on the gate every authored recipe of the pinned build carries, where
    /// <c>discovered</c> is the whole of what <c>AlchemyRecipeSO.IsAvailable()</c> reads.
    /// </summary>
    private static WorldAlchemyRecipe Recipe(Guid id, bool discovered) => new(
        id,
        Guid.Empty,
        discovered,
        maxLevel: 1,
        advancementLevel: 0,
        discoveryRarityLevel: 0,
        masteryXp: BigDouble.Zero,
        masteryLevel: 0,
        recipeTime: BigDouble.One,
        isRequiredDiscovery: false,
        isCompletionRecipe: false,
        isAdvancementRecipe: false,
        completionTime: 0,
        isDebugAlchemy: false,
        power: BigDouble.Zero,
        speed: BigDouble.Zero,
        drainCostMod: BigDouble.Zero,
        special: BigDouble.Zero,
        timeReqMod: BigDouble.Zero,
        timeScalingMod: BigDouble.Zero,
        masteryXpRate: BigDouble.Zero,
        effectLevels: BigDouble.Zero,
        overdrivePower: BigDouble.Zero,
        overdriveSpeed: BigDouble.Zero,
        overdriveDrainCostMod: BigDouble.Zero,
        overdriveXpRate: BigDouble.Zero,
        freeUsageSlots: BigDouble.Zero,
        maxUsageSlots: BigDouble.One,
        cachedCompletionTime: BigDouble.Zero,
        requiredExperience: BigDouble.One);

    private static WorldResearch ResearchRow(Guid id, params Guid[] types) => new(
        id,
        level: 0,
        queuedLevels: 0,
        researchStage: 0,
        selfBonusLevels: 0,
        maxLevel: 10,
        researchTime: 60,
        isDeveloping: false,
        isActive: false,
        flagged: false,
        available: true,
        visible: true,
        complete: false,
        canDevelop: true,
        withinDevelopRange: true,
        meetsLevelRequirements: true,
        stillHasLeeway: true,
        belowArtificialMaxLevel: true,
        belowMaxInvestmentLevel: true,
        purchasedLevels: 0,
        baseLevel: 0,
        bonusLevel: 0,
        totalLevel: 0,
        artificialMaxLevel: 0,
        hiddenLevel: false,
        levelVisibilityRange: 2,
        requiredStagesCached: 0,
        requiredTimeCached: BigDouble.Zero,
        baseRequirementLevel: 0,
        effectiveRequirementLevel: 0,
        PublicationTable<WorldResearchRequirementAdjustment>.Empty,
        new RawResearchModifiers(
            bonusLevels: BigDouble.Zero,
            baseLevels: BigDouble.Zero,
            power: new BigDouble(100d),
            maxLevelCap: BigDouble.Zero,
            leewayPoints: BigDouble.Zero),
        new WorldResearchDecision(
            queueMode: false,
            multiBuy: 1,
            queuedLevels: 0,
            levelsAvailable: 0,
            currentInvestmentLevel: 0,
            currentTime: BigDouble.Zero,
            remainingTime: BigDouble.Zero,
            timeRatio: BigDouble.Zero,
            canApplyBonusLevel: false,
            freeBonusLevels: 0,
            developmentCostAffordable: false,
            developmentCosts: PublicationTable<WorldResearchCost>.Empty,
            investment: PublicationTable<WorldResearchInvestment>.Empty,
            researchTypes: PublicationTable<WorldResearchTypeDecision>.Create(
                types.Select(type => new WorldResearchTypeDecision(type, 0, 0, 0)).ToArray())));

    private static WorldUpgrade Upgrade(Guid id, bool exhausted, bool locked)
    {
        var reading = new RawUpgradeSample(
            id,
            level: exhausted ? 10 : 3,
            maxLevel: 10,
            available: !exhausted && !locked,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1d,
            cachedCostLevel: 0);
        return new WorldUpgrade(
            in reading,
            isBounded: true,
            isExhausted: exhausted,
            remainingLevels: exhausted ? 0 : 7,
            committedLevel: exhausted ? 10 : 3,
            isDeveloping: false,
            developmentProgress: 0d);
    }

    private static WorldStructure Structure(Guid id, bool unlocked)
    {
        var modifiers = new RawStructureModifiers(
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero);
        var reading = new RawStructureSample(
            id,
            Guid.Empty,
            BigDouble.Zero,
            BigDouble.Zero,
            unlocked,
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
            BigDouble.Zero,
            hasWorkInFlight: false,
            BigDouble.Zero,
            developmentProgress: 0);
    }
}
