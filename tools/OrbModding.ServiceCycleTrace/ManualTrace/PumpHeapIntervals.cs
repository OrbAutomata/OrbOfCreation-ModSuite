using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

/// <summary>
/// Pairs each accepted pump with the heap sample written behind it, and turns consecutive pairs
/// into frames: the game's part from the previous pump's end to this one's start, then the suite's
/// pump.
/// </summary>
internal sealed class PumpHeapIntervals
{
    private ServiceCycleSemanticPayload? _pump;
    private ServiceCycleSemanticPayload? _previous;

    internal bool TryObserve(in ServiceCycleSemanticEvent item, out PumpHeapFrame frame)
    {
        frame = default;
        if (item.Kind == ServiceCycleSemanticEventKind.PumpCompleted)
        {
            if (item.Payload.PumpAccepted) _pump = item.Payload;
            return false;
        }
        if (item.Kind != ServiceCycleSemanticEventKind.PumpHeapSampled) return false;
        var heap = item.Payload;
        if (_pump is not { } pump || pump.FrameIdentity != heap.FrameIdentity)
            throw new InvalidDataException(
                $"Heap sample #{item.Id.Sequence} for frame {heap.FrameIdentity} follows no pump of that frame.");
        _pump = null;
        frame = new PumpHeapFrame(in pump, in heap, _previous);
        _previous = heap;
        return true;
    }
}

/// <summary>One pumped frame, as the heap saw it.</summary>
internal readonly struct PumpHeapFrame
{
    internal PumpHeapFrame(
        in ServiceCycleSemanticPayload pump,
        in ServiceCycleSemanticPayload heap,
        ServiceCycleSemanticPayload? previous)
    {
        TimestampTicks = heap.TimestampTicks;
        FrameIdentity = heap.FrameIdentity;
        PumpTicks = pump.TotalDurationTicks;
        CollectionsInPump = heap.CollectionsAtPumpEnd - heap.CollectionsAtPumpStart;
        SuiteGrowth = CollectionsInPump == 0 ? heap.HeapAtPumpEnd - heap.HeapAtPumpStart : null;
        CaptureGrowth = heap.CaptureHeapGrowth;
        CapturesMeasured = heap.CapturesMeasured;
        CapturesAcrossCollection = heap.CapturesAcrossCollection;
        PreviousFrameAllocated = heap.PreviousFrameAllocated;
        HasPrevious = previous is not null;
        GameToTicks = heap.TimestampTicks - pump.TotalDurationTicks;
        if (previous is not { } before)
        {
            GameFromTicks = GameToTicks;
            WallTicks = 0;
            CollectionsInGame = 0;
            GameGrowth = null;
            return;
        }
        GameFromTicks = before.TimestampTicks;
        WallTicks = Math.Max(0, heap.TimestampTicks - before.TimestampTicks);
        CollectionsInGame = heap.CollectionsAtPumpStart - before.CollectionsAtPumpEnd;
        GameGrowth = CollectionsInGame == 0 ? heap.HeapAtPumpStart - before.HeapAtPumpEnd : null;
    }

    internal long TimestampTicks { get; }
    internal long FrameIdentity { get; }

    /// <summary>Whether a sampled pump precedes this one, which is what bounds the game's part.</summary>
    internal bool HasPrevious { get; }

    /// <summary>The frame's wall time: previous pump's end to this pump's end.</summary>
    internal long WallTicks { get; }

    /// <summary>The game's part of the frame: the previous pump's end to this pump's start.</summary>
    internal long GameFromTicks { get; }

    internal long GameToTicks { get; }

    internal long PumpTicks { get; }
    internal int CollectionsInPump { get; }
    internal int CollectionsInGame { get; }
    internal int Collections => CollectionsInPump + CollectionsInGame;

    /// <summary>Heap growth across the pump, unknown when a collection completed inside it.</summary>
    internal long? SuiteGrowth { get; }

    /// <summary>Heap growth between pumps, unknown when a collection completed there.</summary>
    internal long? GameGrowth { get; }

    internal long CaptureGrowth { get; }
    internal int CapturesMeasured { get; }
    internal int CapturesAcrossCollection { get; }
    internal long? PreviousFrameAllocated { get; }

    /// <summary>A whole frame with no collection anywhere in it, so both growths are known.</summary>
    internal bool IsClean => HasPrevious && Collections == 0 && SuiteGrowth >= 0 && GameGrowth >= 0;
}

