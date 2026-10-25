using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Runtime.World;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using Xunit;

namespace OrbModding.Tests.Strategist;

public sealed class StrategistServiceCycleFeatureTests
{
    [Fact]
    public void TheSharedHostStartsWithTheStrategistRegistered()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var frame = 0L;
        var log = new ManualLogSource();

        using var runtime = AutomataServiceCycleComposition.TryCreate(
            configuration.Current,
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => ++frame,
                () => 1,
                new ServiceActionTimelineRegistry()),
            new IAutomataServiceCycleFeature[] { new StrategistServiceCycleFeature() },
            log);

        Assert.True(runtime is not null, string.Join("\n", log.Entries));
        for (var index = 0; index < 3; index++)
            runtime.Tick(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedSourceExplainsWorkerFaultsAndClearsThemOnRecoveryAndLifecycle(bool previouslyPublished)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        using var registration = registry.Register(StrategistService.Define(registry.StrategistBulletinPublication));
        var feature = new StrategistFeatureRuntime(registration, registry.StrategistBulletinPublication);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var source = (IStrategistBulletinSource)registry.Strategy;
        long frame = 3;
        ServiceRunnerTestWait.ForWorkerReady(registration);

        if (previouslyPublished)
        {
            RunCycle(withClock: true);
            Assert.NotNull(source.ReadStrategistBulletin());
            clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        }
        var retained = source.ReadStrategistBulletin();
        var previousPolicy = registry.Strategy.ReadLatest();
        Assert.Equal(previouslyPublished ? GoalId : Guid.Empty, previousPolicy.Bulletin.ActiveGoalId);

        RunCycle(withClock: false);

        Assert.Same(retained, source.ReadStrategistBulletin());
        var fault = registration.Runner.Snapshot.Fault;
        Assert.Equal(ServiceFaultCategory.Evaluation, fault.Category);
        Assert.Contains("retrying Evaluation", source.UnavailableReason);
        Assert.Contains("1 failed attempt", source.UnavailableReason);
        Assert.Contains(fault.Reason, source.UnavailableReason);
        Assert.Same(SuiteStrategyDefaults.Neutral, registry.Strategy.ReadLatest().Bulletin);
        var neutralGeneration = registry.Strategy.ReadLatest().Generation;
        Assert.Equal(previousPolicy.Generation.Value + (previouslyPublished ? 1UL : 0UL), neutralGeneration.Value);

        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        RunCycle(withClock: false);
        Assert.Contains("2 failed attempt", source.UnavailableReason);
        Assert.Same(retained, source.ReadStrategistBulletin());
        Assert.Same(SuiteStrategyDefaults.Neutral, registry.Strategy.ReadLatest().Bulletin);
        Assert.Equal(neutralGeneration, registry.Strategy.ReadLatest().Generation);

        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        RunCycle(withClock: true);

        Assert.NotNull(source.ReadStrategistBulletin());
        Assert.Empty(source.UnavailableReason);
        Assert.False(registration.Runner.Snapshot.Fault.IsValid);
        Assert.Equal(GoalId, registry.Strategy.ReadLatest().Bulletin.ActiveGoalId);
        Assert.Equal(neutralGeneration.Value + 1, registry.Strategy.ReadLatest().Generation.Value);

        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        RunCycle(withClock: false);
        Assert.NotEmpty(source.UnavailableReason);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        feature.ObserveLifecycle(2, new ConfigGeneration(1));
        Assert.Empty(source.UnavailableReason);
        Assert.Null(source.ReadStrategistBulletin());

        void RunCycle(bool withClock)
        {
            TestWorldCollector.CollectedAt(registry, frame, World(withClock));
            ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
            ServiceRunnerTestWait.ForResponse(registration);
            var response = ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
            if (withClock && response.ActionsAttempted == 0)
                ServiceCyclePumpTestWait.UntilAction(pump, ref frame);
            ServiceRunnerTestWait.ForHandback(registration.Runner);
            var quiet = pump.PumpFrame(frame++);
            feature.ObserveFrame(pump, in quiet);
        }
    }

    private static readonly Guid GoalId = Guid.Parse("2f6b8c1d-4e5a-4b7c-9d0e-1f2a3b4c5d6e");
    private static readonly Guid ResourceId = Guid.Parse("7a8b9c0d-1e2f-4a3b-8c4d-5e6f7a8b9c0d");

    private static GameWorldState World(bool withClock)
    {
        var memberships = new WorldRelationBuffer<WorldUpgradeListMembership>();
        memberships.Append(new WorldUpgradeListMembership(GoalId, KnownEntities.UpgradesMagicScreen.Uuid));
        return new GameWorldState
        {
            Resources = WorldTable.Create(new[]
            {
                new WorldResourceDeriver(default).Derive(WorldSamples.Resource(
                    ResourceId, new BigDouble(100), new BigDouble(1000))),
            }),
            Upgrades = WorldTable.Create(new[] { WorldUpgradeDeriver.Shared.Derive(WorldSamples.Upgrade(GoalId)) }),
            UpgradeListMemberships = WorldRelationTable.Build(memberships, WorldUpgradeListMembershipLookup.Order),
            Views = WorldTable.Create(new[] { new WorldView(KnownEntities.ScreenMagic.Uuid, false, false, true) }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                new WorldPurchaseCost(GoalId, ResourceId, new BigDouble(10)),
            }),
            DoubleVariables = withClock
                ? PublicationTable<WorldNumberVariable>.Create(new[]
                {
                    new WorldNumberVariable(KnownEntities.TimePlayed.Uuid, new BigDouble(600), true),
                })
                : PublicationTable<WorldNumberVariable>.Empty,
        };
    }
}
