using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

internal static class ManualFullTraceReport
{
    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);
        var document = session.Document;
        writer.WriteLine("# ServiceCycle manual full-trace report");
        writer.WriteLine();
        writer.WriteLine($"- Session: `{session.Name}`");
        writer.WriteLine("- Format: OSCS/OSCM v1");
        writer.WriteLine("- Eligibility: DiagnosticOnly");
        writer.WriteLine($"- Completeness: {document.State}");
        writer.WriteLine($"- Terminal reason: {document.TerminalReason?.ToString() ?? "Unavailable (manifest absent)"}");
        writer.WriteLine($"- Segments: {document.SegmentCount.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.Write("- Accepted records: ");
        if (!document.HasTerminalEvidence) writer.Write("at least ");
        writer.WriteLine(document.AcceptedRecords.ToString("N0", CultureInfo.InvariantCulture));
        writer.WriteLine($"- Durable records: {document.WrittenRecords.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- Segment bytes: {document.SegmentBytes.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- Observed window: {WindowMilliseconds(document).ToString("F3", CultureInfo.InvariantCulture)} ms");
        if (document.State != FullTraceSessionState.Complete)
        {
            writer.WriteLine($"- First incomplete transport sequence: {document.FirstIncompleteTransportSequence.ToString("N0", CultureInfo.InvariantCulture)}");
            writer.WriteLine($"- First incomplete semantic sequence: {FormatOptional(document.FirstIncompleteSemanticSequence)}");
        }
        if (document.FirstSemanticSequence > 1)
            writer.WriteLine("- Causal boundary: parents before this recording are external ancestry and are not present.");
        writer.WriteLine();
        ManualFullTraceStoreView.Write(writer, session);
        ManualFullTraceServiceView.Write(writer, session);
        ManualFullTraceCategoryView.Write(writer, session);
        ManualFullTracePumpView.Write(writer, session);
        ManualFullTraceTimelineView.Write(writer, session);
    }

    private static double WindowMilliseconds(FullTraceSessionDocument document) =>
        document.WrittenRecords == 0 || document.LastTimestampTicks < document.FirstTimestampTicks
            ? 0
            : TraceMetric.ToMilliseconds(document.LastTimestampTicks - document.FirstTimestampTicks);

    private static string FormatOptional(ulong value) => value == 0
        ? "Unavailable"
        : value.ToString("N0", CultureInfo.InvariantCulture);
}
