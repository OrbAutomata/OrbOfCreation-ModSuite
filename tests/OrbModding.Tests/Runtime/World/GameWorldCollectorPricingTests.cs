using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorPricingTests : GameWorldCollectorTestBase
{

    [Fact]
    public void AnUpgradeWhoseRewardHasNoFreePedestalPublishesItsDestinationAsFull()
    {
        var pedestals = new ViewListVariable { Maximum = 0 };
        var aspect = new UpgradeSO { Identity = Guid.NewGuid() };
        aspect.viewListAdditions.Add(new ViewListVariable.ListTuple { list = pedestals });
        var space = new UpgradeSO { Identity = Guid.NewGuid() };
        UpgradeSO.All.Add(aspect);
        UpgradeSO.All.Add(space);

        var collector = Collector();
        var report = collector.Collect();
        var full = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(full.Upgrades, aspect.Identity, out var aspectRow));
        Assert.True(aspectRow.Reading.RewardDestinationFull);
        Assert.True(WorldLookup.TryFind(full.Upgrades, space.Identity, out var spaceRow));
        Assert.False(spaceRow.Reading.RewardDestinationFull);

        pedestals.Maximum = 1;
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().Upgrades, aspect.Identity, out aspectRow));
        Assert.False(aspectRow.Reading.RewardDestinationFull);
    }

    /// <summary>
    /// A cost entry whose resource throws something the game is not known to throw faults the pass
    /// rather than costing its structure's row.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingAStructureCostFaultsThePass()
    {
        var failure = new NullReferenceException("cost resource was destroyed");
        var structure = new StructureSO();
        structure.baseCost.costs.Add(new ResourceTuple
        {
            resource = new ResourceSO { GetGuidFailure = failure },
            Amount = new BigDouble(100d),
        });
        StructureSO.All.Add(structure);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// An upgrade cost entry whose resource throws unexpectedly faults the pass rather than costing
    /// its upgrade's row.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingAnUpgradeCostFaultsThePass()
    {
        var failure = new NullReferenceException("cost resource was destroyed");
        var upgrade = new UpgradeSO();
        upgrade.resourceCost.costs.Add(new ResourceTuple
        {
            resource = new ResourceSO { GetGuidFailure = failure },
            Amount = new BigDouble(50d),
        });
        UpgradeSO.All.Add(upgrade);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A structure's price is computed from what was collected, not asked of the game.
    /// </summary>
    /// <remarks>
    /// Every term is distinct and away from parity, so a chain that dropped one moves the answer
    /// instead of landing on a multiplier of one that was going to be one anyway. Derived by hand from
    /// the decompiled original, not from the port:
    /// <list type="number">
    /// <item>attribute: <c>100 × 200% = 200</c></item>
    /// <item>per-quantity <c>Raw 0.5</c>, scaled by cost scaling <c>300%</c> and two committed levels,
    /// is <c>Raw 3</c>, and Raw adds: <c>200 + 3 = 203</c></item>
    /// <item>the multiplier is <c>Max(120, 100 / (1 + 0.5 × 2)) × ((150 × 200%) as percent)</c>
    /// = <c>Max(120, 50) × 3 = 360</c>, whose percent is <c>3.6</c></item>
    /// <item><c>203 × 3.6 = 730.8</c>, which RoundToTwoSigsEarly leaves alone above 100</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void AStructuresPriceIsComputedFromWhatWasCollected()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        IntVariable.All.Add(new IntVariable
        {
            Identity = KnownEntities.BulkDevelopment.Uuid,
            value = new ValueModifierRecord(3d),
        });
        SetStructureCost(200d);
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new ValueModifierRecord(200d),
        });

        var modifier = new ValueModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new ValueModifier(ValueModifier.ValueModifierType.Raw, 0.5d, order: 0),
        };
        ValueModifierVariable.All.Add(modifier);
        StructureSO.All.Add(new StructureSO
        {
            Identity = cauldron,
            Level = 1,
            Queued = 1,
            passiveCostMod = new ValueModifierRecord(120d),
            activeCostMod = new ValueModifierRecord(150d),
            costScalingMod = new ValueModifierRecord(300d),
            costPerQuantity = new ValueModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out var count));
        Assert.Equal(1, count);

        var row = world.PurchaseCosts[start];
        Assert.Equal(water, row.ResourceId);
        Assert.Equal(100d, row.BaseExactAmount.ToDouble(), 6);
        Assert.Equal(730.8d, row.EffectiveExactAmount.ToDouble(), 6);
        Assert.Equal(730.8d, row.Amount.ToDouble(), 6);
        Assert.Equal(3, row.ExactGroupedLevels);
        Assert.Equal(2208.6d, row.ExactGroupedAmount.ToDouble(), 6);
        Assert.True(row.AffordabilityEvaluated);
        Assert.Equal(500d, row.AvailableAmount.ToDouble(), 6);
        Assert.Equal(730.8d, row.CombinedEffectiveAmount.ToDouble(), 6);
        Assert.False(row.ResourceAffordable);
        Assert.Equal("insufficient_quantity", row.ResourceAffordabilityReasonCode);
        Assert.False(row.Affordable);
        Assert.Equal("insufficient_quantity", row.AffordabilityReasonCode);

        var sources = row.ModifierSources.AsSpan().ToArray();
        Assert.Equal(10, sources.Length);
        Assert.Equal(
            new[]
            {
                "resource.attribute_cost_modifier",
                "resource.quality",
                "player.attribute_quality_bonus",
                "resource.effective_attribute_cost",
                "structure.cost_per_quantity",
                "structure.cost_scaling",
                "structure.passive_cost",
                "structure.active_cost",
                "player.structure_cost",
                "structure.committed_quantity",
            },
            sources.Select(source => source.Name).ToArray());
        var perQuantity = sources.Single(source => source.Name == "structure.cost_per_quantity");
        Assert.Equal(modifier.Identity, perQuantity.SourceId);
        Assert.Equal("ValueModifierVariable", perQuantity.SourceNativeType);
        Assert.True(perQuantity.HasModifierType);
        Assert.Equal((int)ValueModifier.ValueModifierType.Raw, perQuantity.ModifierType);
        Assert.Equal(0.5d, perQuantity.Value.ToDouble(), 6);
    }

    /// <summary>
    /// A structure whose per-quantity modifier does not resolve publishes no price at all.
    /// </summary>
    /// <remarks>
    /// The alternative — pricing it without the modifier — produces a number that is confidently too
    /// low, and Auto Buy cannot tell a cheap structure from a mispriced one. An absent price it can
    /// see and refuse to act on.
    /// </remarks>
    [Fact]
    public void AStructureWithNoResolvableScalingModifierIsNotPriced()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO { Identity = water, Quantity = 500d });
        StructureSO.All.Add(new StructureSO
        {
            Identity = cauldron,
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        // Reading the cost list still succeeded — the shortfall is in what could be derived from it,
        // which is why this is not a collection failure.
        Assert.True(report.IsComplete, report.Describe());
        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// A price paid in several resources is several rows, and the lookup returns all of them.
    /// </summary>
    /// <remarks>
    /// The range is the whole point of the table's shape: a binary search that landed anywhere inside
    /// an entity's rows and walked forward would report a partial price, and a consumer checking
    /// affordability against a partial price buys things it cannot pay for.
    /// </remarks>
    [Fact]
    public void AMultiResourcePriceIsReturnedWhole()
    {
        var water = Guid.NewGuid();
        var mana = Guid.NewGuid();
        var stone = Guid.NewGuid();
        var perQuantity = Guid.NewGuid();

        foreach (var resource in new[] { water, mana, stone })
            ResourceSO.All.Add(new ResourceSO { Identity = resource, Quantity = 500d });

        var modifier = new ValueModifierVariable
        {
            Identity = perQuantity,
            value = new ValueModifier(ValueModifier.ValueModifierType.Raw, 0d, order: 0),
        };
        ValueModifierVariable.All.Add(modifier);

        // Two structures, so the search has to find the right one's first row rather than the table's.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        StructureSO.All.Add(new StructureSO
        {
            Identity = first,
            costPerQuantity = new ValueModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d), (mana, 200d), (stone, 300d)),
        });
        StructureSO.All.Add(new StructureSO
        {
            Identity = second,
            costPerQuantity = new ValueModifierRef { variable = modifier },
            baseCost = CostOf((water, 400d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, first, out var start, out var count));
        Assert.Equal(3, count);

        var total = 0d;
        for (var index = start; index < start + count; index++)
        {
            Assert.Equal(first, world.PurchaseCosts[index].EntityId);
            total += world.PurchaseCosts[index].Amount.ToDouble();
        }

        Assert.Equal(600d, total, 6);

        for (var index = start; index < start + count; index++)
        {
            Assert.True(world.PurchaseCosts[index].AffordabilityEvaluated);
            Assert.True(world.PurchaseCosts[index].ResourceAffordable);
            Assert.True(world.PurchaseCosts[index].Affordable);
            Assert.Equal("affordable", world.PurchaseCosts[index].AffordabilityReasonCode);
        }

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, second, out _, out var otherCount));
        Assert.Equal(1, otherCount);
    }

    /// <summary>
    /// A resource whose attribute-cost modifier has never been calculated takes the price with it.
    /// </summary>
    /// <remarks>
    /// The game authors that modifier at 100 and reaches it through an accessor that recalculates on
    /// demand; collection reads the cached field and does not. So a zero means "not yet calculated",
    /// and pricing on it would multiply the whole entity to nothing — the one error direction that
    /// makes Auto Buy buy something it cannot afford. See W5.
    /// </remarks>
    [Fact]
    public void AnUncalculatedAttributeModifierWithholdsThePriceRatherThanZeroingIt()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new ValueModifierRecord(0d),
        });

        var modifier = new ValueModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new ValueModifier(ValueModifier.ValueModifierType.Raw, 0d, order: 0),
        };
        ValueModifierVariable.All.Add(modifier);
        StructureSO.All.Add(new StructureSO
        {
            Identity = cauldron,
            costPerQuantity = new ValueModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// The attribute-cost modifier is the game's quotient, so a quality bonus makes a structure
    /// cheaper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetAttributeCostMod()</c> is <c>attributeCostMod / Pow(quality.AsPercent(), bonus)</c>.
    /// Here that is <c>200 / Pow(4, 2) = 12.5</c>, whose percent is <c>0.125</c>, against the
    /// <c>2</c> the numerator alone would have given.
    /// </para>
    /// <para>
    /// The rest is the fixture from the test above, and the discount does <em>not</em> just divide
    /// its answer: it lands on the base cost before the per-quantity modifier adds to it. So
    /// <c>100 × 0.125 = 12.5</c>, then Raw adds its 3 for <c>15.5</c>, then the multiplier of 3.6
    /// gives <c>55.8</c> — and that one is under 100, where RoundToTwoSigsEarly does its only work,
    /// snapping it to <c>56</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void AQualityBonusDiscountsAStructuresPriceTheWayTheGameDiscountsIt()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        SetStructureCost(200d);
        SetAttributeQualityBonus(2d);
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = water,
            Quantity = 500d,
            quality = new ValueModifierRecord(400d),
            attributeCostMod = new ValueModifierRecord(200d),
        });

        var modifier = PerQuantity();
        ValueModifierVariable.All.Add(modifier);
        StructureSO.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out _));
        Assert.Equal(56d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// With no bonus earned, the quality discount is one and the price is the undiscounted one.
    /// </summary>
    /// <remarks>
    /// The exponent, not the quality, is what switches the discount off: <c>Pow(4, 0) = 1</c> whatever
    /// the quality is. This pins the neutral case on the term that actually carries it, so a reading
    /// that dropped the exponent and divided by the quality itself would fail here rather than only
    /// on a developed save.
    /// </remarks>
    [Fact]
    public void WithoutTheQualityBonusAStructurePricesAsThoughTheDiscountWereAbsent()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        SetStructureCost(200d);
        SetAttributeQualityBonus(0d);
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = water,
            Quantity = 500d,
            quality = new ValueModifierRecord(400d),
            attributeCostMod = new ValueModifierRecord(200d),
        });

        var modifier = PerQuantity();
        ValueModifierVariable.All.Add(modifier);
        StructureSO.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out _));
        Assert.Equal(730.8d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// A resource whose quality has never been calculated takes the price with it, like its
    /// attribute-cost modifier does.
    /// </summary>
    /// <remarks>
    /// Quality is the base of the power the discount divides by, so a zero divides the price by zero
    /// and publishes an infinity — the same W5 refusal as an uncalculated attribute modifier, from the
    /// other end of the quotient. An infinite price is not the safe direction either: it is a number
    /// no consumer can compare against and every downstream sum turns into a NaN.
    /// </remarks>
    [Fact]
    public void AnUncalculatedQualityWithholdsThePriceRatherThanSendingItToInfinity()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        SetAttributeQualityBonus(2d);
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = water,
            Quantity = 500d,
            quality = new ValueModifierRecord(0d),
            attributeCostMod = new ValueModifierRecord(200d),
        });

        var modifier = PerQuantity();
        ValueModifierVariable.All.Add(modifier);
        StructureSO.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// The overpricing this fixed has a signature: the error is one factor per resource, the same for
    /// every structure priced in that resource, whatever their levels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Live spin 6 published every one of the 342 structure cost rows too high and not one of the 402
    /// upgrade rows, because only the structure chain calls <c>AdjustAsAttribute</c>. The diagnosis
    /// that stuck first read the gap as <c>costPerQuantity^committed</c> — but the two spins that
    /// measured it disagree with that and agree with this: on the same resource, Rapid Gathering and
    /// Transmute Power were over by <c>1.8107e133</c> and <c>1.8173e133</c> despite differing by 208
    /// committed levels, and a level-driven fault would have separated them by <c>1.25^208</c>.
    /// </para>
    /// <para>
    /// So this asserts the shape rather than a number: two structures at different levels, one
    /// resource, one bonus. Without the divisor both prices are wrong; with it both are right. What
    /// makes it a regression test is the ratio — it is identical across the two, which is the
    /// fingerprint that named the term.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheQualityDiscountIsOneFactorPerResourceRatherThanOnePerLevel()
    {
        var water = Guid.NewGuid();
        var shallow = Guid.NewGuid();
        var deep = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO
        {
            Identity = water,
            Quantity = 500d,
            quality = new ValueModifierRecord(1000d),
            attributeCostMod = new ValueModifierRecord(100d),
        });

        // Multiplicative, so the levels genuinely price apart — 1.25²⁰⁸ between them — while still
        // cancelling exactly out of each structure's own before-and-after ratio.
        var modifier = new ValueModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 1.25d, order: 0),
        };
        ValueModifierVariable.All.Add(modifier);

        StructureSO Structure(Guid identity, int level) => new()
        {
            Identity = identity,
            Level = level,
            costPerQuantity = new ValueModifierRef { variable = modifier },
            baseCost = CostOf((water, 1e12d)),
        };

        StructureSO.All.Add(Structure(shallow, level: 1));
        StructureSO.All.Add(Structure(deep, level: 209));

        var collector = Collector();

        SetAttributeQualityBonus(0d);
        collector.Collect();
        var undiscounted = collector.Build();
        var shallowBefore = PriceOf(undiscounted, shallow);
        var deepBefore = PriceOf(undiscounted, deep);

        SetAttributeQualityBonus(3d);
        collector.Collect();
        var discounted = collector.Build();
        var shallowAfter = PriceOf(discounted, shallow);
        var deepAfter = PriceOf(discounted, deep);

        // The two are 1.25²⁰⁸ apart, so a fault that scaled with level could not show one ratio.
        Assert.NotEqual(shallowBefore, deepBefore);

        Assert.Equal(shallowBefore / shallowAfter, deepBefore / deepAfter, 6);

        // Pow(1000%, 3) = 10³, the one factor the resource contributes however deep the structure is.
        Assert.Equal(1000d, shallowBefore / shallowAfter, 6);
    }

    /// <summary>The published price of an entity's single row.</summary>
    private static double PriceOf(GameWorldState world, Guid entityId)
    {
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, entityId, out var start, out _));
        return world.PurchaseCosts[start].Amount.ToDouble();
    }

    /// <summary>The per-quantity modifier the priced-structure fixtures share: <c>Raw 0.5</c>.</summary>
    private static ValueModifierVariable PerQuantity() =>
        new()
        {
            Identity = Guid.NewGuid(),
            value = new ValueModifier(ValueModifier.ValueModifierType.Raw, 0.5d, order: 0),
        };

    /// <summary>
    /// The fixture from <see cref="AStructuresPriceIsComputedFromWhatWasCollected"/>, whose hand-derived
    /// price is 730.8 before any quality discount.
    /// </summary>
    private static StructureSO PricedCauldron(Guid identity, Guid resource, ValueModifierVariable modifier) =>
        new()
        {
            Identity = identity,
            Level = 1,
            Queued = 1,
            passiveCostMod = new ValueModifierRecord(120d),
            activeCostMod = new ValueModifierRecord(150d),
            costScalingMod = new ValueModifierRecord(300d),
            costPerQuantity = new ValueModifierRef { variable = modifier },
            baseCost = CostOf((resource, 100d)),
        };

    /// <summary>
    /// An upgrade prices off the list it grows by, with the exponents kept apart from the modifiers.
    /// </summary>
    /// <remarks>
    /// Every term is deliberately off parity, because a fixture built from defaults tests almost
    /// nothing about a multiplicative chain — the defaults are the identity and every wire crosses
    /// unobserved. Derived by hand: three committed levels price level four, so both lists scale by
    /// three; the stacking modifier becomes 2³ = 8 and the raw exponent becomes 3; the exponent
    /// strengthens the modifier to 8⁽¹⁺³⁾ = 4096 before it touches the cost; 50 × 4096 = 204800,
    /// snapped to two significant digits at 200000. A reader that dropped the exponent list would
    /// publish 400 instead.
    /// </remarks>
    [Fact]
    public void AnUpgradePricesOffTheModifierListItGrowsBy()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO { Identity = water, Quantity = 500d });
        UpgradeSO.All.Add(new UpgradeSO
        {
            Identity = insight,
            Level = 2,
            queuedLevels = 1,
            resourceCost = CostOf((water, 50d)),
            resourceCostModPerLevel = LevelModifiers(
                new[] { new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 2d, order: 0) },
                new[] { new ValueModifier(ValueModifier.ValueModifierType.Raw, 1d, order: 0) }),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(water, world.PurchaseCosts[start].ResourceId);
        Assert.Equal(50d, world.PurchaseCosts[start].BaseExactAmount.ToDouble(), 6);
        Assert.Equal(200000d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
        Assert.Equal(
            new[]
            {
                "upgrade.priced_level",
                "upgrade.resource_cost_per_level.modifier",
                "upgrade.resource_cost_per_level.exponent",
            },
            world.PurchaseCosts[start].ModifierSources.AsSpan().ToArray()
                .Select(source => source.Name)
                .ToArray());
    }

    /// <summary>
    /// Duplicate authored rows paid in one resource are one affordability obligation. This is the
    /// same stricter-than-native aggregation Auto Buy uses, not two independent checks which would
    /// each pass while their combined payment fails.
    /// </summary>
    [Fact]
    public void DuplicateResourceCostsUseTheSharedExactCombinerForAffordability()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        var modifier = new ValueModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new ValueModifier(ValueModifier.ValueModifierType.Raw, 0d, order: 0),
        };
        ValueModifierVariable.All.Add(modifier);
        ResourceSO.All.Add(new ResourceSO { Identity = water, Quantity = 100d });
        StructureSO.All.Add(new StructureSO
        {
            Identity = cauldron,
            costPerQuantity = new ValueModifierRef { variable = modifier },
            baseCost = CostOf((water, 40d), (water, 70d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(
            world.PurchaseCosts, cauldron, out var start, out var count));
        Assert.Equal(2, count);
        for (var index = start; index < start + count; index++)
        {
            var row = world.PurchaseCosts[index];
            Assert.Equal(110d, row.CombinedEffectiveAmount.ToDouble(), 6);
            Assert.Equal(100d, row.AvailableAmount.ToDouble(), 6);
            Assert.False(row.ResourceAffordable);
            Assert.False(row.Affordable);
            Assert.Equal("insufficient_quantity", row.AffordabilityReasonCode);
        }
    }

    [Fact]
    public void BandwidthPurchaseAffordabilityUsesHeadroomRatherThanHoldings()
    {
        var bandwidth = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        var modifier = new ValueModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new ValueModifier(ValueModifier.ValueModifierType.Raw, 0d, order: 0),
        };
        ValueModifierVariable.All.Add(modifier);
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = bandwidth,
            Quantity = 80d,
            bandwidthResource = true,
            maxQuantity = new ValueModifierRecord(100d),
        });
        StructureSO.All.Add(new StructureSO
        {
            Identity = cauldron,
            costPerQuantity = new ValueModifierRef { variable = modifier },
            baseCost = CostOf((bandwidth, 30d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(
            world.PurchaseCosts, cauldron, out var start, out _));
        var row = world.PurchaseCosts[start];
        Assert.Equal(80d, world.Resources.AsSpan().ToArray()
            .Single(resource => resource.EntityId == bandwidth).TrueQuantity.ToDouble(), 6);
        Assert.Equal(20d, row.AvailableAmount.ToDouble(), 6);
        Assert.Equal(30d, row.CombinedEffectiveAmount.ToDouble(), 6);
        Assert.False(row.ResourceAffordable);
        Assert.False(row.Affordable);
        Assert.Equal("insufficient_bandwidth", row.ResourceAffordabilityReasonCode);
        Assert.Equal("insufficient_bandwidth", row.AffordabilityReasonCode);
    }

    /// <summary>
    /// Two per-level modifiers at different orders are applied in sequence, not merged.
    /// </summary>
    /// <remarks>
    /// The order field is the one part of a modifier that is silently substitutable: a reader that
    /// never bound it, or bound it to a constant, still produces a plausible number. Here it changes
    /// the answer. Three committed levels scale the stacking modifier to 2³ = 8 and the raw one to 30;
    /// at separate orders that is 50 × 8 + 30 = 430, and at the same order the raw one merges in first
    /// for (50 + 30) × 8 = 640.
    /// </remarks>
    [Fact]
    public void PerLevelModifiersAtDifferentOrdersAreAppliedInSequence()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO { Identity = water, Quantity = 500d });
        UpgradeSO.All.Add(new UpgradeSO
        {
            Identity = insight,
            Level = 3,
            resourceCost = CostOf((water, 50d)),
            resourceCostModPerLevel = LevelModifiers(new[]
            {
                new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 2d, order: 0),
                new ValueModifier(ValueModifier.ValueModifierType.Raw, 10d, order: 1),
            }),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(430d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// A finite upgrade keeps quoting the last level it can buy rather than one past the ceiling.
    /// </summary>
    /// <remarks>
    /// The cap is the game's, and it is easy to leave out because nothing else about an upgrade
    /// mentions it: <c>min(level + queued, maxLevel - 1)</c>. Both upgrades here sit at five committed
    /// levels and differ only in whether a ceiling applies — the bounded one prices level three at
    /// 50 × 2² and the unbounded one prices level six at 50 × 2⁵, so an implementation that ignored
    /// the cap would quote the maxed-out upgrade eight times too much.
    /// </remarks>
    [Fact]
    public void AFiniteUpgradeStopsRepricingOnceItsCeilingIsReached()
    {
        var water = Guid.NewGuid();
        var bounded = Guid.NewGuid();
        var unbounded = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO { Identity = water, Quantity = 500d });
        foreach (var (identity, maxLevel) in new[] { (bounded, 3), (unbounded, 0) })
        {
            UpgradeSO.All.Add(new UpgradeSO
            {
                Identity = identity,
                Level = 5,
                maxLevel = maxLevel,
                resourceCost = CostOf((water, 50d)),
                resourceCostModPerLevel = LevelModifiers(
                    new[] { new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 2d, order: 0) }),
            });
        }

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, bounded, out var cappedRow, out _));
        Assert.Equal(200d, world.PurchaseCosts[cappedRow].Amount.ToDouble(), 6);

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, unbounded, out var openRow, out _));
        Assert.Equal(1600d, world.PurchaseCosts[openRow].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// An upgrade with nothing to grow by still publishes a price, rounded.
    /// </summary>
    /// <remarks>
    /// A flat-cost upgrade is ordinary in this game, not a degraded reading: <c>SetToLevel</c> simply
    /// skips its scaling branch. The rounding still runs, which is what 137 landing at 140 shows —
    /// treating an empty list as "could not price this" would withhold every flat upgrade, and
    /// returning the authored value untouched would disagree with the game by three.
    /// </remarks>
    [Fact]
    public void AnUpgradeWithNothingToGrowByStillPublishesARoundedPrice()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO { Identity = water, Quantity = 500d });
        UpgradeSO.All.Add(new UpgradeSO
        {
            Identity = insight,
            Level = 4,
            resourceCost = CostOf((water, 137d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(140d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// An upgrade prices even when the resource's attribute-cost modifier reads zero.
    /// </summary>
    /// <remarks>
    /// That modifier withholds a <em>structure's</em> price, because the structure chain multiplies by
    /// it and a zero would make the structure free. The upgrade chain never touches it, so making it a
    /// shared precondition would withhold upgrade prices for a reading that says nothing about them.
    /// </remarks>
    [Fact]
    public void AnUpgradeDoesNotWaitOnTheAttributeModifierAStructureNeeds()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new ValueModifierRecord(0d),
        });
        UpgradeSO.All.Add(new UpgradeSO
        {
            Identity = insight,
            resourceCost = CostOf((water, 400d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(400d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }
}
