using System;
using OrbAutomata;
using Xunit;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Derivation is the off-thread half of world collection, and its output decides whether a
/// capacity-relative stance binds at all. These tests pin the boundary cases where "no capacity
/// information" and "a capacity of zero" must stay distinguishable, and where an unreadable value
/// must produce neutral output rather than a confident wrong one.
/// </summary>
public sealed class GameWorldStateDeriverTests
{
    private static readonly Guid Mana = new("b11072bf-7980-4e23-bc6c-8034ba09b925");
    private static readonly Guid Cauldron = new("182ce873-3b20-4e74-8c5f-07f057666871");
    private static readonly Guid ImprovedAlchemy = new("d4a9711d-e1f8-4951-999c-11e1026e586b");

    [Fact]
    public void ACappedResourceReportsHeadroomAndFill()
    {
        // The motivating case: 60 held against a cap of 100 is 0.6 full with 40 to spare.
        var row = DeriveRow(WorldSamples.Resource(Mana, 60d, 100d, 2d, true));

        Assert.True(row.IsCapped);
        Assert.Equal(0.6d, row.FillFraction, 10);
        Assert.Equal(40d, row.Headroom.ToDouble());
        Assert.False(row.IsAtCapacity);
        Assert.Equal(2d, row.TrueRate.ToDouble());
    }

    [Fact]
    public void AnUncappedResourceIsNotReportedAsFullOrEmpty()
    {
        // The game marks an absent ceiling with a negative value — HasMaxQuantity() is
        // `maxQuantity >= 0`. Reporting zero headroom as though it were a real bound would strand
        // every uncapped resource; consumers must read Headroom together with IsCapped.
        var row = DeriveRow(WorldSamples.Resource(Mana, 1e12d, -1d, 0d, true));

        Assert.False(row.IsCapped);
        Assert.Equal(0d, row.FillFraction);
        Assert.Equal(0d, row.Headroom.ToDouble());
        Assert.False(row.IsAtCapacity);

        // The captured reading itself is preserved untouched.
        Assert.Equal(1e12d, row.Reading.Quantity.ToDouble());
    }

    [Fact]
    public void AZeroCeilingIsARealCeilingRatherThanAnAbsentOne()
    {
        // The distinction the game draws, which this deriver must not blur: zero is a ceiling of
        // zero, and only a negative value means "no ceiling". Folding the two together would report
        // a resource that can hold nothing as one with unlimited room — and would make every
        // capacity-relative stance silently stop binding on it.
        var row = DeriveRow(WorldSamples.Resource(Mana, 0d, 0d, 0d, true));

        Assert.True(row.IsCapped);
        Assert.True(row.IsAtCapacity);
        Assert.Equal(0d, row.Headroom.ToDouble());
    }

    [Fact]
    public void ReachingAndPassingTheCapBothCountAsFull()
    {
        var exactly = DeriveRow(WorldSamples.Resource(Mana, 100d, 100d, 0d, true));
        Assert.True(exactly.IsAtCapacity);
        Assert.Equal(1d, exactly.FillFraction, 10);
        Assert.Equal(0d, exactly.Headroom.ToDouble());

        // Overflow is legitimate in game; headroom clamps to zero and the fraction stays bounded
        // rather than exceeding one and breaking every consumer that treats it as a percentage.
        var over = DeriveRow(WorldSamples.Resource(Mana, 150d, 100d, 0d, true));
        Assert.True(over.IsAtCapacity);
        Assert.Equal(1d, over.FillFraction, 10);
        Assert.Equal(0d, over.Headroom.ToDouble());
        Assert.Equal(150d, over.Reading.Quantity.ToDouble());
    }

