using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// Pins the mirror of <c>Utils.BeautifyNumber(BigDouble)</c> — the rule behind every magnitude the
/// suite prints that is not a modifier's own — branch by branch, against the rule read out of the
/// pinned build's IL.
/// </summary>
/// <remarks>
/// The sample is chosen to land on each branch rather than to look plausible: the tenth below
/// which the plain form gives way to the exponent form, each of the four decimal widths the plain
/// form narrows through, the whole numbers that skip all four, and the thousand above which the
/// exponent form resumes. A wrong branch is a number that still reads like a number, which is why
/// the sample is the test rather than a round trip through one value.
/// </remarks>
public sealed class GameNumberSpellingTests
{
    /// <summary>
    /// Under a thousand and from a tenth up, the decimals narrow as the number grows: three below
    /// one, two below ten, one below a hundred, none above it — and a whole number takes none of
    /// them at any size.
    /// </summary>
    [Theory]
    [InlineData(0d, "0")]
    [InlineData(0.1d, "0.100")]
    [InlineData(0.7d, "0.700")]
    [InlineData(0.999d, "0.999")]
    [InlineData(1d, "1")]
    [InlineData(1.15d, "1.15")]
    [InlineData(9.99d, "9.99")]
    [InlineData(10d, "10")]
    [InlineData(12.34d, "12.3")]
    [InlineData(99.9d, "99.9")]
    [InlineData(100d, "100")]
    [InlineData(123.456d, "123")]
    [InlineData(999d, "999")]
    public void ThePlainFormNarrowsItsDecimalsAsTheNumberGrows(double value, string expected) =>
        Assert.Equal(expected, GameScientificNumber.Beautify(value));

    /// <summary>
    /// Outside that window the exponent form takes over on both sides, and its mantissa is written
    /// to exactly two decimals — so a tenth of a tenth is <c>1.00e-3</c> and a stored 1.2e5 keeps
    /// the zero the plain form would have dropped.
    /// </summary>
    [Theory]
    [InlineData(0.001d, "1.00e-3")]
    [InlineData(0.0999d, "9.99e-2")]
    [InlineData(1000d, "1.00e3")]
    [InlineData(1234d, "1.23e3")]
    [InlineData(100000d, "1.00e5")]
    [InlineData(120000d, "1.20e5")]
    [InlineData(1000000d, "1.00e6")]
    [InlineData(1.2345e15d, "1.23e15")]
    public void TheExponentFormPadsItsMantissaToTwoDecimals(double value, string expected) =>
        Assert.Equal(expected, GameScientificNumber.Beautify(value));

    /// <summary>
    /// The original rounds the mantissa where it stands rather than renormalising it, so a number
    /// a hair under a power of ten is written with a mantissa of ten rather than carried into the
    /// next exponent. The screen shows it that way, so the wire does.
    /// </summary>
    [Fact]
    public void AMantissaThatRoundsToTenIsNotCarriedIntoTheNextExponent() =>
        Assert.Equal("10.00e5", GameScientificNumber.Beautify(new BigDouble(9.9999999d, 5)));

    /// <summary>A negative magnitude is the same rendering behind a minus sign.</summary>
    [Theory]
    [InlineData(-0.7d, "-0.700")]
    [InlineData(-12.34d, "-12.3")]
    [InlineData(-999d, "-999")]
    [InlineData(-1000d, "-1.00e3")]
    [InlineData(-1.2345e15d, "-1.23e15")]
    public void ANegativeMagnitudeIsTheSameRenderingBehindAMinusSign(double value, string expected) =>
        Assert.Equal(expected, GameScientificNumber.Beautify(value));

    /// <summary>
    /// A threshold-free magnitude and a modifier's magnitude are one rule: passing a zero
    /// threshold is what the game's own one-argument overload does, so the two entry points cannot
    /// drift into two notations.
    /// </summary>
    [Theory]
    [InlineData(0.7d)]
    [InlineData(12.34d)]
    [InlineData(1.2345e15d)]
    public void AThresholdFreeMagnitudeIsTheSameRuleWithAZeroThreshold(double value) =>
        Assert.Equal(
            GameScientificNumber.Beautify(new BigDouble(value), BigDouble.Zero),
            GameScientificNumber.Beautify(value));

    /// <summary>
    /// The arm the mirror implements is the arm the suite writes into the game's own setting. One
    /// constant holds both, so a build that chose a different notation would be a screen the
    /// mirror does not spell rather than a silent disagreement.
    /// </summary>
    [Fact]
    public void TheNotationTheMirrorSpellsIsTheNotationTheSuiteWrites() =>
        Assert.Equal(GameScientificNumber.Notation, AgentSettingsNormalization.NumberNotation);

    /// <summary>
    /// A magnitude the game's own rule would write as a mantissa of <c>NaN</c> against an exponent
    /// of <c>long.MinValue</c> is not a number any screen draws, so the suite says the word instead
    /// of spelling the original's wreckage.
    /// </summary>
    [Fact]
    public void ANonFiniteMagnitudeIsAWordRatherThanTheOriginalsWreckage()
    {
        Assert.Equal("nan", GameScientificNumber.Beautify(double.NaN));
        Assert.Equal("infinity", GameScientificNumber.Beautify(double.PositiveInfinity));
        Assert.Equal("-infinity", GameScientificNumber.Beautify(double.NegativeInfinity));
    }
}
