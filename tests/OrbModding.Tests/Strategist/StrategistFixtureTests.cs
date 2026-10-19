using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
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
        var (parsed, window) = StrategistFactsParser.Parse(facts);
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
        StrategistService.Publish(registry.StrategistBulletinPublication, new StrategistCycleAction(Derive(Fixture)));
        Assert.NotNull(registry.Strategy.ReadStrategistBulletin());

        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(12)));

        Assert.Same(SuiteStrategyDefaults.Neutral, registry.Strategy.ReadLatest().Bulletin);
        var after = registry.Strategy.ReadStrategistBulletin();
        Assert.Null(after);
        Assert.Equal(StrategistBulletinText.NoBulletin, StrategistBulletinText.Summary(after));
    }

    /// <summary>
    /// Every bulletin differs from the last — the world generation and the clock move — but nothing
    /// evaluates against it, so publishing one is not a strategy change. A strategy change is a
    /// suite-wide journal record that closes every service's open span; a lifecycle still makes one.
    /// </summary>
    [Fact]
    public void ManyBulletinsWithUnchangedStancesJournalNoStrategyChange()
    {
        using var registry = new ServiceCycleRegistry(1);
        registry.Seal();
        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(11)));
        var sink = new RecordingSink();
        var observer = new ServiceCycleDecisionJournalObserver(
            new DecisionJournalCoalescer(1, sink, new MonotonicDuration(1000), default), 1);
        observer.BindPublications(
            registry.Configuration.ReadLatest().Generation,
            registry.Strategy.ReadLatest().Generation);
        var scanner = new SuiteFramePumpEvidenceScanner(registry, 1);

        for (var world = 1; world <= 240; world++)
        {
            var facts = Fixture.Replace("facts world=4200", "facts world=" + (4200 + world));
            StrategistService.Publish(registry.StrategistBulletinPublication, new StrategistCycleAction(Derive(facts)));
            scanner.ObservePublications(null, observer, new MonotonicTimestamp(world), includeJournal: true);
        }
        Assert.DoesNotContain(sink.Records, record => record.Kind == DecisionJournalRecordKind.StrategyChanged);

        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(12)));
        scanner.ObservePublications(null, observer, new MonotonicTimestamp(300), includeJournal: true);
        Assert.Single(sink.Records, record => record.Kind == DecisionJournalRecordKind.StrategyChanged);
    }

    /// <summary>
    /// The strategist judges once a minute, so a fresh game waits a minute for its income to be measured.
    /// </summary>
    [Fact]
    public void TheStrategistWakesOnceAMinuteNotOnEachWorld()
    {
        using var registry = new ServiceCycleRegistry(1);
        var definition = StrategistService.Define(registry.StrategistBulletinPublication);

        Assert.Equal(
            WakePolicy.AfterDecision(MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1))),
            definition.DefaultWakePolicy);
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
    /// seconds away; over one full harvest-and-regrowth cycle it is over a minute. Minute samples
    /// land anywhere in the cycle, so a window holds one burst more or less than its share: the
    /// reading stays within half again of the sustained rate, never near the burst's.
    /// </summary>
    [Fact]
    public void AChopBurstIsTieredOnSustainedIncomeNotOnTheBurst()
    {
        var id = Guid.Parse(Wood);
        const double harvest = 10d;
        const double growth = 120d;
        const double cycle = harvest + growth;
        var (facts, _) = StrategistFactsParser.Parse(Fixture);
        for (var phase = 0d; phase < cycle; phase += 5d)
        {
            var window = new StrategistIncomeWindow();
            var now = 0d;
            for (var minute = 0; minute <= 10; minute++)
            {
                now = phase + minute * 60d;
                window.Observe(now, cycle, new[] { Sample(id, ChoppedWood(now, harvest, cycle)) });
            }

            var bulletin = StrategistDerivation.Derive(facts, window.Read(now, cycle));

            var income = Array.Find(bulletin.Incomes.AsSpan().ToArray(), row => row.ResourceId == id);
            Assert.True(income.Measured);
            Assert.InRange(income.PerSecond.ToDouble(), 5.5d, 11.2d);
            var lumber = Array.Find(
                bulletin.Demand.AsSpan().ToArray(), row => row.ResourceId == id).Sinks[0];
            Assert.Equal(Guid.Parse(Lumber), lumber.NodeId);
            Assert.InRange(lumber.Tier, StrategistCostTier.Cheap, StrategistCostTier.Notable);
        }
    }

    /// <summary>
    /// A fresh game with no harvest producer reads "measuring income" for one wake: the second sample,
    /// a minute later, covers the window — even when the two worlds it pinned are a hair under a
    /// minute apart on the game clock.
    /// </summary>
    [Fact]
    public void MinuteSamplesMeasureIncomeOnTheSecondWake()
    {
        var id = Guid.Parse(Mana);
        var window = new StrategistIncomeWindow();
        var (facts, _) = StrategistFactsParser.Parse(Fixture);
        const double required = StrategistIncomeWindow.MinimumSeconds;

        window.Observe(1000d, required, new[] { Sample(id, 0d) });
        var first = StrategistDerivation.Derive(facts, window.Read(1000d, required));
        window.Observe(1059.8d, required, new[] { Sample(id, 599d) });
        var second = StrategistDerivation.Derive(facts, window.Read(1059.8d, required));

        Assert.False(Array.Find(first.Incomes.AsSpan().ToArray(), row => row.ResourceId == id).Measured);
        var income = Array.Find(second.Incomes.AsSpan().ToArray(), row => row.ResourceId == id);
        Assert.True(income.Measured);
        Assert.InRange(income.PerSecond.ToDouble(), 9.9d, 10.1d);
    }

    /// <summary>
    /// Mana sat at its cap for the whole window, so the game kept none of its gain and the window
    /// reads zero as a floor. Spent below the price since, it is not out of reach for lack of income.
    /// </summary>
    [Fact]
    public void AWindowSpentAtTheCapIsAFloorNotNoIncome()
    {
        var capped = Fixture
            .Replace($"resource {Mana} held=2e2 cap=1e5", $"resource {Mana} held=1e1 cap=1e5")
            .Replace($"gain {Mana} gained=1.3e3 span=130", $"gain {Mana} gained=0e0 span=130 clipped");

        var trinket = Goal(Derive(capped), Trinket).Costs[0];

        Assert.Equal(StrategistCostTier.Measuring, trinket.Tier);
        Assert.Equal(StrategistOutOfReach.None, trinket.OutOfReach);
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

    private static double ChoppedWood(double now, double harvest, double cycle)
    {
        var cycles = Math.Floor(now / cycle);
        return (cycles * harvest + Math.Min(now - cycles * cycle, harvest)) * 100d;
    }

    private sealed class RecordingSink : IDecisionJournalRecordSink
    {
        internal List<DecisionJournalRecord> Records { get; } = new();

        public bool TryAppend(in DecisionJournalRecord record)
        {
            Records.Add(record);
            return true;
        }

        public bool TryFlush() => true;
        public void Stop() { }
    }
}
