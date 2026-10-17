using System;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// The resource rate chain, checked against values derived by hand from the decompiled original.
/// </summary>
/// <remarks>
/// These tests prove the port is self-consistent with a reading of the game's source. They cannot
/// prove that reading was right — a misreading would be reproduced identically here and in the port
/// — which is what the in-game differential run exists for. What they do protect is every branch and
/// every surprising detail, so a later "cleanup" that changes behaviour fails immediately rather than
/// at the next differential run.
/// </remarks>
public sealed class GameResourceRateMathTests
{
    /// <summary>
    /// A resource that does nothing: uncapped, no modifiers, neutral quality and gain. Percent-style
    /// records sit at 100 because that is the game's identity for them, and rate-style records at
    /// zero. Tests change only the fields they are about.
    /// </summary>
    private static GameResourceRateInputs Neutral() => new()
    {
        MaxQuantity = -1d,
        Quality = 100d,
        GainRate = 100d,
        ResetTimePassed = 1d,

        // Both globals arrive already converted from the game's percent representation, so the
        // neutral value is 1 (the game's 100), not 100. Getting this wrong is easy and quiet: a
        // hundredfold overflow allowance silently moves GetOverflowGain onto its damping branch.
        ResourceOverflowPercent = 1d,
        ResourceOverflowLossPercent = 1d,
        FixedDeltaTime = 0.02d,
        Visible = true,
    };

    [Fact]
    public void AnUncappedResourceHasNoCeilingAndNoMissingQuantity()
    {
        var r = Neutral();
        r.Quantity = 500d;

        Assert.False(GameResourceRateMath.HasMaxQuantity(in r));
        Assert.Equal(0d, GameResourceRateMath.GetMissing(in r).ToDouble());

        // A max-percent rate on an uncapped resource has nothing to be a percent of.
        r.RateMaxPercent = 60d;
        Assert.Equal(0d, GameResourceRateMath.GetMaxPercentRate(in r).ToDouble());
    }

    [Fact]
    public void AZeroCeilingIsStillACeiling()
    {
        var r = Neutral();
        r.MaxQuantity = 0d;

        Assert.True(GameResourceRateMath.HasMaxQuantity(in r));
    }

    [Fact]
    public void PercentRatesAreQuotedPerMinuteAndDeliveredPerSecond()
    {
        // The divisor of 60 is easy to mistake for a magic number. 60% of a 1000 cap per minute is
        // 10 per second.
        var r = Neutral();
        r.MaxQuantity = 1000d;
        r.RateMaxPercent = 60d;
        Assert.Equal(10d, GameResourceRateMath.GetMaxPercentRate(in r).ToDouble(), 10);

        r = Neutral();
        r.Quantity = 1000d;
        r.RateInterestPercent = 60d;
        Assert.Equal(10d, GameResourceRateMath.GetInterestPercentRateFlat(in r).ToDouble(), 10);

        r = Neutral();
        r.MaxQuantity = 1000d;
        r.Quantity = 400d;
        r.RateMissingPercent = 60d;
        Assert.Equal(6d, GameResourceRateMath.GetMissingPercentRateFlat(in r).ToDouble(), 10);
    }

    [Fact]
    public void SplashRateIsSuppressedForAnUndiscoveredResource()
    {
        // Visibility gates a number here rather than a display, so this cannot be treated as a
        // presentation concern the worker may skip.
        var r = Neutral();
        r.RateSplash = 10d;
        r.Visible = false;

        Assert.Equal(0d, GameResourceRateMath.GetSplashRate(in r).ToDouble());

        r.Visible = true;
        Assert.Equal(10d, GameResourceRateMath.GetSplashRate(in r).ToDouble(), 10);
    }

    [Fact]
    public void RaritySplashInvertsANonZeroRarityAndOtherwiseVanishes()
    {
        var r = Neutral();
        Assert.Equal(1d, GameResourceRateMath.GetCalcRaritySplash(in r).ToDouble());

        r.CalcRarityValue = 4d;
        Assert.Equal(0.25d, GameResourceRateMath.GetCalcRaritySplash(in r).ToDouble(), 10);

        r.RateSplash = 10d;
        Assert.Equal(2.5d, GameResourceRateMath.GetSplashRate(in r).ToDouble(), 10);
    }

