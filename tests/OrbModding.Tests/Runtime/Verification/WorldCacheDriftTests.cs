using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// The staleness statistic on the provenance line. It is not a verdict and never has been; the job
/// here is that a reader can tell what it is looking at, which a percentage with no ceiling and no
/// operands could not do.
/// </summary>
public sealed class WorldCacheDriftTests
{
    /// <summary>Same magnitude, no drift to report, whatever the digits are.</summary>
    [Fact]
    public void Two_readings_of_the_same_size_are_no_orders_apart()
    {
        Assert.Equal(0d, AutomataWorldCollectionCheck.DriftOrders(new BigDouble(100d), new BigDouble(100d)));
    }

    /// <summary>
    /// The record that produced the 2.25e118% headline, in the shape it now reports: a hundred
    /// against a recompute deep cost reduction has driven to nothing. The pair is 116 orders apart,
    /// which is a number a reader can hold — and the ratio it replaced was not.
    /// </summary>
    [Fact]
    public void An_endgame_pair_reports_the_orders_between_them_rather_than_a_ratio()
    {
        var apart = AutomataWorldCollectionCheck.DriftOrders(
            new BigDouble(100d),
            BigDouble.FromMantissaExponentNoNormalize(4.44d, -115));

        Assert.Equal(116.4d, apart, 1);
        Assert.Equal("116.4", AutomataWorldCollectionCheck.Orders(apart));
    }

    /// <summary>
    /// A recompute of exactly zero is not an edge to suppress: the game is acting on a memo its own
    /// recalculation says is nothing, and no finite number of orders describes that.
    /// </summary>
    [Fact]
    public void A_recompute_of_nothing_is_unbounded_rather_than_a_large_number()
    {
        var apart = AutomataWorldCollectionCheck.DriftOrders(new BigDouble(100d), BigDouble.Zero);

        Assert.Equal(double.PositiveInfinity, apart);
        Assert.Equal("unbounded", AutomataWorldCollectionCheck.Orders(apart));
    }

    /// <summary>Two readings that are both nothing are not drifting.</summary>
    [Fact]
    public void Two_readings_of_nothing_are_no_orders_apart()
    {
        Assert.Equal(0d, AutomataWorldCollectionCheck.DriftOrders(BigDouble.Zero, BigDouble.Zero));
    }

    /// <summary>
    /// A reading that is not a number cannot be ranked against anything, so it never becomes the
    /// widest and never becomes the line a reader is asked to act on.
    /// </summary>
    [Fact]
    public void A_reading_that_is_not_a_number_never_wins()
    {
        Assert.Equal(0d, AutomataWorldCollectionCheck.DriftOrders(BigDouble.NaN, new BigDouble(1d)));
        Assert.Equal(
            0d,
            AutomataWorldCollectionCheck.DriftOrders(BigDouble.PositiveInfinity, new BigDouble(1d)));
    }
}
