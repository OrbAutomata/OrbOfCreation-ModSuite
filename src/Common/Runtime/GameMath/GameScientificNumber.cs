using System;
using System.Globalization;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// The game's Scientific display style: plain below 1,000, compact exponent above it.
/// </summary>
/// <remarks>
/// <para>
/// The suite forces the game's number notation to Scientific on load, so this is the form the player
/// is looking at while they read anything the suite prints. One notation for the whole numeric
/// surface is the point: a magnitude written one way on the screen and another way on the wire makes
/// the reader do a conversion before they can tell whether two numbers are the same number.
/// </para>
/// <para>
/// It lives here rather than beside the MCP wire because the differential check prints magnitudes
/// from the Runtime page too, in builds the MCP surface is compiled out of. A second formatter for
/// those builds would be a second notation, which is the defect.
/// </para>
/// </remarks>
internal static class GameScientificNumber
{
    /// <summary>Renders a mantissa and exponent the way the screen renders them.</summary>
    internal static string Format(double mantissa, long exponent)
    {
        if (double.IsNaN(mantissa)) return "nan";
        if (double.IsPositiveInfinity(mantissa)) return "infinity";
        if (double.IsNegativeInfinity(mantissa)) return "-infinity";
        if (mantissa == 0d) return "0";

        Normalize(ref mantissa, ref exponent);
        if (exponent < 3 && exponent >= -1)
        {
            var plain = mantissa * Math.Pow(10d, exponent);
            var roundedPlain = Math.Round(plain, 2, MidpointRounding.AwayFromZero);
            return roundedPlain.ToString("0.##", CultureInfo.InvariantCulture);
        }
        return Scientific(mantissa, exponent);
    }

    internal static string Format(double value) => Format(value, 0);

    internal static string Format(BigDouble value) => Format(value.Mantissa, value.Exponent);

    /// <summary>
    /// The game's own <c>Utils.BeautifyNumber(BigDouble, bool, BigDouble)</c>, mirrored so a
    /// magnitude the suite prints beside a tooltip reads character for character like the tooltip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the pinned build's IL. A negative number is the same rendering behind a minus
    /// sign, zero is <c>"0"</c>, and a number just above a caller's threshold — above it, within
    /// a tenth of it, and carrying a hundredth the coarse rendering would swallow — is written to
    /// three decimals so that an <c>x1.001</c> multiplier does not print as <c>x1</c>. Everything
    /// else is the notation branch, which is <c>Scientific</c> because
    /// <c>AgentSettingsNormalization</c> pins the game's own <c>numDisplay</c> there; the game's
    /// suffix flag reaches only the branches that ignore it, so the mirror does not carry it.
    /// </para>
    /// <para>
    /// <see cref="Format"/> is the suite's own rounding for a general magnitude and is not this:
    /// it rounds everything under a thousand to two decimals, where the game widens the decimals as
    /// the number shrinks. They agree on <c>1.15</c> and disagree on <c>0.7</c>, which the game
    /// writes <c>0.700</c>. Only the surfaces that must match a tooltip exactly call this one.
    /// </para>
    /// </remarks>
    internal static string Beautify(BigDouble number, BigDouble decimalThreshold)
    {
        if (number < BigDouble.Zero)
            return "-" + Beautify(BigDouble.Abs(number), decimalThreshold);
        if (number == BigDouble.Zero) return "0";
        if (decimalThreshold != BigDouble.Zero &&
            number > decimalThreshold &&
            number < decimalThreshold + new BigDouble(0.1d) &&
            HasDecimals(number.ToDouble() * 100d))
        {
            return ToDecimalPlace(number.ToDouble(), 3);
        }
        return BeautifyScientific(number);
    }

    private static string BeautifyScientific(BigDouble number) =>
        number < new BigDouble(1000d) && number >= new BigDouble(0.1d)
            ? BeautifySimplify(number)
            : number.Mantissa.ToString("F2", CultureInfo.InvariantCulture) +
                "e" + number.Exponent.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The decimals narrow as the number grows: three below one, then two, one, and none.
    /// </summary>
    private static string BeautifySimplify(BigDouble number)
    {
        var value = number.ToDouble();
        if (!HasDecimalsOld(value)) return BigDouble.Round(number).ToString();
        if (number < BigDouble.One) return ToDecimalPlace(value, 3);
        if (number < new BigDouble(10d)) return ToDecimalPlace(value, 2);
        if (number < new BigDouble(100d)) return ToDecimalPlace(value, 1);
        return ToDecimalPlace(value, 0);
    }

    /// <summary>The game's <c>Utils.HasDecimals</c>: a thousandth away from whole counts.</summary>
    private static bool HasDecimals(double value) =>
        Math.Abs(Math.Round(value) - value) > 0.001d;

    /// <summary>
    /// The game's <c>Utils.HasDecimalsOld</c>, the coarser predicate its number rendering still
    /// uses: whole floor and ceiling disagree. The truncation to <c>int</c> is the original's and is
    /// reached only under a thousand, where it cannot overflow.
    /// </summary>
    private static bool HasDecimalsOld(double value) =>
        (int)Math.Floor(value) != (int)Math.Ceiling(value);

    /// <summary>
    /// The game's <c>Utils.ToDecimalPlace</c>: rounded to that many places and written with exactly
    /// that many, so a trailing zero is kept.
    /// </summary>
    private static string ToDecimalPlace(double value, int places) =>
        Math.Round(value, places).ToString(
            "0.".PadRight(2 + places, '0'), CultureInfo.InvariantCulture);

    private static string Scientific(double mantissa, long exponent)
    {
        var rounded = Math.Round(mantissa, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) >= 10d)
        {
            rounded /= 10d;
            checked { exponent++; }
        }
        if (rounded == 0d) return "0";
        return rounded.ToString("0.##", CultureInfo.InvariantCulture) +
            "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    private static void Normalize(ref double mantissa, ref long exponent)
    {
        var shift = (long)Math.Floor(Math.Log10(Math.Abs(mantissa)));
        if (shift == 0) return;
        mantissa /= Math.Pow(10d, shift);
        checked { exponent += shift; }
    }
}
