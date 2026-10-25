using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.GameMath;

internal readonly struct GamePlotScalingValue
{
    internal GamePlotScalingValue(int kind, BigDouble amount) { Kind = kind; Amount = amount; }
    internal int Kind { get; }
    internal BigDouble Amount { get; }
}

internal readonly struct GamePlotModifierList
{
    internal GamePlotModifierList(PublicationTable<GameValueModifier> modifiers, PublicationTable<GameValueModifier> exponents)
    { Modifiers = modifiers; Exponents = exponents; }
    internal PublicationTable<GameValueModifier> Modifiers { get; }
    internal PublicationTable<GameValueModifier> Exponents { get; }
    internal BigDouble Adjust(BigDouble value, BigDouble scalar) =>
        GameModifierStack.AdjustScaledList(value, Modifiers.AsSpan(), Exponents.AsSpan(), scalar);
}

internal static class GamePlotModifierListDefaults
{
    internal static readonly GamePlotModifierList Empty = new(
        PublicationTable<GameValueModifier>.Empty, PublicationTable<GameValueModifier>.Empty);
}

/// <summary>One transmuter in native map/list order; Extracted belongs to the quoted level context.</summary>
internal readonly struct GamePlotTransmuter
{
    internal GamePlotTransmuter(int kind, int extractionType, bool invert, bool empty,
        BigDouble extracted, GameValueModifier modifier, GamePlotModifierList list)
    { Kind = kind; ExtractionType = extractionType; Invert = invert; Empty = empty; Extracted = extracted; Modifier = modifier; List = list; }
    internal int Kind { get; }
    internal int ExtractionType { get; }
    internal bool Invert { get; }
    internal bool Empty { get; }
    internal BigDouble Extracted { get; }
    internal GameValueModifier Modifier { get; }
    internal GamePlotModifierList List { get; }
}

internal readonly struct GamePlotSizeConverter
{
    internal GamePlotSizeConverter(int kind, double magnitudeRatio, GameValueModifier modifier)
    { Kind = kind; MagnitudeRatio = magnitudeRatio; Modifier = modifier; }
    internal int Kind { get; }
    internal double MagnitudeRatio { get; }
    internal GameValueModifier Modifier { get; }
}

/// <summary>
/// Ports the plot action's scaling pipeline through its speed ceiling and persistent drain multiplier.
/// Inputs are captured values, including any level-sensitive extraction sources, never native objects.
/// </summary>
internal static class GamePlotActionScalingMath
{
    internal const int ScalingKindCount = 13;
    internal const int DrainKind = 5;
    internal const int SpeedKind = 6;

    internal static bool TryDrainPercent(
        PublicationTable<GamePlotScalingValue> action,
        PublicationTable<GamePlotScalingValue> plot,
        PublicationTable<GamePlotTransmuter> transmuters,
        PublicationTable<GamePlotSizeConverter> converters,
        BigDouble quickMod, BigDouble maximumSpeedPercent, BigDouble ratio,
        out BigDouble drainPercent, out string reason)
    {
        drainPercent = default;
        reason = string.Empty;
        var values = new BigDouble[ScalingKindCount];
        var present = new bool[ScalingKindCount];
        foreach (var row in action.AsSpan())
        {
            if (!Known(row.Kind) || present[row.Kind] || !Finite(row.Amount))
                return Refuse("The action scaling map has an unknown, repeated, or non-finite entry.", out reason);
            values[row.Kind] = row.Amount;
            present[row.Kind] = true;
        }
        var plotSeen = new bool[ScalingKindCount];
        foreach (var row in plot.AsSpan())
        {
            if (!Known(row.Kind) || plotSeen[row.Kind] || !Finite(row.Amount))
                return Refuse("The plot scaling map has an unknown, repeated, or non-finite entry.", out reason);
            plotSeen[row.Kind] = true;
            values[row.Kind] = !present[row.Kind] ? row.Amount : row.Kind <= 1
                ? BigDouble.Max(row.Amount, values[row.Kind])
                : OrbGameMath.AsPercent(row.Amount) * values[row.Kind];
            present[row.Kind] = true;
        }
        foreach (var row in transmuters.AsSpan())
        {
            if (!Known(row.Kind) || !TryTransmute(Get(values, present, row.Kind), row, out var adjusted))
                return Refuse("A plot action value transmuter cannot be evaluated.", out reason);
            values[row.Kind] = adjusted;
            present[row.Kind] = true;
        }
        if (!Finite(quickMod) || !Finite(maximumSpeedPercent) || !Finite(ratio) || maximumSpeedPercent < 0 || ratio < 0)
            return Refuse("The plot action's quick modifier, speed ceiling, or persistent ratio is invalid.", out reason);
        values[3] = Get(values, present, 3) * quickMod; // Native AdjustScalingInfo multiplies Duration.
        present[3] = true;
        foreach (var row in converters.AsSpan())
        {
            if (!Known(row.Kind) || !KnownModifier(row.Modifier) ||
                (double.IsNaN(row.MagnitudeRatio) || double.IsInfinity(row.MagnitudeRatio)) || row.MagnitudeRatio <= 0 || row.MagnitudeRatio == 1 || quickMod == 0)
                return Refuse("A plot action size converter cannot be evaluated.", out reason);
            var magnitude = new BigDouble(BigDouble.Log10(BigDouble.Abs(quickMod)) / Math.Log10(row.MagnitudeRatio));
            if (row.Kind <= 1) magnitude = BigDouble.Floor(magnitude);
            values[row.Kind] = row.Modifier.MultiplyScalar(magnitude).Adjust(Get(values, present, row.Kind));
            present[row.Kind] = true;
        }
        var speed = BigDouble.Min(Get(values, present, SpeedKind), maximumSpeedPercent);
        drainPercent = Get(values, present, DrainKind) * OrbGameMath.AsPercent(speed) * ratio;
        if (!Finite(drainPercent) || drainPercent < 0)
            return Refuse("The projected plot action drain is invalid.", out reason);
        return true;
    }

