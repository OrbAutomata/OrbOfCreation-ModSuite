using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyStrategistGoalTests
{
    private static readonly Guid Structure = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUpgrade = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Goal = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Resource = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void ShippingWorkerUsesThePinnedGoalBeforeCheaperCompetingPurchases()
    {
        var worker = new AutoBuyWorkerDefinition();
        var state = worker.CreateState(new LifecycleGeneration(1));
        var world = World();
        var config = Configuration();
        var context = Context();
        var store = new ReusableActionStore<AutoBuyCycleAction>();
        store.BeginWrite();

        worker.Evaluate(in config, world, new SuiteStrategy { ActiveGoalId = Goal },
            in context, ref state, new ServiceActionWriter<AutoBuyCycleAction>(store));

        Assert.Equal(Goal, Assert.Single(Drain(store)).Uuid);
    }

    [Fact]
    public void ShippingWorkerUsesNeutralStrategyOnTheNextCycleWithoutRetainingTheGoal()
    {
        var worker = new AutoBuyWorkerDefinition();
        var state = worker.CreateState(new LifecycleGeneration(1));
        var world = World();
        var config = Configuration();
        var context = Context();
        var store = new ReusableActionStore<AutoBuyCycleAction>();
        store.BeginWrite();
        worker.Evaluate(in config, world, new SuiteStrategy { ActiveGoalId = Goal },
            in context, ref state, new ServiceActionWriter<AutoBuyCycleAction>(store));
        Assert.Equal(Goal, Assert.Single(Drain(store)).Uuid);

        store.BeginWrite();
        worker.Evaluate(in config, world, SuiteStrategyDefaults.Neutral,
            in context, ref state, new ServiceActionWriter<AutoBuyCycleAction>(store));

        Assert.Equal(Structure, Assert.Single(Drain(store)).Uuid);
    }

    [Theory]
    [InlineData(false, "goal")]
    [InlineData(true, "neutral")]
    [InlineData(true, "unknown")]
    [InlineData(true, "structure")]
    public void DisabledNeutralAndUnsupportedGoalsKeepTheCompleteOrdinaryPlan(bool follow, string selected)
    {
        var goalId = selected switch
        {
            "goal" => Goal,
            "structure" => Structure,
            "unknown" => Guid.Parse("55555555-5555-5555-5555-555555555555"),
            _ => Guid.Empty,
        };
        var frame = Frame(quantity: 1_000, bulk: 3);
        var expected = Plan(frame, Configuration(follow: false), Guid.Empty);
        var actual = Plan(frame, Configuration(follow), goalId);

        Assert.Equal(expected.Select(action => action.Uuid), actual.Select(action => action.Uuid));
        Assert.Equal(expected.Select(action => action.Count), actual.Select(action => action.Count));
        Assert.Equal(expected.Select(action => action.Belief.CostRatio), actual.Select(action => action.Belief.CostRatio));
        Assert.Equal(expected.SelectMany(action => action.PlannedSpend.AsSpan().ToArray())
                .Select(row => row.RemainingBeforeSpend.ToDouble()),
            actual.SelectMany(action => action.PlannedSpend.AsSpan().ToArray())
                .Select(row => row.RemainingBeforeSpend.ToDouble()));
    }

    [Fact]
    public void AffordableGoalGoesFirstAndTheRemainingCandidatesKeepRatioThenUuidOrder()
    {
        var actions = Plan(Frame(quantity: 1_000), Configuration(), Goal);

        Assert.Equal(new[] { Goal, Structure, OtherUpgrade }, actions.Select(action => action.Uuid));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("requirements")]
    [InlineData("terminal")]
    [InlineData("destination")]
    [InlineData("view")]
    [InlineData("unaffordable")]
    [InlineData("reserve")]
    [InlineData("affordability")]
    [InlineData("retry")]
    [InlineData("upgrades-disabled")]
    public void AGoalThatFailsOrdinaryAdmissionDoesNotBlockOtherPurchases(string reason)
    {
        var config = Configuration() with
        {
            AutoBuy = Configuration().AutoBuy with
            {
                IncludeUpgrades = reason != "upgrades-disabled",
                UpgradeAffordability = reason == "affordability"
                    ? AutoBuyAffordabilityMode.Excess100 : AutoBuyAffordabilityMode.BuyAll,
            },
            Reserves = new AutomataReserveConfiguration
            {
                AbsoluteReserve = reason == "reserve" ? "20" : "0",
            },
        };
        var retry = reason == "retry"
            ? new ServiceRetryGateSnapshot(new[]
            {
                new ServiceRetryEntry(AutoBuyServicePolicies.RetryTarget(AutoBuyCandidateKind.Upgrade, Goal),
                    CommonActionResultCodes.AdapterFault, "goal failed", 1, default,
                    new MonotonicTimestamp(10), false),
            })
            : ServiceRetryGateSnapshot.Empty;

        var actions = Plan(Frame(quantity: 100, goalFailure: reason), config, Goal, retry);

        Assert.DoesNotContain(actions, action => action.Uuid == Goal);
        Assert.Equal(Structure, actions[0].Uuid);
    }

    [Fact]
    public void PriorityKeepsExactGroupedCostsAndTheCumulativeReserveLedger()
    {
        var config = Configuration() with
        {
            Reserves = new AutomataReserveConfiguration { AbsoluteReserve = "25" },
        };
        var actions = Plan(Frame(quantity: 150, bulk: 3), config, Goal);

        Assert.Equal(new[] { Goal, Structure }, actions.Select(action => action.Uuid));
        Assert.Equal(new[] { 1, 1 }, actions.Select(action => action.Count));
        var spend = Assert.Single(actions[1].PlannedSpend.AsSpan().ToArray());
        Assert.Equal(60d, spend.RemainingBeforeSpend.ToDouble(), 10);
        Assert.Equal(20d, spend.Cost.ToDouble(), 10);
        Assert.Equal(25d, spend.ReserveFloor.ToDouble(), 10);
    }

    private static SuiteRuntimeConfiguration Configuration(bool follow = true) => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        AutoBuy = new AutoBuyConfiguration
        {
            Mode = AutoBuyOperationMode.Active,
            IncludeStructures = true,
            IncludeUpgrades = true,
            StructureAffordability = AutoBuyAffordabilityMode.BuyAll,
            UpgradeAffordability = AutoBuyAffordabilityMode.BuyAll,
            FollowStrategistGoal = follow,
        },
    };

    private static AutoBuyCycleFrame Frame(double quantity, int bulk = 1, string goalFailure = "")
    {
        var candidates = new[]
        {
            new AutoBuyCandidateRow(AutoBuyCandidateKind.Upgrade, Goal,
                goalFailure == "view" ? AutoBuyOwningViewStatus.Unavailable : AutoBuyOwningViewStatus.Available,
                isAvailable: goalFailure != "unavailable", currentLevel: 0, queuedLevels: 0,
                hasFiniteLevels: goalFailure == "terminal", isMaxLevel: goalFailure == "terminal",
                isMaxQueuedLevel: false, meetsNextLevelRequirements: goalFailure != "requirements",
                costRowStart: 0, costRowCount: 1, rewardDestinationFull: goalFailure == "destination"),
            new AutoBuyCandidateRow(AutoBuyCandidateKind.Upgrade, OtherUpgrade,
                true, 0, 0, false, false, false, true, 1, 1),
            new AutoBuyCandidateRow(AutoBuyCandidateKind.Structure, Structure,
                true, 0, 0, false, false, false, true, 2, 1),
        };
        var costs = new[]
        {
            new AutoBuyCostRow(0, new BigDouble(goalFailure == "unaffordable" ? 200d : 90d)),
            new AutoBuyCostRow(0, new BigDouble(20d)),
            new AutoBuyCostRow(0, new BigDouble(20d), bulk, new BigDouble(20d * bulk)),
        };
        return new AutoBuyCycleFrame(new AutoBuyGlobalRow(bulk, 1), candidates, 3, 1, 2,
            new[] { new AutoBuyResourceRow(Resource, false, new BigDouble(quantity)) }, 1, costs, 3);
    }

    private static IReadOnlyList<AutoBuyCycleAction> Plan(AutoBuyCycleFrame frame,
        SuiteRuntimeConfiguration config, Guid goal, ServiceRetryGateSnapshot? retry = null)
    {
        var store = new ReusableActionStore<AutoBuyCycleAction>();
        store.BeginWrite();
        AutoBuyCycleEvaluator.Evaluate(in frame, in config, retry ?? ServiceRetryGateSnapshot.Empty,
            default, new ServiceActionWriter<AutoBuyCycleAction>(store), out _, goal);
        return Drain(store);
    }

    private static IReadOnlyList<AutoBuyCycleAction> Drain(ReusableActionStore<AutoBuyCycleAction> store)
    {
        var result = new List<AutoBuyCycleAction>();
        while (!store.IsComplete)
        {
            result.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }
        store.CompleteSuccessfulBatch();
        return result;
    }

    private static ServiceCycleContext Context() => new(new ServiceCycleIdentity(
        AutoBuyServicePolicies.ServiceId, new LifecycleGeneration(1), new ConfigGeneration(1),
        new StrategyGeneration(1), new WorldGeneration(1), new CycleId(1)), default, default);

    private static GameWorldState World()
    {
        var structureType = Guid.NewGuid();
        var list = Guid.NewGuid();
        var view = Guid.NewGuid();
        return new GameWorldState
        {
            CollectedAtEpoch = 1,
            Structures = WorldTable.Create(new[]
            {
                WorldStructureDeriver.Shared.Derive(WorldSamples.Structure(Structure, structureTypeId: structureType)),
            }),
            Upgrades = WorldTable.Create(new[] { WorldUpgradeDeriver.Shared.Derive(WorldSamples.Upgrade(Goal)) }),
            Views = WorldTable.Create(new[] { new WorldView(view, false, false, true) }),
            PurchaseViewRelations = PublicationTable<WorldPurchaseViewRelation>.Create(new[]
            {
                new WorldPurchaseViewRelation(Structure, WorldPurchaseCandidateKind.Structure, structureType,
                    1, WorldPurchaseViewRelationStatus.Resolved),
                new WorldPurchaseViewRelation(Goal, WorldPurchaseCandidateKind.Upgrade, Guid.Empty,
                    1, WorldPurchaseViewRelationStatus.Resolved),
            }),
            PurchaseViewRoutes = PublicationTable<WorldPurchaseViewRoute>.Create(new[]
            {
                new WorldPurchaseViewRoute(Structure, list, view),
                new WorldPurchaseViewRoute(Goal, list, view),
            }),
            Resources = WorldTable.Create(new[]
            {
                new WorldResourceDeriver(default).Derive(
                    WorldSamples.Resource(Resource, new BigDouble(100d), new BigDouble(-1d))),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                new WorldPurchaseCost(Structure, Resource, new BigDouble(20d)),
                new WorldPurchaseCost(Goal, Resource, new BigDouble(90d)),
            }),
        };
    }
}
