using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpUpgradePageTestSupport;

namespace OrbModding.ProfileTests;

public sealed class GameMcpListColumnWordTests
{
    /// <summary>
    /// One vocabulary, and every word in it readable at a glance. A cell that carried a sentence
    /// made the reader parse prose out of a column; a cell that carried an <c>ERR_</c> class made
    /// them look the class up. Both are gone, and this is the shape of what replaced them.
    /// </summary>
    /// <remarks>
    /// The <c>screen</c> column is not in this vocabulary and is pinned by
    /// <see cref="Every_screen_word_is_a_destination_game_navigate_accepts"/> instead: its cells are
    /// not facts about the row, they are destinations, and a destination has to be spelled the way
    /// the tool that takes it spells it.
    /// </remarks>
    [Fact]
    public void Every_word_a_cell_can_carry_is_one_lowercase_fact()
    {
        var vocabulary = new[]
        {
            GameMcpListColumns.Yes,
            GameMcpListColumns.No,
            GameMcpListColumns.Uncapped,
            GameMcpListColumns.Locked,
            GameMcpListColumns.Available,
            GameMcpListColumns.Completed,
            GameMcpListColumns.Met,
            GameMcpListColumns.Unmet,
            GameMcpListColumns.Unmodelled,
            GameMcpListColumns.Unpriced,
            GameMcpListColumns.Unevaluated,
            GameMcpListColumns.Unreadable,
            GameMcpListColumns.Empty,
            GameMcpListColumns.Manual,
            GameMcpListColumns.Unslotted,
            GameMcpListColumns.RunIdle,
            GameMcpListColumns.RunQueued,
            GameMcpListColumns.RunActive,
            GameMcpListColumns.RunPassed,
            GameMcpListColumns.RunFailed,
        }
            .Concat(BlockedCodes.Select(GameMcpListColumns.Word))
            .ToArray();

        Assert.All(vocabulary, word =>
        {
            Assert.Equal(word.ToLowerInvariant(), word);
            Assert.DoesNotContain(" ", word, StringComparison.Ordinal);
            Assert.DoesNotContain(".", word, StringComparison.Ordinal);
            Assert.DoesNotContain("ERR_", word, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(word.Length, 2, 14);
        });

        // One fact, one word: the game publishing no price is the same fact whether an
        // `affordable` column or an agromancy `add` cell is the one asking.
        Assert.Equal(GameMcpListColumns.Unpriced, GameMcpListColumns.Word("cost_unavailable"));

        // Absence is not a word in this vocabulary; it is the one mark, and no word may spell it a
        // second time. `empty` and `uncapped` are here because each states a fact — a slot exists
        // and holds nothing, a ceiling does not exist — and neither means "nothing here".
        Assert.Equal("-", GameMcpListColumns.Absent);
        Assert.DoesNotContain(GameMcpListColumns.Absent, vocabulary);
        Assert.Contains(GameMcpListColumns.Empty, vocabulary);

        // The lifecycle is three words and no more.
        Assert.Equal(
            new[] { "available", "completed", "locked" },
            new[]
            {
                GameMcpListColumns.Locked,
                GameMcpListColumns.Available,
                GameMcpListColumns.Completed,
            }.OrderBy(word => word, StringComparer.Ordinal).ToArray());

        // The run vocabulary is a fifth of the surface's words and none of it is a lifecycle word:
        // the two shared the column name `state` on challenges, and that is the collision the
        // rename resolved. `active` in particular is not `available` in disguise.
        var run = new[]
        {
            GameMcpListColumns.RunIdle, GameMcpListColumns.RunQueued,
            GameMcpListColumns.RunActive, GameMcpListColumns.RunPassed,
            GameMcpListColumns.RunFailed,
        };
        Assert.Equal(5, run.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(run.Intersect(new[]
        {
            GameMcpListColumns.Locked,
            GameMcpListColumns.Available,
            GameMcpListColumns.Completed,
        }, StringComparer.Ordinal));

        // A code with no word is a defect, not a cell to improvise in.
        Assert.Throws<InvalidOperationException>(
            () => GameMcpListColumns.Word("some_code_nobody_gave_a_word"));
    }

    /// <summary>
    /// The whole point of the vocabulary is that it fits. A word that needed a column wider than a
    /// short number would put the page back where the sentences had it.
    /// </summary>
    private static readonly string[] BlockedCodes =
    {
        "not_offered", "ambiguous_offer", "cost_unavailable", "plot_quantity_insufficient",
        "plot_action_list_full", "prerequisite_unverified", "not_active", "insufficient_bandwidth",
        "queue_not_published", "queue_reading_inconsistent",
    };

    /// <summary>
    /// Every purchasable row says exactly one of three words, and it says how far the player has
    /// come — never whether the next press would go through. A reader who sorts by <c>state</c> is
    /// asking a progression question; a reader who sorts by <c>affordable</c> is asking a wallet
    /// question; the page answers both without either word standing in for the other.
    /// </summary>
    [Fact]
    public void An_upgrades_page_says_one_lifecycle_word_per_row_beside_a_separate_price_axis()
    {
        var page = GameMcpTextPage.Render(Page(
            Upgrade(Capped, bounded: true, locked: true),
            Upgrade(Uncapped, bounded: false),
            Upgrade(Exhausted, bounded: true, exhausted: true)));
        var rows = Rows(Page(
            Upgrade(Capped, bounded: true, locked: true),
            Upgrade(Uncapped, bounded: false),
            Upgrade(Exhausted, bounded: true, exhausted: true)));

        Assert.Equal(
            new[] { "locked", "available", "completed" },
            rows.Select(row => (string?)row["state"]).ToArray());

        // The ceiling is the honest one on all three rows: a finite count, the word, a finite
        // count. Nothing on this page repeats the state as a number.
        Assert.Equal(
            new object?[] { 10, "uncapped", 10 },
            rows.Select(row => row["maximum"]!.Type == JTokenType.String
                ? (object?)(string?)row["maximum"]
                : (int?)row["maximum"]).ToArray());
        Assert.DoesNotContain("remainingLevels", page, StringComparison.Ordinal);
        Assert.DoesNotContain("already_maxed", page, StringComparison.Ordinal);

        // The word the whole model exists to keep off the surface.
        Assert.DoesNotContain("purchasable", page, StringComparison.OrdinalIgnoreCase);

        // The two axes this test is about both vary here, so both are columns. The four that do not
        // vary are named once above the rows, and between the two lines the declared set is whole.
        Assert.Contains(
            "these 3 share: queuedLevels=0, screen=Magic, nextLevelRequirements=met, affordable=unpriced",
            page,
            StringComparison.Ordinal);
        Assert.Equal("[id | name | level | state | maximum]", Bracket(page));
    }

    /// <summary>
    /// Every row says which screen shows it, including the twenty-seven Scholar upgrades and the
    /// three aspects whose lists no view in the game points at.
    /// </summary>
    /// <remarks>
    /// This is the page the column exists for. Before it, deriving the screen from the captured
    /// view routes would have left Scholar's upgrades blank — indistinguishable from the four
    /// cap-raisers that genuinely sit on no screen panel — which is why the column was held back
    /// rather than built from routes alone. The Scholar and aspect words come from pinned list
    /// identities, and they are exactly as much of a fact as the routed ones.
    /// </remarks>
    [Fact]
    public void An_upgrades_page_names_the_screen_that_shows_each_row_including_the_prefab_only_lists()
    {
        var memberships = new[]
        {
            new WorldUpgradeListMembership(Capped, KnownEntities.UpgradesMagicScreen.Uuid),
            new WorldUpgradeListMembership(Capped, KnownEntities.UpgradesAll.Uuid),
            new WorldUpgradeListMembership(Scholarly, KnownEntities.UpgradesScholarScreen.Uuid),
            new WorldUpgradeListMembership(Scholarly, KnownEntities.UpgradesAll.Uuid),
            new WorldUpgradeListMembership(Aspect, KnownEntities.UpgradesAspectsScreen.Uuid),
            new WorldUpgradeListMembership(Aspect, KnownEntities.UpgradesAll.Uuid),
            new WorldUpgradeListMembership(Uncapped, KnownEntities.UpgradesAll.Uuid),
        };
        var upgrades = new[]
        {
            Upgrade(Capped, bounded: true),
            Upgrade(Scholarly, bounded: true),
            Upgrade(Aspect, bounded: true),
            Upgrade(Uncapped, bounded: false),
            Upgrade(Exhausted, bounded: true, exhausted: true),
        };

        var rows = Rows(Page(memberships, upgrades));

        Assert.Equal(
            new[] { "Magic", "Scholar", "World/Aspects", "all", "unreadable" },
            rows.Select(row => (string?)row["screen"]).ToArray());

        // The catch-all carries every upgrade in the game, so it is never the word for a row a
        // screen panel also carries — only for the row no screen panel carries at all.
        Assert.Equal("Magic", (string?)rows[0]["screen"]);

        // Five rows, five different answers, so the column that this page exists for is on the page
        // rather than settled above it — the share line takes constants and nothing else.
        var page = GameMcpTextPage.Render(Page(memberships, upgrades));
        Assert.Contains(
            "these 5 share: queuedLevels=0, nextLevelRequirements=met, affordable=unpriced",
            page,
            StringComparison.Ordinal);
        Assert.Equal("[id | name | level | screen | state | maximum]", Bracket(page));
    }

    /// <summary>
    /// A `screen` cell answers "where do I go", so its value has to be something the tool that goes
    /// there accepts. `game_navigate` matches the live catalog label with
    /// <c>StringComparison.Ordinal</c>, so the grammar is the game's own label exactly, or
    /// <c>Screen/Subtab</c> where the destination is a subtab, or the sentinel `all` — which is
    /// deliberately not any label, so it can never be mistaken for a destination.
    /// </summary>
    [Fact]
    public void Every_screen_word_is_a_destination_game_navigate_accepts()
    {
        var labels = ViewLabels();

        Assert.Equal(
            new[]
            {
                "Magic", "Workshop", "World", "Alchemy",
                "Rituals", "Scholar", "World/Aspects", "Time",
            },
            GameMcpListColumns.Screens.Select(screen => screen.Word).ToArray());

        Assert.All(GameMcpListColumns.Screens, screen =>
        {
            var segments = screen.Word.Split('/');
            Assert.InRange(segments.Length, 1, 2);
            Assert.All(segments, segment => Assert.Contains(segment, labels));
        });

        Assert.DoesNotContain(GameMcpListColumns.ScreenAll, labels);

        // The screen vocabulary is closed and every word in it is distinct: one authored list, one
        // word, and the catch-all is not one of the eight screens.
        Assert.Equal(
            GameMcpListColumns.Screens.Length,
            GameMcpListColumns.Screens.Select(screen => screen.ListId).Distinct().Count());
        Assert.DoesNotContain(
            GameMcpListColumns.EveryUpgradeList,
            GameMcpListColumns.Screens.Select(screen => screen.ListId));
        Assert.DoesNotContain(
            GameMcpListColumns.ScreenAll,
            GameMcpListColumns.Screens.Select(screen => screen.Word));
    }

    /// <summary>Every label the authored view graph offers a navigator, from the shipped mapping.</summary>
    private static HashSet<string> ViewLabels() => File
        .ReadLines(Path.Combine(AppContext.BaseDirectory, "data", "entity-display-names.tsv"))
        .Skip(1)
        .Select(line => line.Split('\t'))
        .Where(cells => cells.Length > 3 && string.Equals(cells[1], "ViewSO", StringComparison.Ordinal))
        .Select(cells => cells[3])
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A membership publication that did not land says so on every row instead of demoting thirty
    /// upgrades to "on no screen", which is a different and equally sayable fact.
    /// </summary>
    [Fact]
    public void A_withheld_membership_publication_never_reads_as_a_screen()
    {
        var rows = Rows(Page(
            Array.Empty<WorldUpgradeListMembership>(),
            Upgrade(Capped, bounded: true),
            Upgrade(Uncapped, bounded: false)));

        Assert.Equal(
            new[] { "unreadable", "unreadable" },
            rows.Select(row => (string?)row["screen"]).ToArray());
    }

    /// <summary>
    /// Two screen panels claiming one row is not a screen fact this suite is entitled to pick a
    /// winner for. It cannot happen on the pinned build — the eight lists are disjoint — and the
    /// column is what would say so if it ever did.
    /// </summary>
    [Fact]
    public void A_row_two_screens_both_claim_is_refused_rather_than_worded()
    {
        var rows = Rows(Page(
            new[]
            {
                new WorldUpgradeListMembership(Capped, KnownEntities.UpgradesMagicScreen.Uuid),
                new WorldUpgradeListMembership(Capped, KnownEntities.UpgradesWorldScreen.Uuid),
            },
            Upgrade(Capped, bounded: true)));

        Assert.Equal("unreadable", (string?)rows.Single()["screen"]);
    }

    /// <summary>
    /// A structure has no <c>maxLevel</c> field at all, so there is no level at which one is
    /// finished. Two words is its whole lifecycle, and the third can never appear on the category
    /// however many levels a structure is taken to.
    /// </summary>
    [Fact]
    public void A_structures_page_never_reaches_the_third_lifecycle_word()
    {
        var rows = Rows(Structures(
            Structure(Capped, unlocked: false),
            Structure(Uncapped, unlocked: true, level: 2136)));

        Assert.Equal(
            new[] { "locked", "available" },
            rows.Select(row => (string?)row["state"]).ToArray());
        Assert.DoesNotContain(
            "completed",
            rows.Select(row => (string?)row["state"]).ToArray());
    }

    /// <summary>
    /// The lifecycle word belongs to every category the player can meet a locked thing in, and each
    /// of these five says it off the member the game's own row renderer asks — so a row this page
    /// calls <c>locked</c> is exactly a row the player is shown no way to click.
    /// </summary>
    /// <remarks>
    /// Before this, each of the five said its own half of the answer in its own grammar:
    /// <c>discovered</c> for a recipe and a ritual, <c>available</c> for a glyph, <c>visible</c> for
    /// a plot node, and for a challenge nothing at all — its <c>state</c> column was the run.
    /// </remarks>
    [Fact]
    public void Every_category_a_locked_thing_can_be_met_in_says_the_lifecycle_word()
    {
        var context = GameMcpTestHarness.Context(LockedAndOpen(), generation: 4243);

        Assert.All(
            new[] { "alchemy-recipes", "augment-glyphs", "rituals", "plot-nodes", "challenges" },
            category =>
            {
                var rows = GameMcpTestHarness
                    .Json(GameMcpWorldList.ListRows(context, category, 0, 50))["rows"]!
                    .Values<JObject>()
                    .Select(row => (string?)row!["state"])
                    .ToArray();
                Assert.Equal(new[] { "locked", "available" }, rows);
            });
    }

    /// <summary>
    /// Two words is the honest whole of four of them. Only a ceiling makes <c>completed</c>
    /// reachable, and a recipe's <c>maxLevel</c> is the level it has reached, a glyph's
    /// <c>CanLevel()</c> is the constant <c>true</c>, a ritual is re-run forever and a plot node's
    /// mastery has no top — so the word is absent because the fact is, not because it was withheld.
    /// </summary>
    [Fact]
    public void A_category_with_no_ceiling_never_reaches_the_third_word()
    {
        var context = GameMcpTestHarness.Context(LockedAndOpen(), generation: 4244);

        Assert.All(
            new[] { "alchemy-recipes", "augment-glyphs", "rituals", "plot-nodes" },
            category => Assert.DoesNotContain(
                "completed",
                GameMcpTestHarness
                    .Json(GameMcpWorldList.ListRows(context, category, 0, 50))["rows"]!
                    .Values<JObject>()
                    .Select(row => (string?)row!["state"])
                    .ToArray()));

        // Challenges do have one — ChallengeSO.IsMaxLevel() — so they reach all three, and it is
        // asked before availability exactly as the game's own IsAvailableToRun() asks it.
        var challenges = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(ChallengeWorld(
                Challenge(Capped, availableToRun: false, maxLevelReached: false, run: 0),
                Challenge(Uncapped, availableToRun: true, maxLevelReached: false, run: 3),
                Challenge(Exhausted, availableToRun: false, maxLevelReached: true, run: 3)),
                generation: 4245),
            "challenges", 0, 50));

        Assert.Equal(
            new[] { "locked", "available", "completed" },
            challenges["rows"]!.Values<JObject>()
                .Select(row => (string?)row!["state"]).ToArray());
    }

