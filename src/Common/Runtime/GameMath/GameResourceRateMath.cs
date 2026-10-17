namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// Everything <see cref="GameResourceRateMath"/> reads for one resource, gathered on the Unity
/// thread so the arithmetic can run anywhere.
/// </summary>
/// <remarks>
/// <para>
/// A plain mutable struct with fields rather than the readonly, constructor-taking shape used for
/// published values. This is neither a publication nor a boundary type: it is the argument list of a
/// pure function, and that list is two dozen entries long. A constructor with two dozen positional
/// parameters would be a defect factory, and the readonly discipline buys nothing for a value that
/// never crosses a thread or outlives the call.
/// </para>
/// <para>
/// Every modifier field holds a value folded by the collector from the record's own
/// <c>baseValue</c> and modifier sets, rather than read through <c>GetValue()</c> — that accessor
/// recalculates and re-stamps its observable when dirty, and the suite does not write game state
/// outside the action boundary.
/// </para>
/// <para>
/// The three globals at the end are per-tick, not per-resource: read them once per collection and
/// copy them into every resource's inputs. <c>FixedDeltaTime</c> in particular is a Unity static that
/// may only be touched on the main thread, which is exactly why it is an input rather than a call.
/// </para>
/// </remarks>
internal struct GameResourceRateInputs
{
    // Cached modifier values.
    internal BigDouble Rate;
    internal BigDouble RateSplash;
    internal BigDouble RateMaxPercent;
    internal BigDouble RateInterestPercent;
    internal BigDouble RateMissingPercent;
    internal BigDouble RateLifetimePercent;
    internal BigDouble MaxQuantity;
    internal BigDouble Quality;
    internal BigDouble GainRate;
    internal BigDouble Drain;
    internal BigDouble LossPercent;
    internal BigDouble DisplayRate;

    // Plain persisted or derived fields.
    internal BigDouble Quantity;
    internal BigDouble LifetimeQuantity;
    internal BigDouble CalcRarityValue;
    internal double BaseLoss;
    internal bool Visible;
    internal bool InLossMode;

    // Whether each rate record carries any active modifier. The game asks the records directly;
    // reading the count of the active-modifier dictionary is the same question without the call.
    internal bool RateHasActive;
    internal bool RateSplashHasActive;
    internal bool RateMaxPercentHasActive;
    internal bool RateInterestPercentHasActive;
    internal bool RateMissingPercentHasActive;
    internal bool RateLifetimePercentHasActive;

    // Per-tick globals, already converted from their percent representation by the caller.
    internal BigDouble ResourceOverflowPercent;
    internal BigDouble ResourceOverflowLossPercent;
    internal BigDouble ResetTimePassed;
    internal double FixedDeltaTime;
}

/// <summary>
/// The resource rate chain, ported from <c>ResourceSO</c> so the suite can evaluate it off the Unity
/// thread instead of asking the game to recompute it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance.</b> Transcribed from <c>Assembly-CSharp.dll</c>, SHA-256 <c>5652EBE3…</c>, matching
/// the audited <c>steam-macos-2026-07-13</c> baseline in <c>data/native-contracts.json</c>. Every
/// member names the original it reproduces.
/// </para>
/// <para>
/// <b>Why this one matters more than the cost chain.</b> <c>GetTrueRate()</c> reaches a dozen
/// <c>ValueModifierRecord</c> values through operator overloads, and every one of those is a
/// <c>GetValue()</c> call that recalculates and re-stamps an observable when its dirty flag is set.
/// Calling it from the suite therefore does not merely cost time; it makes the game recompute and
/// mutate its own caches at whatever point in the frame the suite's pump happens to run. Owning the
/// arithmetic removes the write, not just the work.
/// </para>
/// <para>
/// <b>Each step is separately verifiable.</b> The game exposes every intermediate — <c>GetLossRate</c>,
/// <c>GetModdedFlatRate</c>, <c>GetQuantityNegRate</c>, <c>GetOverflowGain</c> — as a public method,
/// so a differential run can compare them one at a time. A disagreement names the step rather than
/// the chain.
/// </para>
/// </remarks>
internal static class GameResourceRateMath
{
    /// <summary>Ported from <c>ResourceSO.HasMaxQuantity()</c>: <c>maxQuantity >= 0</c>.</summary>
    /// <remarks>
    /// Zero is a ceiling of zero. Only a negative value means the resource is uncapped, and the whole
    /// world snapshot follows this convention rather than the friendlier-looking "non-positive".
    /// </remarks>
    internal static bool HasMaxQuantity(in GameResourceRateInputs r) => r.MaxQuantity >= 0;

    /// <summary>Ported from <c>ResourceSO.GetMissing()</c>.</summary>
    internal static BigDouble GetMissing(in GameResourceRateInputs r)
    {
        if (!HasMaxQuantity(in r)) return 0;
        return BigDouble.Max(r.MaxQuantity - r.Quantity, 0);
    }