/// <summary>Running totals over frames, for the whole capture or one pace stretch.</summary>
/// <remarks>
/// The suite's workers allocate between pumps too, so growth in the game's part of a frame is only
/// the game's where no suite worker was evaluating. That rate is the game's own, and whatever the
/// frames with a worker grew beyond it is the workers'.
/// </remarks>
internal sealed class PumpHeapTally
{
    private readonly List<CleanFrame> _clean = new();

    internal long Frames { get; private set; }
    internal long Collections { get; private set; }
    internal long FirstTicks { get; private set; } = -1;
    internal long LastTicks { get; private set; }
    internal long CleanFrames => _clean.Count;

    internal void Add(in PumpHeapFrame frame)
    {
        Frames++;
        if (FirstTicks < 0) FirstTicks = frame.TimestampTicks;
        LastTicks = frame.TimestampTicks;
        Collections += frame.Collections;
        if (!frame.IsClean) return;
        _clean.Add(new CleanFrame(
            frame.GameFromTicks,
            frame.GameToTicks,
            frame.WallTicks,
            frame.SuiteGrowth!.Value,
            frame.GameGrowth!.Value));
    }

    internal double Seconds => FirstTicks < 0 ? 0 : TraceMetric.ToMilliseconds(LastTicks - FirstTicks) / 1000d;

    internal double? CollectionsPerMinute => Seconds > 0 ? Collections / Seconds * 60d : null;

    internal PumpHeapShares Shares(SuiteWorkerSpans workers)
    {
        ArgumentNullException.ThrowIfNull(workers);
        long cleanTicks = 0, pumpGrowth = 0, gameGrowth = 0;
        var between = new (long Ticks, long Growth)[3];
        foreach (var frame in _clean)
        {
            cleanTicks += frame.WallTicks;
            pumpGrowth += frame.SuiteGrowth;
            gameGrowth += frame.GameGrowth;
            var presence = (int)workers.During(frame.GameFromTicks, frame.GameToTicks);
            between[presence].Ticks += frame.GameToTicks - frame.GameFromTicks;
            between[presence].Growth += frame.GameGrowth;
        }
        var none = between[(int)SuiteWorkerPresence.None];
        double? baseline = none.Ticks > 0 ? (double)none.Growth / none.Ticks : null;
        return new PumpHeapShares(
            TraceMetric.ToMilliseconds(cleanTicks) / 1000d,
            pumpGrowth + gameGrowth,
            pumpGrowth,
            Excess(between[(int)SuiteWorkerPresence.Capturing], baseline),
            Excess(between[(int)SuiteWorkerPresence.OtherOnly], baseline),
            baseline is { } perTick ? perTick * TimeSpan.TicksPerSecond / 1_048_576d : null);
    }

    private static long? Excess((long Ticks, long Growth) frames, double? baseline) =>
        frames.Ticks == 0 ? 0 : baseline is { } perTick ? frames.Growth - (long)(perTick * frames.Ticks) : null;

    private readonly record struct CleanFrame(
        long GameFromTicks,
        long GameToTicks,
        long WallTicks,
        long SuiteGrowth,
        long GameGrowth);
}

/// <summary>
/// Heap growth over the frames no collection touched, split by who allocated it. The workers' part is
/// unknown when no frame ran without a worker, because then the game's own rate is unknown.
/// </summary>
internal readonly record struct PumpHeapShares(
    double Seconds,
    long TotalGrowth,
    long PumpGrowth,
    long? CapturingWorkerGrowth,
    long? OtherWorkerGrowth,
    double? GameMegabytesPerSecond)
{
    internal long? SuiteGrowth => PumpGrowth + CapturingWorkerGrowth + OtherWorkerGrowth;

    internal double? TotalMegabytesPerSecond => Rate(TotalGrowth);

    internal double? SuiteMegabytesPerSecond => Rate(SuiteGrowth);

    internal double? PumpMegabytesPerSecond => Rate(PumpGrowth);

    internal double? CapturingWorkerMegabytesPerSecond => Rate(CapturingWorkerGrowth);

    internal double? OtherWorkerMegabytesPerSecond => Rate(OtherWorkerGrowth);

    private double? Rate(long? bytes) =>
        Seconds > 0 && bytes is { } known ? known / 1_048_576d / Seconds : null;
}
