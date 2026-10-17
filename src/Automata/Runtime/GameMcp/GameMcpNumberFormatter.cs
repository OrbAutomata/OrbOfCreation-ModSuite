#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.GameMath;

namespace OrbAutomata.GameMcp;

/// <summary>The wire's entry into the one Scientific display style the suite writes numbers in.</summary>
/// <remarks>
/// The style itself is <see cref="GameScientificNumber"/>, shared with the surfaces that print
/// magnitudes in builds this one is compiled out of. What is MCP's own is the rule that only a
/// <c>BigDouble</c> reaches it: a JSON number that slipped through would be a second notation.
/// </remarks>
internal static class GameMcpNumberFormatter
{
    internal static string Format(object value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value is not BigDouble number)
            throw new ArgumentException("Only BigDouble values use the MCP large-number formatter.", nameof(value));
        return GameScientificNumber.Beautify(number);
    }

    internal static string Format(double value) => GameScientificNumber.Beautify(value);

    /// <summary>A price and the holding beside it, spelled so a shortfall never reads as met.</summary>
    internal static (string Needed, string Held) FormatApart(BigDouble needed, BigDouble held)
    {
        GameScientificNumber.TryBeautifyApart(needed, held, out var neededText, out var heldText);
        return (neededText, heldText);
    }

    /// <summary>
    /// The same pair as two cells of a price row: domain values while the screen's spellings already
    /// read apart, the widened text where they do not.
    /// </summary>
    internal static (object Needed, object Held) CellsApart(BigDouble needed, BigDouble held) =>
        GameScientificNumber.TryBeautifyApart(needed, held, out var neededText, out var heldText)
            ? (neededText, heldText)
            : (new GameMcpDomainValue(needed), new GameMcpDomainValue(held));
}
#endif