    [Fact]
    public void GainRateScalesRateAndSplashButNotThePercentTerms()
    {
        // (rate + splash) * gainRate, then the percent terms are added — not scaled. Distributing
        // gainRate across all four would be the obvious "simplification" and would be wrong.
        var r = Neutral();
        r.Rate = 10d;
        r.RateSplash = 10d;
        r.CalcRarityValue = 4d;      // splash contributes 10 * 0.25 = 2.5
        r.GainRate = 200d;           // doubles
        r.MaxQuantity = 1000d;
        r.RateMaxPercent = 60d;      // contributes 10, unscaled

        Assert.Equal(35d, GameResourceRateMath.GetBaseModdedRate(in r).ToDouble(), 10);
    }

    [Fact]
    public void LifetimeRateIsIgnoredUnlessItsPercentIsPositive()
    {
        var r = Neutral();
        r.LifetimeQuantity = 120d;
        r.ResetTimePassed = 60d;

        Assert.Equal(2d, GameResourceRateMath.GetAvgLifeTimeRate(in r).ToDouble(), 10);
        Assert.Equal(0d, GameResourceRateMath.GetLifetimePercentRate(in r).ToDouble());

        r.RateLifetimePercent = 50d;
        Assert.Equal(1d, GameResourceRateMath.GetLifetimePercentRate(in r).ToDouble(), 10);
    }

    [Fact]
    public void DrainIsDividedByQualityRatherThanMultiplied()
    {
        // Draining higher-quality stock costs less of it. Getting this backwards would still produce
        // plausible numbers, which is exactly why it is pinned.
        var r = Neutral();
        r.Drain = 5d;
        r.Quality = 200d;

        Assert.Equal(2.5d, GameResourceRateMath.GetModdedDrain(in r).ToDouble(), 10);
    }

    [Fact]
    public void LossOnlyAppliesInLossModeAndNeverToAnEmptyResource()
    {
        var r = Neutral();
        r.Quantity = 100d;
        r.LossPercent = 10d;

        Assert.Equal(0d, GameResourceRateMath.GetLossRate(in r).ToDouble());

        r.InLossMode = true;
        r.Quantity = 0d;
        Assert.Equal(0d, GameResourceRateMath.GetLossRate(in r).ToDouble());
    }

    [Fact]
    public void APercentageLossAlwaysCarriesTheBaseLossWithIt()
    {
        var r = Neutral();
        r.InLossMode = true;
        r.Quantity = 100d;
        r.LossPercent = 10d;
        r.BaseLoss = 0.5d;
        r.MaxQuantity = 1000d;

        // 10% of 100, plus the flat base loss. No overflow term: the resource is under its cap.
        Assert.Equal(10.5d, GameResourceRateMath.GetLossRate(in r).ToDouble(), 10);
    }

    [Fact]
    public void AResourceWithNoLossPercentStillBleedsWhenItOverflows()
    {
        var r = Neutral();
        r.InLossMode = true;
        r.Quantity = 2000d;
        r.MaxQuantity = 1000d;
        r.BaseLoss = 0.5d;
        r.LossPercent = 0d;

        // 1000 over the cap, at the game's literal 0.85, plus base loss.
        Assert.Equal(850.5d, GameResourceRateMath.GetLossRate(in r).ToDouble(), 10);

        // Asking without overflow takes the early return instead, because lossPercent is zero — so
        // the answer is not "the same minus the overflow term", it is nothing at all. The base loss
        // disappears with it.
        Assert.Equal(0d, GameResourceRateMath.GetLossRate(in r, withoutOverflow: true).ToDouble());
    }

    [Fact]
    public void InterestAloneCountsAsAnActiveRateOnlyWhenTheResourceHoldsSomething()
    {
        // The original leaves rateInterestPercent out of its first conjunction, so it is checked only
        // after the other five come back empty, and then gated on quantity. This asymmetry decides
        // which branch GetTrueRate takes for an empty interest-bearing resource.
        var r = Neutral();
        r.RateInterestPercentHasActive = true;

        r.Quantity = 0d;
        Assert.False(GameResourceRateMath.HasActiveRate(in r));

        r.Quantity = 5d;
        Assert.True(GameResourceRateMath.HasActiveRate(in r));

        // Any of the other five is enough on its own, whatever the quantity.
        r = Neutral();
        r.RateMaxPercentHasActive = true;
        r.Quantity = 0d;
        Assert.True(GameResourceRateMath.HasActiveRate(in r));
    }

    [Fact]
    public void BelowTheCapTheRateIsTheStraightSum()
    {
        var r = Neutral();
        r.MaxQuantity = 1000d;
        r.Quantity = 100d;
        r.Rate = 10d;
        r.Drain = 5d;
        r.Quality = 200d;

        // 10 gained, 2.5 drained.
        Assert.Equal(7.5d, GameResourceRateMath.GetTrueRate(in r).ToDouble(), 10);
    }

