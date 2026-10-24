using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpUpgradePageTestSupport;

namespace OrbModding.ProfileTests;

public sealed class GameMcpUpgradeLockTests
{
    /// <summary>
    /// An upgrade whose own prerequisites hold is still not offered while the screen it is drawn on
    /// is locked. Aspect: Workshop has no prerequisites at all, and a live run with no World >
    /// Aspects pedestal read it as available and buyable on the page and in the available search,
    /// then had the purchase refused.
    /// </summary>
    [Fact]
    public void An_upgrade_on_a_locked_screen_is_locked_on_the_page_the_search_and_its_verdicts()
    {
        var memberships = new[]
        {
            new WorldUpgradeListMembership(Capped, KnownEntities.UpgradesMagicScreen.Uuid),
            new WorldUpgradeListMembership(Aspect, KnownEntities.UpgradesAspectsScreen.Uuid),
        };
        var upgrades = new[] { Upgrade(Capped, bounded: true), Upgrade(Aspect, bounded: true) };
        var context = GameMcpTestHarness.Context(UpgradesWorld(
            memberships, upgrades, KnownEntities.WorldAspects.Uuid));

        Assert.Equal(
            new[] { "available", "locked" },
            Rows(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(context, "upgrades", 0, 50)))
                .Select(row => (string?)row["state"]).ToArray());
        Assert.Equal(
            new[] { GameMcpTestHarness.Handle(Capped) },
            GameMcpTestHarness
                .Json(GameMcpWorldSearch.Search(
                    context, string.Empty, 0, 50, "upgrades", stateFilter: "available"))["rows"]!
                .Values<JObject>().Select(row => (string?)row!["uuid"]).ToArray());

        var predicates = GameMcpTestHarness.Detail(context, Aspect)["predicates"]!;
        Assert.All(new[] { "available", "canPurchase" }, name =>
        {
            Assert.False((bool)predicates[name]!["available"]!);
            Assert.Equal(
                "World > Aspects is not unlocked yet, so the game draws no row for this.",
                (string?)predicates[name]!["reason"]);
        });

        var opened = GameMcpTestHarness.Context(UpgradesWorld(memberships, upgrades));
        Assert.True(
            (bool)GameMcpTestHarness.Detail(opened, Aspect)["predicates"]!["available"]!["available"]!);
    }

    /// <summary>
    /// A row whose screen membership was withheld has no screen anyone asked about, so it is not
    /// offered: the page says the suite could not read it, the available search leaves it out, and
    /// the detail read names the gap instead of inviting a purchase the game may not draw.
    /// </summary>
    [Fact]
    public void An_upgrade_whose_screen_cannot_be_read_is_never_offered()
    {
        var context = GameMcpTestHarness.Context(UpgradesWorld(
            Array.Empty<WorldUpgradeListMembership>(), new[] { Upgrade(Capped, bounded: true) }));

        Assert.Equal(
            "unreadable",
            (string?)Rows(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(context, "upgrades", 0, 50)))
                .Single()["state"]);
        Assert.Empty(GameMcpTestHarness
            .Json(GameMcpWorldSearch.Search(
                context, string.Empty, 0, 50, "upgrades", stateFilter: "available"))["rows"]!);
        var predicates = GameMcpTestHarness.Detail(context, Capped)["predicates"]!;
        Assert.All(new[] { "available", "canPurchase" }, name =>
        {
            Assert.False((bool)predicates[name]!["available"]!);
            Assert.Equal(
                "Which screen the game draws this on could not be told, so whether that screen is " +
                "unlocked is unknown.",
                (string?)predicates[name]!["reason"]);
        });
    }

    /// <summary>
    /// A finished upgrade is finished, not shut. <c>UpgradeSO.IsAvailable()</c> is the game's own
    /// <c>!IsMaxLevel() &amp;&amp; prerequisites.Check()</c>, so it goes false the moment an upgrade
    /// maxes — and <c>available</c> answered that with the locked sentence, "the game keeps this
    /// locked, and says nothing about what would unlock it", for an upgrade the player had just
    /// completed. <c>canPurchase</c> beside it already knew the difference.
    /// </summary>
    [Fact]
    public void A_finished_upgrade_says_it_is_finished_rather_than_locked()
    {
        var context = GameMcpTestHarness.Context(UpgradesWorld(
            Upgrade(Capped, bounded: true),
            Upgrade(Uncapped, bounded: false),
            Upgrade(Exhausted, bounded: true, exhausted: true)));

        var done = GameMcpTestHarness.Detail(context, Exhausted)["predicates"]!;
        Assert.False((bool)done["available"]!["available"]!);
        Assert.Equal(
            "This is already at its maximum level.",
            (string?)done["available"]!["reason"]);
        Assert.Equal(
            (string?)done["canPurchase"]!["reason"],
            (string?)done["available"]!["reason"]);

        // A locked upgrade still says it is locked, so the two states stay told apart.
        var shut = GameMcpTestHarness.Detail(context, Capped)["predicates"]!;
        Assert.True((bool)shut["available"]!["available"]!);
    }

    /// <summary>
    /// The purchase verdict is the AND the game's own button is enabled on, and it never read the
    /// requirement verdict printed two lines below it in the same block: a row whose screen said
    /// "Has Requirements" answered <c>canPurchase: yes</c> while its own requirements were unmet.
    /// </summary>
    [Fact]
    public void The_purchase_verdict_reads_the_requirement_verdict_beside_it()
    {
        var noScaling = default(WorldRequirementScaling);
        var world = UpgradesWorld(
            Upgrade(Capped, bounded: true),
            Upgrade(Uncapped, bounded: false),
            Upgrade(Exhausted, bounded: true, exhausted: true)) with
        {
            EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(new[]
            {
                // Capped needs Uncapped at level 9 or better, and Uncapped holds 3.
                new WorldEntityRequirement(
                    Capped,
                    WorldRequirementOwnerKind.Upgrade,
                    ordinal: 0,
                    WorldRequirementConditionKind.Upgrade,
                    "UpgradeRequirement",
                    Uncapped,
                    reqType: 2,
                    baseValue: 9d,
                    in noScaling,
                    in noScaling),
            }),
        };

        var context = GameMcpTestHarness.Context(world);
        var blocked = GameMcpTestHarness.Detail(context, Capped);

        Assert.Equal("Unmet", (string?)blocked["requirements"]!["suiteVerdict"]);
        Assert.False((bool)blocked["predicates"]!["canPurchase"]!["available"]!);
        Assert.Equal(
            "This does not meet its level requirements yet.",
            (string?)blocked["predicates"]!["canPurchase"]!["reason"]);

        // The page says which list it evaluated, so `state=available` beside a requirement verdict
        // is two different authored lists rather than a contradiction.
        var row = GameMcpTestHarness
            .Json(GameMcpWorldList.ListRows(context, "upgrades", 0, 50))["rows"]!
            .Values<JObject>()
            .Single(candidate =>
                (string?)candidate!["uuid"] == GameMcpTestHarness.Handle(Capped))!;
        Assert.Equal("available", (string?)row["state"]);
        Assert.Equal("unmet", (string?)row["nextLevelRequirements"]);
    }
}
