using System;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The frame is the piece that has to satisfy two masters at once: it carries mutable per-cycle
/// buffers, and it crosses to a worker thread. These pin that it really does.
/// </summary>
public sealed class GameWorldCycleFrameTests
{
    /// <summary>
    /// The reason the frame exists as its own type. The collector cannot be a frame — it is almost
    /// entirely compiled delegates, which a frame may not hold — so if this ever stops passing, the
    /// split was undone and collection can no longer be a registered service.
    /// </summary>
    [Fact]
    public void TheFrameIsAcceptedAsAServiceFrame()
    {
        ServiceCycleTypeSafetyValidator.EnsureServiceTypes<
            AutomataWorldCollectionState,
            AutomataWorldCollectionAction>();
    }

    [Fact]
    public void TheSnapshotIsStillAcceptedAsAPublishedWorld()
    {
        ServiceCycleTypeSafetyValidator.EnsureWorldType<GameWorldState>();
    }

    /// <summary>
    /// Derivation must read the frame and nothing else. A frame that has never been captured into
    /// derives to an empty snapshot rather than throwing, because a service's first cycle can reach
    /// the worker before the game has anything to give.
    /// </summary>
    [Fact]
    public void AnUntouchedFrameDerivesToAnEmptySnapshot()
    {
        var world = GameWorldFrameDeriver.Build(new GameWorldCycleFrame());

        Assert.Equal(0, world.Resources.Count);
        Assert.Equal(0, world.Structures.Count);
        Assert.Equal(0, world.TreasurePools.Count);
        Assert.Equal(0d, world.FixedDeltaTime);
    }

    /// <summary>
    /// The buffer is reused across cycles, so the snapshot built from one cycle must not change when
    /// the next cycle overwrites it. This is the same bargain the published tables make, checked one
    /// level lower — at the buffer the tables are built from.
    /// </summary>
    [Fact]
    public void ASnapshotSurvivesTheNextCycleOverwritingTheFrame()
    {
        var mana = Guid.NewGuid();
        var frame = new GameWorldCycleFrame();

        var first = WorldSamples.Resource(mana, 10d);
        frame.Resources.Append(in first);
        var pinned = GameWorldFrameDeriver.Build(frame);

        frame.Resources.Reset();
        var second = WorldSamples.Resource(mana, 999d);
        frame.Resources.Append(in second);
        GameWorldFrameDeriver.Build(frame);

        Assert.True(WorldLookup.TryFind(pinned.Resources, mana, out var row));
        Assert.Equal(10d, row.Reading.Quantity.ToDouble());
    }

    [Fact]
    public void ResettingKeepsCapacitySoASteadyCycleStopsAllocating()
    {
        var frame = new GameWorldCycleFrame();
        for (var index = 0; index < 100; index++)
        {
            var sample = WorldSamples.Resource(Guid.NewGuid(), index);
            frame.Resources.Append(in sample);
        }

        Assert.Equal(100, frame.Resources.Count);

        frame.Resources.Reset();
        Assert.Equal(0, frame.Resources.Count);

        var reused = WorldSamples.Resource(Guid.NewGuid(), 1d);
        frame.Resources.Append(in reused);
        Assert.Equal(1, frame.Resources.Count);
    }

    /// <summary>
    /// Queue slots derive sorted by queue and then position, which is the invariant their lookup is
    /// a binary search because of.
    /// </summary>
    /// <remarks>
    /// The reader appends one queue's slots in order, so a table built from one pass looks sorted
    /// whatever the deriver does. Two queues appended interleaved is the case that tells them apart,
    /// and it is the case a second queue with slots would produce.
    /// </remarks>
    [Fact]
    public void QueueSlotsDeriveSortedByQueueThenPosition()
    {
        var first = Guid.Parse("11111111-0000-0000-0000-000000000000");
        var second = Guid.Parse("22222222-0000-0000-0000-000000000000");

        var frame = new GameWorldCycleFrame();
        frame.ActionQueueSlots.Append(new WorldActionQueueSlot(second, 1, true, default, default, 0, false));
        frame.ActionQueueSlots.Append(new WorldActionQueueSlot(first, 1, true, default, default, 0, false));
        frame.ActionQueueSlots.Append(new WorldActionQueueSlot(second, 0, true, default, default, 0, false));
        frame.ActionQueueSlots.Append(new WorldActionQueueSlot(first, 0, true, default, default, 0, false));

        var world = GameWorldFrameDeriver.Build(frame);

        Assert.True(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, second, out var start, out var count));
        Assert.Equal(2, count);
        Assert.Equal(0, world.ActionQueueSlots[start].Index);
        Assert.Equal(1, world.ActionQueueSlots[start + 1].Index);

