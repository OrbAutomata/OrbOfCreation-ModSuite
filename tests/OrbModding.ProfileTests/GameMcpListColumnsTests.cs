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
/// A category's list columns are declared and total, so the header a page shows is a fact about the
/// category rather than about which rows that page happened to hold.
/// </summary>
public sealed class GameMcpListColumnsTests
{
    private static readonly Guid Capped = Guid.Parse("41111111-1111-4111-8111-111111111111");
    private static readonly Guid Uncapped = Guid.Parse("42222222-2222-4222-8222-222222222222");
    private static readonly Guid Exhausted = Guid.Parse("43333333-3333-4333-8333-333333333333");
    private static readonly Guid Scholarly = Guid.Parse("44444444-4444-4444-8444-444444444445");
    private static readonly Guid Aspect = Guid.Parse("45555555-5555-4555-8555-555555555555");

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
    /// A page whose every row says the same word names the word once beside the count as well as in
    /// the rows — and names the columns either way, because a reader who has only ever seen this
    /// page still has to be able to learn from it that ceilings exist.
    /// </summary>
    [Fact]
    public void A_page_of_uncapped_upgrades_says_uncapped_once_and_still_shows_both_ceiling_columns()
    {
        var page = GameMcpTextPage.Render(Page(
            Upgrade(Uncapped, bounded: false),
            Upgrade(Capped, bounded: false),
            Upgrade(Exhausted, bounded: false),
            Upgrade(Uncapped, bounded: false),
            Upgrade(Capped, bounded: false),
            Upgrade(Exhausted, bounded: false)));

        Assert.Contains(
            "these 6 share: level=3, queuedLevels=0, screen=magic, state=available, " +
            "maximum=uncapped, requirements=met, affordable=unpriced",
            page,
            StringComparison.Ordinal);
        Assert.Equal(
            "[id | name | level | queuedLevels | screen | state | maximum | requirements | " +
            "affordable]",
            Bracket(page));
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

        Assert.Equal("unset", (string?)rows[1]["selectedLevel"]);
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
            GameMcpWorldQuery.RegisteredCategoryNames(), StringComparer.Ordinal);

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

        Assert.All(GameMcpWorldQuery.RegisteredCategoryNames(), category =>
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

    /// <summary>
    /// One vocabulary, and every word in it readable at a glance. A cell that carried a sentence
    /// made the reader parse prose out of a column; a cell that carried an <c>ERR_</c> class made
    /// them look the class up. Both are gone, and this is the shape of what replaced them.
    /// </summary>
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
            GameMcpListColumns.Unset,
            GameMcpListColumns.ScreenAll,
        }
            .Concat(GameMcpListColumns.Screens.Select(screen => screen.Word))
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