    /// <summary>
    /// The collision the rename exists for: a challenge whose last run passed is <c>available</c>
    /// again at the next level, so the two columns disagree on the same row and neither is the
    /// other's synonym. One name meant both facts before, on the one category that has both.
    /// </summary>
    [Fact]
    public void A_challenge_says_its_lifecycle_and_its_run_in_two_columns_that_disagree()
    {
        var response = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(ChallengeWorld(
                Challenge(Uncapped, availableToRun: true, maxLevelReached: false, run: 3),
                Challenge(Capped, availableToRun: false, maxLevelReached: false, run: 0)),
                generation: 4246),
            "challenges", 0, 50));
        var page = GameMcpTextPage.Render(response);
        var row = Rows(response)[0];

        Assert.Equal("available", (string?)row["state"]);
        Assert.Equal("passed", (string?)row["run"]);
        Assert.Equal("locked", (string?)Rows(response)[1]["state"]);
        Assert.Equal("idle", (string?)Rows(response)[1]["run"]);

        // Two columns that move apart from each other are two columns on the page: neither can be
        // settled above the rows, because neither holds one value for the whole page.
        Assert.Contains("| available | passed |", page, StringComparison.Ordinal);
        Assert.Contains("| locked | idle |", page, StringComparison.Ordinal);
        Assert.Equal("[id | name | state | run | level]", Bracket(page));
    }

    /// <summary>
    /// A recipe behind a visibility prerequisite has a lock this suite cannot read without making
    /// the game latch a prerequisite container mid-collection, so the cell says the read failed
    /// rather than calling it locked. No authored recipe on the pinned build is on that branch; the
    /// word exists so that a build where one was would say so.
    /// </summary>
    [Fact]
    public void A_recipe_whose_gate_this_suite_cannot_read_says_so_instead_of_guessing()
    {
        var rows = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(
                AlchemyWorld(
                    Recipe(Capped, discovered: true, gate: 1),
                    Recipe(Uncapped, discovered: false, gate: 1)),
                generation: 4247),
            "alchemy-recipes", 0, 50))["rows"]!.Values<JObject>().ToArray();

        // Both branches of `discovered` answer the same way, because `discovered` is not what the
        // game reads for this recipe at all.
        Assert.Equal(
            new[] { "unreadable", "unreadable" },
            rows.Select(row => (string?)row!["state"]).ToArray());
    }

    private static JObject Structures(params WorldStructure[] structures)
    {
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(Capped, "StructureSO", "Shut Hall", "shutHall"),
                new EntityIdentityName(Uncapped, "StructureSO", "Open Hall", "openHall"),
            }),
            Structures = PublicationTable<WorldStructure>.Create(structures),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "attributes", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
            }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        return GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(world), "attributes", 0, 50));
    }

    private static WorldStructure Structure(Guid id, bool unlocked, int level = 0)
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
            new BigDouble(level),
            hasWorkInFlight: false,
            new BigDouble(level),
            developmentProgress: 0);
    }

    /// <summary>
    /// One shut row and one open row in each of the five categories the player can meet a locked
    /// thing in, each shut through the member that category's own screen renders on.
    /// </summary>
    private static GameWorldState LockedAndOpen() => new()
    {
        AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(new[]
        {
            Recipe(Capped, discovered: false),
            Recipe(Uncapped, discovered: true),
        }),
        AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new[]
        {
            Glyph(Capped, learned: false),
            Glyph(Uncapped, learned: true),
        }),
        Rituals = PublicationTable<WorldRitual>.Create(new[]
        {
            Ritual(Capped, discovered: false),
            Ritual(Uncapped, discovered: true),
        }),
        PlotNodes = PublicationTable<WorldPlotNode>.Create(new[]
        {
            Plot(Capped, visible: false),
            Plot(Uncapped, visible: true),
        }),
        Challenges = PublicationTable<WorldChallenge>.Create(new[]
        {
            Challenge(Capped, availableToRun: false, maxLevelReached: false, run: 0),
            Challenge(Uncapped, availableToRun: true, maxLevelReached: false, run: 0),
        }),
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(Reports(
            "alchemy-recipes", "augment-glyphs", "rituals", "plot-nodes", "challenges")),
        CollectedAtEpoch = 25,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
    };

    private static GameWorldState AlchemyWorld(params WorldAlchemyRecipe[] recipes) => new()
    {
        AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(recipes),
        CollectionCategories =
            PublicationTable<WorldCollectionCategoryStatus>.Create(Reports("alchemy-recipes")),
        CollectedAtEpoch = 25,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
    };

    private static GameWorldState ChallengeWorld(params WorldChallenge[] challenges) => new()
    {
        Challenges = PublicationTable<WorldChallenge>.Create(challenges),
        CollectionCategories =
            PublicationTable<WorldCollectionCategoryStatus>.Create(Reports("challenges")),
        CollectedAtEpoch = 25,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
    };

    private static WorldCollectionCategoryStatus[] Reports(params string[] categories) => categories
        .Select(name => new WorldCollectionCategoryStatus(
            name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
        .ToArray();

    private static WorldAlchemyRecipe Recipe(
        Guid id,
        bool discovered,
        int gate = WorldAlchemyRecipe.DiscoverGate) => new(
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
        requiredExperience: BigDouble.One,
        discovery: default,
        currentLevel: 1,
        visibilityGate: gate);

    /// <summary>
    /// <c>learned</c> is the binder's name for <c>GlyphSO.IsAvailable()</c>, which is the whole of
    /// what the picker renders a glyph on.
    /// </summary>
    private static WorldGlyph Glyph(Guid id, bool learned) => new(
        id,
        level: 0,
        freeLevels: 0,
        discoveryRarityLevel: 0,
        learned,
        discoverable: true,
        discoveryRequired: true,
        augmentsSpells: false,
        requiresDuration: false,
        requiresToggleable: false,
        masteryReqCount: 0,
        freeUsages: BigDouble.Zero,
        freeLoadoutUsages: BigDouble.Zero,
        maxUsages: BigDouble.Zero);

    private static WorldRitual Ritual(Guid id, bool discovered)
    {
        var modifiers = default(RawRitualModifiers);
        return new WorldRitual(
            id,
            discovered,
            inBattle: false,
            activeInstances: 0,
            reachedLevel: 0,
            lastReachedLevel: 0,
            selectedLevel: 1,
            wavesCompleted: 0,
            discoveryRarityLevel: 0,
            critLevel: 0,
            echoLevel: 0,
            chainLevel: 0,
            durationRewardBlocks: 0,
            battleTotalWeight: BigDouble.Zero,
            in modifiers,
            hideEndScreenResults: false,
            isDiscoverRequired: false,
            forceLevel: false,
            forceLevelValue: 0,
            baseWaves: 0,
            maxWaves: 0,
            requiredWaves: 0,
            baseWeight: 0,
            minimumEffectLevel: 0,
            failedRun: false);
    }

    private static WorldPlotNode Plot(Guid id, bool visible)
    {
        var reading = new RawPlotNodeSample(
            id, visible, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            masteryLevel: 0, noMastery: false, noSizeDisplay: false, useVisibilityPrereq: true,
            hasErraticGrowth: false, debugMode: false, erraticQuantity: 0,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            lastQuantity: 0, idleQuantity: 0, totalQuantity: 0);
        return new WorldPlotNode(in reading, remainingQuantity: 0, remainingTotalQuantity: 0);
    }

    /// <summary>
    /// <c>run</c> is <c>ChallengeSO.state</c> — 0 idle, 3 passed — and it moves without the
    /// lifecycle moving, which is the whole reason the two are separate columns.
    /// </summary>
    private static WorldChallenge Challenge(
        Guid id,
        bool availableToRun,
        bool maxLevelReached,
        int run) => new(
        id,
        level: 0,
        state: run,
        seen: true,
        rewardQueued: false,
        maxLevel: 5,
        weight: 0,
        difficulty: 0,
        baseReward: 0,
        availableToRun,
        completedOnce: run == 3,
        maxLevelReached);
}
