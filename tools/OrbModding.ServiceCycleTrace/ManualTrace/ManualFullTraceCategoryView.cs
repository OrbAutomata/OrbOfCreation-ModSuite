using System.Globalization;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

/// <summary>
/// What collection spent its session on, per category.
/// </summary>
/// <remarks>
/// Collection is the suite's largest main-thread cost and the whole pass used to be one number, so
/// every proposal to make it cheaper was an argument about which category was probably expensive.
/// Sorted by total cost, because the question this view exists to answer is which part to fix first.
/// </remarks>
internal static class ManualFullTraceCategoryView
{
    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);
        Write(writer, WorldCategorySpanSummary.Read(session));
    }

    internal static void Write(TextWriter writer, WorldCategorySpanSummary summary)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(summary);
        writer.WriteLine("## World collection categories");
        writer.WriteLine();
        if (!summary.HasSpans)
        {
            writer.WriteLine(
                "This capture holds no collection spans. Captures recorded by a newer runtime carry " +
                "one span per category per pass.");
            writer.WriteLine();
            return;
        }

        writer.WriteLine(
            Count(summary.Spans) + " span(s) over " + Count(summary.Passes) +
            " collection pass(es), sorted by what the session spent on each category. A category read " +
            "once per lifecycle epoch charges the passes that re-read it and nothing to the passes " +
            "that reused its rows.");
        if (summary.Discrepancy.Length != 0)
        {
            writer.WriteLine();
            writer.WriteLine("**Incomplete:** " + summary.Discrepancy);
        }
        writer.WriteLine();
        writer.WriteLine("| Category | Passes | Total ms | Avg ms | p50 ms | Worst ms | Sampled last | Sampled max |");
        writer.WriteLine("|:---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in summary.Rows)
        {
            writer.Write("| ");
            writer.Write(Label(in row));
            writer.Write(" | ");
            writer.Write(Count(row.Passes));
            writer.Write(" | ");
            writer.Write(Milliseconds(row.TotalMilliseconds));
            writer.Write(" | ");
            writer.Write(Milliseconds(row.AverageMilliseconds));
            writer.Write(" | ");
            writer.Write(Milliseconds(row.MedianMilliseconds));
            writer.Write(" | ");
            writer.Write(Milliseconds(row.WorstMilliseconds));
            writer.Write(" | ");
            writer.Write(Count(row.SampledLast));
            writer.Write(" | ");
            writer.Write(Count(row.SampledMaximum));
            writer.WriteLine(" |");
        }
        writer.WriteLine();
    }

    /// <summary>
    /// The name and the number together, for the same reason the service view prints both: the number
    /// is what the records carry, and a capture with no roster has only the number.
    /// </summary>
    private static string Label(in WorldCategorySpanRow row) => row.Name.Length == 0
        ? row.Category.ToString(CultureInfo.InvariantCulture)
        : row.Name + " (" + row.Category.ToString(CultureInfo.InvariantCulture) + ")";

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Milliseconds(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
