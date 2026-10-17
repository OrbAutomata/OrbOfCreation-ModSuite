using System;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Strategist;

public sealed class StrategistFixtureTests
{
    private const string Restoration = "14b35ebc-f284-4d53-bd3f-f57a885cf2b1";
    private const string Mana = "3f1c0a52-7d4e-4b8a-9c21-5e6f70819203";
    private const string Wood = "8b27e4d1-0c93-4f5a-b6e8-1d2c3b4a5f60";
    private const string Gold = "c45d9e02-6a1b-4c7d-8e3f-a0b1c2d3e4f5";
    private const string Points = "e7a81b36-2f4c-4d5e-9a0b-c1d2e3f40516";
    private const string Keystone = "5a9c3e71-8b2d-4f60-a1c4-d7e8f9012345";
    private const string Trinket = "0d6e2f84-3a5b-4c9d-8e1f-23456789abcd";
    private const string Bauble = "91b4c7d2-5e6f-4a0b-9c3d-e4f5a6b7c8d9";
    private const string Lumber = "6c0f8a93-1d2e-4b3f-a4c5-6d7e8f9a0b1c";
    private const string Vault = "d2e5f816-4b7c-4d8e-9f0a-1b2c3d4e5f6a";
    private const string Tenet = "47f3a6b9-0c1d-4e2f-8a3b-4c5d6e7f8091";

    private static readonly string Fixture = string.Join("\n",
        $"facts world=4200 played=5400 reset=1800 challenges=0 root={Restoration}",
        "open name=Spellcraft",
        "open level=3/10 name=Output Lv",
        $"resource {Mana} held=2e2 cap=1e5 name=Mana",
        $"resource {Wood} held=1e1 name=Wood",
        $"resource {Gold} held=0e0 cap=1e2 name=Gold",
        $"resource {Points} held=2e0 points name=Research Points",
        $"node {Restoration} upgrade gate name=Restoration",
        $"node {Keystone} upgrade offered gate name=Keystone Of The World",
        $"node {Trinket} upgrade offered gate name=Trinket",
        $"node {Bauble} upgrade offered gate name=Bauble",
        $"node {Lumber} upgrade offered name=Lumber Yard",
        $"node {Vault} upgrade offered gate name=Gold Vault",
        $"node {Tenet} upgrade offered gate name=Tenet",
        $"price {Keystone} {Mana} amount=4.5e4",
        $"price {Trinket} {Mana} amount=5e1",
        $"price {Bauble} {Mana} amount=8e1",
        $"price {Lumber} {Wood} amount=6e2",
        $"price {Vault} {Gold} amount=5e2",
        $"price {Tenet} {Points} amount=3e0",
        $"edge {Restoration} {Keystone}",
        "window required=130",
        $"gain {Mana} gained=1.3e3 span=130",
        $"gain {Wood} gained=1e3 span=130",
        $"gain {Gold} gained=0e0 span=130",
        "");

    private static StrategistBulletin Derive(string facts)
    {
        var (parsed, window) = StrategistFactsText.Parse(facts);
        return StrategistDerivation.Derive(parsed, window);
    }

    private static StrategistGoal Goal(StrategistBulletin bulletin, string gate)
    {
        foreach (var goal in bulletin.Goals.AsSpan())
        {
            if (goal.GateId == Guid.Parse(gate)) return goal;
        }
        throw new InvalidOperationException("no goal for " + gate);
    }

