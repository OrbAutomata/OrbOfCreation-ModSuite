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
    /// A page whose every row says the same word says it once, in the header — which is what keeps a
    /// total column set from costing a reader anything.
    /// </summary>
    [Fact]
    public void A_page_of_uncapped_upgrades_says_uncapped_once()
    {
        var header = Header(Page(
            Upgrade(Uncapped, bounded: false),
            Upgrade(Capped, bounded: false)));

        Assert.Contains("maxLevel=uncapped", header, StringComparison.Ordinal);
        Assert.Contains("remainingLevels=uncapped", header, StringComparison.Ordinal);
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

    /// <summary>
    /// Every column the page shows: the ones it prints per row, plus the ones it hoisted into the
    /// header because they held one value throughout. Hoisting is a rendering choice, so a column
    /// that moved into the header has not left the page.
    /// </summary>
    private static IReadOnlyList<string> Columns(JObject page)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows(page))
            foreach (var property in row.Properties())
                names.Add(property.Name);
        return names.ToArray();
    }

    private static string Header(JObject page) => GameMcpTextPage.Render(page)
        .Split('\n')
        .Single(line => line.Contains("rows ", StringComparison.Ordinal));

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
