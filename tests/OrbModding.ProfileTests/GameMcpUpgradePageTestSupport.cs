using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.ProfileTests;

internal static class GameMcpUpgradePageTestSupport
{
    internal static readonly Guid Capped = Guid.Parse("41111111-1111-4111-8111-111111111111");
    internal static readonly Guid Uncapped = Guid.Parse("42222222-2222-4222-8222-222222222222");
    internal static readonly Guid Exhausted = Guid.Parse("43333333-3333-4333-8333-333333333333");
    internal static readonly Guid Scholarly = Guid.Parse("44444444-4444-4444-8444-444444444445");
    internal static readonly Guid Aspect = Guid.Parse("45555555-5555-4555-8555-555555555555");

    /// <summary>
    /// The authored list each fixture upgrade sits on. All three share one screen so the pages that
    /// pin a share line still hoist every column; the screen vocabulary itself is pinned by the page
    /// that gives each row its own list.
    /// </summary>
    internal static WorldUpgradeListMembership[] OneScreen() => new[]
    {
        new WorldUpgradeListMembership(Capped, KnownEntities.UpgradesMagicScreen.Uuid),
        new WorldUpgradeListMembership(Uncapped, KnownEntities.UpgradesMagicScreen.Uuid),
        new WorldUpgradeListMembership(Exhausted, KnownEntities.UpgradesMagicScreen.Uuid),
    };

    /// <summary>
    /// The membership table exactly as the world publishes it, through the production deriver, so a
    /// fixture cannot hand the surface an ordering the collector would never produce.
    /// </summary>
    internal static PublicationTable<WorldUpgradeListMembership> Published(
        WorldUpgradeListMembership[] memberships)
    {
        var buffer = new WorldRelationBuffer<WorldUpgradeListMembership>();
        foreach (var membership in memberships) buffer.Append(membership);
        return WorldRelationTable.Build(buffer, WorldUpgradeListMembershipLookup.Order);
    }

    internal static GameWorldState UpgradesWorld(params WorldUpgrade[] upgrades) =>
        UpgradesWorld(OneScreen(), upgrades);

    /// <summary>
    /// The view behind every screen word, unlocked unless named. A live world publishes every
    /// <c>ViewSO</c>, so a fixture that put upgrades on a screen and published no view for it was a
    /// world the game cannot be in.
    /// </summary>
    internal static PublicationTable<WorldView> ScreenViews(params Guid[] locked) =>
        PublicationTable<WorldView>.Create(GameMcpListColumns.Screens
            .Select(screen => screen.ViewId)
            .OrderBy(view => view)
            .Select(view => new WorldView(view, false, false, !locked.Contains(view)))
            .ToArray());

    internal static GameWorldState UpgradesWorld(
        WorldUpgradeListMembership[] memberships,
        WorldUpgrade[] upgrades,
        params Guid[] lockedScreens) =>
        new()
        {
            Views = ScreenViews(lockedScreens),
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

    internal static JObject Page(params WorldUpgrade[] upgrades) =>
        Page(OneScreen(), upgrades);

    internal static JObject Page(
        WorldUpgradeListMembership[] memberships,
        params WorldUpgrade[] upgrades)
    {
        return GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(UpgradesWorld(memberships, upgrades)), "upgrades", 0, 50));
    }

    internal static JObject[] Rows(JObject page) =>
        page["rows"]!.Values<JObject>().Select(row => row!).ToArray();

    /// <summary>The rendered column set: its own line, so a reader finds it the same way twice.</summary>
    internal static string Bracket(string page) => page
        .Split('\n')
        .Single(line => line.StartsWith("[", StringComparison.Ordinal));

    internal static WorldUpgrade Upgrade(
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