    /// <summary>Ported from <c>ResourceSO.GetCalcRaritySplash()</c>.</summary>
    /// <remarks>The negated comparison is the original's and is kept so the NaN path is unchanged.</remarks>
    internal static BigDouble GetCalcRaritySplash(in GameResourceRateInputs r)
    {
        if (!(r.CalcRarityValue != 0)) return 1;
        return OrbGameMath.Invert(r.CalcRarityValue);
    }

    /// <summary>Ported from <c>ResourceSO.GetSplashRate()</c>.</summary>
    /// <remarks>
    /// Visibility genuinely gates a number here: an undiscovered resource contributes no splash rate
    /// at all, so this is not a display concern that can be skipped off-thread.
    /// </remarks>
    internal static BigDouble GetSplashRate(in GameResourceRateInputs r)
    {
        if (r.Visible && !(r.RateSplash == 0)) return r.RateSplash * GetCalcRaritySplash(in r);
        return 0;
    }

    /// <summary>Ported from <c>ResourceSO.GetMaxPercentRate()</c>.</summary>
    /// <remarks>The divisor is 60: these percent rates are per minute, expressed per second.</remarks>
    internal static BigDouble GetMaxPercentRate(in GameResourceRateInputs r)
    {
        if (!HasMaxQuantity(in r)) return 0;
        return OrbGameMath.AsPercent(r.RateMaxPercent) * r.MaxQuantity / 60;
    }

    /// <summary>Ported from <c>ResourceSO.GetInterestPercentRateFlat()</c>.</summary>
    internal static BigDouble GetInterestPercentRateFlat(in GameResourceRateInputs r) =>
        OrbGameMath.AsPercent(r.RateInterestPercent) * r.Quantity / 60;

    /// <summary>Ported from <c>ResourceSO.GetMissingPercentRateFlat()</c>.</summary>
    internal static BigDouble GetMissingPercentRateFlat(in GameResourceRateInputs r) =>
        OrbGameMath.AsPercent(r.RateMissingPercent) * GetMissing(in r) / 60;

    /// <summary>
    /// Ported from <c>ResourceSO.GetLifeTimeRateSinceBeginning()</c>, which
    /// <c>GetAvgLifeTimeRate()</c> forwards to unchanged.
    /// </summary>
    internal static BigDouble GetAvgLifeTimeRate(in GameResourceRateInputs r) =>
        r.LifetimeQuantity / r.ResetTimePassed;

    /// <summary>Ported from <c>ResourceSO.GetLifetimePercentRate()</c>.</summary>
    internal static BigDouble GetLifetimePercentRate(in GameResourceRateInputs r)
    {
        if (!(r.RateLifetimePercent > 0)) return 0;
        return OrbGameMath.AsPercent(r.RateLifetimePercent) * GetAvgLifeTimeRate(in r);
    }

    /// <summary>Ported from <c>ResourceSO.GetBaseModdedRate()</c>.</summary>
    internal static BigDouble GetBaseModdedRate(in GameResourceRateInputs r) =>
        ((r.Rate + GetSplashRate(in r)) * OrbGameMath.AsPercent(r.GainRate)) +
        GetMaxPercentRate(in r) +
        GetLifetimePercentRate(in r);

    /// <summary>Ported from <c>ResourceSO.GetModdedFlatRate()</c>.</summary>
    internal static BigDouble GetModdedFlatRate(in GameResourceRateInputs r) =>
        GetBaseModdedRate(in r) + GetInterestPercentRateFlat(in r) + GetMissingPercentRateFlat(in r);

    /// <summary>Ported from <c>ResourceSO.GetDisplayRate()</c>.</summary>
    internal static BigDouble GetDisplayRate(in GameResourceRateInputs r) =>
        r.DisplayRate * OrbGameMath.AsPercent(r.GainRate);

    /// <summary>Ported from <c>ResourceSO.GetModdedDrain()</c>.</summary>
    /// <remarks>Drain divides by quality rather than multiplying: draining low-quality stock costs more of it.</remarks>
    internal static BigDouble GetModdedDrain(in GameResourceRateInputs r) =>
        r.Drain / OrbGameMath.AsPercent(r.Quality);

    /// <summary>
    /// Ported from <c>ResourceSO.GetEquilibrium()</c>: the stock at which percentage loss, net of
    /// interest, cancels the flat gain. The ceiling bounds it, and with no net loss to balance
    /// against it is the ceiling itself, or unbounded on an uncapped resource.
    /// </summary>
    internal static BigDouble GetEquilibrium(in GameResourceRateInputs r)
    {
        var net = GetBaseModdedRate(in r) - GetModdedDrain(in r);
        if (net < r.BaseLoss) return 0;
        var loss = OrbGameMath.AsPercent(r.LossPercent - r.RateInterestPercent);
        if (loss <= 0) return HasMaxQuantity(in r) ? r.MaxQuantity : BigDouble.PositiveInfinity;
        var balance = (net - r.BaseLoss) / loss;
        return HasMaxQuantity(in r) ? BigDouble.Min(r.MaxQuantity, balance) : balance;
    }