        // The lifecycle is three words and no more, and none of them is `purchasable` — a word
        // that reads as "you can buy this now" while naming a state that says nothing about price.
        Assert.Equal(
            new[] { "available", "completed", "locked" },
            new[]
            {
                GameMcpListColumns.Locked,
                GameMcpListColumns.Available,
                GameMcpListColumns.Completed,
            }.OrderBy(word => word, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("purchasable", vocabulary);

        // The screen vocabulary is closed and every word in it is distinct: one authored list, one
        // word, and the catch-all is not one of the eight screens.
        Assert.Equal(8, GameMcpListColumns.Screens.Length);
        Assert.Equal(
            new[]
            {
                "alchemy", "aspects", "magic", "rituals", "scholar", "time", "workshop", "world",
            },
            GameMcpListColumns.Screens
                .Select(screen => screen.Word)
                .OrderBy(word => word, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            GameMcpListColumns.Screens.Length,
            GameMcpListColumns.Screens.Select(screen => screen.ListId).Distinct().Count());
        Assert.DoesNotContain(
            GameMcpListColumns.EveryUpgradeList,
            GameMcpListColumns.Screens.Select(screen => screen.ListId));
        Assert.DoesNotContain(
            GameMcpListColumns.ScreenAll,
            GameMcpListColumns.Screens.Select(screen => screen.Word));

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
    };

    private static GameWorldState OneRowOfEach()
    {
        // A public category's availability also rests on the helper collections its rows are
        // joined from, so every report a category can require is present and clean here.
        var collected = GameMcpWorldQuery.RegisteredCategoryNames()
            .Concat(new[]
            {
                "structure-costs", "upgrade-costs", "crafting-recipe-state", "crafting-decisions",
                "loadouts", "concept-instances", "ordinary-alchemy-loadout", "action-queues",
                "plot-actions", "plot-action-instances", "harvest-resources",
                "harvest-element-controls", "harvest-action-controls", "harvest-lifecycle-costs",
                "crafting-stations", "crafting-station-options", "crafting-station-drains",
            })
            .Distinct(StringComparer.Ordinal)
            .Select(name => new WorldCollectionCategoryStatus(
                name, WorldCategoryOutcome.Collected, 1, 0, string.Empty))
            .ToArray();
        return new GameWorldState
        {
            Structures = PublicationTable<WorldStructure>.Create(new WorldStructure[1]),
            Upgrades = PublicationTable<WorldUpgrade>.Create(new WorldUpgrade[1]),
            Equipment = PublicationTable<WorldEquipment>.Create(new WorldEquipment[1]),
            Rituals = PublicationTable<WorldRitual>.Create(new WorldRitual[1]),
            Research = PublicationTable<WorldResearch>.Create(new WorldResearch[1]),
            ResourceTypes = PublicationTable<WorldResourceType>.Create(new WorldResourceType[1]),
            Glyphs = PublicationTable<WorldGlyph>.Create(new WorldGlyph[1]),
            PlotNodes = PublicationTable<WorldPlotNode>.Create(new WorldPlotNode[1]),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new WorldPurchaseCost[1]),
            Challenges = PublicationTable<WorldChallenge>.Create(new WorldChallenge[1]),
            CraftingRecipes =
                PublicationTable<WorldCraftingRecipe>.Create(new WorldCraftingRecipe[1]),
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new WorldDiscoveryTree[1]),
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
                PublicationTable<WorldCollectionCategoryStatus>.Create(collected),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

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
        Assert.Equal(
            "[id | name | level | queuedLevels | screen | state | maximum | requirements | " +
            "affordable]",
            Bracket(page));
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
            new[] { "magic", "scholar", "aspects", "all", "unreadable" },
            rows.Select(row => (string?)row["screen"]).ToArray());

        // The catch-all carries every upgrade in the game, so it is never the word for a row a
        // screen panel also carries — only for the row no screen panel carries at all.
        Assert.Equal("magic", (string?)rows[0]["screen"]);
        Assert.Equal(
            "[id | name | level | queuedLevels | screen | state | maximum | requirements | " +
            "affordable]",
            Bracket(GameMcpTextPage.Render(Page(memberships, upgrades))));
    }

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
            () => GameMcpListColumns.Verify("structures", partial));

        Assert.Contains("affordable", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_that_invents_a_column_is_refused_rather_than_published()
    {
        var extra = new GameMcpObjectBuilder
        {
            ["entityId"] = Guid.Empty.ToString("D"),
            ["level"] = 1,
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
    /// Absence says one thing per surface. In a table it is a word, because the header promised a
    /// column and a page that dropped it would be a header about its rows. Outside a table there is
    /// no header to keep and no siblings to line up with, so absence is silence — the same answer a
    /// spell with nothing to toggle already gives, and the same one the wire normalizer already
    /// gives for the zero identity on every projection that declares no paths.
    /// </summary>
    [Fact]
    public void A_member_the_row_carries_nothing_under_is_a_word_in_a_table_and_silence_outside_one()
    {
        var identity = Guid.Parse("45000000-0000-4000-8000-000000000000");
        var context = GameMcpTestHarness.Context(
            AlchemyTypesWorld(Guid.Empty), generation: 736);

        var row = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            context, "alchemy-types", 0, 50))["rows"]!.Values<JObject>());
        Assert.Equal("unset", (string?)row!["selectedLevel"]);

        var detail = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
            context, "alchemy-types", new[] { identity.ToString("D") }));
        var got = Assert.Single(detail["results"]!.Values<JObject>())!["row"]!;

        Assert.Null(got["selectedLevel"]);
        Assert.DoesNotContain(
            "unset",
            GameMcpTextPage.Render(detail),
            StringComparison.Ordinal);
    }

    private static GameWorldState AlchemyTypesWorld(params Guid[] selectedLevels)
    {
        var types = new WorldAlchemyType[selectedLevels.Length];
        for (var index = 0; index < selectedLevels.Length; index++)
        {
            types[index] = new WorldAlchemyType(
                Guid.Parse("4500000" + index + "-0000-4000-8000-000000000000"),
                selectedLevels[index],
                maxUsageByMastery: false,
                level: BigDouble.Zero,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(AlchemyTypesWorld(selectedLevels), new WorldGeneration(734));
        return GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(publisher.ReadLatest()), "alchemy-types", 0, 50));
    }

    /// <summary>
    /// The authored list each fixture upgrade sits on. All three share one screen so the pages that
    /// pin a share line still hoist every column; the screen vocabulary itself is pinned by the page
    /// that gives each row its own list.
    /// </summary>
    private static WorldUpgradeListMembership[] OneScreen() => new[]
    {
        new WorldUpgradeListMembership(Capped, KnownEntities.UpgradesMagicScreen.Uuid),
        new WorldUpgradeListMembership(Uncapped, KnownEntities.UpgradesMagicScreen.Uuid),
        new WorldUpgradeListMembership(Exhausted, KnownEntities.UpgradesMagicScreen.Uuid),
    };

    /// <summary>
    /// The membership table exactly as the world publishes it, through the production deriver, so a
    /// fixture cannot hand the surface an ordering the collector would never produce.
    /// </summary>
    private static PublicationTable<WorldUpgradeListMembership> Published(
        WorldUpgradeListMembership[] memberships)
    {
        var buffer = new WorldRelationBuffer<WorldUpgradeListMembership>();
        foreach (var membership in memberships) buffer.Append(membership);
        return WorldUpgradeListMembershipDeriver.Build(buffer);
    }

    private static GameWorldState UpgradesWorld(params WorldUpgrade[] upgrades) =>
        UpgradesWorld(OneScreen(), upgrades);

    private static GameWorldState UpgradesWorld(
        WorldUpgradeListMembership[] memberships,
        WorldUpgrade[] upgrades) =>
        new()
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(Capped, "UpgradeSO", "Capped Rite", "cappedRite"),
                new EntityIdentityName(Uncapped, "UpgradeSO", "Endless Rite", "endlessRite"),
                new EntityIdentityName(Exhausted, "UpgradeSO", "Spent Rite", "spentRite"),
                new EntityIdentityName(Scholarly, "UpgradeSO", "Scribism Scrolls II", "scribeScroll2"),
                new EntityIdentityName(Aspect, "UpgradeSO", "Aspect: Rituals", "aspectRituals"),
            }),
            Upgrades = PublicationTable<WorldUpgrade>.Create(upgrades),
            UpgradeListMemberships = Published(memberships),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "upgrades", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
            }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

    private static JObject Page(params WorldUpgrade[] upgrades) =>
        Page(OneScreen(), upgrades);

    private static JObject Page(
        WorldUpgradeListMembership[] memberships,
        params WorldUpgrade[] upgrades)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(UpgradesWorld(memberships, upgrades), new WorldGeneration(733));
        return GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(publisher.ReadLatest()), "upgrades", 0, 50));
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
                    "structures", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
            }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(734));
        return GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(publisher.ReadLatest()), "structures", 0, 50));
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

    private static JObject[] Rows(JObject page) =>
        page["rows"]!.Values<JObject>().Select(row => row!).ToArray();

    /// <summary>Every column the page's rows carry, which is every column it declares.</summary>
    private static IReadOnlyList<string> Columns(JObject page)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows(page))
            foreach (var property in row.Properties())
                names.Add(property.Name);
        return names.ToArray();
    }

    /// <summary>The rendered column set: its own line, so a reader finds it the same way twice.</summary>
    private static string Bracket(string page) => page
        .Split('\n')
        .Single(line => line.StartsWith("[", StringComparison.Ordinal));

    private static WorldUpgrade Upgrade(
        Guid id,
        bool bounded,
        bool exhausted = false,
        bool locked = false)
    {
        var reading = new RawUpgradeSample(
            id,
            level: exhausted ? 10 : 3,
            maxLevel: bounded ? 10 : -1,

            // IsAvailable() is the game's own `!IsMaxLevel() && prerequisites.Check()`, so it is
            // false for both of the states that are not `available`, for two different reasons.
            available: !exhausted && !locked,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1d,
            cachedCostLevel: 0);
        return new WorldUpgrade(
            in reading,
            isBounded: bounded,
            isExhausted: exhausted,
            remainingLevels: bounded ? (exhausted ? 0 : 7) : 0,
            committedLevel: exhausted ? 10 : 3,
            isDeveloping: false,
            developmentProgress: 0d);
    }
}
