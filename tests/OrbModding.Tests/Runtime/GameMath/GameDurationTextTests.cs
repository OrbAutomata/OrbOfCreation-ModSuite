using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// Pins the mirror of <c>Utils.BeautifyTimeUltraPreciseWithYears</c>, the resource tooltip's
/// Maxed in and Empty in clock, branch by branch against the rule read out of the pinned build's IL.
/// </summary>
public sealed class GameDurationTextTests
{
    /// <summary>
    /// Under a minute is a number of seconds; past a century a number of years. Between them each
    /// unit is padded to two digits and leading empty units drop, and a year is a Julian one, so two
    /// whole years leave twelve hours over.
    /// </summary>
    [Theory]
    [InlineData(45d, "45s")]
    [InlineData(10.04d, "10.0s")]
    [InlineData(60d, "01:00")]
    [InlineData(285d, "04:45")]
    [InlineData(25205d, "07:00:05")]
    [InlineData(702000d, "08:03:00:00")]
    [InlineData(63203407d, "02:01:12:30:07")]
    [InlineData(4e9d, "127y")]
    public void TheYearsClockSpellsWhatTheResourceTooltipPrints(double seconds, string expected) =>
        Assert.Equal(expected, GameDurationText.UltraPreciseWithYears(new BigDouble(seconds)));
}
