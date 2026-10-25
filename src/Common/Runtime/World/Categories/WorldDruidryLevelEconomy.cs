using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct DruidryEffect
{
    internal DruidryEffect(Guid modifierId, GameValueModifier modifier) { ModifierId = modifierId; Modifier = modifier; }
    internal Guid ModifierId { get; }
    internal GameValueModifier Modifier { get; }
}

/// <summary>One audited LinearLevelEffect distributor and the keys of its downstream snapshots.</summary>
internal readonly struct DruidryDistribution
{
    internal DruidryDistribution(PublicationTable<WorldModifierProgramEntry> entries,
        PublicationTable<Guid> generatedKeys, PublicationTable<DruidryEffect> effects)
    { Entries = entries; GeneratedKeys = generatedKeys; Effects = effects; }
    internal PublicationTable<WorldModifierProgramEntry> Entries { get; }
    internal PublicationTable<Guid> GeneratedKeys { get; }
    internal PublicationTable<DruidryEffect> Effects { get; }

    internal bool TryModifiers(int level, out GameValueModifier[] modifiers)
    {
        var rows = new List<GameValueModifier>();
        var replacedIds = new HashSet<Guid>();
        foreach (var entry in Entries.AsSpan())
        {
            var replaced = false;
            foreach (var effect in Effects.AsSpan())
            {
                if (effect.ModifierId != entry.ModifierId) continue;
                replaced = true; replacedIds.Add(effect.ModifierId);
                if (level > 1) rows.Add(effect.Modifier.MultiplyScalar(new BigDouble(level - 1)));
                break;
            }
            if (!replaced) rows.Add(entry.Modifier);
        }
        if (level > 1)
            foreach (var effect in Effects.AsSpan())
                if (!replacedIds.Contains(effect.ModifierId)) rows.Add(effect.Modifier.MultiplyScalar(new BigDouble(level - 1)));
        var groups = rows.GroupBy(row => row.Order).OrderBy(group => group.Key);
        var result = new List<GameValueModifier>();
        foreach (var group in groups)
        {
            // OrderedMultiplierRecord.CollectOrderedMultipliers: AdjustWithRaw over a merged
            // same-order group at 100, then StackingRaw at that original order.
            var amount = GameModifierStack.AdjustWith(new BigDouble(100), group.ToArray());
            result.Add(new GameValueModifier(GameValueModifierType.MultiStacking, OrbGameMath.AsPercent(amount), group.Key));
        }
        modifiers = result.ToArray();
        return rows.All(modifier => (int)modifier.Type >= 0 && (int)modifier.Type <= 4 &&
            !BigDouble.IsNaN(modifier.Amount) && !BigDouble.IsInfinity(modifier.Amount)) &&
            modifiers.All(modifier => !BigDouble.IsNaN(modifier.Amount) && !BigDouble.IsInfinity(modifier.Amount));
    }

    internal bool MatchesLevel(int level)
    {
        foreach (var effect in Effects.AsSpan())
        {
            var expected = level > 1 ? effect.Modifier.MultiplyScalar(new BigDouble(level - 1)) : default;
            var found = false;
            foreach (var entry in Entries.AsSpan())
            {
                if (entry.ModifierId != effect.ModifierId) continue;
                if (found || level <= 1 || entry.Set != WorldModifierProgramEntrySet.Passive || entry.Modifier.Type != expected.Type || entry.Modifier.Order != expected.Order ||
                    entry.Modifier.Amount != expected.Amount) return false;
                found = true;
            }
            if (level > 1 && !expected.IsEmpty() && !found) return false;
        }
        return true;
    }
}

/// <summary>One value record and the precise distributor snapshots replaced by a prospective level.</summary>
internal sealed class DruidryValueRecord
{
    internal DruidryValueRecord(WorldModifierProgram header, PublicationTable<WorldModifierProgramEntry> entries,
        PublicationTable<DruidryDistribution> distributions)
    { Header = header; Entries = entries; Distributions = distributions; }
    internal WorldModifierProgram Header { get; }
    internal PublicationTable<WorldModifierProgramEntry> Entries { get; }
    internal PublicationTable<DruidryDistribution> Distributions { get; }