        Assert.True(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, first, out var firstStart, out var firstCount));
        Assert.Equal(2, firstCount);
        Assert.Equal(0, firstStart);
        Assert.Equal(0, world.ActionQueueSlots[firstStart].Index);
    }

    [Fact]
    public void ConsumableRelationsDeriveIntoContiguousSortedRanges()
    {
        var first = Guid.Parse("11111111-0000-0000-0000-000000000000");
        var second = Guid.Parse("22222222-0000-0000-0000-000000000000");
        var lowType = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
        var highType = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");
        var lowResource = Guid.Parse("cccccccc-0000-0000-0000-000000000000");
        var highResource = Guid.Parse("dddddddd-0000-0000-0000-000000000000");
        var lowUsage = Guid.Parse("eeeeeeee-0000-0000-0000-000000000000");
        var highUsage = Guid.Parse("ffffffff-0000-0000-0000-000000000000");
        var frame = new GameWorldCycleFrame();

        frame.ConsumableTypes.Append(new WorldConsumableType(second, highType));
        frame.ConsumableTypes.Append(new WorldConsumableType(first, highType));
        frame.ConsumableTypes.Append(new WorldConsumableType(first, lowType));
        frame.ConsumableCosts.Append(new WorldConsumableCost(
            second, WorldConsumableCostKind.Consume, highResource, new BigDouble(1d)));
        frame.ConsumableCosts.Append(new WorldConsumableCost(
            first, WorldConsumableCostKind.Usage, highResource, new BigDouble(2d)));
        frame.ConsumableCosts.Append(new WorldConsumableCost(
            first, WorldConsumableCostKind.Consume, highResource, new BigDouble(3d)));
        frame.ConsumableCosts.Append(new WorldConsumableCost(
            first, WorldConsumableCostKind.Consume, lowResource, new BigDouble(4d)));
        frame.ConsumableUsages.Append(new WorldConsumableUsage(
            second, highUsage, 3, engaged: false, new BigDouble(7d), new BigDouble(8d)));
        frame.ConsumableUsages.Append(new WorldConsumableUsage(
            first, highUsage, 2, engaged: true, new BigDouble(5d), new BigDouble(6d)));
        frame.ConsumableUsages.Append(new WorldConsumableUsage(
            first, lowUsage, 1, engaged: false, new BigDouble(3d), new BigDouble(4d)));
        frame.ConsumableCounts.Append(new WorldConsumableCount(first, 7, 2, 1));
        frame.ConsumableCounts.Append(new WorldConsumableCount(first, 3, 5, 4));
        frame.ConsumableCounts.Append(new WorldConsumableCount(second, 2, 1, 1));

        var world = GameWorldFrameDeriver.Build(frame);

        Assert.True(WorldConsumableTypeLookup.TryFindRange(
            world.ConsumableTypes, first, out var typeStart, out var typeCount));
        Assert.Equal(2, typeCount);
        Assert.Equal(lowType, world.ConsumableTypes[typeStart].TypeId);
        Assert.Equal(highType, world.ConsumableTypes[typeStart + 1].TypeId);

        Assert.True(WorldConsumableCostLookup.TryFindRange(
            world.ConsumableCosts,
            first,
            WorldConsumableCostKind.Consume,
            out var costStart,
            out var costCount));
        Assert.Equal(2, costCount);
        Assert.Equal(lowResource, world.ConsumableCosts[costStart].ResourceId);
        Assert.Equal(highResource, world.ConsumableCosts[costStart + 1].ResourceId);

        Assert.True(WorldConsumableUsageLookup.TryFindRange(
            world.ConsumableUsages, first, out var usageStart, out var usageCount));
        Assert.Equal(2, usageCount);
        Assert.Equal(lowUsage, world.ConsumableUsages[usageStart].UsageId);
        Assert.True(world.ConsumableUsages[usageStart].Pending);
        Assert.Equal(highUsage, world.ConsumableUsages[usageStart + 1].UsageId);
        Assert.True(world.ConsumableUsages[usageStart + 1].Engaged);

        Assert.True(WorldConsumableCountLookup.TryFindRange(
            world.ConsumableCounts, first, out var countStart, out var countCount));
        Assert.Equal(2, countCount);
        Assert.Equal(3, world.ConsumableCounts[countStart].Level);
        Assert.Equal(7, world.ConsumableCounts[countStart + 1].Level);
        Assert.True(WorldConsumableCountLookup.TryGetStrongestOwnedLevel(
            world.ConsumableCounts, first, out var strongest));
        Assert.Equal(7, strongest);
    }

    /// <summary>
    /// The frame's rate globals have to reach the rows the deriver publishes. Nothing else would
    /// notice if they did not: the chain still returns a number, just the wrong one.
    /// </summary>
    [Fact]
    public void TheFramesFrameGlobalsReachThePublishedRows()
    {
        var resource = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000f");
        var rates = new RawResourceRateInputs(
            rate: new BigDouble(10d),
            rateSplash: default,
            rateMaxPercent: default,
            rateInterestPercent: default,
            rateMissingPercent: default,
            rateLifetimePercent: default,
            rateModifiers: 1,
            rateSplashModifiers: 0,
            rateMaxPercentModifiers: 0,
            rateInterestPercentModifiers: 0,
            rateMissingPercentModifiers: 0,
            rateLifetimePercentModifiers: 0,
            ratePassiveModifiers: 0,
            lossPercent: default,
            displayRate: default,
            calcRarityValue: new BigDouble(1d),
            baseLoss: 0d);

        BigDouble Publish(BigDouble overflowPercent)
        {
            var frame = new GameWorldCycleFrame
            {
                FrameGlobals = new WorldFrameGlobals(
                    overflowPercent, new BigDouble(1d), default, BigDouble.One, default, 0.02d),
            };
            frame.Resources.Append(WorldSamples.Resource(
                resource, quantity: 150d, capacity: 100d, quality: 100d, gainRate: 100d, drain: 0d,
                lifetimeQuantity: 400d, rateInputs: rates));

            var world = GameWorldFrameDeriver.Build(frame);
            Assert.True(WorldLookup.TryFind(world.Resources, resource, out var row));
            return row.TrueRate;
        }

        Assert.NotEqual(Publish(new BigDouble(2d)), Publish(new BigDouble(1d)));
    }

}
