using System;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// The two sides of one comparison, written so the difference is visible in the digits that differ.
/// </summary>
/// <remarks>
/// <para>
/// Both sides go through <see cref="GameScientificNumber"/>, so a reader comparing them is comparing
/// the same form at the same precision — the form they are also looking at on the game's own screen.
/// Printing one side as the screen writes it and the other at full <c>BigDouble</c> precision would
/// make every pair look like a disagreement.
/// </para>
/// <para>
/// Three cases, three sentences. Two values that agree are one value, written once. Two values that
/// differ are written out, and the difference is in the digits. Two values that differ only below
/// the three digits the screen keeps are <b>stated</b> — printing the same string twice under a
/// heading that says they disagree is the report contradicting itself, and a reader who sees it
/// either stops trusting the heading or goes hunting a rendering bug that is not there.
/// </para>
/// </remarks>
internal static class VerificationValue
{
    /// <summary>One value in the notation the game's screen uses.</summary>
    internal static string Format(BigDouble value) => GameScientificNumber.Format(value);

    /// <summary>
    /// One value of whatever type a comparison happened to hold — magnitudes follow the screen,
    /// everything else (counts, flags, enum terms) is already its own shortest honest form.
    /// </summary>
    internal static string Format(object? value) => value switch
    {
        null => "none",
        BigDouble number => GameScientificNumber.Format(number),
        double number => GameScientificNumber.Format(number),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Both sides of one comparison.</summary>
    internal static string Sides(BigDouble ours, BigDouble theirs) =>
        Sides(Format(ours), Format(theirs), ours == theirs);

    /// <summary>Both sides of one comparison, for values of any type.</summary>
    internal static string Sides(object? ours, object? theirs) =>
        Sides(Format(ours), Format(theirs), Equals(ours, theirs));

    private static string Sides(string ours, string theirs, bool agreed)
    {
        if (agreed) return $"ours=theirs={ours}";
        return string.Equals(ours, theirs, StringComparison.Ordinal)
            ? $"both read {ours}, differing below what the screen shows"
            : $"ours={ours} theirs={theirs}";
    }
}