    /// <summary>
    /// An inverted counter displays missing capacity, so "full" is the mirror of an ordinary
    /// counter's: Potion Toxicity holding its whole tolerance displays zero accrued toxicity and is
    /// not at capacity, while a depleted Stability pool displays its ceiling and is.
    /// </summary>
    [Fact]
    public void AnInvertedCounterIsFullWhenItsDisplayedNumberReachesTheCeiling()
    {
        var inverted = Inverted();

        var untouched = DeriveRow(
            WorldSamples.Resource(Mana, 100d, 100d, 0d, true, traits: inverted));
        Assert.False(untouched.IsAtCapacity);
        Assert.Equal(0d, untouched.Headroom.ToDouble());

        var partway = DeriveRow(
            WorldSamples.Resource(Mana, 40d, 100d, 0d, true, traits: inverted));
        Assert.False(partway.IsAtCapacity);
        Assert.Equal(60d, partway.Headroom.ToDouble());

        var maxed = DeriveRow(WorldSamples.Resource(Mana, 0d, 100d, 0d, true, traits: inverted));
        Assert.True(maxed.IsAtCapacity);
        Assert.Equal(100d, maxed.Headroom.ToDouble());
    }

    private static RawResourceTraits Inverted() => new(
        0d, 0d, 0d,
        false, false, false,
        false, true, false, false,
        BigDouble.Zero, 0, 0, 0d, false, 0d,
        BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);

    [Fact]
    public void AstronomicalMagnitudesStillProduceAUsableFraction()
    {
        // Real holdings run past 1e30. The ratio is bounded even when both operands are not, which
        // is why the fraction is a double while the magnitudes stay BigDouble.
        var row = DeriveRow(
            WorldSamples.Resource(Mana, new BigDouble(2.5d, 31), new BigDouble(1d, 32), 0d, true));

        Assert.True(row.IsCapped);
        Assert.Equal(0.25d, row.FillFraction, 6);
        Assert.False(row.IsAtCapacity);
        Assert.Equal(7.5d, row.Headroom.ToDouble() / 1e31, 6);
    }

    [Fact]
    public void UnreadableValuesDeriveNeutrallyInsteadOfConfidently()
    {
        // A NaN must never become a fabricated bound. Reporting "no capacity information" routes
        // capacity-relative stances down their documented not-applicable path, which is visible in
        // diagnostics, instead of silently producing a floor nobody authored.
        var badCapacity = DeriveRow(
            WorldSamples.Resource(Mana, 10d, BigDouble.NaN, 0d, true));
        Assert.False(badCapacity.IsCapped);
        Assert.Equal(0d, badCapacity.FillFraction);

        var badQuantity = DeriveRow(
            WorldSamples.Resource(Mana, BigDouble.NaN, 100d, 0d, true));
        Assert.False(badQuantity.IsCapped);
        Assert.Equal(0d, badQuantity.FillFraction);
        Assert.False(badQuantity.IsAtCapacity);
    }

    [Fact]
    public void ANegativeCapacityIsNotACap()
    {
        var row = DeriveRow(WorldSamples.Resource(Mana, 10d, -5d, 0d, true));

        Assert.False(row.IsCapped);
        Assert.Equal(0d, row.Headroom.ToDouble());
    }

    [Fact]
    public void QueuedStructureLevelsCountAsCommitted()
    {
        // Queued levels are already bought and developing. A purchase decision that ranks on owned
        // level alone re-buys work that is in flight.
        var row = DeriveRow(WorldSamples.Structure(Cauldron, 12d, 3d, true));

        Assert.Equal(12d, row.Reading.Level.ToDouble());
        Assert.Equal(15d, row.CommittedLevel.ToDouble());
        Assert.True(row.HasWorkInFlight);

        var idle = DeriveRow(WorldSamples.Structure(Cauldron, 12d, 0d, true));
        Assert.False(idle.HasWorkInFlight);
        Assert.Equal(12d, idle.CommittedLevel.ToDouble());
    }

    [Fact]
    public void BoundedAndUnboundedUpgradesAreDistinguishable()
    {
        // Improved Alchemy really does have maxLevel 1 in the extracted definitions.
        var unbought = DeriveRow(WorldSamples.Upgrade(ImprovedAlchemy, 0, 1, true));
        Assert.True(unbought.IsBounded);
        Assert.False(unbought.IsExhausted);
        Assert.Equal(1, unbought.RemainingLevels);

        var bought = DeriveRow(WorldSamples.Upgrade(ImprovedAlchemy, 1, 1, false));
        Assert.True(bought.IsExhausted);
        Assert.Equal(0, bought.RemainingLevels);

        // An unbounded upgrade is never exhausted, and its remaining count is meaningless rather
        // than zero — callers must branch on IsBounded.
        var unbounded = DeriveRow(WorldSamples.Upgrade(ImprovedAlchemy, 40, 0, true));
        Assert.False(unbounded.IsBounded);
        Assert.False(unbounded.IsExhausted);
        Assert.Equal(0, unbounded.RemainingLevels);
    }

