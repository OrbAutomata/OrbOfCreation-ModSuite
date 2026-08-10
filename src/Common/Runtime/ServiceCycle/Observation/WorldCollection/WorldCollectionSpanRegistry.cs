using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.WorldCollection;

/// <summary>
/// Where a world-collection pass and a recording session find each other.
/// </summary>
/// <remarks>
/// <para>
/// Collection is a feature and the semantic wire is runtime-owned, and the two have no other seam:
/// the runtime's own emission is driven by <c>ServiceCaptureResult</c>, which is service-agnostic by
/// design and could only carry per-category facts by learning what a category is. This registry is
/// the alternative — the collector publishes what it read, a session subscribes while it records, and
/// neither knows the other exists. It follows the same shape as the suite's other cross-cutting
/// diagnostic seams (<c>PerformanceProfileControlRegistry</c>, <c>RuntimeDiagnosticsRegistry</c>): one
/// registry with a shared instance, and a registration handle that ends the subscription.
/// </para>
/// <para>
/// No session, no records. A pass observed while nothing is recording returns before it looks at
/// anything, which is the ordinary case: the full trace is a profiling-build artifact and a release
/// build never starts one. The thread rule lives on the subscription rather than on the registry, so
/// it says the thing worth saying — spans reach the wire on the thread that owns the trace — and says
/// nothing at all about a suite that is not recording.
/// </para>
/// </remarks>
internal sealed class WorldCollectionSpanRegistry
{
    private string[] _categories = Array.Empty<string>();
    private WorldCollectionSpanRecording? _recording;

    internal static WorldCollectionSpanRegistry Shared { get; } = new();

    /// <summary>
    /// What the collector calls its categories, in the traversal order that gives each one the
    /// identity its spans carry. Read once per recording, to name numbers in the session roster.
    /// </summary>
    internal ReadOnlySpan<string> Categories => Volatile.Read(ref _categories);

    /// <summary>Whether a session is recording spans right now.</summary>
    internal bool IsRecording => Volatile.Read(ref _recording) is not null;

    /// <summary>
    /// Says what the categories of a pass will be called, before any pass has run.
    /// </summary>
    /// <remarks>
    /// Published from the collector rather than restated here, so a category added to the collector
    /// cannot go unnamed or be named as its neighbour.
    /// </remarks>
    internal void PublishCategories(string[] categories)
    {
        if (categories is null) throw new ArgumentNullException(nameof(categories));
        Volatile.Write(ref _categories, categories);
    }

    /// <summary>
    /// Starts recording spans onto <paramref name="trace"/>, or refuses when a session already is.
    /// </summary>
    internal bool TryStartRecording(
        ServiceCycleSemanticRuntimeTrace trace,
        out WorldCollectionSpanRecording? recording)
    {
        if (trace is null) throw new ArgumentNullException(nameof(trace));
        if (Volatile.Read(ref _recording) is not null)
        {
            recording = null;
            return false;
        }
        recording = new WorldCollectionSpanRecording(this, trace);
        Volatile.Write(ref _recording, recording);
        return true;
    }

    /// <summary>
    /// Records one span per category of <paramref name="report"/>: which category, what this pass
    /// spent on it, and how many rows it produced.
    /// </summary>
    /// <remarks>
    /// The durations are the pass's own <see cref="WorldCategoryReport.ElapsedTicks"/>, converted from
    /// the raw timestamp ticks a <see cref="Stopwatch"/> counts to the hundred-nanosecond ticks every
    /// other duration on this wire is measured in. A structural category reused from an earlier epoch
    /// charges nothing and its span says so; re-charging it would make the cheapest categories of a
    /// pass render as its dearest.
    /// </remarks>
    internal void Observe(
        WorldCollectionReport report,
        long frameIdentity,
        long lifecycleEpoch,
        MonotonicTimestamp observedAt)
    {
        var recording = Volatile.Read(ref _recording);
        if (recording is null || report is null) return;
        recording.EnsureOwner();
        var categories = report.Categories;
        if (categories.Length == 0) return;
        var lifecycle = lifecycleEpoch <= 0 ? 0UL : (ulong)lifecycleEpoch;
        for (var index = 0; index < categories.Length; index++)
        {
            recording.Trace.WorldCategoryCollected(
                index + 1,
                categories[index].Sampled,
                categories.Length,
                lifecycle,
                frameIdentity,
                observedAt,
                new MonotonicDuration(ToMonotonicTicks(categories[index].ElapsedTicks)));
        }
    }

    internal void Remove(WorldCollectionSpanRecording recording)
    {
        Interlocked.CompareExchange(ref _recording, null, recording);
    }

    /// <summary>
    /// Raw timestamp ticks as hundred-nanosecond ticks. A frequency that already counts those is the
    /// common case and is passed through rather than scaled back to itself.
    /// </summary>
    private static long ToMonotonicTicks(long rawTicks)
    {
        if (rawTicks <= 0) return 0;
        if (Stopwatch.Frequency == MonotonicDuration.TicksPerSecond) return rawTicks;
        return checked(rawTicks * MonotonicDuration.TicksPerSecond) / Stopwatch.Frequency;
    }
}

/// <summary>One session's subscription to the collection spans. Disposing it ends the recording.</summary>
internal sealed class WorldCollectionSpanRecording : IDisposable
{
    private readonly WorldCollectionSpanRegistry _registry;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    internal WorldCollectionSpanRecording(
        WorldCollectionSpanRegistry registry,
        ServiceCycleSemanticRuntimeTrace trace)
    {
        _registry = registry;
        Trace = trace;
    }

    internal ServiceCycleSemanticRuntimeTrace Trace { get; }

    /// <summary>
    /// The semantic ring is single-owner, so a pass recorded from anywhere but the thread that owns
    /// the trace would corrupt the stream rather than merely mis-record itself.
    /// </summary>
    internal void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "World-collection spans must be recorded on the thread that owns the trace.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registry.Remove(this);
    }
}