    /// <summary>Ported from <c>ResourceSO.GetLossRate(bool)</c>.</summary>
    /// <remarks>
    /// The literal <c>0.85</c> is the original's, and matches the <c>OverflowLossPercent = 85.0</c>
    /// constant the game declares but does not reference here. <c>baseLoss</c> is added
    /// unconditionally once either guard is passed, so a resource in loss mode always sheds at least
    /// that much.
    /// </remarks>
    internal static BigDouble GetLossRate(in GameResourceRateInputs r, bool withoutOverflow = false)
    {
        if (!r.InLossMode || r.Quantity == 0) return 0;

        if (r.LossPercent == 0 &&
            (withoutOverflow || !HasMaxQuantity(in r) || r.Quantity <= r.MaxQuantity))
        {
            return 0;
        }

        var overflowLoss = BigDouble.Max(r.Quantity - r.MaxQuantity, 0) * 0.85 * r.ResourceOverflowLossPercent;
        return (r.Quantity * OrbGameMath.AsPercent(r.LossPercent)) +
            (withoutOverflow ? (BigDouble)0 : overflowLoss) +
            r.BaseLoss;
    }

    /// <summary>Ported from <c>ResourceSO.GetQuantityNegRate(bool)</c>.</summary>
    internal static BigDouble GetQuantityNegRate(in GameResourceRateInputs r, bool withoutOverflow = false) =>
        GetModdedDrain(in r) + GetLossRate(in r, withoutOverflow);

    /// <summary>Ported from <c>ResourceSO.HasActiveRate()</c>.</summary>
    /// <remarks>
    /// The asymmetry is the original's and is load-bearing: <c>rateInterestPercent</c> is deliberately
    /// absent from the first conjunction, so interest alone counts as an active rate only when the
    /// resource actually holds something. Folding it into the other five would change which branch
    /// <see cref="GetTrueRate"/> takes for an empty, interest-bearing resource.
    /// </remarks>
    internal static bool HasActiveRate(in GameResourceRateInputs r)
    {
        if (!r.RateHasActive && !r.RateSplashHasActive && !r.RateMaxPercentHasActive &&
            !r.RateMissingPercentHasActive && !r.RateLifetimePercentHasActive)
        {
            if (r.RateInterestPercentHasActive) return r.Quantity > 0;
            return false;
        }

        return true;
    }

    /// <summary>Ported from <c>ResourceSO.GetOverflowGain(BigDouble, double)</c>.</summary>
    /// <remarks>
    /// Gain past the cap is not discarded but geometrically damped: each further "cap's worth" is
    /// worth a fixed fraction of the last. The <c>1000</c> ceiling on the term count is the game's
    /// <c>OverflowMaxSequences</c>, bounding the series rather than the resource.
    /// </remarks>
    internal static BigDouble GetOverflowGain(in GameResourceRateInputs r, BigDouble amount, double fraction = 1.0)
    {
        if (r.Quantity + amount < r.MaxQuantity || !HasMaxQuantity(in r)) return amount;

        var scaled = amount * fraction;
        var ratio = 1 - (1 / r.ResourceOverflowPercent);
        if (ratio >= 1) return amount;
        if (ratio <= 0) return BigDouble.Max(r.MaxQuantity - r.Quantity, 0);

        var over = BigDouble.Max(r.Quantity - r.MaxQuantity, 0);
        var effective = r.Quantity;
        if (over > 0)
        {
            effective = (OrbGameMath.FindGeometricSequenceN(r.Quantity, r.MaxQuantity, ratio) + 1) * r.MaxQuantity;
            if (BigDouble.IsInfinity(effective) || BigDouble.IsNaN(effective)) return 0;
        }

        var terms = BigDouble.Min(((effective + scaled) / r.MaxQuantity) - 1, 1000.0);
        return (OrbGameMath.SumGeometricSequence(r.MaxQuantity, ratio, terms) - r.Quantity) / fraction;
    }

    /// <summary>Ported from <c>ResourceSO.GetTrueRate()</c>: the net per-second change a player sees.</summary>
    /// <remarks>
    /// Three branches. Below or at the cap — including within one part in a thousand of it — the rate
    /// is the plain sum. Over the cap with no active rate, the result is clamped at zero from above,
    /// so an overflowing resource can lose but never gain. Over the cap with an active rate, gain is
    /// damped through <see cref="GetOverflowGain"/> before losses are subtracted.
    /// </remarks>
    internal static BigDouble GetTrueRate(in GameResourceRateInputs r)
    {
        if (!HasMaxQuantity(in r) || r.Quantity <= r.MaxQuantity ||
            OrbGameMath.ApproxError(r.Quantity, r.MaxQuantity))
        {
            return GetModdedFlatRate(in r) + GetDisplayRate(in r) - GetQuantityNegRate(in r);
        }

        if (!HasActiveRate(in r))
        {
            return BigDouble.Min(
                GetModdedFlatRate(in r) + GetDisplayRate(in r) - GetQuantityNegRate(in r), 0);
        }

        return GetOverflowGain(in r, GetModdedFlatRate(in r) + GetDisplayRate(in r), r.FixedDeltaTime) -
            GetQuantityNegRate(in r);
    }
}