    [Fact]
    public void QualityScalesWhatHoldingsAreActuallyWorth()
    {
        // The game's GetTrueQuantity() is quantity * quality.AsPercent(), and quality is stored in
        // the game's percent representation where 100 is parity. Comparing a cost against the raw
        // quantity is wrong by exactly this factor.
        var doubled = DeriveRow(WorldSamples.Resource(Mana, 50d, quality: 200d));
        Assert.Equal(100d, doubled.TrueQuantity.ToDouble(), 10);

        var parity = DeriveRow(WorldSamples.Resource(Mana, 50d));
        Assert.Equal(50d, parity.TrueQuantity.ToDouble(), 10);

        // Quality of zero is a real reading, not an absent one: the holdings are worth nothing.
        var worthless = DeriveRow(WorldSamples.Resource(Mana, 50d, quality: 0d));
        Assert.Equal(0d, worthless.TrueQuantity.ToDouble());

        // Capacity is a bound on the raw quantity, so it is untouched by quality.
        var capped = DeriveRow(
            WorldSamples.Resource(Mana, 50d, capacity: 100d, quality: 200d));
        Assert.Equal(50d, capped.Headroom.ToDouble(), 10);
        Assert.Equal(0.5d, capped.FillFraction, 10);
    }

    [Fact]
    public void WhatAStructureDoesAndWhatItsNextLevelCostsAreDifferentNumbers()
    {
        // GetPurchaseLevel excludes every granted level, because cost scales on owned levels alone.
        // A consumer ranking by strength needs the other number, and reaching for Level would
        // undercount every structure carrying a bonus.
        var row = DeriveRow(WorldSamples.Structure(
            Cauldron, level: 10d, queuedLevels: 2d, selfBonusLevels: 3, bonusLevels: 5d, effectLevels: 1d));

        Assert.Equal(10d, row.Reading.Level.ToDouble());
        Assert.Equal(12d, row.CommittedLevel.ToDouble());
        Assert.Equal(19d, row.EffectiveLevel.ToDouble());
    }

    [Fact]
    public void DevelopmentProgressReadsTheGamesCountdownRatherThanACountUp()
    {
        // Both timers count down, and both pair with the total the game itself divides by:
        // GetQueueTimeRatio() is 1 - queueTimeLeft / currentBuildTime, and the upgrade equivalent is
        // 1 - buildTime / developmentTime. Reading either as elapsed time would invert the bar.
        var quarterDone = DeriveRow(WorldSamples.Structure(
            Cauldron, queuedLevels: 1d, queueTimeLeft: 30d, currentBuildTime: 40d));
        Assert.Equal(0.25d, quarterDone.DevelopmentProgress, 10);

        var upgrade = DeriveRow(WorldSamples.Upgrade(
            ImprovedAlchemy, queuedLevels: 1, buildTime: 1d, developmentTime: 5d));
        Assert.Equal(0.8d, upgrade.DevelopmentProgress, 10);
        Assert.True(upgrade.IsDeveloping);
        Assert.Equal(1, upgrade.CommittedLevel);
    }

    [Fact]
    public void AnIdleEntityNeverReadsAsPartlyBuilt()
    {
        // Nothing in flight means both timers sit at zero, and a ratio of 0/0 must not surface as a
        // completed build. This is the case a naive `1 - remaining / total` gets wrong.
        var idle = DeriveRow(WorldSamples.Structure(Cauldron, level: 4d));
        Assert.Equal(0d, idle.DevelopmentProgress);

        var upgrade = DeriveRow(WorldSamples.Upgrade(ImprovedAlchemy));
        Assert.Equal(0d, upgrade.DevelopmentProgress);
        Assert.False(upgrade.IsDeveloping);
    }