    internal bool TryValue(int currentLevel, bool next, out BigDouble value)
    {
        var header = Header;
        var entries = Entries;
        if (next)
            foreach (var entry in Entries.AsSpan())
                if ((int)entry.Type < 0 || (int)entry.Type > 4 || BigDouble.IsNaN(entry.Amount) || BigDouble.IsInfinity(entry.Amount))
                { value = default; return false; }
        if (next && Distributions.Count > 0)
        {
            var kept = new List<WorldModifierProgramEntry>();
            var nextPosition = 0;
            foreach (var row in Entries.AsSpan())
                if (row.Set == WorldModifierProgramEntrySet.Passive) nextPosition = Math.Max(nextPosition, row.Position + 1);
            foreach (var row in Entries.AsSpan())
            {
                var replaced = false;
                foreach (var distribution in Distributions.AsSpan())
                    foreach (var key in distribution.GeneratedKeys.AsSpan())
                        if (row.Set == WorldModifierProgramEntrySet.Passive && row.ModifierId == key) replaced = true;
                if (!replaced) kept.Add(row);
            }
            foreach (var distribution in Distributions.AsSpan())
            {
                if (!distribution.MatchesLevel(currentLevel) || !distribution.TryModifiers(currentLevel + 1, out var replacements))
                { value = default; return false; }
                foreach (var modifier in replacements)
                    kept.Add(new WorldModifierProgramEntry(Guid.Empty, WorldModifierProgramRole.PlotActionForecast,
                        WorldModifierProgramEntrySet.Passive, nextPosition++, Guid.Empty, modifier.Type, modifier.Order, modifier.Amount));
            }
            entries = PublicationTable<WorldModifierProgramEntry>.Create(kept.ToArray());
            header = new WorldModifierProgram(Guid.Empty, WorldModifierProgramRole.PlotActionForecast,
                true, Header.BaseValue, true, default);
        }
        return WorldModifierProgramMath.TryFoldRecord(PublicationTable<WorldModifierProgram>.Create(new[] { header }),
            entries, Guid.Empty, WorldModifierProgramRole.PlotActionForecast, out value);
    }
}

internal readonly struct DruidryScalingRecord
{
    internal DruidryScalingRecord(int kind, DruidryValueRecord record) { Kind = kind; Record = record; }
    internal int Kind { get; }
    internal DruidryValueRecord Record { get; }
}
internal readonly struct DruidryTransmuter
{
    internal DruidryTransmuter(int kind, int extractionType, bool invert, bool empty,
        DruidryValueRecord? record, BigDouble plainValue, GameValueModifier modifier, GamePlotModifierList list)
    { Kind = kind; ExtractionType = extractionType; Invert = invert; Empty = empty; Record = record; PlainValue = plainValue; Modifier = modifier; List = list; }
    internal int Kind { get; }
    internal int ExtractionType { get; }
    internal bool Invert { get; }
    internal bool Empty { get; }
    internal DruidryValueRecord? Record { get; }
    internal BigDouble PlainValue { get; }
    internal GameValueModifier Modifier { get; }
    internal GamePlotModifierList List { get; }
}

internal readonly struct WorldDruidryLevelSample
{
    internal WorldDruidryLevelSample(PublicationTable<DruidryScalingRecord> action, PublicationTable<DruidryScalingRecord> plot,
        PublicationTable<DruidryTransmuter> transmuters, PublicationTable<GamePlotSizeConverter> converters,
        PublicationTable<GameResourceCost> authoredDrain, PublicationTable<GameResourceCost> nativeCurrent,
        BigDouble quickMod, BigDouble maxSpeed, BigDouble ratio, BigDouble appliedRatio)
        : this(action, plot, transmuters, converters, authoredDrain, nativeCurrent,
            new DruidryTiming(quickMod, maxSpeed), ratio, appliedRatio) { }
    internal WorldDruidryLevelSample(PublicationTable<DruidryScalingRecord> action, PublicationTable<DruidryScalingRecord> plot,
        PublicationTable<DruidryTransmuter> transmuters, PublicationTable<GamePlotSizeConverter> converters,
        PublicationTable<GameResourceCost> authoredDrain, PublicationTable<GameResourceCost> nativeCurrent,
        DruidryTiming timing, BigDouble ratio, BigDouble appliedRatio, string forecastReason = "")
    {
        Action = action; Plot = plot; Transmuters = transmuters; Converters = converters;
        AuthoredDrain = authoredDrain; NativeCurrent = nativeCurrent; Timing = timing;
        Ratio = ratio; AppliedRatio = appliedRatio; ForecastReason = forecastReason;
    }
    internal string ForecastReason { get; }
    internal static WorldDruidryLevelSample Actual(PublicationTable<GameResourceCost> native, BigDouble appliedRatio, string reason) => new(
        PublicationTable<DruidryScalingRecord>.Empty, PublicationTable<DruidryScalingRecord>.Empty,
        PublicationTable<DruidryTransmuter>.Empty, PublicationTable<GamePlotSizeConverter>.Empty,
        PublicationTable<GameResourceCost>.Empty, native, new DruidryTiming(BigDouble.One, new BigDouble(100)),
        BigDouble.One, appliedRatio, reason);
    internal PublicationTable<DruidryScalingRecord> Action { get; }
    internal PublicationTable<DruidryScalingRecord> Plot { get; }
    internal PublicationTable<DruidryTransmuter> Transmuters { get; }
    internal PublicationTable<GamePlotSizeConverter> Converters { get; }
    internal PublicationTable<GameResourceCost> AuthoredDrain { get; }
    internal PublicationTable<GameResourceCost> NativeCurrent { get; }
    internal DruidryTiming Timing { get; }
    internal BigDouble Ratio { get; }
    internal BigDouble AppliedRatio { get; }

    internal bool TryPercent(int currentLevel, bool next, out BigDouble percent, out string reason)
    {
        percent = default; reason = ForecastReason;
        if (ForecastReason.Length != 0) return false;
        var action = new List<GamePlotScalingValue>();
        var plot = new List<GamePlotScalingValue>();
        foreach (var row in Action.AsSpan())
        { if (!row.Record.TryValue(currentLevel, next, out var value)) return false; action.Add(new GamePlotScalingValue(row.Kind, value)); }
        foreach (var row in Plot.AsSpan())
        { if (!row.Record.TryValue(currentLevel, next, out var value)) return false; plot.Add(new GamePlotScalingValue(row.Kind, value)); }
        var transmuters = new List<GamePlotTransmuter>();
        foreach (var row in Transmuters.AsSpan())
        {
            var value = row.PlainValue;
            if (row.Record is not null && !row.Record.TryValue(currentLevel, next, out value)) return false;
            transmuters.Add(new GamePlotTransmuter(row.Kind, row.ExtractionType, row.Invert, row.Empty, value, row.Modifier, row.List));
        }
        return GamePlotActionScalingMath.TryDrainPercent(PublicationTable<GamePlotScalingValue>.Create(action.ToArray()),
            PublicationTable<GamePlotScalingValue>.Create(plot.ToArray()), PublicationTable<GamePlotTransmuter>.Create(transmuters.ToArray()),
            Converters, Timing.QuickMod(), Timing.MaximumSpeed(), Ratio, out percent, out reason);
    }
}

