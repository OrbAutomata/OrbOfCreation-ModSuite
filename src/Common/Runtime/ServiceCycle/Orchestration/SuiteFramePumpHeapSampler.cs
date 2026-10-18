#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>
/// Reads the heap at a pump's two ends and around each capture inside it, so a profiling capture can
/// say whose allocation tipped each collection.
/// </summary>
/// <remarks>
/// <para>
/// The used heap only says what the suite allocated while no collection ran in between: a collection
/// frees the game's garbage and the suite's alike, so a reading across one is unknown, never zero.
/// The collection count at both ends is what tells the two apart.
/// </para>
/// <para>
/// Nothing is read until a host attaches its counters, and nothing at all while no trace is open.
/// </para>
/// </remarks>
internal sealed class SuiteFramePumpHeapSampler
{
    private ISuiteFramePumpHeapCounters? _counters;
    private bool _sampling;
    private int _collectionsAtStart;
    private long _heapAtStart;
    private long _captureGrowth;
    private int _capturesMeasured;
    private int _capturesAcrossCollection;

    internal const int WitnessBytes = 1 << 20;

    /// <summary>
    /// Attaches the host's counters once a witness allocation shows the used-heap reading moves with
    /// what is allocated, or says why it does not.
    /// </summary>
    internal bool TryAttach(ISuiteFramePumpHeapCounters counters, out string unavailable)
    {
        if (counters is null) throw new ArgumentNullException(nameof(counters));
        if (_counters is not null)
            throw new InvalidOperationException("Heap counters are already attached to this pump.");
        if (!Witness(counters, out unavailable)) return false;
        _counters = counters;
        return true;
    }

    private static bool Witness(ISuiteFramePumpHeapCounters counters, out string unavailable)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var collections = counters.ReadCollectionCount();
            var before = counters.ReadUsedHeapBytes();
            var witness = new byte[WitnessBytes];
            witness[0] = 1;
            var after = counters.ReadUsedHeapBytes();
            GC.KeepAlive(witness);
            if (counters.ReadCollectionCount() != collections) continue;
            if (before > 0 && after - before >= WitnessBytes)
            {
                unavailable = string.Empty;
                return true;
            }
            unavailable = "the used-heap reading did not grow with a 1 MiB allocation";
            return false;
        }
        unavailable = "a collection interrupted both witness allocations";
        return false;
    }

    internal void BeginPump(bool tracing)
    {
        var counters = _counters;
        _sampling = tracing && counters is not null;
        if (!_sampling) return;
        _captureGrowth = 0;
        _capturesMeasured = 0;
        _capturesAcrossCollection = 0;
        _collectionsAtStart = counters!.ReadCollectionCount();
        _heapAtStart = counters.ReadUsedHeapBytes();
    }

    internal SuiteFramePumpHeapReading BeginCapture() =>
        _sampling
            ? new SuiteFramePumpHeapReading(
                _counters!.ReadCollectionCount(),
                _counters.ReadUsedHeapBytes())
            : default;

    /// <summary>
    /// Charges a capture's growth to the frame, or counts it as unknown when a collection ran inside
    /// it. A heap that fell without the count moving is an incremental collection's sweep, and just as
    /// unknown.
    /// </summary>
    internal void EndCapture(in SuiteFramePumpHeapReading before)
    {
        if (!_sampling) return;
        var collections = _counters!.ReadCollectionCount();
        var heap = _counters.ReadUsedHeapBytes();
        if (collections != before.Collections || heap < before.Heap)
        {
            _capturesAcrossCollection++;
            return;
        }
        _capturesMeasured++;
        _captureGrowth += heap - before.Heap;
    }

    internal bool TryEndPump(long frameIdentity, out SuiteFramePumpHeapSample sample)
    {
        if (!_sampling)
        {
            sample = default;
            return false;
        }
        _sampling = false;
        var counters = _counters!;
        sample = new SuiteFramePumpHeapSample(
            frameIdentity,
            _collectionsAtStart,
            counters.ReadCollectionCount(),
            _heapAtStart,
            counters.ReadUsedHeapBytes(),
            _captureGrowth,
            _capturesMeasured,
            _capturesAcrossCollection,
            counters.TryReadPreviousFrameAllocated(out var allocated) ? allocated : null);
        return true;
    }
}

internal readonly struct SuiteFramePumpHeapReading
{
    internal SuiteFramePumpHeapReading(int collections, long heap)
    {
        Collections = collections;
        Heap = heap;
    }

    internal int Collections { get; }
    internal long Heap { get; }
}

internal readonly struct SuiteFramePumpHeapSample
{
    internal SuiteFramePumpHeapSample(
        long frameIdentity,
        int collectionsAtStart,
        int collectionsAtEnd,
        long heapAtStart,
        long heapAtEnd,
        long captureGrowth,
        int capturesMeasured,
        int capturesAcrossCollection,
        long? previousFrameAllocated)
    {
        FrameIdentity = frameIdentity;
        CollectionsAtStart = collectionsAtStart;
        CollectionsAtEnd = collectionsAtEnd;
        HeapAtStart = heapAtStart;
        HeapAtEnd = heapAtEnd;
        CaptureGrowth = captureGrowth;
        CapturesMeasured = capturesMeasured;
        CapturesAcrossCollection = capturesAcrossCollection;
        PreviousFrameAllocated = previousFrameAllocated;
    }

    internal long FrameIdentity { get; }
    internal int CollectionsAtStart { get; }
    internal int CollectionsAtEnd { get; }
    internal long HeapAtStart { get; }
    internal long HeapAtEnd { get; }
    internal long CaptureGrowth { get; }
    internal int CapturesMeasured { get; }
    internal int CapturesAcrossCollection { get; }
    internal long? PreviousFrameAllocated { get; }
}
#endif
