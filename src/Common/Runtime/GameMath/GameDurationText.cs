using System.Globalization;
using System.Text;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// The game's two duration formats, in one place, for every surface that prints a countdown.
/// </summary>
/// <remarks>
/// The wire had one of these and the spell-loadout boundary had its own rounding, so the same
/// countdown reached a caller as <c>8.4s away</c> from a refusal and <c>8.40s</c> from the row
/// beside it. One concept, one rendering: both now read the game's own
/// <c>Utils.BeautifyTimeAccurate</c> / <c>Utils.BeautifyTimeUltraPrecise</c> through here.
/// </remarks>
internal static class GameDurationText
{
    private static readonly BigDouble Minute = new BigDouble(60d);
    private static readonly BigDouble Year = new BigDouble(31557600d);

    /// <summary>
    /// <c>Utils.BeautifyTimeAccurate</c>: one unit, and nothing under it. Its second branches lose
    /// a decimal each decade — <c>1.23s</c>, <c>12.3s</c>, <c>123s</c> — and its unit thresholds
    /// are the game's own rather than the obvious ones: seconds up to 1000, minutes up to 60000,
    /// hours up to 86400000. Two hours reads <c>128m</c> on the game's screens, so it reads
    /// <c>128m</c> here.
    /// </summary>
    internal static string Accurate(BigDouble seconds)
    {
        if (seconds < BigDouble.Zero) return "-" + Accurate(BigDouble.Abs(seconds));
        if (seconds < new BigDouble(10)) return Fixed(seconds, 2) + "s";
        if (seconds < new BigDouble(100)) return Fixed(seconds, 1) + "s";
        if (seconds < new BigDouble(1000)) return Fixed(seconds, 0) + "s";
        if (seconds < new BigDouble(60000)) return Fixed(seconds / new BigDouble(60), 0) + "m";
        if (seconds < new BigDouble(3600000)) return Fixed(seconds / new BigDouble(3600), 0) + "h";
        if (seconds < new BigDouble(86400000)) return Fixed(seconds / new BigDouble(86400), 0) + "d";
        return Beautified(seconds / Year) + "y";
    }

    /// <summary>
    /// <c>Utils.BeautifyTimeUltraPrecise</c>: under a minute the number and <c>s</c>; over a Julian
    /// year the number of years and <c>y</c>; between the two hours, minutes and seconds, each
    /// padded to two digits, with leading empty units dropped — so a run of two hours reads
    /// <c>02:07:41</c> and one of seven minutes reads <c>07:41</c>.
    /// </summary>
    internal static string UltraPrecise(BigDouble seconds)
    {
        if (seconds < BigDouble.Zero) return "-" + UltraPrecise(BigDouble.Abs(seconds));
        if (seconds < Minute) return Beautified(seconds) + "s";
        if (seconds > Year) return Beautified(seconds / Year) + "y";

        var total = (long)seconds.ToDouble();
        var units = new[] { total / 3600L, total / 60L % 60L, total % 60L };
        var text = new StringBuilder();
        for (var index = 0; index < units.Length; index++)
        {
            if (text.Length == 0 && units[index] < 1) continue;
            if (text.Length > 0) text.Append(':');
            text.Append(units[index].ToString("D2", CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }

    private static string Beautified(BigDouble value) =>
        GameScientificNumber.Beautify(value);

    private static string Fixed(BigDouble value, int decimals) =>
        value.ToDouble().ToString(
            "F" + decimals.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
}
