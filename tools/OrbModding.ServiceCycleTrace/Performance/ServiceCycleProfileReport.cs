#if SERVICE_CYCLE_PROFILE
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbModding.ServiceCycleTrace.Performance;

internal static class ServiceCycleProfileReport
{
    internal static void Write(TextWriter writer, ServiceCycleProfileSession session)
    {
        var manifest = session.Manifest;
        var calibration = manifest.Calibration;
        writer.WriteLine("# ServiceCycle performance profile");
        writer.WriteLine();
        writer.WriteLine($"- Session: `{manifest.Session.Value:x16}`");
        writer.WriteLine($"- Result: `{manifest.Completeness}` / `{manifest.Reason}`");
        writer.WriteLine($"- Build: `{calibration.BuildId}`");
        writer.WriteLine($"- Semantic trace active at start: `{calibration.TraceActive}`");
        writer.WriteLine($"- Allocation measurement: `{(calibration.AllocationAvailable ? "Available" : "Unavailable")}`");
        writer.WriteLine($"- Durable records: `{manifest.WrittenRecords}`");
        writer.WriteLine();
        writer.WriteLine("## Stage aggregates");
        writer.WriteLine();
        writer.WriteLine("| Stage | Service | Temperature | Count | Average us | Min us | Max us | Allocation / call | Operations |");
        writer.WriteLine("|---|---:|---|---:|---:|---:|---:|---:|---|");
        foreach (var record in session.Records)
        {
            if (record.Kind != ServiceCycleProfileRecordKind.Aggregate) continue;
            var operations = record.Operations;
            var averageTicks = (double)record.TotalElapsedRawTicks / record.OccurrenceCount;
            var allocation = calibration.AllocationAvailable
                ? ((double)record.TotalAllocatedBytes / record.OccurrenceCount)
                    .ToString("0.0", CultureInfo.InvariantCulture)
                : "Unavailable";
            writer.WriteLine(
                $"| {ServiceCycleProfileNames.Stage(record.StageCode)} | {record.ServiceOrdinal} | {record.Temperature} | " +
                $"{record.OccurrenceCount} | {Microseconds(averageTicks, calibration.TimestampFrequency):0.000} | " +
                $"{Microseconds(record.MinimumElapsedRawTicks, calibration.TimestampFrequency):0.000} | " +
                $"{Microseconds(record.MaximumElapsedRawTicks, calibration.TimestampFrequency):0.000} | " +
                $"{allocation} | {Operations(in operations)} |");
        }
    }

    private static double Microseconds(double ticks, long frequency) => ticks * 1_000_000d / frequency;

    private static string Operations(in ServiceCycleProfileOperations value)
    {
        var parts = new List<string>(2);
        Add(parts, "ids", value.StableIdReads);
        Add(parts, "entries", value.ListEntries);
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    private static void Add(List<string> parts, string name, ulong value)
    {
        if (value != 0) parts.Add(name + "=" + value.ToString(CultureInfo.InvariantCulture));
    }
}
#endif
