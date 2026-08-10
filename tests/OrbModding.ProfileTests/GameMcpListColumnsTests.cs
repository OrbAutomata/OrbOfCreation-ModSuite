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

    /// <summary>
    /// The round-8 defect: reading the upgrades page before a prestige listed
    /// <c>maxLevel</c>/<c>remainingLevels</c>, and reading it after — when every upgrade was
    /// uncapped — dropped both columns, so the one page that most needed to say caps exist was the
    /// page that said nothing about them.
    /// </summary>
    [Fact]
    public void An_all_uncapped_upgrades_page_shows_the_columns_a_capped_page_shows()
    {
        var mixed = Columns(Page(Upgrade(Capped, bounded: true), Upgrade(Uncapped, bounded: false)));
        var allUncapped = Columns(Page(
            Upgrade(Uncapped, bounded: false),
            Upgrade(Capped, bounded: false)));

        Assert.Equal(mixed, allUncapped);
        Assert.Contains("maxLevel", mixed);
        Assert.Contains("remainingLevels", mixed);
        Assert.Contains("affordable", mixed);
    }

    /// <summary>
    /// The ceiling a page has none of is spelled, not omitted and not invented: <c>0</c> would read
    /// as a cap of zero and as nothing left to buy, which is the opposite of what it means.
    /// </summary>
    [Fact]
    public void An_upgrade_with_no_ceiling_says_so_instead_of_publishing_a_number()
    {
        var row = Rows(Page(Upgrade(Uncapped, bounded: false))).Single();

        Assert.Equal("uncapped", (string?)row["maxLevel"]);
        Assert.Equal("uncapped", (string?)row["remainingLevels"]);
    }

    /// <summary>
    /// The two reasons a row carries no affordability are different facts, and the cell says which.
    /// </summary>
    [Fact]
    public void A_row_with_no_price_names_which_kind_of_no_price_it_is()
    {
        var rows = Rows(Page(
            Upgrade(Exhausted, bounded: true, exhausted: true),
            Upgrade(Uncapped, bounded: false)));

        Assert.Equal("already_maxed", (string?)rows[0]["affordable"]);
        Assert.Equal("unpriced", (string?)rows[1]["affordable"]);
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
            "these 6 share: level=3, queuedLevels=0, maxLevel=uncapped, " +
            "remainingLevels=uncapped, affordable=unpriced, available=yes",
            page,
            StringComparison.Ordinal);
        Assert.Equal(
            "[id | name | level | queuedLevels | maxLevel | remainingLevels | affordable | " +
            "available]",
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
            // wrong with nothing on it to say so. The verdict pair is the one exception the
            // declaration itself makes: a refusal explains itself in the grammar every surface
            // shares, and no row that answers yes carries it.
            var next = 0;
            foreach (var column in shown)
            {
                if (column is "reason" or "reasonCode") continue;
                var found = Array.IndexOf(declared, column, next);
                Assert.True(
                    found >= 0,
                    category + " renders [" + string.Join(" | ", shown) + "] against declared [" +
                    string.Join(" | ", declared) + "]");
                next = found + 1;
            }
        });
    }

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

    private static JObject AlchemyTypes(params Guid[] selectedLevels)
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
        var world = new GameWorldState
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(734));
        return GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(publisher.ReadLatest()), "alchemy-types", 0, 50));
    }

    private static JObject Page(params WorldUpgrade[] upgrades)
    {
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(Capped, "UpgradeSO", "Capped Rite", "cappedRite"),
                new EntityIdentityName(Uncapped, "UpgradeSO", "Endless Rite", "endlessRite"),
                new EntityIdentityName(Exhausted, "UpgradeSO", "Spent Rite", "spentRite"),
            }),
            Upgrades = PublicationTable<WorldUpgrade>.Create(upgrades),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "upgrades", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
            }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(733));
        return GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(publisher.ReadLatest()), "upgrades", 0, 50));
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

    private static WorldUpgrade Upgrade(Guid id, bool bounded, bool exhausted = false)
    {
        var reading = new RawUpgradeSample(
            id,
            level: exhausted ? 10 : 3,
            maxLevel: bounded ? 10 : -1,
            available: !exhausted,
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
