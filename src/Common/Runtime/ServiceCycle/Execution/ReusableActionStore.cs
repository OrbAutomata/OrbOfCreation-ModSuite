using System;
using System.Runtime.CompilerServices;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal readonly struct ServiceActionStoreMetrics
{
    internal ServiceActionStoreMetrics(
        int count,
        int cursor,
        int capacity,
        int highWaterCount,
        long growthAllocationCount)
    {
        Count = count;
        Cursor = cursor;
        Capacity = capacity;
        HighWaterCount = highWaterCount;
        GrowthAllocationCount = growthAllocationCount;
    }

    internal int Count { get; }
    internal int Cursor { get; }
    internal int Capacity { get; }
    internal int HighWaterCount { get; }
    internal long GrowthAllocationCount { get; }
    internal int RetainedSlots => Capacity;
}

internal sealed class ReusableActionStore<TAction>
{
    private static readonly bool ContainsReferences = RuntimeHelpers.IsReferenceOrContainsReferences<TAction>();
    private TAction[] _items;

    internal ReusableActionStore(int initialCapacity = 0, LifecycleGeneration lifecycle = default)
    {
        if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        Lifecycle = lifecycle;
        _items = initialCapacity == 0 ? Array.Empty<TAction>() : new TAction[initialCapacity];
    }

    internal LifecycleGeneration Lifecycle { get; }
    internal int Count { get; private set; }
    internal int Cursor { get; private set; }
    internal int Capacity => _items.Length;
    internal int HighWaterCount { get; private set; }
    internal long GrowthAllocationCount { get; private set; }
    internal int LastCleanupThreadId { get; private set; }
    internal ServiceActionStoreMetrics Metrics => new(
        Count,
        Cursor,
        Capacity,
        HighWaterCount,
        GrowthAllocationCount);

    internal void BeginWrite()
    {
        if (Count != 0 || Cursor != 0)
            throw new InvalidOperationException("The previous action batch has not returned ownership.");
    }

    internal void ValidateLifecycle(LifecycleGeneration lifecycle)
    {
        if (Lifecycle.Value != 0 && Lifecycle != lifecycle)
            throw new InvalidOperationException("The action store belongs to another lifecycle generation.");
    }

    internal void Add(in TAction action)
    {
        EnsureCapacity(checked(Count + 1));
        var index = Count;
        _items[index] = action;
        Count = index + 1;
        if (Count > HighWaterCount) HighWaterCount = Count;
    }

    internal ref readonly TAction GetCurrent()
    {
        if ((uint)Cursor >= (uint)Count)
            throw new InvalidOperationException("The action cursor is not positioned on an action.");
        return ref _items[Cursor];
    }

    internal void AdvanceCurrentAndClear()
    {
        if ((uint)Cursor >= (uint)Count)
            throw new InvalidOperationException("The action cursor is not positioned on an action.");
        if (ContainsReferences) _items[Cursor] = default!;
        Cursor++;
    }

    internal void CommitCurrentAndClear() => AdvanceCurrentAndClear();

    internal bool IsComplete => Cursor == Count;

    internal void CompleteSuccessfulBatch()
    {
        if (!IsComplete)
            throw new InvalidOperationException("An incomplete action batch cannot be completed successfully.");
        Count = 0;
        Cursor = 0;
    }

    /// <summary>
    /// Ends a rejected/faulted batch in O(1) on the owner thread. A reference-bearing suffix remains
    /// worker-owned until <see cref="ClearRejectedSuffixOnWorker"/> acknowledges cleanup.
    /// </summary>
    internal bool ReleaseRejectedBatchForWorkerCleanup(out int clearFrom, out int clearCount)
    {
        clearFrom = Cursor;
        clearCount = Count - Cursor;
        if (!ContainsReferences || clearCount == 0)
        {
            Count = 0;
            Cursor = 0;
            return false;
        }

        return true;
    }

    internal void ClearRejectedSuffixOnWorker(int clearFrom, int clearCount)
    {
        if (clearFrom < 0 || clearCount < 0 || clearFrom + clearCount > Count)
            throw new ArgumentOutOfRangeException(nameof(clearFrom));
        Array.Clear(_items, clearFrom, clearCount);
        LastCleanupThreadId = Thread.CurrentThread.ManagedThreadId;
        Count = 0;
        Cursor = 0;
    }

    internal void AbortWorkerWrite()
    {
        if (ContainsReferences && Count != 0)
            Array.Clear(_items, 0, Count);
        Count = 0;
        Cursor = 0;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _items.Length) return;
        var capacity = _items.Length == 0 ? 4 : _items.Length;
        while (capacity < required)
        {
            var doubled = checked(capacity * 2);
            capacity = doubled < required ? doubled : required > doubled / 2 ? doubled : required;
        }

        Array.Resize(ref _items, capacity);
        GrowthAllocationCount++;
    }
}

/// <summary>A non-escapable typed view over the runner-owned reusable action store.</summary>
public readonly ref struct ServiceActionWriter<TAction>
{
    private readonly ReusableActionStore<TAction> _store;
    private readonly ServiceRetryReportBuffer? _retryReports;

    internal ServiceActionWriter(
        ReusableActionStore<TAction> store,
        ServiceRetryReportBuffer? retryReports = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _retryReports = retryReports;
    }

    public int Count => _store.Count;
    public void Add(in TAction action) => _store.Add(in action);

    /// <summary>
    /// A target whose committed action this world shows did not take: it backs off with doubling
    /// until the evaluator ends the episode. A cap of the caller's own replaces the service's maximum
    /// backoff and may not be shorter than its initial backoff.
    /// </summary>
    public void ReportFailure(
        ServiceRetryTarget target,
        ServiceActionResultCode code,
        string reason,
        MonotonicDuration? maximumBackoff = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A failure report names why the target failed.", nameof(reason));
        RetryReports.Add(ServiceRetryReport.Failed(target, code, reason, maximumBackoff));
    }

    /// <summary>
    /// Ends the target's backoff: this world shows it working again, or the evaluator no longer
    /// considers it.
    /// </summary>
    public void EndRetry(ServiceRetryTarget target) =>
        RetryReports.Add(ServiceRetryReport.End(target));

    private ServiceRetryReportBuffer RetryReports =>
        _retryReports ?? throw new InvalidOperationException(
            "This action writer carries no retry reports; only a service evaluation can report a target.");
}