    [Fact]
    public void BeingAThousandthOverTheCapStillCountsAsBeingAtIt()
    {
        // ApproxError's one-in-a-thousand window keeps a resource sitting exactly at its cap from
        // flickering into the overflow branch on rounding noise.
        var r = Neutral();
        r.MaxQuantity = 1000d;
        r.Quantity = 1000.5d;
        r.Rate = 10d;

        Assert.True(OrbGameMath.ApproxError(1000.5d, 1000d));
        Assert.Equal(10d, GameResourceRateMath.GetTrueRate(in r).ToDouble(), 10);
    }

    [Fact]
    public void AnOverflowingResourceWithNoActiveRateCanLoseButNeverGain()
    {
        var r = Neutral();
        r.MaxQuantity = 1000d;
        r.Quantity = 2000d;
        r.Rate = 10d;

        // Clamped from above at zero rather than reported as +10.
        Assert.Equal(0d, GameResourceRateMath.GetTrueRate(in r).ToDouble());

        // The clamp is on the net figure, not on the gain: a drain smaller than the gain still nets
        // out to zero rather than to the drain.
        r.Drain = 5d;
        Assert.Equal(0d, GameResourceRateMath.GetTrueRate(in r).ToDouble());

        // Only a net loss gets through.
        r.Drain = 15d;
        Assert.Equal(-5d, GameResourceRateMath.GetTrueRate(in r).ToDouble(), 10);
    }

    [Fact]
    public void GainThatWouldNotReachTheCapIsNotDamped()
    {
        var r = Neutral();
        r.MaxQuantity = 1000d;
        r.Quantity = 100d;

        Assert.Equal(50d, GameResourceRateMath.GetOverflowGain(in r, 50d).ToDouble(), 10);
    }

    [Fact]
    public void AnOverflowAllowanceBelowParityAdmitsNothingPastTheCap()
    {
        // ratio = 1 - 1/overflow, and the overflow allowance is already a fraction here: the game's
        // 50 arrives as 0.5, giving ratio -1. The original's `<= 0` branch then clamps to whatever
        // headroom is left, which for a resource already past its cap is nothing.
        var r = Neutral();
        r.MaxQuantity = 1000d;
        r.Quantity = 2000d;
        r.ResourceOverflowPercent = 0.5d;

        Assert.Equal(0d, GameResourceRateMath.GetOverflowGain(in r, 100d).ToDouble());
    }

    [Fact]
    public void GainPastTheCapIsGeometricallyDampedRatherThanDiscarded()
    {
        // The branch that justifies the whole function existing. With the game's overflow at 200 the
        // ratio is 0.5, so each further cap's worth of headroom is worth half the last: 1500 held
        // against a 1000 cap is already two terms in, and a further 100 of raw gain is worth ~33.5.
        var r = Neutral();
        r.MaxQuantity = 1000d;
        r.Quantity = 1500d;
        r.ResourceOverflowPercent = 2d;

        Assert.Equal(33.48d, GameResourceRateMath.GetOverflowGain(in r, 100d).ToDouble(), 2);
    }

    [Fact]
    public void EquilibriumIsWhereLossNetOfInterestCancelsTheFlatGain()
    {
        // 10/s against 5% loss balances at 200; a 100 ceiling holds it at 100.
        var r = Neutral();
        r.Rate = 10d;
        r.LossPercent = 5d;
        Assert.Equal(200d, GameResourceRateMath.GetEquilibrium(in r).ToDouble(), 10);

        r.MaxQuantity = 100d;
        Assert.Equal(100d, GameResourceRateMath.GetEquilibrium(in r).ToDouble(), 10);
    }

    [Fact]
    public void WithNoNetLossEquilibriumIsTheCeilingOrUnbounded()
    {
        var r = Neutral();
        r.Rate = 10d;
        r.LossPercent = 5d;
        r.RateInterestPercent = 5d;
        Assert.True(BigDouble.IsPositiveInfinity(GameResourceRateMath.GetEquilibrium(in r)));

        r.MaxQuantity = 300d;
        Assert.Equal(300d, GameResourceRateMath.GetEquilibrium(in r).ToDouble());
    }

    [Fact]
    public void ADrainThatOutrunsTheGainBalancesAtNothing()
    {
        var r = Neutral();
        r.Rate = 10d;
        r.Drain = 20d;
        r.LossPercent = 5d;
        r.MaxQuantity = 300d;

        Assert.Equal(0d, GameResourceRateMath.GetEquilibrium(in r).ToDouble());
    }
}
