using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Strategy;

namespace OrbAutomata;

/// <summary>
/// The strategist's exact inputs as text: what <c>strategist section=facts</c> prints and what a
/// fixture pastes back. Numbers are written losslessly, so parsing the text and deriving again
/// reproduces the bulletin.
/// </summary>
/// <remarks>
/// One record per line, a keyword then <c>key=value</c> fields; a <c>name=</c> field is always last
/// and runs to the end of the line, so names keep their spaces.
/// </remarks>
internal static class StrategistFactsText
{
    internal static string Write(StrategistFacts facts, StrategistWindowReading window)
    {
        var text = new StringBuilder();
        var clock = facts.Clock;
        text.Append("facts world=").Append(facts.WorldGeneration.ToString(CultureInfo.InvariantCulture))
            .Append(" played=").Append(Number(clock.TotalSeconds));
        if (clock.HasThisReset) text.Append(" reset=").Append(Number(clock.ThisResetSeconds));
        text.Append(" challenges=").Append(clock.ActiveChallenges.ToString(CultureInfo.InvariantCulture))
            .Append(" root=").Append(facts.ChainRoot.ToString("D")).Append('\n');

        foreach (var open in facts.Open.AsSpan())
        {
            text.Append("open");
            if (open.HasLevel)
                text.Append(" level=").Append(open.Current.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(open.Maximum.ToString(CultureInfo.InvariantCulture));
            text.Append(" name=").Append(open.Name).Append('\n');
        }

        foreach (var resource in facts.Resources.AsSpan())
        {
            text.Append("resource ").Append(resource.Id.ToString("D"))
                .Append(" held=").Append(Number(resource.Held));
            if (resource.Capped) text.Append(" cap=").Append(Number(resource.Cap));
            if (!resource.Visible) text.Append(" hidden");
            if (resource.Bandwidth) text.Append(" points");
            text.Append(" name=").Append(resource.Name).Append('\n');
        }

        foreach (var node in facts.Nodes.AsSpan())
        {
            text.Append("node ").Append(node.Id.ToString("D")).Append(' ').Append(Kind(node.Kind));
            if (node.Offered) text.Append(" offered");
            if (node.Owned) text.Append(" owned");
            if (node.Gate) text.Append(" gate");
            text.Append(" name=").Append(node.Name).Append('\n');
        }

        foreach (var price in facts.Prices.AsSpan())
        {
            text.Append("price ").Append(price.NodeId.ToString("D")).Append(' ')
                .Append(price.ResourceId.ToString("D")).Append(" amount=").Append(Number(price.Amount))
                .Append('\n');
        }

        foreach (var edge in facts.Edges.AsSpan())
        {
            text.Append("edge ").Append(edge.OwnerId.ToString("D")).Append(' ')
                .Append(edge.TargetId.ToString("D"));
            if (edge.Met) text.Append(" met");
            text.Append('\n');
        }

        text.Append("window required=").Append(Number(window.RequiredSeconds)).Append('\n');
        foreach (var row in window.Rows.AsSpan())
        {
            text.Append("gain ").Append(row.ResourceId.ToString("D"))
                .Append(" gained=").Append(Number(row.Gained))
                .Append(" span=").Append(Number(row.SpanSeconds));
            if (row.ClippedAtCap) text.Append(" clipped");
            text.Append('\n');
        }

        return text.ToString();
    }

    internal static (StrategistFacts Facts, StrategistWindowReading Window) Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        ulong world = 0;
        var played = 0d;
        var hasReset = false;
        var reset = 0d;
        var challenges = 0;
        var root = Guid.Empty;
        var required = 0d;
        var open = new List<StrategistOpenFact>();
        var resources = new List<StrategistResourceFact>();
        var nodes = new List<StrategistNodeFact>();
        var prices = new List<StrategistPriceFact>();
        var edges = new List<StrategistEdgeFact>();
        var rows = new List<StrategistWindowRow>();
        var sawHeader = false;
        var sawWindow = false;

        var lines = text.Split('\n');
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number].TrimEnd('\r');
            if (line.Trim().Length == 0) continue;
            var record = new Record(line.TrimStart(), number + 1);
            switch (record.Keyword)
            {
                case "facts":
                    sawHeader = true;
                    world = ulong.Parse(record.Value("world"), CultureInfo.InvariantCulture);
                    played = ParseDouble(record.Value("played"));
                    hasReset = record.Has("reset");
                    reset = hasReset ? ParseDouble(record.Value("reset")) : 0d;
                    challenges = int.Parse(record.Value("challenges"), CultureInfo.InvariantCulture);
                    root = Guid.Parse(record.Value("root"));
                    break;
                case "open":
                    if (record.Has("level"))
                    {
                        var level = record.Value("level").Split('/');
                        open.Add(new StrategistOpenFact(
                            record.Name,
                            true,
                            int.Parse(level[0], CultureInfo.InvariantCulture),
                            int.Parse(level[1], CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        open.Add(new StrategistOpenFact(record.Name, false, 0, 0));
                    }
                    break;
                case "resource":
                    resources.Add(new StrategistResourceFact(
                        record.Guid(0),
                        record.Name,
                        !record.Has("hidden"),
                        ParseNumber(record.Value("held")),
                        record.Has("cap"),
                        record.Has("cap") ? ParseNumber(record.Value("cap")) : BigDouble.Zero,
                        record.Has("points")));
                    break;
                case "node":
                    nodes.Add(new StrategistNodeFact(
                        record.Guid(0),
                        record.Name,
                        ParseKind(record.Word(1)),
                        record.Has("offered"),
                        record.Has("owned"),
                        record.Has("gate")));
                    break;
                case "price":
                    prices.Add(new StrategistPriceFact(
                        record.Guid(0), record.Guid(1), ParseNumber(record.Value("amount"))));
                    break;
                case "edge":
                    edges.Add(new StrategistEdgeFact(record.Guid(0), record.Guid(1), record.Has("met")));
                    break;
                case "window":
                    sawWindow = true;
                    required = ParseDouble(record.Value("required"));
                    break;
                case "gain":
                    rows.Add(new StrategistWindowRow(
                        record.Guid(0),
                        ParseNumber(record.Value("gained")),
                        ParseDouble(record.Value("span")),
                        record.Has("clipped")));
                    break;
                default:
                    throw new FormatException($"Line {number + 1}: unknown record '{record.Keyword}'.");
            }
        }

        if (!sawHeader) throw new FormatException("The facts have no 'facts' header line.");
        if (!sawWindow) throw new FormatException("The facts have no 'window' line.");
        var facts = new StrategistFacts(
            world,
            new StrategistClock(played, hasReset, reset, challenges),
            root,
            PublicationTable<StrategistOpenFact>.Create(open.ToArray()),
            PublicationTable<StrategistResourceFact>.Create(resources.ToArray()),
            PublicationTable<StrategistNodeFact>.Create(nodes.ToArray()),
            PublicationTable<StrategistPriceFact>.Create(prices.ToArray()),
            PublicationTable<StrategistEdgeFact>.Create(edges.ToArray()));
        return (facts, new StrategistWindowReading(
            required, PublicationTable<StrategistWindowRow>.Create(rows.ToArray())));
    }

    private static string Kind(StrategistNodeKind kind) => kind switch
    {
        StrategistNodeKind.Upgrade => "upgrade",
        StrategistNodeKind.Research => "research",
        StrategistNodeKind.Structure => "structure",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static StrategistNodeKind ParseKind(string word) => word switch
    {
        "upgrade" => StrategistNodeKind.Upgrade,
        "research" => StrategistNodeKind.Research,
        "structure" => StrategistNodeKind.Structure,
        _ => throw new FormatException($"Unknown node kind '{word}'."),
    };

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Mantissa and exponent exactly, so a number survives the trip.</summary>
    private static string Number(BigDouble value) =>
        value.Mantissa.ToString("R", CultureInfo.InvariantCulture) + "e" +
        value.Exponent.ToString(CultureInfo.InvariantCulture);

    private static double ParseDouble(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static BigDouble ParseNumber(string text)
    {
        var split = text.LastIndexOf('e');
        if (split < 0) return new BigDouble(ParseDouble(text));
        return new BigDouble(
            ParseDouble(text.Substring(0, split)),
            long.Parse(text.Substring(split + 1), CultureInfo.InvariantCulture));
    }

    private sealed class Record
    {
        private readonly string[] _words;
        private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);
        private readonly int _line;

        internal Record(string line, int number)
        {
            _line = number;
            var nameAt = line.IndexOf(" name=", StringComparison.Ordinal);
            Name = nameAt < 0 ? string.Empty : line.Substring(nameAt + " name=".Length);
            var head = nameAt < 0 ? line : line.Substring(0, nameAt);
            var tokens = head.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Keyword = tokens[0];
            var words = new List<string>();
            for (var index = 1; index < tokens.Length; index++)
            {
                var equals = tokens[index].IndexOf('=');
                if (equals < 0)
                {
                    words.Add(tokens[index]);
                    _fields[tokens[index]] = string.Empty;
                }
                else
                {
                    _fields[tokens[index].Substring(0, equals)] = tokens[index].Substring(equals + 1);
                }
            }
            _words = words.ToArray();
        }

        internal string Keyword { get; }
        internal string Name { get; }

        internal bool Has(string key) => _fields.ContainsKey(key);

        internal string Value(string key) =>
            _fields.TryGetValue(key, out var value) && value.Length > 0
                ? value
                : throw new FormatException($"Line {_line}: '{Keyword}' needs {key}=.");

        internal string Word(int index) =>
            index < _words.Length
                ? _words[index]
                : throw new FormatException($"Line {_line}: '{Keyword}' is missing a field.");

        internal Guid Guid(int index) => System.Guid.Parse(Word(index));
    }
}
