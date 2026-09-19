using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

/// <summary>
/// What one world-collection category cost a session.
/// </summary>
/// <param name="Passes">Passes this category was recorded by.</param>
/// <param name="SampledLast">Rows the category produced on the last pass in the window.</param>
/// <param name="SampledMaximum">
/// The most rows it ever produced. Read beside <paramref name="SampledLast"/>: the two differ across
/// a prestige or a save load, which is exactly when collection cost moves.
/// </param>
internal readonly record struct WorldCategorySpanRow(
    int Category,
    string Name,
    int Passes,
    double TotalMilliseconds,
    double AverageMilliseconds,
    double MedianMilliseconds,
    double WorstMilliseconds,
    int SampledLast,
    int SampledMaximum);

/// <summary>
/// The per-category reading of a capture, sorted so the expensive part of collection is the first
/// thing read.
/// </summary>
/// <param name="Passes">Collection passes the window holds spans for.</param>
/// <param name="Spans">Spans read.</param>
/// <param name="ShortPasses">
/// Passes that carried fewer spans than they said they had categories. Named rather than thrown on:
/// a capture that ended incomplete legitimately truncates its last pass, and refusing to read the
/// other forty minutes over its final half-pass would cost more than the half-pass does.
/// </param>
internal sealed record WorldCategorySpanSummary(
    WorldCategorySpanRow[] Rows,
    int Passes,
    int Spans,
    int ShortPasses,
    string Discrepancy)
{
    internal static readonly WorldCategorySpanSummary Empty =
        new(Array.Empty<WorldCategorySpanRow>(), 0, 0, 0, string.Empty);

    internal bool HasSpans => Spans != 0;

    internal static WorldCategorySpanSummary Read(ManualFullTraceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var collector = new WorldCategorySpanCollector();
        foreach (var segment in session.Segments())
        foreach (var item in segment.Events) collector.Observe(in item);
        return collector.Freeze(CategoryNames(session));
    }

    /// <summary>What the recording called each category, by the identity its spans carry.</summary>
    internal static Dictionary<int, string> CategoryNames(ManualFullTraceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var names = new Dictionary<int, string>();
        foreach (var entry in session.Roster().Entries)
        {
            if (!string.Equals(entry.Kind, ServiceCycleTraceRoster.WorldCategoryKind, StringComparison.Ordinal))
                continue;
            if (entry.Identity > int.MaxValue) continue;
            names[(int)entry.Identity] =
                entry.DisplayName.Length == 0 ? entry.MachineId : entry.DisplayName;
        }
        return names;
    }
}

/// <summary>
/// Folds collection spans into their per-category distributions as the events arrive, so a reader
/// that is already walking the segments pays one pass for this view rather than a second one.
/// </summary>
internal sealed class WorldCategorySpanCollector
{
    private readonly Dictionary<int, CategoryAccumulator> _categories = new();
    private readonly Dictionary<(long Frame, long Timestamp), PassAccumulator> _passes = new();
    private int _spans;

    /// <summary>
    /// Takes one event and folds it if it is a span. True when it was, so a caller building its own
    /// event list can leave spans out of it: a session holds tens of thousands of them and they are a
    /// distribution rather than a timeline.
    /// </summary>
    internal bool Observe(in ServiceCycleSemanticEvent item)
    {
        if (item.Kind != ServiceCycleSemanticEventKind.WorldCategoryCollected) return false;
        var payload = item.Payload;
        _spans++;
        var pass = (payload.FrameIdentity, payload.TimestampTicks);
        _passes.TryGetValue(pass, out var counted);
        _passes[pass] = counted.Observe(payload.WorldPassCategories);
        if (!_categories.TryGetValue(payload.WorldCategory, out var accumulator))
            _categories[payload.WorldCategory] = accumulator = new CategoryAccumulator();
        accumulator.Observe(in payload);
        return true;
    }

    internal WorldCategorySpanSummary Freeze(Dictionary<int, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (_spans == 0) return WorldCategorySpanSummary.Empty;
        var rows = new List<WorldCategorySpanRow>(_categories.Count);
        foreach (var pair in _categories)
        {
            rows.Add(pair.Value.Freeze(
                pair.Key,
                names.TryGetValue(pair.Key, out var name) ? name : string.Empty));
        }
        rows.Sort(static (left, right) => right.TotalMilliseconds.CompareTo(left.TotalMilliseconds));

        var shortPasses = 0;
        foreach (var pair in _passes)
        {
            if (pair.Value.Spans < pair.Value.Declared) shortPasses++;
        }
        return new WorldCategorySpanSummary(
            rows.ToArray(),
            _passes.Count,
            _spans,
            shortPasses,
            Describe(shortPasses, _passes.Count));
    }

    /// <summary>
    /// The linear-interpolated quantile the dashboard page uses, so a median read in the report and a
    /// median read in the browser are the same number.
    /// </summary>
    internal static double Quantile(List<double> sorted, double quantile)
    {
        if (sorted.Count == 0) return 0;
        var position = (sorted.Count - 1) * quantile;
        var lower = (int)Math.Floor(position);
        var fraction = position - lower;
        return lower + 1 >= sorted.Count
            ? sorted[lower]
            : sorted[lower] + fraction * (sorted[lower + 1] - sorted[lower]);
    }

    private static string Describe(int shortPasses, int passes) => shortPasses == 0
        ? string.Empty
        : Count(shortPasses) + " of " + Count(passes) +
            " pass(es) carry fewer spans than the categories they reported, so those categories are " +
            "under-counted here. A capture that ended incomplete truncates its last pass; more than " +
            "one short pass is records lost mid-session.";

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private readonly record struct PassAccumulator(int Spans, int Declared)
    {
        internal PassAccumulator Observe(int declared) => new(Spans + 1, Math.Max(Declared, declared));
    }

    private sealed class CategoryAccumulator
    {
        private readonly List<double> _milliseconds = new();
        private double _total;
        private double _worst;
        private int _sampledLast;
        private int _sampledMaximum;

        internal void Observe(in ServiceCycleSemanticPayload payload)
        {
            var milliseconds = TraceMetric.ToMilliseconds(payload.DurationTicks);
            _milliseconds.Add(milliseconds);
            _total += milliseconds;
            _worst = Math.Max(_worst, milliseconds);
            _sampledLast = payload.WorldCategorySampled;
            _sampledMaximum = Math.Max(_sampledMaximum, payload.WorldCategorySampled);
        }

        internal WorldCategorySpanRow Freeze(int category, string name)
        {
            _milliseconds.Sort();
            return new WorldCategorySpanRow(
                category,
                name,
                _milliseconds.Count,
                _total,
                _total / _milliseconds.Count,
                Quantile(_milliseconds, 0.5),
                _worst,
                _sampledLast,
                _sampledMaximum);
        }
    }
}