    [Fact]
    public void DerivedRowsFeedTheSpendPolicyDirectly()
    {
        // The world row and the spend policy have to agree about what "has a capacity" means, or a
        // fraction-of-capacity stance silently stops binding. This pins the seam between them.
        var capped = DeriveRow(WorldSamples.Resource(Mana, 100d, 200d, 0d, true));
        var stance = SuiteResourceStance.FractionOfCapacity(Mana, 0.4d);

        // Leaving 40% of a 200 cap means 80 must remain; spending 30 of 100 leaves 70, so it fails.
        var refused = SuiteResourceSpendPolicy.Evaluate(
            in stance, 30d, capped.Reading.Quantity, capped.IsCapped, capped.Reading.Capacity, default, 0d);
        Assert.Equal(SuiteSpendOutcome.BlockedStrategyFloor, refused.Outcome);

        // Spending 15 leaves 85, above the floor.
        var allowed = SuiteResourceSpendPolicy.Evaluate(
            in stance, 15d, capped.Reading.Quantity, capped.IsCapped, capped.Reading.Capacity, default, 0d);
        Assert.Equal(SuiteSpendOutcome.Allowed, allowed.Outcome);

        // The same stance on an uncapped resource reports itself as inapplicable rather than
        // inventing a bound — the authoring error stays visible.
        var uncapped = DeriveRow(WorldSamples.Resource(Mana, 100d, -1d, 0d, true));
        var notApplicable = SuiteResourceSpendPolicy.Evaluate(
            in stance, 30d, uncapped.Reading.Quantity, uncapped.IsCapped, uncapped.Reading.Capacity, default, 0d);
        Assert.Equal(SuiteSpendOutcome.AllowedStanceNotApplicable, notApplicable.Outcome);
    }

    // The resource chain needs frame-wide rate terms. These tests are about the per-resource
    // derivations, so they pass the neutral globals explicitly rather than letting a default hide
    // which value was used.
    private static WorldFrameGlobals Globals => default;

    private static WorldResource DeriveRow(in RawResourceSample sample) =>
        GameWorldStateDeriver.Derive(in sample, Globals);

    private static WorldStructure DeriveRow(in RawStructureSample sample) =>
        GameWorldStateDeriver.Derive(in sample);

    private static WorldUpgrade DeriveRow(in RawUpgradeSample sample) =>
        GameWorldStateDeriver.Derive(in sample);


    /// <summary>
    /// The published rate is the ported chain's answer over the captured reading — the point of the
    /// whole design being that this is computed once, off the Unity thread, rather than asked of the
    /// game per consumer.
    /// </summary>
    /// <summary>
    /// Both sides of the capacity line, because they exercise disjoint halves of the chain: over the
    /// cap the overflow terms participate and the missing-percent term is clamped to zero, and under
    /// it the reverse. One sample alone leaves half the projection unpinned.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDerivedRateIsThePortedChainOverTheCapturedReading(bool overCapacity)
    {
        var sample = LiveResourceSample(overCapacity);
        var globals = LiveGlobals();

        var row = GameWorldStateDeriver.Derive(in sample, globals);

        Assert.Equal(GameResourceRateMath.GetTrueRate(Expected(in sample, in globals)), row.TrueRate);
        Assert.False(BigDouble.IsNaN(row.TrueRate) || BigDouble.IsInfinity(row.TrueRate));
    }

    /// <summary>
    /// Each frame-wide term on its own changes the answer. Asserted per term rather than in bulk
    /// because dropping any one of them still produces a plausible number, and a single combined
    /// comparison would pass on three out of four reaching the chain.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryFrameWideRateGlobalReachesTheChain(int term)
    {
        var sample = LiveResourceSample(overCapacity: true);
        var baseline = LiveGlobals();
        var varied = term switch
        {
            0 => new WorldFrameGlobals(new BigDouble(5d), baseline.ResourceOverflowLossPercent,
                baseline.ResetTimePassed, baseline.StructureCostPercent,
                baseline.AttributeQualityBonus, baseline.FixedDeltaTime),
            1 => new WorldFrameGlobals(baseline.ResourceOverflowPercent, new BigDouble(0.25d),
                baseline.ResetTimePassed, baseline.StructureCostPercent,
                baseline.AttributeQualityBonus, baseline.FixedDeltaTime),
            2 => new WorldFrameGlobals(baseline.ResourceOverflowPercent,
                baseline.ResourceOverflowLossPercent, new BigDouble(500d),
                baseline.StructureCostPercent, baseline.AttributeQualityBonus,
                baseline.FixedDeltaTime),
            _ => new WorldFrameGlobals(baseline.ResourceOverflowPercent,
                baseline.ResourceOverflowLossPercent, baseline.ResetTimePassed,
                baseline.StructureCostPercent, baseline.AttributeQualityBonus, 0.05d),
        };

        Assert.NotEqual(
            GameWorldStateDeriver.Derive(in sample, baseline).TrueRate,
            GameWorldStateDeriver.Derive(in sample, varied).TrueRate);
    }

