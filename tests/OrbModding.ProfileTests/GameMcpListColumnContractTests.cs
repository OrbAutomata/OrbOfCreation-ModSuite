using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
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
    /// happens to read. The rows carry the game's zero values on purpose: that is the state in
    /// which a conditional field is most likely to disappear.
    /// </summary>
    [Fact]
    public void Every_declaring_category_builds_rows_its_declaration_accepts()
    {
        var context = GameMcpTestHarness.Context(OneRowOfEach(), generation: 4242);

        Assert.All(GameMcpListColumns.Categories, category =>
        {
            var page = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ListRows(context, category, 0, 50));
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

        Assert.All(GameMcpWorldCategories.RegisteredCategoryNames(), category =>
        {
            var page = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ListRows(context, category, 0, 50));
            if (page["rows"] is not JArray rows || rows.Count == 0) return;
            var empty = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ListRows(context, category, rows.Count, 50));
            var emptyPage = GameMcpTextPage.Render(empty);

            Assert.Empty(empty["rows"]!.Values<JObject>());
            Assert.Equal(
                "rows 0/" + rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                emptyPage.Split('\n')[0]);
            var declared = Bracket(emptyPage)
                .Trim('[', ']')
                .Split(" | ", StringSplitOptions.None);
            var shown = Bracket(GameMcpTextPage.Render(page))
                .Trim('[', ']')
                .Split(" | ", StringSplitOptions.None);

            // The fixture's rows carry the game's zero identity, which the wire drops rather than
            // handing back an address nothing answers to, so a page here can show fewer columns
            // than it declares. It may never show one the declaration does not name, and never in
            // another order — either would be a header the empty page of this category would get
            // wrong with nothing on it to say so. The verdict pair used to be exempt here, which
            // is what let a page holding one blocked row be wider than the same page without it.
            var next = 0;
            foreach (var column in shown)
            {
                var found = Array.IndexOf(declared, column, next);
                Assert.True(
                    found >= 0,
                    category + " renders [" + string.Join(" | ", shown) + "] against declared [" +
                    string.Join(" | ", declared) + "]");
                next = found + 1;
            }
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
        return new GameWorldState
        {
            Structures = PublicationTable<WorldStructure>.Create(new WorldStructure[1]),
            Upgrades = PublicationTable<WorldUpgrade>.Create(new WorldUpgrade[1]),
            Equipment = PublicationTable<WorldEquipment>.Create(new WorldEquipment[1]),
            Rituals = PublicationTable<WorldRitual>.Create(new WorldRitual[1]),
            Research = PublicationTable<WorldResearch>.Create(new WorldResearch[1]),
            ResourceTypes = PublicationTable<WorldResourceType>.Create(new WorldResourceType[1]),
            EquipmentTypes = PublicationTable<WorldEquipmentType>.Create(new WorldEquipmentType[1]),
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new WorldGlyph[1]),
            // `alchemy-recipes` and `concepts` are one native class split by the recipe's own
            // core alchemy type, so one row of each category is two rows here. Everything but
            // that type stays at the game's zero values, like every other row in this fixture.
            AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(new[]
            {
                ZeroRecipe(OrdinaryRecipe, KnownEntities.Brewing.Uuid),
                ZeroRecipe(ConceptRecipe, KnownEntities.Reductive.Uuid),
            }.OrderBy(recipe => recipe.EntityId).ToArray()),
            PlotNodes = PublicationTable<WorldPlotNode>.Create(new WorldPlotNode[1]),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new WorldPurchaseCost[1]),
            Challenges = PublicationTable<WorldChallenge>.Create(new WorldChallenge[1]),
            CraftingRecipes =
                PublicationTable<WorldCraftingRecipe>.Create(new WorldCraftingRecipe[1]),
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new WorldDiscoveryTree[1]),
            RecipeBooks = PublicationTable<WorldRecipeBook>.Create(new WorldRecipeBook[1]),
            Resources = PublicationTable<WorldResource>.Create(new WorldResource[1]),
            PlayerLoadouts = PublicationTable<WorldPlayerLoadout>.Create(new WorldPlayerLoadout[1]),
            SnapshotLoadouts =
                PublicationTable<WorldSnapshotLoadout>.Create(new WorldSnapshotLoadout[1]),
            SnapshotSlots = PublicationTable<WorldSnapshotSlot>.Create(new WorldSnapshotSlot[1]),
            SnapshotEntries =
                PublicationTable<WorldSnapshotEntry>.Create(new WorldSnapshotEntry[1]),
            CraftingQueueEntries =
                PublicationTable<WorldCraftingQueueEntry>.Create(new WorldCraftingQueueEntry[1]),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new WorldSpellSlot[1]),
            SpellCosts = PublicationTable<WorldSpellCost>.Create(new WorldSpellCost[1]),
            AlchemyInstances =
                PublicationTable<WorldAlchemyInstance>.Create(new WorldAlchemyInstance[1]),
            AlchemyLoadout = PublicationTable<WorldAlchemyLoadoutDecision>.Create(
                new WorldAlchemyLoadoutDecision[1]),
            ActionQueueSlots =
                PublicationTable<WorldActionQueueSlot>.Create(new WorldActionQueueSlot[1]),
            PlotActions = PublicationTable<WorldPlotAction>.Create(new WorldPlotAction[1]),
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
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(
                    GameMcpTestHarness.AllCategoriesCollected()),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

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

        var whole = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            context, "upgrades", 0, GameMcpWorldQuery.DefaultLimit, limitFromCaller: false));

        Assert.Equal(3, Rows(whole).Length);
        Assert.Equal(3, (int)whole["total"]!);
        Assert.Null(whole["nextOffset"]);
        Assert.Equal("rows 3/3", GameMcpTextPage.Render(whole).Split('\n')[0]);

        // A caller that named a smaller page gets the page it named, and is told where to resume.
        // The rule raises no page above what was asked for; it only stops lowering one below the
        // whole of a category nobody asked to have cut up.
        var asked = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
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

        var row = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            context, "alchemy-types", 0, 50))["rows"]!.Values<JObject>());
        Assert.Equal("-", (string?)row!["selectedLevel"]);

        var detail = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
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
        (JObject)Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
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
        return GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
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