    internal static bool TryTransmute(BigDouble value, in GamePlotTransmuter row, out BigDouble adjusted)
    {
        adjusted = value;
        if (row.Empty) return true;
        if (!Finite(value) || !Finite(row.Extracted) || row.ExtractionType < 0 || row.ExtractionType > 5)
            return false;
        var extraction = row.Extracted;
        var percent = OrbGameMath.AsPercent(extraction);
        switch (row.ExtractionType)
        {
            case 0:
                if (row.Invert && percent == 0) return false;
                adjusted = row.Invert ? value / percent : value * percent;
                break;
            case 1:
            case 2:
                if (row.Invert && percent == 0) return false;
                var scalar = (row.Invert ? BigDouble.One / percent : percent) - BigDouble.One;
                if (row.ExtractionType == 1)
                {
                    if (!KnownModifier(row.Modifier)) return false;
                    adjusted = row.Modifier.MultiplyScalar(scalar).Adjust(value);
                }
                else if (!TryList(row.List, value, scalar, out adjusted)) return false;
                break;
            case 3:
                adjusted = row.Invert ? value - extraction : value + extraction;
                break;
            case 4:
                if (!KnownModifier(row.Modifier)) return false;
                adjusted = row.Modifier.MultiplyScalar(row.Invert ? -extraction : extraction).Adjust(value);
                break;
            case 5:
                if (!TryList(row.List, value, row.Invert ? -extraction : extraction, out adjusted)) return false;
                break;
        }
        return Finite(adjusted);
    }

    private static bool TryList(in GamePlotModifierList list, BigDouble value, BigDouble scalar, out BigDouble adjusted)
    {
        adjusted = value;
        if (list.Modifiers is null || list.Exponents is null) return false;
        foreach (var modifier in list.Modifiers.AsSpan()) if (!KnownModifier(modifier)) return false;
        foreach (var exponent in list.Exponents.AsSpan()) if (!KnownModifier(exponent)) return false;
        adjusted = list.Adjust(value, scalar);
        return Finite(adjusted);
    }

    private static BigDouble Get(BigDouble[] values, bool[] present, int kind) => present[kind]
        ? values[kind]
        : kind == 1 ? Get(values, present, 0)
        : kind is 0 or 7 or 10 or 11 ? BigDouble.One : new BigDouble(100);
    private static bool Known(int kind) => kind >= 0 && kind < ScalingKindCount;
    private static bool KnownModifier(in GameValueModifier modifier) =>
        modifier.Type >= GameValueModifierType.Raw && modifier.Type <= GameValueModifierType.Exponent && Finite(modifier.Amount);
    private static bool Finite(BigDouble value) => !double.IsNaN(value.Mantissa) && !double.IsInfinity(value.Mantissa);
    private static bool Refuse(string sentence, out string reason) { reason = sentence; return false; }
}
