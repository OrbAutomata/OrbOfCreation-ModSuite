using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

public sealed class VerificationValueTests
{
    [Fact]
    public void A_magnitude_reads_the_way_the_screen_writes_it_rather_than_in_full()
    {
        Assert.Equal("7.46e290", VerificationValue.Format(new BigDouble(7.46022149467469d, 290)));
        Assert.Equal("0", VerificationValue.Format(BigDouble.Zero));
        Assert.Equal("26", VerificationValue.Format(new BigDouble(2.6d, 1)));
    }

    [Fact]
    public void A_drift_percentage_is_three_digits_and_an_exponent_not_a_hundred_and_twenty_one_digits()
    {
        var drift = VerificationValue.Format(2.46486756265886e121d);

        Assert.Equal("2.46e121", drift);
        Assert.DoesNotContain("0000", drift, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Two_values_that_agree_are_written_once()
    {
        Assert.Equal(
            "ours=theirs=4.4e3",
            VerificationValue.Sides(new BigDouble(4.4d, 3), new BigDouble(4.4d, 3)));
    }

    [Fact]
    public void Two_values_that_differ_differ_in_the_digits_that_are_printed()
    {
        Assert.Equal(
            "ours=1.1e24 theirs=5.63e24",
            VerificationValue.Sides(new BigDouble(1.1d, 24), new BigDouble(5.634d, 24)));
    }

    [Fact]
    public void A_difference_below_the_screens_precision_is_stated_rather_than_printed_twice()
    {
        var ours = new BigDouble(7.46022149467469d, 290);
        var theirs = new BigDouble(7.46022149467470d, 290);

        Assert.Equal(
            "both read 7.46e290, differing below what the screen shows",
            VerificationValue.Sides(ours, theirs));
    }

    [Fact]
    public void A_value_that_is_not_a_magnitude_keeps_its_own_shortest_form()
    {
        Assert.Equal("ours=True theirs=False", VerificationValue.Sides(true, false));
        Assert.Equal("ours=theirs=3", VerificationValue.Sides(3, 3));
        Assert.Equal("none", VerificationValue.Format((object?)null));
    }
}
