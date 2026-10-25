using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoLevels;

public sealed class AutoLevelsPolicyTests
{
    private static readonly Guid Mana = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Wood = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ReserveApproachesItsNativeMaximumWithoutEconomicEvidenceOrLoadedWork()
    {
        var world = World(LevelDial.Reserve, Quote(active: false));
        var decision = Evaluate(world, LevelDial.Reserve);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
        Assert.Equal(4, decision.TargetLevel);
        Assert.Equal(AutoLevelsDecisionReason.ReserveMaximum, decision.Reason);
        var maximum = Evaluate(World(LevelDial.Reserve, Quote(level: 5, maximum: 5)), LevelDial.Reserve);
        Assert.Equal(AutoLevelsDecisionKind.Hold, maximum.Kind);
        Assert.Equal(AutoLevelsDecisionReason.AtMaximum, maximum.Reason);
    }

    [Fact]
    public void DisabledPolicyNeverChangesADial()
    {
        var decision = AutoLevelsPolicy.Evaluate(World(LevelDial.Reserve, Quote()), LevelDial.Reserve, new());
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(3, decision.TargetLevel);
        Assert.Equal(AutoLevelsDecisionReason.Disabled, decision.Reason);
    }

    [Theory]
    [InlineData((int)LevelDial.Output)]
    [InlineData((int)LevelDial.Druidry)]
    [InlineData((int)LevelDial.Alchemy)]
    public void EveryEconomicDialHoldsWithoutActiveWork(int dialValue)
    {
        var dial = (LevelDial)dialValue;
        var decision = Evaluate(World(dial, Quote(active: false)), dial);
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.NoActiveWork, decision.Reason);
    }

    [Theory]
    [InlineData((int)LevelDial.Druidry)]
    [InlineData((int)LevelDial.Alchemy)]
    public void MissingControlAvailabilityCannotPlanAChangeEvenWithACompletePriceQuote(int dialValue)
    {
        var dial = (LevelDial)dialValue;
        var world = World(dial, Quote()) with { Views = PublicationTable<WorldView>.Empty };

        var decision = Evaluate(world, dial);

        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.EvidenceUnavailable, decision.Reason);
        Assert.Contains("availability was not published", decision.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)LevelDial.Druidry)]
    [InlineData((int)LevelDial.Alchemy)]
    public void LockedControlCannotPlanEvenAReductionFromAProvenUnsafeCurrentLevel(int dialValue)
    {
        var dial = (LevelDial)dialValue;
        var controlId = dial == LevelDial.Druidry
            ? KnownEntities.WorldAgromancyDruidryLvs.Uuid : KnownEntities.AlchAlchemyLevel.Uuid;
        var world = World(dial, Quote(new[] { Drain(Mana, 9) }), Resource(Mana, 1, -1, -1, 10)) with
        {
            Views = WorldTable.Create(new[] { new WorldView(controlId, false, false, false) }),
        };

        var decision = Evaluate(world, dial);

        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.ControlLocked, decision.Reason);
        Assert.Contains("level control is locked", decision.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteProjectionHoldsAndPreservesTheExactReason()
    {
        var quote = new WorldLevelDomainQuote(false, "native next-level drain cannot be quoted", true,
            3, 5, Table(Drain(Mana, 9)), Table(Drain(Mana, 20)));
        var decision = Evaluate(World(LevelDial.Alchemy, quote, Resource(Mana, 1, -10, -10, 10)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.EvidenceUnavailable, decision.Reason);
        Assert.Equal(quote.Reason, decision.Detail);
    }

    [Fact]
    public void ProvenCurrentDrainDangerCanFallWithoutAProspectiveNextLevelQuote()
    {
        var quote = new WorldLevelDomainQuote(true, string.Empty, true, 3, 5,
            Table(Drain(Mana, 9)), Table(), nextComplete: false,
            nextReason: "future effect pipeline has not been captured");
        var decision = Evaluate(World(LevelDial.Druidry, quote,
            Resource(Mana, 10, -1, -1, 10)), LevelDial.Druidry);
        Assert.Equal(AutoLevelsDecisionKind.Lower, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.DominantDrain, decision.Reason);
    }

    [Fact]
    public void MissingNextQuoteHoldsARaiseAndNeverMakesTheNextLevelFree()
    {
        var quote = new WorldLevelDomainQuote(true, string.Empty, true, 3, 5,
            Table(Drain(Mana, 9)), Table(), nextComplete: false,
            nextReason: "future effect pipeline has not been captured");
        var decision = Evaluate(World(LevelDial.Druidry, quote,
            Resource(Mana, 1000, 1, 1, 10)), LevelDial.Druidry);
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.EvidenceUnavailable, decision.Reason);
        Assert.Equal(quote.NextReason, decision.Detail);
    }

    [Fact]
    public void OutputPricesEachLoadedSpellSeparatelyAtTheExactOneSecondThreshold()
    {
        var quote = Quote(new[] { Immediate(0, Mana, 50), Immediate(1, Mana, 50) },
            new[] { Immediate(0, Mana, 100), Immediate(1, Mana, 100) });
        var decision = Evaluate(World(LevelDial.Output, quote, Resource(Mana, 1000, 0, 100)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
        Assert.Equal(4, decision.TargetLevel);
    }

    [Fact]
    public void EveryResourceOfEverySpellMustFitAndTheBindingResourceIsReported()
    {
        var quote = Quote(new[] { Immediate(0, Mana, 2), Immediate(0, Wood, 3) },
            new[] { Immediate(0, Mana, 3), Immediate(0, Wood, 6) });
        var decision = Evaluate(World(LevelDial.Output, quote,
            Resource(Mana, 100, 10, 10), Resource(Wood, 100, 5, 5)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.NextImmediateCost, decision.Reason);
        Assert.Equal(Wood, decision.ResourceId);
        Assert.Equal(new BigDouble(6), decision.Required);
        Assert.Equal(new BigDouble(5), decision.Budget);
    }

    [Fact]
    public void DuplicateResourceComponentsOfOneSpellCannotEachUseTheWholeBudget()
    {
        var quote = Quote(new[] { Immediate(0, Mana, 6), Immediate(0, Mana, 6) });
        var decision = Evaluate(World(LevelDial.Output, quote, Resource(Mana, 100, 10, 10)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Lower, decision.Kind);
        Assert.Equal(new BigDouble(12), decision.Required);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    public void PositiveImmediateCostCannotUseZeroOrNegativeNetRegeneration(double net)
    {
        var decision = Evaluate(World(LevelDial.Output,
            Quote(new[] { Immediate(0, Mana, 1) }), Resource(Mana, 100, net, net, 20)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Lower, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.ImmediateCost, decision.Reason);
        Assert.Equal(BigDouble.Zero, decision.Budget);
    }

    [Fact]
    public void AFullResourceUsesTheSharedPreClampNetRateForImmediateCosts()
    {
        var decision = Evaluate(World(LevelDial.Output,
            Quote(new[] { Immediate(0, Mana, 5) }, new[] { Immediate(0, Mana, 10) }),
            Resource(Mana, 100, 0, 10, capacity: 100)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
    }

    [Fact]
    public void AstronomicalCostsAndRatesKeepTheirBigDoubleScale()
    {
        var huge = new BigDouble(1d, 1000);
        var decision = Evaluate(World(LevelDial.Output,
            Quote(new[] { Immediate(0, Mana, huge) }, new[] { Immediate(0, Mana, huge) }),
            Resource(Mana, new BigDouble(1d, 1002), huge, huge)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
    }

    [Fact]
    public void ImmediateNominalCostsConvertThroughQualityBeforeComparingStockRegeneration()
    {
        var decision = Evaluate(World(LevelDial.Output,
            Quote(new[] { Immediate(0, Mana, 1) }, new[] { Immediate(0, Mana, 2) }),
            Resource(Mana, 100, 1, 1, quality: 200)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
    }

    [Fact]
    public void BandwidthUsesNativeAllocationSnappingWithoutInventingRegeneration()
    {
        var quote = Quote(new[] { Allocation(0, Mana, 1) }, new[] { Allocation(0, Mana, 2.1) });
        var decision = Evaluate(World(LevelDial.Output, quote,
            Resource(Mana, 7.1, 0, 0, capacity: 10, bandwidth: true, inverted: true)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
        var blocked = Evaluate(World(LevelDial.Output,
            Quote(new[] { Allocation(0, Mana, 1) }, new[] { Allocation(0, Mana, 4) }),
            Resource(Mana, 7, 0, 0, capacity: 10, bandwidth: true)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Hold, blocked.Kind);
        Assert.Equal(new BigDouble(3), blocked.Budget);
    }

    [Theory]
    [InlineData((int)LevelDial.Output)]
    [InlineData((int)LevelDial.Druidry)]
    [InlineData((int)LevelDial.Alchemy)]
    public void SharedDrainRuleLowersOnlyWhenOwnedDrainIsStrictlyAboveHalf(int dialValue)
    {
        var dial = (LevelDial)dialValue;
        var half = Evaluate(World(dial, Quote(new[] { Drain(Mana, 5) }, new[] { Drain(Mana, 5) }),
            Resource(Mana, 299, -1, -1, 10)), dial);
        Assert.Equal(AutoLevelsDecisionKind.Hold, half.Kind);
        Assert.Equal(AutoLevelsDecisionReason.OtherDrainPressure, half.Reason);
        var majority = Evaluate(World(dial, Quote(new[] { Drain(Mana, 6) }, new[] { Drain(Mana, 6) }),
            Resource(Mana, 299, -1, -1, 10)), dial);
        Assert.Equal(AutoLevelsDecisionKind.Lower, majority.Kind);
        Assert.Equal(2, majority.TargetLevel);
    }

    [Fact]
    public void ExactlyTheConfiguredBufferIsSafeAndLessThanItLowersOneLevel()
    {
        var quote = Quote(new[] { Drain(Mana, 6) }, new[] { Drain(Mana, 6) });
        var exact = Evaluate(World(LevelDial.Alchemy, quote, Resource(Mana, 300, -1, -1, 10)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Raise, exact.Kind);
        var below = Evaluate(World(LevelDial.Alchemy, quote, Resource(Mana, 299, -1, -1, 10)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Lower, below.Kind);
        Assert.Equal(new BigDouble(300), below.Required);
        Assert.Equal(new BigDouble(299), below.Budget);
    }

    [Fact]
    public void DrainAloneIsNotDepletionWhenActualNetProductionCoversIt()
    {
        var quote = Quote(new[] { Drain(Mana, 6) }, new[] { Drain(Mana, 7) });
        var decision = Evaluate(World(LevelDial.Druidry, quote, Resource(Mana, 1, 2, 2, 10)), LevelDial.Druidry);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
    }

    [Fact]
    public void NextDrainReplacesOwnCurrentContributionRatherThanDoubleCountingIt()
    {
        var quote = Quote(new[] { Drain(Mana, 10) }, new[] { Drain(Mana, 11) });
        var decision = Evaluate(World(LevelDial.Alchemy, quote, Resource(Mana, 100, 0, 0, 10)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.NextDrain, decision.Reason);
        Assert.Equal(new BigDouble(300), decision.Required);
    }

    [Fact]
    public void NextOutputPriceUsesTheNetBudgetAfterItsOwnProjectedChannelDrain()
    {
        var quote = Quote(new[] { Immediate(0, Mana, 1), Drain(Mana, 1) },
            new[] { Immediate(0, Mana, 9), Drain(Mana, 3) });
        var decision = Evaluate(World(LevelDial.Output, quote,
            Resource(Mana, 1000, 10, 10, 10)), LevelDial.Output);
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.NextImmediateCost, decision.Reason);
        Assert.Equal(new BigDouble(8), decision.Budget);
    }

    [Fact]
    public void NextDrainUsesAvailableNetProductionBeforeTheCurrentCapacityClamp()
    {
        var quote = Quote(new[] { Drain(Mana, 6) }, new[] { Drain(Mana, 8) });
        var decision = Evaluate(World(LevelDial.Alchemy, quote,
            Resource(Mana, 10, 0, 10, 10, capacity: 10)), LevelDial.Alchemy);

        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.Sustainable, decision.Reason);
        Assert.Equal(4, decision.TargetLevel);
    }

    [Fact]
    public void AReductionCannotImmediatelyReenterTheSameUnsafeNextLevel()
    {
        var high = Evaluate(World(LevelDial.Alchemy,
            Quote(new[] { Drain(Mana, 11) }, new[] { Drain(Mana, 12) }, level: 4),
            Resource(Mana, 100, -1, -1, 11)), LevelDial.Alchemy);
        Assert.Equal(3, high.TargetLevel);
        var lower = Evaluate(World(LevelDial.Alchemy,
            Quote(new[] { Drain(Mana, 10) }, new[] { Drain(Mana, 11) }, level: 3),
            Resource(Mana, 100, 0, 0, 10)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Hold, lower.Kind);
        Assert.Equal(3, lower.TargetLevel);
    }

    [Fact]
    public void OtherDomainsCurrentPressurePreventsAnOtherwiseNondominantRaise()
    {
        var quote = Quote(new[] { Drain(Mana, 30) }, new[] { Drain(Mana, 35) });
        var decision = Evaluate(World(LevelDial.Druidry, quote, Resource(Mana, 100, -10, -10, 100)), LevelDial.Druidry);
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.OtherDrainPressure, decision.Reason);
    }

    [Fact]
    public void NewlyUsedResourceCannotHideExistingPressureFromOtherDomains()
    {
        var quote = Quote(next: new[] { Drain(Mana, 1) });
        var decision = Evaluate(World(LevelDial.Alchemy, quote, Resource(Mana, 100, -10, -10, 100)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Hold, decision.Kind);
        Assert.Equal(AutoLevelsDecisionReason.OtherDrainPressure, decision.Reason);
    }

    [Fact]
    public void InvertedOrdinaryResourcesUseSpendableStockRatherThanTheirDisplayedHeadroom()
    {
        var quote = Quote(new[] { Drain(Mana, 6) }, new[] { Drain(Mana, 7) });
        var decision = Evaluate(World(LevelDial.Druidry, quote,
            Resource(Mana, 5, -1, -1, 10, capacity: 1000, inverted: true)), LevelDial.Druidry);
        Assert.Equal(AutoLevelsDecisionKind.Lower, decision.Kind);
        Assert.Equal(new BigDouble(5), decision.Budget);
    }

    [Fact]
    public void SustainedBandwidthPaysOnlyTheAdditionalAllocationRatherThanItsAlreadyChargedTotal()
    {
        var quote = Quote(new[] { Allocation(-1, Mana, 90) }, new[] { Allocation(-1, Mana, 92) });
        var decision = Evaluate(World(LevelDial.Alchemy, quote,
            Resource(Mana, 90, 0, 0, capacity: 100, bandwidth: true)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
    }

    [Fact]
    public void ProjectedDrainDeltaUsesQualityAndKeepsOtherDomainContributions()
    {
        var quote = Quote(new[] { Drain(Mana, 12) }, new[] { Drain(Mana, 14) });
        var decision = Evaluate(World(LevelDial.Alchemy, quote,
            Resource(Mana, 300, 0, 0, 20, quality: 200)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Raise, decision.Kind);
        var shortStock = Evaluate(World(LevelDial.Alchemy, quote,
            Resource(Mana, 299, 0, 0, 20, quality: 200)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionKind.Hold, shortStock.Kind);
        Assert.Equal(AutoLevelsDecisionReason.NextDrain, shortStock.Reason);
    }

    [Fact]
    public void NativeMinimumAndMaximumBoundEachStepWhileUnsafeMaximumCanStillFall()
    {
        var minimum = Evaluate(World(LevelDial.Alchemy,
            Quote(new[] { Drain(Mana, 9) }, level: 1), Resource(Mana, 1, -1, -1, 10)), LevelDial.Alchemy);
        Assert.Equal(1, minimum.TargetLevel);
        Assert.Equal(AutoLevelsDecisionKind.Hold, minimum.Kind);
        Assert.Equal(AutoLevelsDecisionReason.AtMinimum, minimum.Reason);
        var unsafeMaximum = Evaluate(World(LevelDial.Alchemy,
            Quote(new[] { Drain(Mana, 9) }, level: 5, maximum: 5), Resource(Mana, 1, -1, -1, 10)), LevelDial.Alchemy);
        Assert.Equal(4, unsafeMaximum.TargetLevel);
        var safeMaximum = Evaluate(World(LevelDial.Alchemy,
            Quote(new[] { Drain(Mana, 9) }, level: 5, maximum: 5), Resource(Mana, 1000, 1, 1, 10)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionReason.AtMaximum, safeMaximum.Reason);
    }

    [Fact]
    public void ContradictoryAttributionAndMissingResourcesHoldInsteadOfInventingBudgets()
    {
        var quote = Quote(new[] { Drain(Mana, 11) }, new[] { Drain(Mana, 12) });
        var contradictory = Evaluate(World(LevelDial.Alchemy, quote, Resource(Mana, 100, -1, -1, 10)), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionReason.InvalidEvidence, contradictory.Reason);
        Assert.Equal(AutoLevelsDecisionKind.Hold, contradictory.Kind);
        var missing = Evaluate(World(LevelDial.Alchemy, quote), LevelDial.Alchemy);
        Assert.Equal(AutoLevelsDecisionReason.InvalidEvidence, missing.Reason);
    }

    private static AutoLevelsDecision Evaluate(GameWorldState world, LevelDial dial) =>
        AutoLevelsPolicy.Evaluate(world, dial, new AutoLevelsConfiguration { Mode = AutoLevelsOperationMode.Active });

    private static WorldLevelDomainQuote Quote(WorldLevelPrice[]? current = null, WorldLevelPrice[]? next = null,
        int level = 3, int maximum = 5, bool active = true) =>
        new(true, string.Empty, active, level, maximum, Table(current ?? Array.Empty<WorldLevelPrice>()),
            Table(next ?? Array.Empty<WorldLevelPrice>()));

    private static PublicationTable<WorldLevelPrice> Table(params WorldLevelPrice[] rows) =>
        PublicationTable<WorldLevelPrice>.Create(rows);
    private static WorldLevelPrice Immediate(int slot, Guid resource, BigDouble amount) =>
        new(slot, resource, amount, WorldLevelPriceKind.Immediate);
    private static WorldLevelPrice Drain(Guid resource, BigDouble amount) => new(-1, resource, amount, WorldLevelPriceKind.Drain);
    private static WorldLevelPrice Allocation(int slot, Guid resource, BigDouble amount) => new(slot, resource, amount, WorldLevelPriceKind.Allocation);

    private static GameWorldState World(LevelDial dial, WorldLevelDomainQuote quote, params WorldResource[] resources) => new()
    {
        SpellWorkbench = new WorldSpellWorkbench(1, 1, false, quote.CurrentLevel, quote.MaximumLevel,
            quote.CurrentLevel, quote.MaximumLevel, druidryLevel: quote.CurrentLevel,
            maximumDruidryLevel: quote.MaximumLevel, alchemyLevel: quote.CurrentLevel,
            maximumAlchemyLevel: quote.MaximumLevel),
        Views = WorldTable.Create(new[]
        {
            new WorldView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, false, false, true),
            new WorldView(KnownEntities.AlchAlchemyLevel.Uuid, false, false, true),
        }),
        Resources = WorldTable.Create(resources),
        LevelEconomy = new WorldLevelEconomy(quote, quote, quote),
    };

    private static WorldResource Resource(Guid id, BigDouble held, BigDouble actualRate, BigDouble availableRate,
        BigDouble totalDrain = default, BigDouble? capacity = null, bool bandwidth = false,
        bool inverted = false, BigDouble? quality = null)
    {
        var traits = new RawResourceTraits(0, 0, 0, false, false, false, bandwidth, inverted,
            false, true, BigDouble.Zero, 0, 0, 0, false, 0, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, false);
        var cap = capacity ?? new BigDouble(1d, 1005);
        var q = quality ?? new BigDouble(100);
        var reading = WorldSamples.Resource(id, held, cap, quality: q, drain: totalDrain, traits: traits);
        return new WorldResource(in reading, true, BigDouble.Max(cap - held, BigDouble.Zero),
            0, false, held * q / new BigDouble(100), actualRate, availableRate);
    }
}
