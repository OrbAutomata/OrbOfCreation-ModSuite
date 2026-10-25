using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorConsumableTests : GameWorldCollectorTestBase
{

    /// <summary>
    /// Stock is the case that proves the point of D17: <c>ConsumableSO</c> keeps it in a private
    /// cached int, while the save record stores a list of per-level counts and rebuilds the total on
    /// load. Building the row from the save record left every item count out of the snapshot.
    /// </summary>
    [Fact]
    public void AConsumableCarriesItsStockEvenThoughTheSaveRecordDoesNot()
    {
        var potion = Guid.NewGuid();

        ConsumableSO.All.Add(new ConsumableSO
        {
            Identity = potion,
            visible = true,
            Quantity = 5,
            QueuedQuantity = 2,
            GainedSince = 3,
            maxCreatedLv = 4,
            MaximumCarryLoad = 12,
            prepSpeed = new ValueModifierRecord(new BigDouble(150d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Consumables, potion, out var row));
        Assert.True(row.Visible);
        Assert.Equal(5, row.Quantity);
        Assert.Equal(2, row.QueuedQuantity);
        Assert.Equal(3, row.GainedSince);
        Assert.Equal(4, row.MaxCreatedLevel);
        Assert.Equal(12, row.MaximumCarryLoad);
        Assert.Equal(150d, row.Modifiers.PrepSpeed.ToDouble());
    }

    /// <summary>
    /// A consumable whose read throws anything but an expected native failure faults the pass instead
    /// of costing one consumable.
    /// </summary>
    [Fact]
    public void AConsumableWhoseReadThrowsUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("consumable was destroyed");
        ConsumableSO.All.Add(new ConsumableSO { GetGuidFailure = failure });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// An inventory slot whose read throws anything but an expected native failure faults the pass
    /// instead of costing the inventory.
    /// </summary>
    [Fact]
    public void AnInventorySlotWhoseReadThrowsUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("slot consumable was destroyed");
        Inventory.Current.allConsumables.value.Add(
            new ConsumableSO { GetGuidFailure = failure });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void ConsumableInventoryPublishesBothOrderedListsAndFrameLocalUseAdmission()
    {
        var first = new ConsumableSO { Identity = Guid.NewGuid(), visible = true, Quantity = 2 };
        var second = new ConsumableSO { Identity = Guid.NewGuid(), visible = true, Quantity = 1 };
        ConsumableSO.All.Add(first);
        ConsumableSO.All.Add(second);
        Inventory.Current.allConsumables.value.Add(first);
        Inventory.Current.allConsumables.value.Add(null!);
        Inventory.Current.allConsumables.value.Add(second);
        Inventory.Current.allConsumables.Maximum = 12;
        Inventory.Current.hotBar.value.Add(second);
        Inventory.Current.hotBar.Maximum = 4;
        Inventory.Preparing = true;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.False(world.ConsumableInventory.CanUse);
        Assert.Equal(12, world.ConsumableInventory.InventoryMaximum);
        Assert.Equal(4, world.ConsumableInventory.HotbarMaximum);
        var slots = world.ConsumableInventory.Slots.AsSpan().ToArray();
        Assert.Equal(4, slots.Length);
        Assert.Equal(
            new[] { first.Identity, Guid.Empty, second.Identity, second.Identity },
            slots.Select(slot => slot.ConsumableId));
        Assert.Equal(
            new[]
            {
                WorldConsumableListKind.Inventory,
                WorldConsumableListKind.Inventory,
                WorldConsumableListKind.Inventory,
                WorldConsumableListKind.Hotbar,
            },
            slots.Select(slot => slot.List));
        Assert.Equal(4, report.For("consumable inventory").Sampled);
    }

    [Fact]
    public void AConsumablePublishesEveryNativeFamilyCostUsageAndCount()
    {
        var item = Guid.NewGuid();
        var extraFamily = Guid.NewGuid();
        var toxicity = KnownEntities.PotionToxicity.Uuid;
        var durationResource = Guid.NewGuid();
        var consumable = new ConsumableSO { Identity = item, Quantity = 1 };
        consumable.consumableTypes.Add(
            new ConsumableTypeSO { Identity = KnownEntities.ConsumableScrollType.Uuid });
        consumable.consumableTypes.Add(new ConsumableTypeSO { Identity = extraFamily });
        consumable.consumeCost.costs.Add(new ResourceTuple(toxicity, 2d));
        consumable.usageCost.costs.Add(new ResourceTuple(durationResource, 3d));
        var usage = new ConsumableUsage
        {
            baseSi = new ScalingInfo { Level = 7 },
            en = true,
            dr = new BigDouble(11d),
            maxDr = new BigDouble(12d),
        };
        consumable.consumableUsages.Add(usage);
        consumable.consumableCounts.Add(new ConsumableCount
        {
            Level = 7,
            Quantity = 4,
            fr = 3,
        });
        ConsumableSO.All.Add(consumable);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldConsumableTypeLookup.TryFindRange(
            world.ConsumableTypes, item, out var typeStart, out var typeCount));
        Assert.Equal(2, typeCount);
        var publishedTypes = new HashSet<Guid>();
        for (var index = 0; index < typeCount; index++)
            publishedTypes.Add(world.ConsumableTypes[typeStart + index].TypeId);
        Assert.Contains(KnownEntities.ConsumableScrollType.Uuid, publishedTypes);
        Assert.Contains(extraFamily, publishedTypes);

        Assert.True(WorldConsumableCostLookup.TryFindRange(
            world.ConsumableCosts,
            item,
            WorldConsumableCostKind.Consume,
            out var consumeStart,
            out var consumeCount));
        Assert.Equal(1, consumeCount);
        Assert.Equal(toxicity, world.ConsumableCosts[consumeStart].ResourceId);
        Assert.Equal(2d, world.ConsumableCosts[consumeStart].Amount.ToDouble());

        Assert.True(WorldConsumableUsageLookup.TryFindRange(
            world.ConsumableUsages, item, out var usageStart, out var usageCount));
        Assert.Equal(1, usageCount);
        Assert.Equal(usage.Identity, world.ConsumableUsages[usageStart].UsageId);
        Assert.Equal(7, world.ConsumableUsages[usageStart].Level);
        Assert.True(world.ConsumableUsages[usageStart].Engaged);

        Assert.True(WorldConsumableCountLookup.TryFindRange(
            world.ConsumableCounts, item, out var countStart, out var countCount));
        Assert.Equal(1, countCount);
        Assert.Equal(7, world.ConsumableCounts[countStart].Level);
        Assert.Equal(4, world.ConsumableCounts[countStart].Quantity);
        Assert.Equal(3, world.ConsumableCounts[countStart].FreeQuantity);
    }

    /// <summary>
    /// Whether an item can be fired is composed from the four terms the world already holds, not
    /// asked of the game.
    /// </summary>
    /// <remarks>
    /// <c>ConsumableSO.CanFire()</c> is
    /// <c>!IsOnCooldown() &amp;&amp; HasEnoughUsage() &amp;&amp; HasEnoughCost() &amp;&amp; quantity &gt; 0</c>, and each
    /// case here holds three of those terms and breaks the fourth, so a composition that dropped
    /// any one of them would publish the wrong verdict for exactly one case.
    /// </remarks>
    [Theory]
    [InlineData(1, 0d, 500d, 500d, true)]
    [InlineData(0, 0d, 500d, 500d, false)]
    [InlineData(1, 4d, 500d, 500d, false)]
    [InlineData(1, 0d, 1d, 500d, false)]
    [InlineData(1, 0d, 500d, 1d, false)]
    public void WhetherAnItemCanFireIsComposedFromTheTermsTheWorldAlreadyHolds(
        int quantity,
        double cooldown,
        double consumeStock,
        double usageStock,
        bool expected)
    {
        var item = Guid.NewGuid();
        var consumeResource = Guid.NewGuid();
        var usageResource = Guid.NewGuid();
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = consumeResource,
            Quantity = new BigDouble(consumeStock),
        });
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = usageResource,
            Quantity = new BigDouble(usageStock),
        });
        var consumable = new ConsumableSO
        {
            Identity = item,
            Quantity = quantity,
            currentCooldown = new BigDouble(cooldown),
        };
        consumable.consumeCost.costs.Add(new ResourceTuple(consumeResource, 250d));
        consumable.usageCost.costs.Add(new ResourceTuple(usageResource, 250d));
        ConsumableSO.All.Add(consumable);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Consumables, item, out var published));
        Assert.Equal(expected, published.CanFire);
    }

    /// <summary>
    /// A cost or family with no identity is unreadable, not absent: the whole consumable is skipped
    /// by name rather than published with a shorter price than the game charges.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AConsumableRelationWithoutAnIdentitySkipsTheWholeItem(bool cost)
    {
        var item = Guid.NewGuid();
        var consumable = new ConsumableSO { Identity = item, Quantity = 1 };
        consumable.consumableTypes.Add(
            new ConsumableTypeSO { Identity = cost ? Guid.NewGuid() : Guid.Empty });
        consumable.consumeCost.costs.Add(new ResourceTuple(KnownEntities.PotionToxicity.Uuid, 2d));
        consumable.consumeCost.costs.Add(new ResourceTuple(cost ? Guid.Empty : Guid.NewGuid(), 5d));
        ConsumableSO.All.Add(consumable);

        var collector = Collector();
        var report = collector.Collect().For("consumables");
        var world = collector.Build();

        Assert.Equal(1, report.Skipped);
        Assert.Equal(
            cost
                ? "a consumable cost at position 1 was null or unidentified"
                : "a consumable type at position 0 was null or unidentified",
            report.FirstFailure);
        Assert.False(WorldLookup.TryFind(world.Consumables, item, out _));
        Assert.Equal(0, world.ConsumableCosts.Count);
    }

    [Fact]
    public void AnUnreadableConsumableRelationSkipsTheWholeItem()
    {
        var item = Guid.NewGuid();
        ConsumableSO.All.Add(new ConsumableSO
        {
            Identity = item,
            Quantity = 1,
            consumableTypes = null!,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.False(report.IsComplete);
        Assert.Contains("type list was null", report.For("consumables").FirstFailure);
        Assert.False(WorldLookup.TryFind(world.Consumables, item, out _));
        Assert.Equal(0, world.ConsumableTypes.Count);
        Assert.Equal(0, world.ConsumableCosts.Count);
        Assert.Equal(0, world.ConsumableUsages.Count);
        Assert.Equal(0, world.ConsumableCounts.Count);
    }
}
