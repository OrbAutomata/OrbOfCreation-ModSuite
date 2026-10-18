using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

/// <summary>
/// What each automation pace cost: one row per stretch the player spent at one pace, from the
/// settings save that chose it to the one that changed it.
/// </summary>
/// <remarks>
/// The pace is read from the configuration store behind each published generation, so a save that
/// changed something else continues the stretch it fell in. Collection is the only capturing service,
/// so the pump's capture phase is collection's main-thread cost.
/// </remarks>
internal static class ManualFullTracePaceView
{
    private const string PaceKey = "General.Pace";
    private const string Unrecorded = "unrecorded";

    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);
        var origin = session.Document.FirstTimestampTicks;
        var stretches = new List<Stretch>();
        Stretch? current = null;
        var heap = new PumpHeapIntervals();
        foreach (var segment in session.Segments())
        foreach (var item in segment.Events)
        {
            if (heap.TryObserve(in item, out var frame))
            {
                current?.Heap.Add(in frame);
                continue;
            }
            var payload = item.Payload;
            if (item.Kind == ServiceCycleSemanticEventKind.ConfigurationPublished)
            {
                var pace = session.PublicationValue("configuration", payload.Configuration, PaceKey) ?? Unrecorded;
                if (current is not null && current.Pace == pace) continue;
                current = new Stretch(pace, payload.TimestampTicks);
                stretches.Add(current);
            }
            else if (item.Kind == ServiceCycleSemanticEventKind.PumpCompleted && payload.PumpAccepted)
            {
                if (current is null)
                {
                    current = new Stretch(Unrecorded, payload.TimestampTicks);
                    stretches.Add(current);
                }
                current.Add(in payload);
            }
        }

        writer.WriteLine("## Pace");
        writer.WriteLine();
        if (stretches.Count == 0)
        {
            writer.WriteLine("This capture holds no pumps.");
            writer.WriteLine();
            return;
        }
        writer.WriteLine("| From ms | Pace | Seconds | Collections | Per second | Avg collection ms | Pumps per second | Avg pump ms | GCs per minute | Heap MB/s | Suite MB/s |");
        writer.WriteLine("|---:|:---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var stretch in stretches)
        {
            var seconds = TraceMetric.ToMilliseconds(stretch.LastTicks - stretch.FromTicks) / 1000d;
            writer.WriteLine(
                $"| {Milliseconds(stretch.FromTicks - origin)} | {stretch.Pace} | {Fixed(seconds, "F1")} | " +
                $"{stretch.Collections.ToString("N0", CultureInfo.InvariantCulture)} | {Rate(stretch.Collections, seconds)} | " +
                $"{Average(stretch.CollectionTicks, stretch.Collections)} | {Rate(stretch.Pumps, seconds)} | " +
                $"{Average(stretch.PumpTicks, stretch.Pumps)} | {Optional(stretch.Heap.CollectionsPerMinute, "F1")} | " +
                $"{Optional(stretch.Heap.TotalMegabytesPerSecond, "F2")} | {Optional(stretch.Heap.SuiteMegabytesPerSecond, "F2")} |");
        }
        writer.WriteLine();
    }

    private static string Milliseconds(long ticks) => Fixed(TraceMetric.ToMilliseconds(Math.Max(0, ticks)), "F3");

    private static string Rate(long count, double seconds) => seconds > 0 ? Fixed(count / seconds, "F2") : "—";

    private static string Average(long ticks, long count) =>
        count > 0 ? Fixed(TraceMetric.ToMilliseconds(ticks) / count, "F3") : "—";

    private static string Optional(double? value, string format) =>
        value is { } known ? Fixed(known, format) : "—";

    private static string Fixed(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private sealed class Stretch
    {
        internal Stretch(string pace, long fromTicks)
        {
            Pace = pace;
            FromTicks = fromTicks;
            LastTicks = fromTicks;
        }

        internal string Pace { get; }
        internal long FromTicks { get; }
        internal long LastTicks { get; private set; }
        internal long Pumps { get; private set; }
        internal long PumpTicks { get; private set; }
        internal long Collections { get; private set; }
        internal long CollectionTicks { get; private set; }
        internal PumpHeapTally Heap { get; } = new();

        internal void Add(in ServiceCycleSemanticPayload payload)
        {
            LastTicks = Math.Max(LastTicks, payload.TimestampTicks);
            Pumps++;
            PumpTicks += payload.TotalDurationTicks;
            if (payload.CapturesAttempted == 0) return;
            Collections += payload.CapturesAttempted;
            CollectionTicks += payload.CaptureDurationTicks;
        }
    }
}
