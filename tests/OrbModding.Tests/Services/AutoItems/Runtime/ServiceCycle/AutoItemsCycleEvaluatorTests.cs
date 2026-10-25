using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsCycleEvaluatorTests
{
    [Fact]
    public void RelicsTakePriorityAndTheEvaluatorPlansAtMostOneActionPerWorld()
    {
        var scrollId = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var relicId = Guid.Parse("00000000-0000-0000-0000-000000000020");
        var world = World(
            Consumable(scrollId, randomizable: true),
            Consumable(relicId, randomizable: false),
            new[]
            {
                new WorldConsumableType(scrollId, KnownEntities.ConsumableScrollType.Uuid),
                new WorldConsumableType(relicId, KnownEntities.ConsumableRelicType.Uuid),
            },
            new[]
            {
                new WorldConsumableCount(scrollId, 3, 1, 0),
            });

        var planned = Plan(world, Configuration(), out var wake);

        var action = Assert.Single(planned);
        Assert.Equal(AutoItemsConsumableFamily.Relic, action.Family);
        Assert.Equal(relicId, action.ItemId);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void ScrollPlanningCarriesTheStrongestOwnedLevel()
    {
        var scrollId = Guid.Parse("00000000-0000-0000-0000-000000000030");
        var world = World(
            Consumable(scrollId, randomizable: true),
            null,
            new[]
            {
                new WorldConsumableType(scrollId, KnownEntities.ConsumableScrollType.Uuid),
            },
            new[]
            {
                new WorldConsumableCount(scrollId, 1, 2, 0),
                new WorldConsumableCount(scrollId, 5, 1, 0),
            });

        var action = Assert.Single(Plan(world, Configuration(), out var wake));

        Assert.Equal(AutoItemsConsumableFamily.Scroll, action.Family);
        Assert.Equal(5, action.PlannedLevel);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void AScrollRanksAboveAnApprovedTemporaryItem()
    {
        var scrollId = Guid.Parse("00000000-0000-0000-0000-000000000040");
        var threadId = Guid.Parse("00000000-0000-0000-0000-000000000041");
        var world = World(
            Consumable(scrollId, randomizable: true),
            Consumable(threadId, randomizable: false, duration: 60),
            new[]
            {
                new WorldConsumableType(scrollId, KnownEntities.ConsumableScrollType.Uuid),
                new WorldConsumableType(threadId, KnownEntities.ConsumableThreadType.Uuid),
            },
            new[]
            {
                new WorldConsumableCount(scrollId, 2, 1, 0),
            });

        var action = Assert.Single(Plan(world, Configuration(allowlist: threadId.ToString("D")), out _));

        Assert.Equal(scrollId, action.ItemId);
    }

    [Fact]
    public void AnActiveTemporaryEffectStillLetsARelicFireButHoldsAnotherTemporary()
    {
        var relicId = Guid.Parse("00000000-0000-0000-0000-000000000050");
        var threadId = Guid.Parse("00000000-0000-0000-0000-000000000051");
        var types = new[]
        {
            new WorldConsumableType(relicId, KnownEntities.ConsumableRelicType.Uuid),
            new WorldConsumableType(threadId, KnownEntities.ConsumableThreadType.Uuid),
        };
        var buffed = World(
            Consumable(relicId, randomizable: false),
            Consumable(threadId, randomizable: false, duration: 60),
            types,
            Array.Empty<WorldConsumableCount>(),
            Usage(threadId, engaged: true));
        var relicSpent = World(
            Consumable(relicId, randomizable: false, quantity: 0),
            Consumable(threadId, randomizable: false, duration: 60),
            types,
            Array.Empty<WorldConsumableCount>(),
            Usage(threadId, engaged: true));
        var configuration = Configuration(allowlist: threadId.ToString("D"));

        var relic = Assert.Single(Plan(buffed, configuration, out _));
        var nothing = Plan(relicSpent, configuration, out _);

        Assert.Equal(relicId, relic.ItemId);
        Assert.Empty(nothing);
    }

    [Fact]
    public void AScrollThatFoundNoTargetStopsBlockingTheScrollsAfterIt()
    {
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000060");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000061");
        var world = World(
            Consumable(firstId, randomizable: true),
            Consumable(secondId, randomizable: true),
            new[]
            {
                new WorldConsumableType(firstId, KnownEntities.ConsumableScrollType.Uuid),
                new WorldConsumableType(secondId, KnownEntities.ConsumableScrollType.Uuid),
            },
            new[]
            {
                new WorldConsumableCount(firstId, 1, 1, 0),
                new WorldConsumableCount(secondId, 1, 1, 0),
            });
        var configuration = Configuration();
        var state = AutoItemsCycleState.Create(new LifecycleGeneration(1));
        state.ObserveConfiguration(new ConfigGeneration(1), configuration.AutoItems);
        var targetless = Refused(AutoItemsActionResultCodes.TargetUnavailable);

        var first = Assert.Single(Evaluate(world, configuration, ref state, default));
        var second = Assert.Single(Evaluate(world, configuration, ref state, targetless));
        var retried = Assert.Single(Evaluate(world, configuration, ref state, targetless));

        Assert.Equal(firstId, first.ItemId);
        Assert.Equal(secondId, second.ItemId);
        Assert.Equal(firstId, retried.ItemId);
    }

    [Fact]
    public void DisabledAndIdlePathsWaitForPublicationWithoutTimers()
    {
        var disabled = Configuration(AutoItemsOperationMode.Disabled);
        var world = new GameWorldState { CollectedAtEpoch = 9 };

        var disabledActions = Plan(world, disabled, out var disabledWake);
        var idleActions = Plan(world, Configuration(), out var idleWake);

        Assert.Empty(disabledActions);
        Assert.Empty(idleActions);
        Assert.Equal(WakePolicy.OnPublication, disabledWake);
        Assert.Equal(WakePolicy.OnPublication, idleWake);
    }

    private static IReadOnlyList<AutoItemsCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration configuration,
        out WakePolicy wake)
    {
        var state = AutoItemsCycleState.Create(Identity().Lifecycle);
        state.ObserveConfiguration(Identity().Config, configuration.AutoItems);
        return Evaluate(world, configuration, ref state, default, out wake);
    }

    private static IReadOnlyList<AutoItemsCycleAction> Evaluate(
        GameWorldState world,
        SuiteRuntimeConfiguration configuration,
        ref AutoItemsCycleState state,
        BatchReceipt previousReceipt) =>
        Evaluate(world, configuration, ref state, previousReceipt, out _);

    private static IReadOnlyList<AutoItemsCycleAction> Evaluate(
        GameWorldState world,
        SuiteRuntimeConfiguration configuration,
        ref AutoItemsCycleState state,
        BatchReceipt previousReceipt,
        out WakePolicy wake)
    {
        var store = new ReusableActionStore<AutoItemsCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoItemsCycleAction>(store);
        var context = new ServiceCycleContext(
            Identity(),
            previousReceipt,
            new MonotonicTimestamp(1));
        wake = AutoItemsCycleEvaluator.Evaluate(
            world,
            in configuration,
            in context,
            ref state,
            writer,
            out _);
        var actions = new List<AutoItemsCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }
        return actions;
    }

    private static ServiceCycleIdentity Identity() =>
        new(
            AutoItemsServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            StrategyGeneration.Initial,
            new WorldGeneration(1),
            new CycleId(1));

    private static BatchReceipt Refused(ServiceActionResultCode code) =>
        BatchReceipt.Terminated(
            Identity(),
            new BatchId(1),
            actionCount: 1,
            committedCount: 0,
            terminalIndex: 0,
            ServiceActionResult.Rejected(code),
            default,
            new MonotonicTimestamp(2));

    private static WorldConsumableUsage Usage(Guid itemId, bool engaged) =>
        new(itemId, Guid.NewGuid(), 1, engaged, new BigDouble(30), new BigDouble(60));

    private static GameWorldState World(
        WorldConsumable first,
        WorldConsumable? second,
        WorldConsumableType[] types,
        WorldConsumableCount[] counts,
        params WorldConsumableUsage[] usages)
    {
        var consumables = second.HasValue
            ? new[] { first, second.Value }
            : new[] { first };
        var costs = new WorldConsumableCost[consumables.Length];
        for (var index = 0; index < consumables.Length; index++)
        {
            costs[index] = new WorldConsumableCost(
                consumables[index].ConsumableId,
                WorldConsumableCostKind.Consume,
                KnownEntities.PotionToxicity.Uuid,
                new BigDouble(1));
        }
        return new GameWorldState
        {
            CollectionCategories = CleanConsumables(),
            Consumables = WorldTable.Create(consumables),
            ConsumableTypes = PublicationTable<WorldConsumableType>.Create(types),
            ConsumableCounts = PublicationTable<WorldConsumableCount>.Create(counts),
            ConsumableCosts = PublicationTable<WorldConsumableCost>.Create(costs),
            ConsumableUsages = PublicationTable<WorldConsumableUsage>.Create(usages),
            Resources = WorldTable.Create(Toxicity()),
            CollectedAtEpoch = 9,
        };
    }

    private static WorldConsumable Consumable(
        Guid id,
        bool randomizable,
        double duration = 0,
        int quantity = 1)
    {
        var modifiers = default(RawConsumableModifiers);
        return new WorldConsumable(
            id,
            visible: true,
            randomized: false,
            quantity,
            queuedQuantity: 0,
            maximumCarryLoad: 10,
            gainedSince: 0,
            maxCreatedLevel: 1,
            currentPrepTime: BigDouble.Zero,
            currentCooldown: BigDouble.Zero,
            currentCooldownTime: BigDouble.Zero,
            in modifiers,
            preparationTime: 1,
            canBeRandomized: randomizable,
            hasDuration: duration > 0,
            durationBase: duration,
            queueOnStart: false);
    }

    private static WorldResource Toxicity()
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = new RawResourceTraits(
            0, 0, 0, false, false, false, false, true, false, true,
            BigDouble.Zero, 0, 0, 0, false, 0,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            KnownEntities.PotionToxicity.Uuid,
            new BigDouble(100),
            new BigDouble(100),
            visible: true,
            lifetimeQuantity: BigDouble.Zero,
            discoveryTime: BigDouble.Zero,
            quality: new BigDouble(100),
            gainRate: new BigDouble(100),
            drain: BigDouble.Zero,
            reservation: BigDouble.Zero,
            usage: BigDouble.Zero,
            inLossMode: false,
            inRestMode: false,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in reading,
            isCapped: true,
            headroom: BigDouble.Zero,
            fillFraction: 1,
            isAtCapacity: true,
            trueQuantity: new BigDouble(100),
            trueRate: BigDouble.Zero);
    }

    private static PublicationTable<WorldCollectionCategoryStatus> CleanConsumables() =>
        PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus("consumables", WorldCategoryOutcome.Collected, 1, 0, default),
        });

    private static SuiteRuntimeConfiguration Configuration(
        AutoItemsOperationMode mode = AutoItemsOperationMode.Active,
        string allowlist = "") =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = mode,
                UseRelics = true,
                UseScrolls = true,
                TemporaryItemAllowlist = allowlist,
            },
        };
}
