using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OrbModding.Common.Runtime.World;

/// <summary>What happened to one entity category during a collection.</summary>
internal enum WorldCategoryOutcome
{
    /// <summary>
    /// The category could not be read at all on this build: its type, its registry, or one of its
    /// required accessors was absent. No rows were produced.
    /// </summary>
    Unavailable,

    /// <summary>The category was traversed. Individual entities may still have been skipped.</summary>
    Collected,
}

/// <summary>The outcome of traversing one category, including what it could not read and why.</summary>
internal readonly struct WorldCategoryReport
{
    internal WorldCategoryReport(
        string category,
        WorldCategoryOutcome outcome,
        int sampled,
        int skipped,
        string firstFailure,
        long elapsedTicks = 0)
    {
        Category = category;
        Outcome = outcome;
        Sampled = sampled;
        Skipped = skipped;
        FirstFailure = firstFailure;
        ElapsedTicks = elapsedTicks;
    }

    internal string Category { get; }
    internal WorldCategoryOutcome Outcome { get; }

    /// <summary>Entities that produced a row.</summary>
    internal int Sampled { get; }

    /// <summary>Entities the traversal reached but could not turn into a row.</summary>
    internal int Skipped { get; }

    /// <summary>
    /// Why the category was unavailable, or why the first skipped entity was skipped. Empty when
    /// nothing went wrong.
    /// </summary>
    internal string FirstFailure { get; }

    /// <summary>
    /// Raw <see cref="System.Diagnostics.Stopwatch"/> ticks this pass spent producing this report.
    /// </summary>
    /// <remarks>
    /// What <em>this</em> pass spent, so the category costs of one collection sum to the collection.
    /// A structural category whose rows were carried over from an earlier epoch therefore reports
    /// zero: re-reporting the read that filled the buffer would charge every later pass for work no
    /// pass after the first one did, which is the reading that makes a cached category look expensive.
    /// </remarks>
    internal long ElapsedTicks { get; }

    internal bool IsClean => Outcome == WorldCategoryOutcome.Collected && Skipped == 0;

    internal static WorldCategoryReport Missing(string category, string reason) =>
        new(category, WorldCategoryOutcome.Unavailable, sampled: 0, skipped: 0, reason);

    /// <summary>The same report, charged with what a pass spent on it.</summary>
    internal WorldCategoryReport WithElapsedTicks(long elapsedTicks) =>
        new(Category, Outcome, Sampled, Skipped, FirstFailure, elapsedTicks);
}

/// <summary>
/// The evidence one collection leaves behind: what was read, what was not, and why.
/// </summary>
/// <remarks>
/// Collection degrades per category rather than all-or-nothing, so a build that renamed one research
/// member still yields resources, structures, and upgrades. That is only defensible if the gap is
/// visible — a partial snapshot that reports itself as complete is worse than no snapshot, because
/// every consumer downstream would read "no research available" as a fact about the save rather than
/// a fact about the read.
/// </remarks>
internal sealed class WorldCollectionReport
{
    private readonly WorldCategoryReport[] _categories;

