using System;
using System.Globalization;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// The game's own number rendering, <c>Utils.BeautifyNumber</c>, mirrored from the pinned build's
/// IL so every magnitude the suite prints reads character for character like the one the screen
/// draws.
/// </summary>
/// <remarks>
/// <para>
/// One rule for the whole numeric surface is the point: a magnitude written one way on the screen
/// and another way on the wire makes the reader do a conversion before they can tell whether two
/// numbers are the same number. The game's own overloads are the entry points here — a magnitude on
/// its own, and a magnitude a caller hands a decimal threshold.
/// </para>
/// <para>
/// It lives here rather than beside the MCP wire because the differential check prints magnitudes
/// from the Runtime page too, in builds the MCP surface is compiled out of. A second formatter for
/// those builds would be a second notation, which is the defect.
/// </para>
/// </remarks>
internal static class GameScientificNumber
{
    /// <summary>The one arm of the game's notation switch this mirror reproduces.</summary>
    /// <remarks>
    /// <c>Utils.BeautifyNumberSwitch</c> dispatches on <c>SettingsManager.GetNumberDisplayOption</c>
    /// to one of five arms — <c>Named</c>, <c>Compact</c>, <c>Compact-Num</c>, <c>Scientific</c>,
    /// <c>Engineering</c>. The option is not captured per pass: the getter's first instruction is
    /// <c>UnityEngine.Application.isPlaying</c> and it reaches the setting through the Unity object
    /// <c>SettingsManager.instance</c>, so there is no honest read of it away from the player loop —
    /// and there is nothing to read back, because the suite writes this value into the game's own
    /// <c>numDisplay</c> on the load that asked for the game and fails that load if it does not
    /// settle. <c>AgentSettingsNormalization</c> writes the notation from here, so the arm the
    /// mirror implements and the arm the screen draws are one constant.
    /// </remarks>
    internal const string Notation = "Scientific";

    /// <summary>
    /// The game's <c>Utils.BeautifyNumber(BigDouble)</c>: a magnitude with no caller threshold,
    /// which is the form behind every number the wire prints that is not a modifier's own.
    /// </summary>
    internal static string Beautify(BigDouble number) => Beautify(number, BigDouble.Zero);

    /// <summary>The same rule for a magnitude the suite happens to hold as a <c>double</c>.</summary>
    internal static string Beautify(double number) => Beautify(new BigDouble(number));

    /// <summary>
    /// The game's <c>Utils.BeautifyNumber(BigDouble, bool, BigDouble)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A negative number is the same rendering behind a minus sign, zero is <c>"0"</c>, and a number
    /// just above a caller's threshold — above it, within a tenth of it, and carrying a hundredth
    /// the coarse rendering would swallow — is written to three decimals so that an <c>x1.001</c>
    /// multiplier does not print as <c>x1</c>. Everything else is the notation branch. The game's
    /// suffix flag reaches only the branches that ignore it, so the mirror does not carry it.
    /// </para>
    /// <para>
    /// The three non-finite answers are the suite's own and have no branch in the original, which
    /// would write a mantissa of <c>NaN</c> against an exponent of <c>long.MinValue</c>. The screen
    /// never draws such a value, so there is no spelling to match and the suite says the word.
    /// </para>
    /// </remarks>
    internal static string Beautify(BigDouble number, BigDouble decimalThreshold)
    {
        if (double.IsNaN(number.Mantissa)) return "nan";
        if (double.IsPositiveInfinity(number.Mantissa)) return "infinity";
        if (double.IsNegativeInfinity(number.Mantissa)) return "-infinity";
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

    /// <summary>
    /// The game's <c>Utils.BeautifyNumberScientific</c>: the plain form from a tenth up to a
    /// thousand, the exponent form either side of that, with the mantissa written to two decimals
    /// exactly as the original leaves it — a stored mantissa of 9.9999999 prints <c>10.00e5</c>.
    /// </summary>
    private static string BeautifyScientific(BigDouble number) =>
        number < new BigDouble(1000d) && number >= new BigDouble(0.1d)
            ? BeautifySimplify(number)
            : number.Mantissa.ToString("F2", CultureInfo.InvariantCulture) +
                "e" + number.Exponent.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The game's <c>Utils.BeautifyNumberSimplify</c>: the decimals narrow as the number grows —
    /// three below one, then two, one, and none — and a whole number keeps none of them.
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
}