    /// <summary>
    /// Any one of the six modifier counts, on its own, makes the resource count as actively gaining —
    /// which over the cap is the difference between damped overflow gain and a rate clamped at zero.
    /// </summary>
    /// <remarks>
    /// Asserted collectively rather than per flag, because five of the six are interchangeable inside
    /// <c>HasActiveRate</c>'s first conjunction: swapping two of them there is unobservable through any
    /// input. What this does pin is that each count reaches the chain at all, and that a count of zero
    /// with a non-zero value still reads as inactive — the distinction the whole flag exists for.
    /// </remarks>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void AnyOneRateModifierCountMakesTheResourceActivelyGaining(int term, bool gains)
    {
        var counts = new int[6];
        if (term >= 0) counts[term] = 1;
        var sample = WorldSamples.Resource(
            Mana,
            quantity: 150d,
            capacity: 100d,
            rateInputs: new RawResourceRateInputs(
                rate: new BigDouble(10d),
                rateSplash: new BigDouble(10d),
                rateMaxPercent: new BigDouble(10d),
                rateInterestPercent: new BigDouble(10d),
                rateMissingPercent: new BigDouble(10d),
                rateLifetimePercent: new BigDouble(10d),
                rateModifiers: counts[0],
                rateSplashModifiers: counts[1],
                rateMaxPercentModifiers: counts[2],
                rateInterestPercentModifiers: counts[3],
                rateMissingPercentModifiers: counts[4],
                rateLifetimePercentModifiers: counts[5],
                lossPercent: default,
                displayRate: new BigDouble(19d),
                calcRarityValue: new BigDouble(1d),
                baseLoss: 0d));

        var rate = GameWorldStateDeriver.Derive(in sample, LiveGlobals()).TrueRate;

        Assert.Equal(gains, rate > 0);
    }

    /// <summary>
    /// In loss mode with every rate term active and distinct, so a projection that dropped or swapped
    /// a term moves the answer instead of landing on a zero that was going to be zero anyway.
    /// </summary>
    private static RawResourceSample LiveResourceSample(bool overCapacity) =>
        WorldSamples.Resource(
            Mana,
            quantity: overCapacity ? 150d : 40d,
            capacity: 100d,
            quality: 120d,
            gainRate: 140d,
            drain: 2d,
            lifetimeQuantity: 400d,
            inLossMode: true,
            rateInputs: new RawResourceRateInputs(
                rate: new BigDouble(10d),
                rateSplash: new BigDouble(3d),
                rateMaxPercent: new BigDouble(5d),
                rateInterestPercent: new BigDouble(7d),
                rateMissingPercent: new BigDouble(11d),
                rateLifetimePercent: new BigDouble(13d),
                rateModifiers: 1,
                rateSplashModifiers: 1,
                rateMaxPercentModifiers: 1,
                rateInterestPercentModifiers: 1,
                rateMissingPercentModifiers: 1,
                rateLifetimePercentModifiers: 1,
                lossPercent: new BigDouble(17d),
                displayRate: new BigDouble(19d),
                calcRarityValue: new BigDouble(23d),
                baseLoss: 29d));

    /// <summary>
    /// Already past <c>AsPercent</c>, which is where the reader applies it — the struct carries
    /// fractions, not the percentages the game stores.
    /// </summary>
    private static WorldFrameGlobals LiveGlobals() =>
        new(new BigDouble(2d), new BigDouble(1.5d), new BigDouble(250d), new BigDouble(1.25d),
            new BigDouble(3d), 0.02d);