    [Fact]
    public void AReplacedLifecycleTakesTheBulletinWithIt()
    {
        using var registry = new ServiceCycleRegistry(1);
        registry.Seal();
        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(11)));
        registry.StrategyPublication.Publish(new SuiteStrategy
        {
            Provenance = SuiteStrategyProvenance.Strategist,
            Bulletin = Derive(Fixture),
        });
        Assert.NotNull(registry.Strategy.ReadLatest().Bulletin.Bulletin);

        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(12)));

        var after = registry.Strategy.ReadLatest().Bulletin;
        Assert.Same(SuiteStrategyDefaults.Neutral, after);
        Assert.Equal(StrategistBulletinText.NoBulletin, StrategistBulletinText.Summary(after.Bulletin));
    }

    [Fact]
    public void TheEmptyWorldALifecycleLeavesBehindHasNothingToJudge()
    {
        var window = new StrategistIncomeWindow();

        Assert.False(StrategistService.TryDerive(GameWorldStateDefaults.Empty, 1, window, out _));
        Assert.Empty(window.Read(0, 60).Rows.AsSpan().ToArray());
    }

    [Fact]
    public void AClockThatRunsBackwardsIsANewRunAndForgetsTheOldIncome()
    {
        var id = Guid.Parse(Wood);
        var window = new StrategistIncomeWindow();
        for (var second = 0; second <= 120; second++)
            window.Observe(1000 + second, 60, new[] { Sample(id, second * 5d) });

        window.Observe(10, 60, new[] { Sample(id, 0d) });

        var row = Assert.Single(window.Read(10, 60).Rows.AsSpan().ToArray());
        Assert.Equal(0d, row.SpanSeconds);
    }

    [Fact]
    public void TheFactsSectionDerivesTheSameBulletinBack()
    {
        var bulletin = Derive(Fixture);
        var facts = StrategistBulletinText.Section(bulletin, "facts");

        var again = Derive(facts);

        Assert.Equal(facts, StrategistBulletinText.Section(again, "facts"));
        Assert.Equal(StrategistBulletinText.Summary(bulletin), StrategistBulletinText.Summary(again));
        foreach (var section in new[] { "goals", "position", "demand" })
        {
            Assert.Equal(
                StrategistBulletinText.Section(bulletin, section),
                StrategistBulletinText.Section(again, section));
        }
    }

    /// <summary>
    /// F6: a chop pays a burst, then the tree regrows. Read over the burst alone, 600 Wood is six
    /// seconds away; over one full harvest-and-regrowth cycle it is over a minute.
    /// </summary>
    [Fact]
    public void AChopBurstIsTieredOnSustainedIncomeNotOnTheBurst()
    {
        var id = Guid.Parse(Wood);
        const double harvest = 10d;
        const double growth = 120d;
        const double cycle = harvest + growth;
        var window = new StrategistIncomeWindow();
        var lifetime = 0d;
        for (var second = 0; second <= 400; second++)
        {
            if (second % cycle < harvest) lifetime += 100d;
            window.Observe(second, cycle, new[] { Sample(id, lifetime) });
        }

        var reading = window.Read(400, cycle);
        var (facts, _) = StrategistFactsText.Parse(Fixture);
        var bulletin = StrategistDerivation.Derive(facts, reading);

        var income = Array.Find(bulletin.Incomes.AsSpan().ToArray(), row => row.ResourceId == id);
        Assert.True(income.Measured);
        Assert.InRange(income.PerSecond.ToDouble(), 7d, 8.5d);
        var lumber = Array.Find(
            bulletin.Demand.AsSpan().ToArray(), row => row.ResourceId == id).Sinks[0];
        Assert.Equal(Guid.Parse(Lumber), lumber.NodeId);
        Assert.Equal(StrategistCostTier.Notable, lumber.Tier);
    }

    /// <summary>
    /// AT1: the Restoration chain's next milestone costs over an hour of Mana while two dribbles
    /// cost seconds. The milestone still leads, because it is what finishing the world waits on.
    /// </summary>
    [Fact]
    public void AnExpensiveRestorationMilestoneRanksAboveCheapDribbles()
    {
        var bulletin = Derive(Fixture);

        var goals = bulletin.Goals.AsSpan();
        Assert.Equal(Guid.Parse(Keystone), goals[0].GateId);
        Assert.Equal(StrategistGoalRank.Chain, goals[0].Rank);
        Assert.Equal(StrategistCostTier.Expensive, goals[0].Costs[goals[0].Binder].Tier);
        Assert.Equal(StrategistCostTier.Trivial, Goal(bulletin, Trinket).Costs[0].Tier);
        Assert.Equal(StrategistCostTier.Trivial, Goal(bulletin, Bauble).Costs[0].Tier);
        Assert.Equal(StrategistGoalRank.Other, Goal(bulletin, Trinket).Rank);
        Assert.Equal(Guid.Parse(Keystone), Assert.Single(bulletin.Restoration.Frontier.AsSpan().ToArray()));
        Assert.Contains(
            "short: 1 Keystone Of The World 5a9c3e",
            StrategistBulletinText.Summary(bulletin),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACostIsATimeOnSustainedIncomeOrSaysWhyItIsNot()
    {
        var bulletin = Derive(Fixture);

        var vault = Goal(bulletin, Vault).Costs[0];
        Assert.Equal(StrategistCostTier.OutOfReach, vault.Tier);
        Assert.Equal(StrategistOutOfReach.AboveCap, vault.OutOfReach);
        var tenet = Goal(bulletin, Tenet).Costs[0];
        Assert.Equal(StrategistCostTier.Allocation, tenet.Tier);
        Assert.Equal(StrategistCostTier.Trivial, StrategistDerivation.Tier(new BigDouble(9.9)));
        Assert.Equal(StrategistCostTier.Cheap, StrategistDerivation.Tier(new BigDouble(10)));
        Assert.Equal(StrategistCostTier.Notable, StrategistDerivation.Tier(new BigDouble(60)));
        Assert.Equal(StrategistCostTier.Expensive, StrategistDerivation.Tier(new BigDouble(3600)));
    }

    [Fact]
    public void AWindowShorterThanOneProducerCycleHasMeasuredNothing()
    {
        var shortWindow = Fixture.Replace("span=130", "span=90");

        var keystone = Goal(Derive(shortWindow), Keystone).Costs[0];

        Assert.Equal(StrategistCostTier.Measuring, keystone.Tier);
    }

    private static StrategistIncomeWindow.Sample Sample(Guid id, double lifetime) =>
        new(id, new BigDouble(lifetime), false);
}
