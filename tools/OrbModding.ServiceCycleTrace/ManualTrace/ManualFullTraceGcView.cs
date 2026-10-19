using System.Globalization;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

/// <summary>
/// Whose garbage the collector collected: how often it ran, what the suite allocated against what
/// everything allocated, and which side of the frame each hitch's collection landed in.
/// </summary>
/// <remarks>
/// A collection runs when enough has been allocated by anyone, its pause scales with the live heap,
/// and it lands on whoever allocated last. So the suite's share of the allocation is its share of
/// how often collections run, and where a collection completed says who paid for the pause.
/// </remarks>
internal static class ManualFullTraceGcView
{
    internal const long HitchTicks = 250 * 10_000L;
    private const int WorstHitches = 10;

    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);
        var origin = session.Document.FirstTimestampTicks;
        var intervals = new PumpHeapIntervals();
        var tally = new PumpHeapTally();
        var workers = new SuiteWorkerSpans();
        var gcInPump = 0L;
        var gcInGame = 0L;
        var captureGrowth = 0L;
        var capturesMeasured = 0L;
        var capturesAcross = 0L;
        var frameAllocated = 0L;
        var frameAllocatedFrames = 0L;
        var gcFrames = new TraceMetricBuilder();
        var cleanFrames = new TraceMetricBuilder();
        var hitches = new HitchClasses();
        var worst = new List<Hitch>();
        foreach (var segment in session.Segments())
        foreach (var item in segment.Events)
        {
            workers.Observe(in item);
            if (!intervals.TryObserve(in item, out var frame)) continue;
            tally.Add(in frame);
            gcInPump += frame.CollectionsInPump;
            gcInGame += frame.CollectionsInGame;
            captureGrowth += frame.CaptureGrowth;
            capturesMeasured += frame.CapturesMeasured;
            capturesAcross += frame.CapturesAcrossCollection;
            if (frame.PreviousFrameAllocated is { } allocated)
            {
                frameAllocated += allocated;
                frameAllocatedFrames++;
            }
            if (!frame.HasPrevious) continue;
            (frame.Collections == 0 ? cleanFrames : gcFrames).AddTicks(frame.WallTicks);
            if (frame.WallTicks < HitchTicks) continue;
            var hitch = new Hitch(frame.TimestampTicks - origin, in frame);
            hitches.Add(in hitch);
            worst.Add(hitch);
        }

        workers.Freeze();
        writer.WriteLine("## Garbage collection");
        writer.WriteLine();
        if (tally.Frames == 0)
        {
            writer.WriteLine(
                "Unavailable: this capture holds no heap samples. A profiling build writes one per pump; " +
                "its log says why when it could not.");
            writer.WriteLine();
            return;
        }

        writer.WriteLine($"- Sampled pumps: {Count(tally.Frames)} over {Fixed(tally.Seconds, "F1")} s");
        writer.WriteLine(
            $"- Collections: {Count(tally.Collections)} ({Optional(tally.CollectionsPerMinute, "F1")} per minute) — " +
            $"{Count(gcInPump)} completed inside a suite pump, {Count(gcInGame)} in the game's part of a frame");
        var shares = tally.Shares(workers);
        writer.WriteLine(
            $"- Heap growth over {Count(tally.CleanFrames)} frames with no collection: " +
            $"{Optional(shares.TotalMegabytesPerSecond, "F2")} MB/s in all, of which the suite " +
            $"{Optional(shares.SuiteMegabytesPerSecond, "F2")} MB/s ({Share(shares.SuiteGrowth, shares.TotalGrowth)})");
        writer.WriteLine(
            $"  - inside suite pumps {Optional(shares.PumpMegabytesPerSecond, "F2")} MB/s, collection's worker " +
            $"between pumps {Optional(shares.CapturingWorkerMegabytesPerSecond, "F2")} MB/s, other services' " +
            $"workers between pumps {Optional(shares.OtherWorkerMegabytesPerSecond, "F2")} MB/s");
        writer.WriteLine(
            $"  - the game alone {Optional(shares.GameMegabytesPerSecond, "F2")} MB/s, read from the game's part of " +
            "frames no suite worker overlapped");
        writer.WriteLine(
            $"- Captures: {Megabytes(captureGrowth)} MB over {Count(capturesMeasured)} measured " +
            $"({Average(captureGrowth, capturesMeasured)} KB each); " +
            $"{Count(capturesAcross)} crossed a collection and are unknown");
        writer.WriteLine($"- Unity's per-frame allocation counter: {FrameCounter(frameAllocated, frameAllocatedFrames, tally.Seconds)}");
        writer.WriteLine(
            $"- Frame time with a collection: {FrameMetric(gcFrames.Freeze())}; without: {FrameMetric(cleanFrames.Freeze())}");
        writer.WriteLine();
        writer.WriteLine(
            "Growth is the used Mono heap, read around each pump. Collections and pauses are the runtime's " +
            "own count. A worker's growth between pumps is what the game's part of a frame grew beyond the " +
            "game's own rate while that worker was evaluating; growth inside a pump includes any worker " +
            "allocating at the same time. Mono's used-heap reading moves in heap blocks rather than bytes, " +
            "so it is a share over many frames rather than an exact count for one.");
        writer.WriteLine();

        writer.WriteLine($"### Hitches (frames of {Fixed(TraceMetric.ToMilliseconds(HitchTicks), "F0")} ms or more)");
        writer.WriteLine();
        if (worst.Count == 0)
        {
            writer.WriteLine("None.");
            writer.WriteLine();
            return;
        }
        writer.WriteLine("| Where the collection completed | Hitches | Total ms |");
        writer.WriteLine("|:---|---:|---:|");
        hitches.Write(writer);
        writer.WriteLine();
        writer.WriteLine("| Offset ms | Frame | Frame ms | Suite pump ms | Collection |");
        writer.WriteLine("|---:|---:|---:|---:|:---|");
        foreach (var hitch in worst.OrderByDescending(x => x.WallTicks).Take(WorstHitches))
        {
            writer.WriteLine(
                $"| {Milliseconds(hitch.OffsetTicks)} | {hitch.FrameIdentity.ToString(CultureInfo.InvariantCulture)} | " +
                $"{Milliseconds(hitch.WallTicks)} | {Milliseconds(hitch.PumpTicks)} | {Label(hitch.Class)} |");
        }
        writer.WriteLine();
    }

    private static string Label(HitchClass hitchClass) => hitchClass switch
    {
        HitchClass.SuitePump => "During the suite's pump",
        HitchClass.GameFrame => "During the game's part of the frame",
        HitchClass.Both => "Both",
        _ => "No collection",
    };

    private static string FrameCounter(long bytes, long frames, double seconds)
    {
        if (frames == 0) return "unavailable in this player";
        if (bytes == 0) return $"reported zero on all {Count(frames)} frames, so it is inert in this player";
        return $"{Fixed(bytes / 1_048_576d / seconds, "F2")} MB/s over {Count(frames)} frames";
    }

    private static string FrameMetric(TraceMetric metric) =>
        metric.Samples == 0
            ? "no frames"
            : $"{Fixed(metric.AverageMilliseconds, "F1")} ms average over {Count(metric.Samples)} frames, " +
              $"worst {Fixed(metric.MaximumMilliseconds, "F1")} ms";

    private static string Share(long? part, long whole) =>
        whole > 0 && part is { } known ? Fixed(100d * known / whole, "F1") + "%" : "—";

    private static string Average(long bytes, long count) =>
        count > 0 ? Fixed(bytes / 1024d / count, "F1") : "—";

    private static string Megabytes(long bytes) => Fixed(bytes / 1_048_576d, "F1");

    private static string Optional(double? value, string format) =>
        value is { } known ? Fixed(known, format) : "—";

    private static string Milliseconds(long ticks) => Fixed(TraceMetric.ToMilliseconds(Math.Max(0, ticks)), "F1");

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Fixed(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private readonly struct Hitch
    {
        internal Hitch(long offsetTicks, in PumpHeapFrame frame)
        {
            OffsetTicks = offsetTicks;
            FrameIdentity = frame.FrameIdentity;
            WallTicks = frame.WallTicks;
            PumpTicks = frame.PumpTicks;
            Class = frame.CollectionsInPump > 0
                ? frame.CollectionsInGame > 0 ? HitchClass.Both : HitchClass.SuitePump
                : frame.CollectionsInGame > 0 ? HitchClass.GameFrame : HitchClass.None;
        }

        internal long OffsetTicks { get; }
        internal long FrameIdentity { get; }
        internal long WallTicks { get; }
        internal long PumpTicks { get; }
        internal HitchClass Class { get; }
    }

    private enum HitchClass
    {
        SuitePump,
        GameFrame,
        Both,
        None,
    }

    private sealed class HitchClasses
    {
        private readonly long[] _counts = new long[4];
        private readonly long[] _ticks = new long[4];

        internal void Add(in Hitch hitch)
        {
            _counts[(int)hitch.Class]++;
            _ticks[(int)hitch.Class] += hitch.WallTicks;
        }

        internal void Write(TextWriter writer)
        {
            foreach (var hitchClass in (HitchClass[])Enum.GetValues(typeof(HitchClass)))
            {
                writer.WriteLine(
                    $"| {Label(hitchClass)} | {Count(_counts[(int)hitchClass])} | " +
                    $"{Milliseconds(_ticks[(int)hitchClass])} |");
            }
        }
    }
}
