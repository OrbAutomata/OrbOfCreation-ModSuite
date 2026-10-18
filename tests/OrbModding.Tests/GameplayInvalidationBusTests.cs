using System;
using System.Collections.Generic;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class GameplayInvalidationBusTests
{
    /// <summary>
    /// The bus routes by domain without interpreting it, so these tests name one of their own
    /// rather than borrowing a feature's — a domain retiring with its feature must not take the
    /// coalescing and budget coverage with it.
    /// </summary>
    private const string TestDomain = "test.domain";

    [Fact]
    public void ChangeKindsCoverRequiredSuiteDependencies()
    {
        Assert.Equal(
            GameplayInvalidationKind.All,
            GameplayInvalidationKind.Lifecycle |
            GameplayInvalidationKind.Progression |
            GameplayInvalidationKind.ResourceQuantity |
            GameplayInvalidationKind.ResourceRate |
            GameplayInvalidationKind.Queue |
            GameplayInvalidationKind.Inventory |
            GameplayInvalidationKind.Registry |
            GameplayInvalidationKind.Configuration);
    }

    [Fact]
    public void LargeSameBurstCoalescesByStableTargetAndMergesKinds()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Queue | GameplayInvalidationKind.Progression,
                TestDomain),
            received.Add);

        for (var index = 0; index < 10_000; index++)
        {
            var kind = index % 2 == 0
                ? GameplayInvalidationKind.Queue
                : GameplayInvalidationKind.Progression;
            Assert.True(bus.Publish(
                kind,
                burst: 5,
                TestDomain,
                "structure-uuid",
                "StructureSO",
                source: "burst-" + index));
        }

        Assert.Empty(received);
        var result = bus.Pump(currentBurstExclusive: 6, maxOperationsPerFrame: 16);

        var change = Assert.Single(received);
        Assert.Equal(
            GameplayInvalidationKind.Queue | GameplayInvalidationKind.Progression,
            change.Kinds);
        Assert.Equal("structure-uuid", change.EntityId);
        Assert.Equal("StructureSO", change.ExpectedTypeName);
        Assert.Equal(9_999, change.CoalescedCount);
        Assert.Equal("burst-9999", change.Source);
        Assert.Equal(1, result.CompletedEvents);
        Assert.Equal(10_000, bus.GetSnapshot().Published);
        Assert.Equal(9_999, bus.GetSnapshot().Coalesced);
    }

    [Fact]
    public void CurrentBurstWaitsAndDistinctBurstsRemainDistinct()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Queue),
            received.Add);

        bus.Publish(GameplayInvalidationKind.Queue, burst: 7, source: "first");
        bus.Publish(GameplayInvalidationKind.Queue, burst: 8, source: "second");

        bus.Pump(currentBurstExclusive: 7, maxOperationsPerFrame: 16);
        Assert.Empty(received);
        bus.Pump(currentBurstExclusive: 8, maxOperationsPerFrame: 16);
        Assert.Equal(new[] { "first" }, received.ConvertAll(change => change.Source));
        bus.Pump(currentBurstExclusive: 9, maxOperationsPerFrame: 16);
        Assert.Equal(new[] { "first", "second" }, received.ConvertAll(change => change.Source));
    }

    [Fact]
    public void BroadDomainChangeDominatesNarrowChangesInSameBurst()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Inventory),
            received.Add);

        bus.Publish(GameplayInvalidationKind.Inventory, 3, GameplayInvalidationDomains.AutomataConcepts, "one", "AlchemyRecipeSO");
        bus.Publish(GameplayInvalidationKind.Inventory, 3, GameplayInvalidationDomains.AutomataConcepts, "two", "AlchemyRecipeSO");
        bus.Publish(GameplayInvalidationKind.Inventory, 3, GameplayInvalidationDomains.AutomataConcepts, source: "broad");
        bus.Publish(GameplayInvalidationKind.Inventory, 3, GameplayInvalidationDomains.AutomataConcepts, "three", "AlchemyRecipeSO");
        bus.Pump(4, 32);

        var change = Assert.Single(received);
        Assert.True(change.IsBroad);
        Assert.Equal(GameplayInvalidationKind.Inventory, change.Kinds);
        Assert.Equal(3, change.CoalescedCount);
        Assert.Equal("broad", change.Source);
        var snapshot = bus.GetSnapshot();
        Assert.Equal(2, snapshot.Superseded);
        Assert.Equal(1, snapshot.Coalesced);
    }

    [Fact]
    public void BroaderTargetPreservesKindsFromSupersededNarrowWork()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.All),
            received.Add);

        bus.Publish(GameplayInvalidationKind.Queue, 4, TestDomain, "one", "StructureSO");
        bus.Publish(GameplayInvalidationKind.Inventory, 4, GameplayInvalidationDomains.AutomataConcepts, source: "between");
        bus.Publish(GameplayInvalidationKind.Progression, 4, TestDomain, source: "broad");
        bus.Pump(5, 16);

        Assert.Equal(2, received.Count);
        var change = received[0];
        Assert.Equal(
            GameplayInvalidationKind.Queue | GameplayInvalidationKind.Progression,
            change.Kinds);
        Assert.Equal(1, change.CoalescedCount);
        Assert.Equal("broad", change.Source);
        Assert.Equal("between", received[1].Source);
    }

    [Fact]
    public void TargetedEventWakesBroadAndMatchingDependencyOnly()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var broad = 0;
        var matching = 0;
        var unrelated = 0;
        using var broadSubscription = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.ResourceQuantity,
                TestDomain),
            _ => broad++);
        using var matchingSubscription = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.ResourceQuantity,
                TestDomain,
                "mana",
                "ResourceSO"),
            _ => matching++);
        using var unrelatedSubscription = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.ResourceQuantity,
                TestDomain,
                "knowledge",
                "ResourceSO"),
            _ => unrelated++);

        bus.Publish(
            GameplayInvalidationKind.ResourceQuantity,
            2,
            TestDomain,
            "mana",
            "ResourceSO");
        bus.Pump(3, 32);

        Assert.Equal(1, broad);
        Assert.Equal(1, matching);
        Assert.Equal(0, unrelated);
    }

    [Fact]
    public void DomainWideEventWakesExactSubscribersAndPreservesFirstPublicationOrder()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var order = new List<string>();
        using var exact = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Inventory,
                GameplayInvalidationDomains.AutomataConcepts,
                "concept-one",
                "AlchemyRecipeSO"),
            change => order.Add(change.Source));

        bus.Publish(
            GameplayInvalidationKind.Inventory,
            2,
            GameplayInvalidationDomains.AutomataConcepts,
            "concept-one",
            "AlchemyRecipeSO",
            "first");
        bus.Publish(
            GameplayInvalidationKind.Inventory,
            2,
            GameplayInvalidationDomains.AutomataConcepts,
            "concept-two",
            "AlchemyRecipeSO",
            "unrelated");
        bus.Publish(
            GameplayInvalidationKind.Inventory,
            3,
            GameplayInvalidationDomains.AutomataConcepts,
            source: "broad-next-burst");
        bus.Pump(4, 32);

        Assert.Equal(new[] { "first", "broad-next-burst" }, order);
    }

    [Fact]
    public void LifecycleTransitionClearsOldAndPartialWorkAndRejectsLateGeneration()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var first = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.All),
            received.Add);
        using var second = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.All),
            received.Add);
        bus.Publish(GameplayInvalidationKind.Queue, 0, TestDomain, "old", "StructureSO");
        bus.Pump(1, 2);
        Assert.Single(received);

        Assert.True(monitor.TryObserve(
            new GameLifecycleObservation(
                GameLifecycleTransitionKind.RuntimeReady,
                1,
                "Main",
                "test"),
            out _,
            out _));
        Assert.False(bus.TryPublish(
            new GameplayInvalidationRequest(
                GameplayInvalidationKind.Queue,
                lifecycleGeneration: 0,
                burst: 1,
                TestDomain,
                "late",
                "StructureSO"),
            out var reason));
        bus.Pump(2, 32);

        Assert.Equal(3, received.Count);
        Assert.All(received.GetRange(1, 2), change =>
        {
            Assert.Equal(GameplayInvalidationKind.Lifecycle, change.Kinds);
            Assert.Equal(1, change.LifecycleGeneration);
        });
        Assert.Contains("stale", reason, StringComparison.Ordinal);
        Assert.Equal(2, bus.GetSnapshot().StaleDiscarded);
    }

    [Fact]
    public void NewestLifecycleBarrierSupersedesEarlierTransition()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Lifecycle),
            received.Add);

        Assert.True(monitor.TryObserve(
            new GameLifecycleObservation(GameLifecycleTransitionKind.RuntimeReady, 1, "Main", "ready"),
            out _, out _));
        Assert.True(monitor.TryObserve(
            new GameLifecycleObservation(GameLifecycleTransitionKind.SaveLoaded, 2, "Main", "load"),
            out _, out _));
        bus.Pump(3, 16);

        var lifecycle = Assert.Single(received);
        Assert.Equal(2, lifecycle.LifecycleGeneration);
        Assert.Equal("load", lifecycle.Source);
        Assert.Equal(1, bus.GetSnapshot().StaleDiscarded);
    }

    [Fact]
    public void OperationBudgetResumesSubscriberDeliveryWithoutReordering()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var order = new List<int>();
        using var first = bus.Subscribe(new GameplayInvalidationFilter(GameplayInvalidationKind.Queue), _ => order.Add(1));
        using var second = bus.Subscribe(new GameplayInvalidationFilter(GameplayInvalidationKind.Queue), _ => order.Add(2));
        using var third = bus.Subscribe(new GameplayInvalidationFilter(GameplayInvalidationKind.Queue), _ => order.Add(3));
        bus.Publish(GameplayInvalidationKind.Queue, burst: 0);

        var sliceOne = bus.Pump(1, 2);
        var sameFrame = bus.Pump(1, 2);
        var sliceTwo = bus.Pump(2, 2);
        var sliceThree = bus.Pump(3, 2);

        Assert.Equal(new[] { 1, 2, 3 }, order);
        Assert.Equal(2, sliceOne.Operations);
        Assert.True(sliceOne.BudgetExhausted);
        Assert.Equal(0, sameFrame.Operations);
        Assert.Equal(2, sliceTwo.Operations);
        Assert.False(sliceThree.BudgetExhausted);
        Assert.Equal(0, sliceThree.PendingCount);
    }

    [Fact]
    public void SingleRemainingOperationStartsEventButDefersSubscriberToNextFrame()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var calls = 0;
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Queue),
            _ => calls++);
        bus.Publish(GameplayInvalidationKind.Queue, burst: 0);

        var firstFrame = bus.Pump(1, 1);
        var repeatedPump = bus.Pump(1, 1);
        var nextFrame = bus.Pump(2, 1);

        Assert.Equal(1, firstFrame.Operations);
        Assert.True(firstFrame.BudgetExhausted);
        Assert.Equal(0, repeatedPump.Operations);
        Assert.Equal(1, nextFrame.Operations);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void CallbackPublicationWaitsForNextPumpFrame()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var order = new List<string>();
        using var publisher = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Queue),
            change =>
            {
                order.Add(change.Source);
                if (change.Source == "first")
                    bus.Publish(
                        GameplayInvalidationKind.Queue,
                        burst: 0,
                        TestDomain,
                        "second",
                        "StructureSO",
                        source: "callback-coalesced");
            });

        bus.Publish(
            GameplayInvalidationKind.Queue,
            burst: 0,
            TestDomain,
            "first",
            "StructureSO",
            source: "first");
        bus.Publish(
            GameplayInvalidationKind.Queue,
            burst: 0,
            TestDomain,
            "second",
            "StructureSO",
            source: "second-pending");
        bus.Pump(1, 16);
        Assert.Equal(new[] { "first" }, order);
        bus.Pump(2, 16);
        Assert.Equal(new[] { "first", "callback-coalesced" }, order);
    }

    [Fact]
    public void CapacityOverflowPromotesToConservativeGlobalInvalidation()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, capacity: 2, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Queue),
            received.Add);
        bus.Publish(GameplayInvalidationKind.Queue, 1, "one");
        bus.Publish(GameplayInvalidationKind.Queue, 1, "two");
        bus.Publish(GameplayInvalidationKind.Queue, 1, "three");
        bus.Pump(2, 16);

        var change = Assert.Single(received);
        Assert.Equal(GameplayInvalidationKind.All, change.Kinds);
        Assert.True(change.IsBroad);
        var snapshot = bus.GetSnapshot();
        Assert.Equal(1, snapshot.OverflowPromotions);
        Assert.Equal(2, snapshot.OverflowDiscarded);
        Assert.InRange(snapshot.PeakPendingCount, 1, 2);
    }

    [Fact]
    public void SubscriberFailureIsIsolatedAndLogsWhatThrew()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var log = new List<string>();
        bus.ConfigureFailureLog(log.Add);
        var healthyCalls = 0;
        using var failing = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Configuration),
            _ => throw new InvalidOperationException("boom"),
            "failing");
        using var healthy = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Configuration),
            _ => healthyCalls++,
            "healthy");
        bus.Publish(GameplayInvalidationKind.Configuration, burst: 0);
        bus.Pump(1, 16);

        Assert.Equal(1, healthyCalls);
        var failure = Assert.Single(log);
        Assert.Contains("subscriber failing threw handling Configuration", failure, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: boom", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void PerFrameOperationCapStopsBurstDeliveryAndResumesLosslessly()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(
            monitor,
            capacity: 256,
            readThreadId: () => 1);
        var observed = new List<string>();
        var secondMatchingCalls = 0;
        var nonMatchingCalls = 0;
        using var firstMatching = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Queue,
                TestDomain),
            change =>
            {
                observed.Add(change.EntityId);
            },
            "first-matching");
        using var secondMatching = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Queue),
            _ => secondMatchingCalls++,
            "second-matching");
        using var nonMatching = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Inventory),
            _ => nonMatchingCalls++,
            "non-matching");
        for (var index = 0; index < 128; index++)
        {
            bus.Publish(
                GameplayInvalidationKind.Queue,
                burst: 0,
                TestDomain,
                $"entity-{index:D3}",
                "StructureSO");
        }

        var firstSlice = bus.Pump(1, 8);
        Assert.Equal(8, firstSlice.Operations);
        Assert.Equal(1, firstSlice.CompletedEvents);
        Assert.True(firstSlice.BudgetExhausted);
        Assert.True(firstSlice.PendingCount > 0);
        var blockedSameFrame = bus.Pump(1, 8);
        Assert.Equal(0, blockedSameFrame.Operations);
        Assert.True(blockedSameFrame.BudgetExhausted);

        for (var frame = 2; bus.GetSnapshot().PendingCount > 0 && frame < 100; frame++)
            bus.Pump(frame, 8);

        Assert.Equal(0, bus.GetSnapshot().PendingCount);
        Assert.Equal(128, observed.Count);
        Assert.Equal(128, secondMatchingCalls);
        Assert.Equal(0, nonMatchingCalls);
        for (var index = 0; index < observed.Count; index++)
            Assert.Equal($"entity-{index:D3}", observed[index]);

        Assert.Equal(512, bus.GetSnapshot().DeliveryOperations);
    }

    [Fact]
    public void PublishingAloneDoesNotRunDelivery()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var calls = 0;
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Queue),
            _ => calls++);

        bus.Publish(GameplayInvalidationKind.Queue, burst: 0);

        Assert.Equal(0, calls);
        Assert.Equal(1, bus.GetSnapshot().PendingCount);
        Assert.Equal(0, bus.GetSnapshot().DeliveryOperations);
    }

    [Fact]
    public void OffThreadPublishFailsBeforeQueueMutation()
    {
        var thread = 1;
        var monitor = new GameLifecycleMonitor(() => thread);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => thread);
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Queue),
            _ => { });
        thread = 2;

        Assert.Throws<InvalidOperationException>(() => bus.Publish(GameplayInvalidationKind.Queue, burst: 0));
        thread = 1;
        var snapshot = bus.GetSnapshot();
        Assert.Equal(0, snapshot.PendingCount);
        Assert.Equal(1, snapshot.OffThreadRejections);
    }

}