/// <summary>Current applied drain and exact next-level requested drain from one native-free capture.</summary>
internal sealed class WorldDruidryLevelInputs
{
    private readonly string _reason;
    private readonly PublicationTable<WorldDruidryLevelSample> _samples;
    internal WorldDruidryLevelInputs(string reason) : this(reason, PublicationTable<WorldDruidryLevelSample>.Empty) { }
    internal WorldDruidryLevelInputs(string reason, PublicationTable<WorldDruidryLevelSample> samples)
    { _reason = reason; _samples = samples; }
    internal WorldLevelDomainQuote Build(PublicationTable<WorldResource> resources, int currentLevel, int maximumLevel)
    {
        if (_reason.Length != 0) return WorldLevelDomainQuote.Unavailable(_reason);
        if (currentLevel < 1 || maximumLevel < currentLevel)
            return WorldLevelDomainQuote.Unavailable("The Agromancy level range is unavailable.");
        // Actual attribution is complete before any forecast can refuse. A failed projection for
        // one action cannot erase another running action's contribution to current depletion.
        var current = new Dictionary<Guid, BigDouble>();
        foreach (var sample in _samples.AsSpan())
        {
            if (!Finite(sample.AppliedRatio) || sample.AppliedRatio < 0 || sample.AppliedRatio > 1)
                return WorldLevelDomainQuote.Unavailable("An Agromancy drain utilization ratio is invalid.");
            foreach (var price in sample.NativeCurrent.AsSpan())
            {
                if (price.ResourceId == Guid.Empty || !Finite(price.Value) || price.Value < 0)
                    return WorldLevelDomainQuote.Unavailable("An actual Agromancy drain has invalid resource evidence.");
                Add(current, price.ResourceId, price.Value * sample.AppliedRatio);
            }
        }
        var next = new Dictionary<Guid, BigDouble>();
        var nextComplete = true;
        var nextReason = string.Empty;
        if (currentLevel < maximumLevel)
        {
            foreach (var sample in _samples.AsSpan())
            {
                if (!sample.TryPercent(currentLevel, false, out var currentPercent, out var reason))
                {
                    nextComplete = false;
                    nextReason = reason.Length == 0 ? "The current Agromancy scaling program is incomplete." : reason;
                    break;
                }
                var expected = new Dictionary<Guid, BigDouble>();
                foreach (var price in sample.AuthoredDrain.AsSpan())
                    Add(expected, price.ResourceId, price.Value * OrbGameMath.AsPercent(currentPercent));
                var observed = new Dictionary<Guid, BigDouble>();
                foreach (var price in sample.NativeCurrent.AsSpan()) Add(observed, price.ResourceId, price.Value);
                if (!Same(expected, observed))
                {
                    nextComplete = false;
                    nextReason = "The game has not applied the captured Agromancy level and drain modifiers yet.";
                    break;
                }
                if (!sample.TryPercent(currentLevel, true, out var nextPercent, out reason))
                {
                    nextComplete = false;
                    nextReason = reason.Length == 0 ? "The prospective Agromancy modifier program is incomplete." : reason;
                    break;
                }
                foreach (var price in sample.AuthoredDrain.AsSpan())
                    Add(next, price.ResourceId, price.Value * OrbGameMath.AsPercent(nextPercent));
            }
        }
        if (!nextComplete) next.Clear();
        return new WorldLevelDomainQuote(true, string.Empty, _samples.Count > 0, currentLevel, maximumLevel,
            Prices(resources, current), Prices(resources, next), nextComplete, nextReason);
    }
    private static bool Finite(BigDouble value) => !BigDouble.IsNaN(value) && !BigDouble.IsInfinity(value);
    private static void Add(Dictionary<Guid, BigDouble> rows, Guid id, BigDouble amount)
    { rows.TryGetValue(id, out var previous); rows[id] = previous + amount; }
    private static bool Same(Dictionary<Guid, BigDouble> expected, Dictionary<Guid, BigDouble> actual)
    {
        foreach (var row in expected) if (!actual.TryGetValue(row.Key, out var value) ? row.Value != 0 : value != row.Value) return false;
        foreach (var row in actual) if (!expected.TryGetValue(row.Key, out var value) ? row.Value != 0 : value != row.Value) return false;
        return true;
    }
    private static PublicationTable<WorldLevelPrice> Prices(PublicationTable<WorldResource> resources, Dictionary<Guid, BigDouble> rows)
    {
        var prices = new List<WorldLevelPrice>();
        foreach (var row in rows.OrderBy(row => row.Key))
        {
            var allocation = WorldLookup.TryFind(resources, row.Key, out var resource) && resource.Reading.Traits.BandwidthResource;
            prices.Add(new WorldLevelPrice(-1, row.Key, row.Value, allocation ? WorldLevelPriceKind.Allocation : WorldLevelPriceKind.Drain));
        }
        return PublicationTable<WorldLevelPrice>.Create(prices.ToArray());
    }
}

