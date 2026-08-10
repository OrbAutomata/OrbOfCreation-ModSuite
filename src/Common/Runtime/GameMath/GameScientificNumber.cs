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