    internal WorldCollectionReport(params WorldCategoryReport[] categories) =>
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));

    /// <summary>Every category the collector attempted, in the order it walked them.</summary>
    internal ReadOnlySpan<WorldCategoryReport> Categories => _categories;

    /// <summary>Whether every category was traversed with no entity skipped.</summary>
    internal bool IsComplete
    {
        get
        {
            foreach (var report in _categories)
            {
                if (!report.IsClean) return false;
            }

            return true;
        }
    }

    internal int TotalSampled
    {
        get
        {
            var total = 0;
            foreach (var report in _categories) total += report.Sampled;
            return total;
        }
    }

    /// <summary>
    /// Raw <see cref="Stopwatch"/> ticks the whole pass spent inside its category readers.
    /// </summary>
    internal long TotalElapsedTicks
    {
        get
        {
            var total = 0L;
            foreach (var report in _categories) total += report.ElapsedTicks;
            return total;
        }
    }

    /// <summary>
    /// The report for one category, or an <see cref="WorldCategoryOutcome.Unavailable"/> stand-in
    /// saying the collector never walked it. Callers asking about a category that was not attempted
    /// deserve that as an answer rather than as an exception.
    /// </summary>
    internal WorldCategoryReport For(string category)
    {
        foreach (var report in _categories)
        {
            if (string.Equals(report.Category, category, StringComparison.Ordinal)) return report;
        }

        return WorldCategoryReport.Missing(category, "the category was not collected");
    }

    /// <summary>
    /// One line: a total when everything read cleanly, otherwise every category that fell short with
    /// the first reason for each. Naming only the shortfalls keeps the line readable as the category
    /// count grows, and keeps the interesting part at the front.
    /// </summary>
    internal string Describe()
    {
        if (IsComplete)
        {
            return $"World collection complete: {TotalSampled} entities across " +
                $"{_categories.Length} categories.";
        }

        var text = new StringBuilder("World collection incomplete:");
        foreach (var report in _categories) Describe(text, in report);
        return text.ToString();
    }

    /// <summary>
    /// One line saying what the pass cost and which categories it went on.
    /// </summary>
    /// <remarks>
    /// Collection is the suite's largest main-thread cost and the only fact anyone had about it was
    /// the total, so every proposal to make it cheaper was an argument about which category was
    /// probably expensive. The three dearest by name with their milliseconds ends that argument, and
    /// the remainder is carried explicitly so the named ones can never be mistaken for the whole
    /// pass. Categories that cost nothing this pass are omitted rather than listed at zero: a
    /// structural category read once per epoch is absent from the pass, not free within it.
    /// </remarks>
    internal string DescribeCost()
    {
        var text = new StringBuilder("World collection cost ")
            .Append(Milliseconds(TotalElapsedTicks))
            .Append(" ms");
        var dearest = Dearest();
        if (dearest.Count == 0) return text.Append(", unattributed.").ToString();

        text.Append(':');
        var named = 0L;
        for (var index = 0; index < dearest.Count; index++)
        {
            var report = _categories[dearest[index]];
            named += report.ElapsedTicks;
            text.Append(index == 0 ? " " : ", ")
                .Append(report.Category)
                .Append(' ')
                .Append(Milliseconds(report.ElapsedTicks));
        }

        var remaining = ChargedCount() - dearest.Count;
        if (remaining > 0)
        {
            text.Append("; ")
                .Append(remaining.ToString(CultureInfo.InvariantCulture))
                .Append(" more ")
                .Append(Milliseconds(TotalElapsedTicks - named));
        }

        return text.Append('.').ToString();
    }

    private DearestCategories Dearest()
    {
        var dearest = default(DearestCategories);
        for (var index = 0; index < _categories.Length; index++)
        {
            if (_categories[index].ElapsedTicks > 0)
                dearest.Offer(index, _categories[index].ElapsedTicks);
        }

        return dearest;
    }

    private int ChargedCount()
    {
        var charged = 0;
        foreach (var report in _categories)
        {
            if (report.ElapsedTicks > 0) charged++;
        }

        return charged;
    }

    private static string Milliseconds(long ticks) =>
        (ticks * 1000.0 / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// The three dearest categories of a pass, held in place rather than sorted, so naming them costs
    /// no allocation on top of the line they are named in.
    /// </summary>
    private struct DearestCategories
    {
        private int _first;
        private int _second;
        private int _third;
        private long _firstTicks;
        private long _secondTicks;
        private long _thirdTicks;

        internal int Count { get; private set; }

        internal int this[int ordinal] => ordinal switch
        {
            0 => _first,
            1 => _second,
            _ => _third,
        };

        internal void Offer(int index, long ticks)
        {
            if (Count < 3) Count++;
            if (ticks > _firstTicks)
            {
                (_second, _secondTicks, _third, _thirdTicks) = (_first, _firstTicks, _second, _secondTicks);
                (_first, _firstTicks) = (index, ticks);
            }
            else if (ticks > _secondTicks)
            {
                (_third, _thirdTicks) = (_second, _secondTicks);
                (_second, _secondTicks) = (index, ticks);
            }
            else if (ticks > _thirdTicks)
            {
                (_third, _thirdTicks) = (index, ticks);
            }
        }
    }

    private static void Describe(StringBuilder text, in WorldCategoryReport report)
    {
        if (report.IsClean) return;

        text.Append(' ').Append(report.Category).Append(": ");
        text.Append(report.Outcome == WorldCategoryOutcome.Unavailable
            ? "unavailable"
            : $"{report.Sampled} read, {report.Skipped} skipped");

        if (report.FirstFailure.Length > 0) text.Append(" — ").Append(report.FirstFailure);
        text.Append('.');
    }
}
