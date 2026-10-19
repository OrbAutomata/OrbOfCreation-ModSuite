using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

namespace OrbModding.ServiceCycleTrace.Journal;

internal static class DecisionJournalReport
{
    internal static void Write(TextWriter writer, DecisionJournalReportData report)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(report);
        var previousNewLine = writer.NewLine;
        writer.NewLine = "\n";
        try
        {
            WriteHeader(writer, report.Window);
            WriteRuns(writer, report.Window);
            WriteServices(writer, report.Analysis);
            report.WriteLineage(writer);
        }
        finally
        {
            writer.NewLine = previousNewLine;
        }
    }

    private static void WriteHeader(TextWriter writer, DecisionJournalWindowDocument window)
    {
        writer.WriteLine("# ServiceCycle decision-journal report");
        writer.WriteLine();
        writer.WriteLine("- Scope: Validated retained durable window");
        writer.WriteLine("- Format: OSJD wire schema 4");
        writer.WriteLine("- Artifact: `journal`");
        writer.WriteLine($"- Segments: {Number(window.SegmentCount)}");
        writer.WriteLine($"- Retained records: {Number(window.RecordCount)}");
        writer.WriteLine($"- Segment bytes: {Number(window.SegmentBytes)}");
        writer.WriteLine($"- Process runs represented: {Number((ulong)window.RunCount)}");
        if (window.HasSegments)
        {
            writer.WriteLine(
                $"- Storage ordinals: {Number(window.FirstStorageOrdinal)}..{Number(window.LastStorageOrdinal)}");
            if (window.FirstStorageOrdinal != 0)
            {
                writer.WriteLine(
                    $"- Earlier storage: {Number(window.FirstStorageOrdinal)} ordinal positions precede this window and are absent; rolling retention and external removal are indistinguishable offline.");
            }
        }
        else
        {
            writer.WriteLine("- Storage ordinals: None committed");
        }
        writer.WriteLine(
            "- Writer terminal state: Unavailable. OSJD has no terminal manifest or persisted live-status result.");
        writer.WriteLine();
        writer.WriteLine(
            "This format omits empty pumps, frame and pump timing, physical worker scheduling, wall-clock time, service names, exception text, projection snapshots, wake policies, and aggregate native-call accounting. Those facts are not inferred.");
        writer.WriteLine();
    }

    private static void WriteRuns(TextWriter writer, DecisionJournalWindowDocument window)
    {
        writer.WriteLine("## Retained run coverage");
        writer.WriteLine();
        if (window.RunCount == 0)
        {
            writer.WriteLine("No process run has a committed segment in this window.");
            writer.WriteLine();
            return;
        }

        writer.WriteLine("Monotonic ticks and elapsed spans are comparable only within their own run.");
        writer.WriteLine();
        writer.WriteLine("| Run | Storage ordinals | Segments | Record sequences | Records | Observed span ms | Retained start |");
        writer.WriteLine("|---|---:|---:|---:|---:|---:|---|");
        for (var index = 0; index < window.RunCount; index++)
        {
            var run = window.GetRun(index);
            writer.Write("| `");
            writer.Write(run.Run.Value.ToString("x16", CultureInfo.InvariantCulture));
            writer.Write("` | ");
            WriteRange(writer, run.FirstStorageOrdinal, run.LastStorageOrdinal);
            writer.Write(" | ");
            writer.Write(Number(run.SegmentCount));
            writer.Write(" | ");
            WriteRange(writer, run.FirstRecordSequence, run.LastRecordSequence);
            writer.Write(" | ");
            writer.Write(Number(run.RecordCount));
            writer.Write(" | ");
            writer.Write(TraceMetric.ToMilliseconds(
                run.LastTimestampTicks - run.FirstTimestampTicks)
                .ToString("F3", CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(run.FirstRecordSequence == 1
                ? "Begins at sequence 1"
                : $"Sequences 1..{Number(run.FirstRecordSequence - 1)} absent");
            writer.WriteLine(" |");
        }
        writer.WriteLine();
        writer.WriteLine(
            "A run ending at its last retained record does not prove clean shutdown; it may still be active or have lost later undurable evidence.");
        writer.WriteLine();
    }

    private static void WriteServices(TextWriter writer, DecisionJournalAnalysisDocument analysis)
    {
        writer.WriteLine("## Numeric run/service view");
        writer.WriteLine();
        writer.WriteLine(
            "OSJD schema 4 carries numeric service identities plus exact per-action candidate, native-type, action-kind, list, view, route-status, disposition, and result-code sentinels. This report does not infer feature names.");
        writer.WriteLine();
        if (analysis.ServiceCount == 0)
        {
            writer.WriteLine("No service-scoped records were retained.");
        }
        else
        {
            writer.WriteLine("| Run | Service | Decision spans / observations / cycles | Batch-only decisions | Actions total / committed / rejected / faulted / skipped | Fault-bearing observations | Lifecycle transitions | World-gate holds |");
            writer.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            for (var index = 0; index < analysis.ServiceCount; index++)
            {
                var service = analysis.GetService(index);
                writer.Write("| `");
                writer.Write(service.Run.ToString("x16", CultureInfo.InvariantCulture));
                writer.Write("` | ");
                writer.Write(Number(service.Service));
                writer.Write(" | ");
                WriteTriple(
                    writer,
                    service.DecisionSpans,
                    service.Observations,
                    service.Cycles);
                writer.Write(" | ");
                writer.Write(Number(service.BatchDecisions));
                writer.Write(" | ");
                WriteFive(
                    writer,
                    service.Actions,
                    service.CommittedActions,
                    service.RejectedActions,
                    service.FaultedActions,
                    service.SkippedActions);
                writer.Write(" | ");
                writer.Write(Number(service.FaultBearingObservations));
                writer.Write(" | ");
                writer.Write(Number(service.LifecycleChanges));
                writer.Write(" | ");
                writer.Write(Number(service.WorldGateHolds));
                writer.WriteLine(" |");
            }
        }
        writer.WriteLine();
        writer.WriteLine(
            $"Suite-wide configuration / strategy publications: {Number(analysis.ConfigurationChanges)} / {Number(analysis.StrategyChanges)}. The suite publishes one configuration record and one strategy bulletin, so a publication is one record rather than one per service.");
        writer.WriteLine();
        writer.WriteLine(
            $"Global emergency transitions entered / cleared: {Number(analysis.EmergencyEntered)} / {Number(analysis.EmergencyCleared)}.");
        writer.WriteLine();
    }

    private static void WriteRange(TextWriter writer, ulong first, ulong last)
    {
        writer.Write(Number(first));
        if (last == first) return;
        writer.Write("..");
        writer.Write(Number(last));
    }

    private static void WriteTriple(TextWriter writer, long first, long second, long third) =>
        writer.Write($"{Number(first)} / {Number(second)} / {Number(third)}");

    private static void WriteFive(
        TextWriter writer,
        long first,
        long second,
        long third,
        long fourth,
        long fifth) => writer.Write(
        $"{Number(first)} / {Number(second)} / {Number(third)} / {Number(fourth)} / {Number(fifth)}");

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Number(ulong value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
