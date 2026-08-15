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

    private static readonly Guid Idle = Guid.Parse("83a00000-0000-4000-8000-000000000001");
    private static readonly Guid Passed = Guid.Parse("83b00000-0000-4000-8000-000000000001");

    private static readonly Guid Building = Guid.Parse("90a00000-0000-4000-8000-000000000001");
    private static readonly Guid Alchemical = Guid.Parse("90b00000-0000-4000-8000-000000000001");
    private static readonly Guid Charm = Guid.Parse("90c00000-0000-4000-8000-000000000001");
    private static readonly Guid Primary = Guid.Parse("90d00000-0000-4000-8000-000000000001");
    private static readonly Guid CharmFocus = Guid.Parse("90e00000-0000-4000-8000-000000000001");
    private static readonly Guid StructureFocus = Guid.Parse("90f00000-0000-4000-8000-000000000001");

    /// <summary>
    /// The page a cross-category search actually returns: five columns whatever category a hit came
    /// from, and an honest empty keywords cell on the classes the game authors no words for. An
    /// upgrade's cell is not filled from its category, its screen, or its name — the game prints no
    /// type line on one, so neither does this. <c>matchedOn</c> is the fifth: the row's own field
    /// the query hit, which is what tells a reader the hit is the thing they meant rather than a
    /// coincidence in a string they never see.
    /// </summary>
    /// <remarks>
    /// The recipe answers under <c>alchemy-recipes</c> rather than <c>concept-recipes</c>: one
    /// entity is one hit however many categories republish it, and the first category holding it
    /// wins. This row used to read <c>concept-recipes</c> only because the fixture published the
    /// concept republication without the alchemy row the real world always carries beside it.
    /// </remarks>
    [Fact]
    public void A_search_across_categories_says_the_same_five_things_about_every_hit()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 5/5",
                "[id | name | category | keywords | matchedOn]",
                "81a000 | Alchemy Insight | upgrades | - | name",
                "81b000 | Alchemy Mastery | upgrades | - | name",
                "81c000 | Alchemy Refinement | upgrades | - | name",
                "82a000 | Alchemy Lab | structures | Building, Alchemical | name",
                "80a000 | Concept of Fire | alchemy-recipes | Alchemical, Structure Focus | category",
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
                "[id | name | category | keywords | matchedOn]",
                "81b000 | Alchemy Mastery | upgrades | - | name",
                "80a000 | Concept of Fire | alchemy-recipes | Alchemical, Structure Focus | category",
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
                "these 3 share: category=structures, matchedOn=keywords",
                "[id | name | keywords]",
                "82b000 | Warded Hall | Charm, Primary",
                "82c000 | Quiet Circle | Charm Focus",
                "82d000 | Still Font | Charm",
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
    /// "What have I not unlocked yet" is a whole question and it names nothing. Requiring a query
    /// beside the filter made a caller invent a word broad enough to reach everything they meant
    /// and then hope it had — a reach nobody could characterise, sitting under a count the whole
    /// sweep was judged on. A call that names at least one filter is a call.
    /// </summary>
    [Fact]
    public void A_call_that_names_only_a_filter_asks_a_whole_question()
    {
        var locked = Json(GameMcpWorldQuery.Search(
            Context(), string.Empty, 0, 50, string.Empty, "locked"));

        Assert.Equal(
            new[] { "80a000", "81b000", "82c000" },
            Rows(locked)
                .Select(row => (string?)row["uuid"])
                .OrderBy(handle => handle, StringComparer.Ordinal)
                .ToArray());

        // Nothing was matched by name, so the column that says what matched says the absence mark
        // rather than naming a field the caller never asked anything about.
        Assert.All(Rows(locked), row => Assert.Equal("-", (string?)row["matchedOn"]));

        // Naming neither a query nor a filter is still not a question, and the refusal says what
        // would have made it one.
        var nothing = Json(GameMcpWorldQuery.Search(Context(), string.Empty, 0, 50));
        Assert.Equal("ERR_INPUT", (string?)nothing["reasonCode"]);
        Assert.Equal(
            "name something to search for: a query, or a category, state, run or keyword filter",
            (string?)nothing["reason"]);
    }

    /// <summary>
    /// <c>run</c> is a column like <c>state</c> is, so it narrows a search like one. A caller who
    /// wants the challenge they left running had to read the whole challenges page to find it.
    /// </summary>
    [Fact]
    public void The_run_column_narrows_a_search_the_way_the_lifecycle_word_does()
    {
        Assert.Equal(
            new[] { "83b000" },
            Rows(Json(GameMcpWorldQuery.Search(
                    Context(), string.Empty, 0, 50, string.Empty, string.Empty, "passed")))
                .Select(row => (string?)row["uuid"])
                .ToArray());
        Assert.Equal(
            new[] { "83a000" },
            Rows(Json(GameMcpWorldQuery.Search(
                    Context(), string.Empty, 0, 50, "challenges", string.Empty, "idle")))
                .Select(row => (string?)row["uuid"])
                .ToArray());

        // One category publishes the column, so a call that narrows to another is told which one
        // rather than being handed an empty page to read as "there are none".
        var elsewhere = Json(GameMcpWorldQuery.Search(
            Context(), string.Empty, 0, 50, "upgrades", string.Empty, "idle"));
        Assert.Equal("ERR_INPUT", (string?)elsewhere["reasonCode"]);
        Assert.Equal(
            "run is a challenges column and no other category publishes one, so it cannot narrow " +
            "upgrades; drop the category or drop the run filter",
            (string?)elsewhere["reason"]);
    }

    /// <summary>
    /// The tool description is the only account of the surface a caller reads before using it, so
    /// every filter and the optional query are named there in the same words the tool answers in.
    /// </summary>
    [Fact]
    public void The_search_tool_describes_the_filter_only_call_and_the_run_column()
    {
        var description = (string?)Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate!["name"] == "world_search")!["description"] ??
            string.Empty;
        var schema = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate!["name"] == "world_search")!["inputSchema"]!;

        Assert.Contains("matchedOn", description, StringComparison.Ordinal);
        Assert.Contains("run", description, StringComparison.Ordinal);
        Assert.Contains("keyword", description, StringComparison.Ordinal);
        Assert.Null(schema["required"]);
        Assert.Equal(
            new[] { "idle", "queued", "active", "passed", "failed" },
            schema["properties"]!["run"]!["enum"]!.Values<string>());

        // The keyword filter takes an id rather than a word, and the schema says so where a caller
        // reads it: a type asset's name is not unique enough to name a node with.
        Assert.Equal("string", (string?)schema["properties"]!["keyword"]!["type"]);
        Assert.Null(schema["properties"]!["keyword"]!["enum"]);
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
    /// The empty page is the one moment the second finder is worth naming: "I searched that word
    /// and got nothing" is exactly when "is it in the game at all?" becomes the next question, and
    /// before this nothing on the page said the question had an answer or which verb held it.
    /// </summary>
    [Fact]
    public void A_search_that_found_nothing_says_what_the_entity_catalog_would_find()
    {
        var page = Json(GameMcpWorldQuery.Search(
            Loaded(), "animation", 0, 50, string.Empty, string.Empty, limitFromCaller: false));

        Assert.Empty(Rows(page));
        Assert.Equal(
            "entity_catalog matches 2 loaded ids the published world has no row for.",
            (string?)page["unprojected"]);
    }

    /// <summary>
    /// The line is said whichever way it comes out. "Nothing loaded is called that" closes the
    /// question on the page that raised it, where silence would send the caller to ask it anyway.
    /// </summary>
    [Fact]
    public void A_word_nothing_in_the_build_answers_to_is_closed_rather_than_left_open()
    {
        var page = Json(GameMcpWorldQuery.Search(
            Loaded(), "thaumaturgy", 0, 50, string.Empty, string.Empty, limitFromCaller: false));

        Assert.Empty(Rows(page));
        Assert.Equal(
            "entity_catalog matches none either, so no id this build loaded answers to this query.",
            (string?)page["unprojected"]);
    }

    /// <summary>
    /// A page with rows answered the question that was asked, and a caller reading it has no second
    /// question — so the line is the empty page's alone and costs every other page nothing. A
    /// filter-only call names no word for the catalog to match either.
    /// </summary>
    [Fact]
    public void A_page_that_found_something_spends_nothing_on_the_signpost()
    {
        Assert.Null(Search("alchemy")["unprojected"]);
        Assert.Null(Json(GameMcpWorldQuery.Search(
            Context(), string.Empty, 0, 50, string.Empty, "locked"))["unprojected"]);
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
            new EntityIdentityName(Idle, "ChallengeSO", "Trial of Ash", "trialAsh"),
            new EntityIdentityName(Passed, "ChallengeSO", "Trial of Frost", "trialFrost"),
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
            Challenges = PublicationTable<WorldChallenge>.Create(new[]
            {
                Challenge(Idle, run: 0),
                Challenge(Passed, run: 3),
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

    /// <summary>
    /// A build whose loaded ids include two the published world holds no category for, which is the
    /// gap the empty page signposts. <c>AnimationSO</c> is one of the ninety-six real types this
    /// build loads and no category claims.
    /// </summary>
    private static GameMcpFrameContext Loaded()
    {
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(2, new[]
            {
                new EntityIdentityName(Lab, "StructureSO", "Alchemy Lab", "alchemyLab"),
                new EntityIdentityName(Idle, "AnimationSO", "Orb Pulse", "animationOrbPulse"),
                new EntityIdentityName(Passed, "AnimationSO", "Glyph Flare", "animationGlyphFlare"),
            }),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
            CollectedAtEpoch = 62,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(862));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    /// <summary>
    /// <c>run</c> is <c>ChallengeSO.state</c> and it moves without the lifecycle moving, so both
    /// rows here are <c>available</c> and differ only in the column this filter reads.
    /// </summary>
    private static WorldChallenge Challenge(Guid id, int run) => new(
        id,
        level: 0,
        state: run,
        seen: true,
        rewardQueued: false,
        maxLevel: 5,
        weight: 0,
        difficulty: 0,
        baseReward: 0,
        availableToRun: true,
        completedOnce: run == 3,
        maximumLevelReached: false);

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
                "loadouts", "harvest-elements", "harvest-actions", "plot-actions",
                "action-queue-slots",
                            // The three type rosters whose wire name is not their collector's name.
                "harvest-types", "harvest-action-types", "consumable-families",
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