internal readonly struct DruidryQuickFactor
{
    internal DruidryQuickFactor(BigDouble firstPercent, BigDouble secondPercent) { FirstPercent = firstPercent; SecondPercent = secondPercent; }
    internal BigDouble FirstPercent { get; }
    internal BigDouble SecondPercent { get; }
    internal BigDouble Percent() => OrbGameMath.AsPercent(FirstPercent) * OrbGameMath.AsPercent(SecondPercent);
}
internal readonly struct DruidryRarityPrice
{
    internal DruidryRarityPrice(double rarity, BigDouble amount) { Rarity = rarity; Amount = amount; }
    internal double Rarity { get; }
    internal BigDouble Amount { get; }
}

internal sealed class DruidryTiming
{
    private readonly PublicationTable<DruidryQuickFactor> _factors;
    private readonly DruidryQuickFactor _parentSize;
    private readonly BigDouble _parentQuality;
    private readonly double _baseTime;
    private readonly bool _useSpace;
    private readonly PublicationTable<DruidryRarityPrice> _space;
    private readonly bool _direct;
    private readonly BigDouble _directQuick, _directMaximum;
    internal DruidryTiming(BigDouble quick, BigDouble maximum)
    {
        _direct = true; _directQuick = quick; _directMaximum = maximum;
        _factors = PublicationTable<DruidryQuickFactor>.Empty;
        _space = PublicationTable<DruidryRarityPrice>.Empty;
    }
    internal DruidryTiming(PublicationTable<DruidryQuickFactor> factors, DruidryQuickFactor parentSize,
        BigDouble parentQuality, double baseTime, bool useSpace, PublicationTable<DruidryRarityPrice> space)
    { _factors = factors; _parentSize = parentSize; _parentQuality = parentQuality; _baseTime = baseTime; _useSpace = useSpace; _space = space; }
    internal BigDouble QuickMod()
    {
        if (_direct) return _directQuick;
        var value = BigDouble.One;
        foreach (var row in _factors.AsSpan()) value *= row.Percent();
        return value * (_parentSize.Percent() * OrbGameMath.AsPercent(_parentQuality));
    }
    internal BigDouble MaximumSpeed()
    {
        if (_direct) return _directMaximum;
        var space = _useSpace ? BigDouble.Zero : BigDouble.One;
        if (_useSpace) foreach (var row in _space.AsSpan()) space += new BigDouble(row.Rarity) * row.Amount;
        return new BigDouble(_baseTime) * QuickMod() * space * new BigDouble(100);
    }
}
