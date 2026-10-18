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
        if (previous is not { } before)
        {
            WallTicks = 0;
            CollectionsInGame = 0;
            GameGrowth = null;
            return;
        }
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
internal sealed class PumpHeapTally
{
    internal long Frames { get; private set; }
    internal long Collections { get; private set; }
    internal long FirstTicks { get; private set; } = -1;
    internal long LastTicks { get; private set; }
    internal long CleanFrames { get; private set; }
    internal long CleanTicks { get; private set; }
    internal long SuiteGrowth { get; private set; }
    internal long GameGrowth { get; private set; }

    internal void Add(in PumpHeapFrame frame)
    {
        Frames++;
        if (FirstTicks < 0) FirstTicks = frame.TimestampTicks;
        LastTicks = frame.TimestampTicks;
        Collections += frame.Collections;
        if (!frame.IsClean) return;
        CleanFrames++;
        CleanTicks += frame.WallTicks;
        SuiteGrowth += frame.SuiteGrowth!.Value;
        GameGrowth += frame.GameGrowth!.Value;
    }

    internal double Seconds => FirstTicks < 0 ? 0 : TraceMetric.ToMilliseconds(LastTicks - FirstTicks) / 1000d;

    internal double? CollectionsPerMinute => Seconds > 0 ? Collections / Seconds * 60d : null;

    /// <summary>All heap growth per second of clean frames: the game, the suite, and every thread.</summary>
    internal double? TotalMegabytesPerSecond => Rate(SuiteGrowth + GameGrowth);

    /// <summary>Heap growth inside the suite's pumps per second of clean frames.</summary>
    internal double? SuiteMegabytesPerSecond => Rate(SuiteGrowth);

    private double? Rate(long bytes)
    {
        var seconds = TraceMetric.ToMilliseconds(CleanTicks) / 1000d;
        return seconds > 0 ? bytes / 1_048_576d / seconds : null;
    }
}