    /// <summary>
    /// How many of a plot node an action may still be started on, per the game's own formula.
    /// </summary>
    /// <remarks>
    /// The asymmetry between the two usage terms is the whole content of this test. Eleven of the
    /// node exist, seven of them idle and four busy; two are spoken for by a main-phase action and
    /// six by an any-phase one. The any-phase six are absorbed by the four busy first, so only two
    /// of them reach the idle count: 7 - 2 - 2 = 3. Treating the two terms alike would give
    /// 7 - 2 - 6 = -1, and treating "any" as never reaching idle would give 5.
    /// </remarks>
    [Theory]
    [InlineData(7, 11, 2, 6, 3)]
    // Nothing busy, so the any-phase usage lands on the idle count in full.
    [InlineData(7, 7, 2, 6, -1)]
    // More busy than the any-phase usage, so it is absorbed entirely and only the main term bites.
    [InlineData(7, 20, 2, 6, 5)]
    // Nothing claimed at all: every idle one is available.
    [InlineData(7, 11, 0, 0, 7)]
    public void APlotNodesRemainingQuantityAbsorbsAnyPhaseUsageIntoWhatIsBusyFirst(
        int idle,
        int total,
        int usageMain,
        int usageAny,
        int expected)
    {
        var derived = GameWorldStateDeriver.Derive(PlotNode(idle, total, usageMain, usageAny));

        Assert.Equal(expected, derived.RemainingQuantity);
        Assert.Equal(idle, derived.Reading.IdleQuantity);
        Assert.Equal(total, derived.Reading.TotalQuantity);
    }

    private static RawPlotNodeSample PlotNode(int idle, int total, int usageMain, int usageAny) =>
        new(
            Guid.NewGuid(),
            visible: true,
            currentTime: default,
            nextErraticTime: default,
            sizeLevel: default,
            masteryXp: default,
            masteryLevel: 0,
            noMastery: false,
            noSizeDisplay: false,
            useVisibilityPrereq: false,
            hasErraticGrowth: false,
            debugMode: false,
            erraticQuantity: 0,
            actionQuantityUsageMain: new BigDouble(usageMain),
            actionQuantityUsageAny: new BigDouble(usageAny),
            actionXpRate: default,
            yieldMod: default,
            specialMod: default,
            actionSpeed: default,
            actionCostMod: default,
            growingSpeed: default,
            restingSpeed: default,
            sizeMod: default,
            qualityMod: default,
            recoverySizeMod: default,
            naturalGrowth: default,
            naturalGrowthPower: default,
            lastQuantity: 0,
            idleQuantity: idle,
            totalQuantity: total);

    /// <summary>
    /// The mapping stated independently of the deriver, so a swapped field fails rather than agreeing
    /// with itself.
    /// </summary>
    private static GameResourceRateInputs Expected(
        in RawResourceSample sample,
        in WorldFrameGlobals globals)
    {
        var rates = sample.RateInputs;
        return new GameResourceRateInputs
        {
            Rate = rates.Rate,
            RateSplash = rates.RateSplash,
            RateMaxPercent = rates.RateMaxPercent,
            RateInterestPercent = rates.RateInterestPercent,
            RateMissingPercent = rates.RateMissingPercent,
            RateLifetimePercent = rates.RateLifetimePercent,
            MaxQuantity = sample.Capacity,
            Quality = sample.Quality,
            GainRate = sample.GainRate,
            Drain = sample.Drain,
            LossPercent = rates.LossPercent,
            DisplayRate = rates.DisplayRate,
            Quantity = sample.Quantity,
            LifetimeQuantity = sample.LifetimeQuantity,
            CalcRarityValue = rates.CalcRarityValue,
            BaseLoss = rates.BaseLoss,
            Visible = sample.Visible,
            InLossMode = sample.InLossMode,
            RateHasActive = true,
            RateSplashHasActive = true,
            RateMaxPercentHasActive = true,
            RateInterestPercentHasActive = true,
            RateMissingPercentHasActive = true,
            RateLifetimePercentHasActive = true,
            ResourceOverflowPercent = globals.ResourceOverflowPercent,
            ResourceOverflowLossPercent = globals.ResourceOverflowLossPercent,
            ResetTimePassed = globals.ResetTimePassed,
            FixedDeltaTime = globals.FixedDeltaTime,
        };
    }
}
