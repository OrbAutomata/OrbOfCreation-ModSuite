using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Runtime.World;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoLevels;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class AutoLevelsWorkerTests
{
    private static readonly Guid Mana = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ShippingWorkerEmitsOneTotalActionAndRequiresAFreshWorldBeforeAnother()
    {
        var harness = new WorkerHarness();
        var first = Assert.Single(harness.Plan(World(), world: 10));
        Assert.Equal(LevelDial.Output, first.Dial);
        Assert.Equal(3, first.CurrentLevel);
        Assert.Equal(4, first.TargetLevel);
        Assert.Equal(1, first.LifecycleEpoch);
        Assert.Empty(harness.Plan(World(), world: 10));

        var next = Assert.Single(harness.Plan(World(), world: 11));

        Assert.Equal(LevelDial.Reserve, next.Dial);
        Assert.Equal(4, next.TargetLevel);
    }

    [Fact]
    public void FreshWorldRaisesRotateAcrossAllFourDialsRatherThanStarvingLaterDials()
    {
        var harness = new WorkerHarness();
        var dials = new List<LevelDial>();
        for (ulong generation = 1; generation <= 8; generation++)
            dials.Add(Assert.Single(harness.Plan(World(), generation)).Dial);

        Assert.Equal(new[] { LevelDial.Output, LevelDial.Reserve, LevelDial.Druidry, LevelDial.Alchemy,
            LevelDial.Output, LevelDial.Reserve, LevelDial.Druidry, LevelDial.Alchemy }, dials);
    }

    [Fact]
    public void AProvenDangerReductionWinsBeforeEveryOtherwiseEligibleRaise()
    {
        var harness = new WorkerHarness();

        var action = Assert.Single(harness.Plan(World(unsafeAlchemy: true), world: 1));

        Assert.Equal(LevelDial.Alchemy, action.Dial);
        Assert.Equal(3, action.CurrentLevel);
        Assert.Equal(2, action.TargetLevel);
        Assert.Equal(AutoLevelsDecisionReason.DominantDrain, harness.State.Alchemy.Reason);
        Assert.Equal(AutoLevelsDecisionKind.Raise, harness.State.Output.Kind);
        Assert.Equal(AutoLevelsDecisionKind.Raise, harness.State.Reserve.Kind);
    }

    [Theory]
    [InlineData("master")]
    [InlineData("stop")]
    [InlineData("mode")]
    public void MasterOffEmergencyStopAndModeOffBlockTheShippingWorker(string disabled)
    {
        var harness = new WorkerHarness();
        var configuration = Configuration() with
        {
            General = new SuiteGeneralConfiguration { Enabled = disabled != "master" },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = disabled == "stop" },
            AutoLevels = new AutoLevelsConfiguration
            {
                Mode = disabled == "mode" ? AutoLevelsOperationMode.Disabled : AutoLevelsOperationMode.Active,
            },
        };

        Assert.Empty(harness.Plan(World(), world: 1, configuration));
        Assert.False(harness.State.Observed);
        Assert.Single(harness.Plan(World(), world: 1));
    }

    [Fact]
    public void NoActiveEconomicWorkKeepsThoseDialsStillWhileReserveCanApproachItsMaximum()
    {
        var harness = new WorkerHarness();

        var action = Assert.Single(harness.Plan(World(active: false), world: 1));

        Assert.Equal(LevelDial.Reserve, action.Dial);
        Assert.Equal(AutoLevelsDecisionReason.NoActiveWork, harness.State.Output.Reason);
        Assert.Equal(AutoLevelsDecisionReason.NoActiveWork, harness.State.Druidry.Reason);
        Assert.Equal(AutoLevelsDecisionReason.NoActiveWork, harness.State.Alchemy.Reason);
        Assert.Empty(harness.Plan(World(active: false, reserve: 5), world: 2));
        Assert.Equal(AutoLevelsDecisionReason.AtMaximum, harness.State.Reserve.Reason);
    }

    [Fact]
    public void LockedEconomicControlsCannotBePromotedByTheWorkersRotation()
    {
        var harness = new WorkerHarness();
        var world = World() with
        {
            Views = WorldTable.Create(new[]
            {
                new WorldView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, false, false, false),
                new WorldView(KnownEntities.AlchAlchemyLevel.Uuid, false, false, false),
            }),
        };

        Assert.Equal(LevelDial.Output, Assert.Single(harness.Plan(world, 1)).Dial);
        Assert.Equal(LevelDial.Reserve, Assert.Single(harness.Plan(world, 2)).Dial);
        Assert.Equal(LevelDial.Output, Assert.Single(harness.Plan(world, 3)).Dial);
        Assert.Equal(AutoLevelsDecisionReason.ControlLocked, harness.State.Druidry.Reason);
        Assert.Equal(AutoLevelsDecisionReason.ControlLocked, harness.State.Alchemy.Reason);
    }

    [Fact]
    public void RegisteredShippingServiceMutatesTheCanonicalReserveDialOncePerFreshReading()
    {
        Player.Current = new Player();
        Player.GetSpellOutputLevel().uuid = KnownEntities.SpellOutputLevel.Uuid;
        Player.GetReserveLevel().uuid = KnownEntities.ReserveLevel.Uuid;
        Player.Current.druidryOutputLevel.uuid = KnownEntities.DruidryLevel.Uuid;
        Player.Current.alchemyOutputLevel.uuid = KnownEntities.AlchemyOutputLevel.Uuid;
        Player.GetSpellOutputLevel().Value = 3;
        Player.Current.maxSpellOutputLevel.Value = 5;
        Player.GetReserveLevel().Value = 3;
        Player.Current.maxReserveLevel.Value = 5;
        Player.Current.druidryOutputLevel.Value = 3;
        Player.Current.maxDruidryOutputLevel.Value = 5;
        Player.Current.alchemyOutputLevel.Value = 3;
        Player.Current.maxAlchemyOutputLevel.Value = 5;
        using var status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(), new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutoLevelsFeatureContribution.FeatureId), "Auto Levels", true,
            FeatureStatusState.NotReady, new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady,
                "Waiting for the first level quote."), 1));
        using var gameAction = new SpellCompositionGameAction(() => 1, () => true, () => string.Empty,
            name => typeof(Player).Assembly.GetTypes().FirstOrDefault(type => type.Name == name || type.FullName == name));
        Assert.True(gameAction.BindingsAvailable, gameAction.BindingFailure);
        using var registry = new ServiceCycleRegistry(1, Configuration(), new ConfigGeneration(1),
            new LifecycleGeneration(1), new ThreadSafeTestClock(1000));
        var dependencies = new AutoLevelsFeatureDependencies(TypedRegistryResolver.Shared, () => 1,
            () => true, () => true, () => string.Empty, status, _ => { });
        using var registration = registry.Register(AutoLevelsService.Define(gameAction, dependencies));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        ServiceRunnerTestWait.ForWorkerReady(registration);
        long frame = 3;

        TestWorldCollector.CollectedAt(registry, 2, World(active: false));
        RunPublishedWorld();
        Assert.Equal(4, Player.GetReserveLevel().AsInt());
        Assert.Equal(1, Player.GetReserveLevel().SetCalls);
        for (var quiet = 0; quiet < 12; quiet++)
            Assert.Equal(0, pump.PumpFrame(frame++).ActionsAttempted);
        Assert.Equal(1, Player.GetReserveLevel().SetCalls);

        TestWorldCollector.CollectedAt(registry, frame++, World(active: false, reserve: 4));
        RunPublishedWorld();
        Assert.Equal(5, Player.GetReserveLevel().AsInt());
        Assert.Equal(2, Player.GetReserveLevel().SetCalls);

        void RunPublishedWorld()
        {
            ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
            ServiceRunnerTestWait.ForResponse(registration);
            var response = ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
            if (response.ActionsAttempted == 0) ServiceCyclePumpTestWait.UntilAction(pump, ref frame);
            ServiceRunnerTestWait.ForHandback(registration.Runner);
        }
    }

    private sealed class WorkerHarness
    {
        private readonly AutoLevelsWorkerDefinition _worker = new();
        private readonly ReusableActionStore<AutoLevelsCycleAction> _store = new();
        private ulong _cycle;
        internal AutoLevelsCycleState State;
        internal WorkerHarness() => State = _worker.CreateState(new LifecycleGeneration(1));

        internal IReadOnlyList<AutoLevelsCycleAction> Plan(GameWorldState snapshot, ulong world,
            SuiteRuntimeConfiguration? configuration = null)
        {
            var config = configuration ?? Configuration();
            var context = new ServiceCycleContext(new ServiceCycleIdentity(AutoLevelsService.ServiceId,
                new LifecycleGeneration(1), new ConfigGeneration(1), new StrategyGeneration(1),
                new WorldGeneration(world), new CycleId(++_cycle)), default, default);
            _store.BeginWrite();
            var wake = _worker.Evaluate(in config, snapshot, SuiteStrategyDefaults.Neutral,
                in context, ref State, new ServiceActionWriter<AutoLevelsCycleAction>(_store));
            Assert.Equal(WakePolicy.OnPublication, wake);
            var actions = new List<AutoLevelsCycleAction>();
            while (!_store.IsComplete)
            {
                actions.Add(_store.GetCurrent());
                _store.CommitCurrentAndClear();
            }
            _store.CompleteSuccessfulBatch();
            return actions;
        }
    }

    private static SuiteRuntimeConfiguration Configuration() => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        AutoLevels = new AutoLevelsConfiguration { Mode = AutoLevelsOperationMode.Active },
    };

    private static GameWorldState World(bool active = true, int reserve = 3, bool unsafeAlchemy = false)
    {
        var empty = PublicationTable<WorldLevelPrice>.Empty;
        var free = new WorldLevelDomainQuote(true, string.Empty, active, 3, 5, empty, empty);
        var alchemy = unsafeAlchemy
            ? new WorldLevelDomainQuote(true, string.Empty, true, 3, 5,
                PublicationTable<WorldLevelPrice>.Create(new[] { new WorldLevelPrice(-1, Mana, new BigDouble(9), WorldLevelPriceKind.Drain) }),
                PublicationTable<WorldLevelPrice>.Create(new[] { new WorldLevelPrice(-1, Mana, new BigDouble(10), WorldLevelPriceKind.Drain) }))
            : free;
        var sample = WorldSamples.Resource(Mana, 1, 100, drain: 10);
        return new GameWorldState
        {
            CollectedAtEpoch = 1,
            SpellWorkbench = new WorldSpellWorkbench(1, 1, false, 3, 5, reserve, 5,
                druidryLevel: 3, maximumDruidryLevel: 5, alchemyLevel: 3, maximumAlchemyLevel: 5),
            Views = WorldTable.Create(new[]
            {
                new WorldView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, false, false, true),
                new WorldView(KnownEntities.AlchAlchemyLevel.Uuid, false, false, true),
            }),
            Resources = WorldTable.Create(new[] { new WorldResource(in sample, true, new BigDouble(99),
                0.01, false, BigDouble.One, new BigDouble(-1), new BigDouble(-1)) }),
            LevelEconomy = new WorldLevelEconomy(free, free, alchemy),
        };
    }
}
