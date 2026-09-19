using System;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// Pins the mirror of <c>ValueModifier.ToStringValue</c> and the
/// <c>Utils.BeautifyNumber(BigDouble, bool, BigDouble)</c> rule underneath it, branch by branch,
/// against the rule read out of the pinned build's IL.
/// </summary>
/// <remarks>
/// The magnitudes are chosen to land on each branch rather than to look plausible: on either side
/// of the decimal threshold and its tenth-wide window, on each of the four decimal widths the
/// notation narrows through, and on the exponent form above a thousand. A wrong branch is a
/// number that still reads like a number, which is why the sample is the test rather than a
/// round-trip through one value.
/// </remarks>
public sealed class GameModifierSpellingTests
{
    private static string Spell(GameValueModifierType type, double amount) =>
        new GameValueModifier(type, new BigDouble(amount)).ToStringValue();

    /// <summary>An addend keeps its sign and is written plainly.</summary>
    [Theory]
    [InlineData(1d, "+1")]
    [InlineData(5d, "+5")]
    [InlineData(0d, "+0")]
    [InlineData(-3d, "-3")]
    [InlineData(2.5d, "+2.50")]
    [InlineData(-2.5d, "-2.50")]
    public void AnAddendIsSignedAndPlain(double amount, string expected) =>
        Assert.Equal(expected, Spell(GameValueModifierType.Raw, amount));

    /// <summary>
    /// A diminishing modifier is a signed percentage of a hundred times its amount, which is why a
    /// stored 0.17 reads +17% and a stored 50 reads as the exponent form.
    /// </summary>
    [Theory]
    [InlineData(0.17d, "+17%")]
    [InlineData(0.5d, "+50%")]
    [InlineData(-0.25d, "-25%")]
    [InlineData(1.15d, "+115%")]
    [InlineData(50d, "+5.00e3%")]
    public void ADiminishingModifierIsASignedPercentage(double amount, string expected) =>
        Assert.Equal(expected, Spell(GameValueModifierType.MultiDiminishing, amount));

    /// <summary>
    /// A reduction is the same percentage with the sign inverted: a positive amount shrinks the
    /// number it is applied to, so the tooltip writes it as a subtraction.
    /// </summary>
    [Theory]
    [InlineData(0.25d, "-25%")]
    [InlineData(-0.25d, "+25%")]
    [InlineData(0d, "-0%")]
    public void AReductionInvertsTheSign(double amount, string expected) =>
        Assert.Equal(expected, Spell(GameValueModifierType.Reduction, amount));

    /// <summary>
    /// A multiplier carries an <c>x</c> and a threshold of one, so the window just above one keeps
    /// three decimals a two-decimal rounding would swallow. 1.05 is inside the window and has no
    /// hundredth to save, so it takes the ordinary width; 1.15 is outside it altogether.
    /// </summary>
    [Theory]
    [InlineData(1.0007d, "x1.001")]
    [InlineData(1.05d, "x1.05")]
    [InlineData(1.15d, "x1.15")]
    [InlineData(0.7d, "x0.700")]
    [InlineData(4d, "x4")]
    [InlineData(12.34d, "x12.3")]
    [InlineData(123.456d, "x123")]
    [InlineData(2500d, "x2.50e3")]
    public void AMultiplierCarriesItsThresholdWindow(double amount, string expected) =>
        Assert.Equal(expected, Spell(GameValueModifierType.MultiStacking, amount));

    /// <summary>An exponent reads the same way behind a caret.</summary>
    [Theory]
    [InlineData(2d, "^2")]
    [InlineData(1.0007d, "^1.001")]
    [InlineData(0.5d, "^0.500")]
    public void AnExponentCarriesTheSameRule(double amount, string expected) =>
        Assert.Equal(expected, Spell(GameValueModifierType.Exponent, amount));

    /// <summary>
    /// Zero is the one magnitude the number rule answers before any threshold, so every kind that
    /// writes it plainly writes the same character.
    /// </summary>
    [Fact]
    public void ZeroIsOneCharacterWhateverTheThreshold()
    {
        Assert.Equal("x0", Spell(GameValueModifierType.MultiStacking, 0d));
        Assert.Equal("^0", Spell(GameValueModifierType.Exponent, 0d));
        Assert.Equal("+0%", Spell(GameValueModifierType.MultiDiminishing, 0d));
    }

    /// <summary>
    /// A sixth kind is a game change to model, never a number to print anyway — the original's
    /// default arm prints the raw <c>BigDouble</c>, which would put an unlabelled magnitude on a
    /// surface whose whole promise is that the kind is spelled into it.
    /// </summary>
    [Fact]
    public void AKindOutsideTheFiveHasNoSpelling() =>
        Assert.Throws<InvalidOperationException>(() => Spell((GameValueModifierType)5, 1d));
}
