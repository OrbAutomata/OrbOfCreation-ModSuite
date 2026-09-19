using System.Threading;

namespace OrbModding.Common.Runtime.Tracing.BufferedSegments;

internal sealed class BufferedSegmentSessionState<TRecord> where TRecord : struct
{
    private int _status = (int)BufferedSegmentStatus.Initializing;
    private int _admissionClosed;
    private int _ownerOperationActive;
    private long _acceptedRecords;
    private long _writtenRecords;
    private long _discardedRecords;
    private long _bytesWritten;
    private long _sealedBlocks;
    private long _writtenBlocks;
    private long _discardedBlocks;
    private int _pendingBlocks;
    private int _peakPendingBlocks;
    private ReusableSegmentBlockLane<TRecord>? _lane;
    private BufferedSegmentFault? _fault;

    internal BufferedSegmentStatus Status =>
        (BufferedSegmentStatus)Volatile.Read(ref _status);

    internal BufferedSegmentFaultReason FaultReason =>
        Volatile.Read(ref _fault)?.Reason ?? BufferedSegmentFaultReason.None;

    internal ReusableSegmentBlockLane<TRecord>? Lane => Volatile.Read(ref _lane);

    internal bool TryPublishRunning(ReusableSegmentBlockLane<TRecord> lane)
    {
        Volatile.Write(ref _lane, lane);
        return Interlocked.CompareExchange(
            ref _status,
            (int)BufferedSegmentStatus.Running,
            (int)BufferedSegmentStatus.Initializing) == (int)BufferedSegmentStatus.Initializing;
    }

    internal bool TryBeginOwnerOperation()
    {
        if (Volatile.Read(ref _admissionClosed) != 0) return false;
        if (Interlocked.CompareExchange(ref _ownerOperationActive, 1, 0) != 0) return false;
        if (Volatile.Read(ref _admissionClosed) == 0) return true;
        Volatile.Write(ref _ownerOperationActive, 0);
        return false;
    }

    internal void EndOwnerOperation() => Volatile.Write(ref _ownerOperationActive, 0);

    internal void CloseAdmissionAndWait()
    {
        Volatile.Write(ref _admissionClosed, 1);
        var spinner = new SpinWait();
        while (Volatile.Read(ref _ownerOperationActive) != 0) spinner.SpinOnce();
    }

    internal bool TryRequestStop()
    {
        while (true)
        {
            var status = Status;
            if (status is not (BufferedSegmentStatus.Initializing or BufferedSegmentStatus.Running))
                return false;
            if (Interlocked.CompareExchange(
                    ref _status,
                    (int)BufferedSegmentStatus.Stopping,
                    (int)status) == (int)status)
                return true;
        }
    }

    internal void PublishStopped() =>
        Interlocked.CompareExchange(
            ref _status,
            (int)BufferedSegmentStatus.Stopped,
            (int)BufferedSegmentStatus.Stopping);

    internal void LatchFault(BufferedSegmentFaultReason reason, long firstIncompleteSequence)
    {
        if (reason == BufferedSegmentFaultReason.None)
            throw new System.ArgumentOutOfRangeException(nameof(reason));
        if (firstIncompleteSequence <= 0)
            firstIncompleteSequence = Interlocked.Read(ref _writtenRecords) + 1;
        var candidate = new BufferedSegmentFault(reason, firstIncompleteSequence);
        while (true)
        {
            var current = Volatile.Read(ref _fault);
            if (current is not null && current.FirstIncompleteSequence <= firstIncompleteSequence)
                break;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _fault, candidate, current), current))
                break;
        }
        Interlocked.Exchange(ref _status, (int)BufferedSegmentStatus.Faulting);
    }

    internal void PublishFaulted() =>
        Interlocked.CompareExchange(
            ref _status,
            (int)BufferedSegmentStatus.Faulted,
            (int)BufferedSegmentStatus.Faulting);

    /// <summary>
    /// Ends a producer's own stop as a stop rather than a fault when every accepted record reached
    /// storage, dropping the fault it latched so status, metrics, and the manifest agree.
    /// </summary>
    internal bool TryPublishStoppedWithoutLoss()
    {
        if (!StoppedWithoutLoss()) return false;
        Volatile.Write(ref _fault, null);
        Interlocked.Exchange(ref _status, (int)BufferedSegmentStatus.Stopped);
        return true;
    }

    internal void RecordAccepted() => Interlocked.Increment(ref _acceptedRecords);

    internal void RecordSealed()
    {
        Interlocked.Increment(ref _sealedBlocks);
        var pending = Interlocked.Increment(ref _pendingBlocks);
        var peak = Volatile.Read(ref _peakPendingBlocks);
        while (pending > peak)
        {
            var observed = Interlocked.CompareExchange(ref _peakPendingBlocks, pending, peak);
            if (observed == peak) break;
            peak = observed;
        }
    }

    internal void RecordWritten(int records, int bytes)
    {
        Interlocked.Add(ref _writtenRecords, records);
        Interlocked.Add(ref _bytesWritten, bytes);
        Interlocked.Increment(ref _writtenBlocks);
        Interlocked.Decrement(ref _pendingBlocks);
    }

    internal void RecordDiscarded(int blocks, int pendingBlocks, long records)
    {
        Interlocked.Add(ref _discardedBlocks, blocks);
        Interlocked.Add(ref _discardedRecords, records);
        Interlocked.Add(ref _pendingBlocks, -pendingBlocks);
    }

    internal BufferedSegmentCompletion Completion(bool complete)
    {
        var fault = complete ? null : Volatile.Read(ref _fault);
        if (fault is not null && LostNothing(fault))
        {
            complete = true;
            fault = null;
        }
        return new BufferedSegmentCompletion(
            complete,
            fault?.Reason ?? BufferedSegmentFaultReason.None,
            Interlocked.Read(ref _acceptedRecords),
            Interlocked.Read(ref _writtenRecords),
            fault?.FirstIncompleteSequence ?? 0);
    }

    /// <summary>
    /// Whether the latched fault is the producer's own stop and nothing was lost to it.
    /// </summary>
    /// <remarks>
    /// A producer that stops because the runtime is going away seals and publishes its partial block
    /// before latching, so the drain that follows makes every accepted record durable. Reporting that
    /// session as truncated cost a 43-minute capture its credibility: the manifest read
    /// <c>Incomplete</c> with a first-missing sequence one past the last record it had actually
    /// written, which the next reader has to disprove by arithmetic before trusting a single number.
    /// The reason is checked as well as the counts, because a session that exhausted its buffers or
    /// its sequence space also ends with everything it accepted on disk — and there the equal counts
    /// mean the sink started refusing records, which is exactly the truncation this word exists to
    /// report.
    /// </remarks>
    private bool LostNothing(BufferedSegmentFault fault) =>
        fault.Reason == BufferedSegmentFaultReason.ProducerStopped &&
        Interlocked.Read(ref _acceptedRecords) == Interlocked.Read(ref _writtenRecords);

    internal bool StoppedWithoutLoss()
    {
        var fault = Volatile.Read(ref _fault);
        return fault is not null && LostNothing(fault);
    }

    internal BufferedSegmentMetrics Metrics()
    {
        var status = Status;
        var fault = status is BufferedSegmentStatus.Faulting or BufferedSegmentStatus.Faulted
            ? Volatile.Read(ref _fault)
            : null;
        var writtenBlocks = Interlocked.Read(ref _writtenBlocks);
        var writtenRecords = Interlocked.Read(ref _writtenRecords);
        return new BufferedSegmentMetrics(
            status,
            fault?.Reason ?? BufferedSegmentFaultReason.None,
            Interlocked.Read(ref _acceptedRecords),
            writtenRecords,
            Interlocked.Read(ref _discardedRecords),
            Interlocked.Read(ref _bytesWritten),
            Interlocked.Read(ref _sealedBlocks),
            writtenBlocks,
            Interlocked.Read(ref _discardedBlocks),
            Volatile.Read(ref _pendingBlocks),
            Volatile.Read(ref _peakPendingBlocks),
            fault?.FirstIncompleteSequence ?? 0);
    }

    private sealed class BufferedSegmentFault
    {
        internal BufferedSegmentFault(
            BufferedSegmentFaultReason reason,
            long firstIncompleteSequence)
        {
            Reason = reason;
            FirstIncompleteSequence = firstIncompleteSequence;
        }

        internal BufferedSegmentFaultReason Reason { get; }
        internal long FirstIncompleteSequence { get; }
    }
}
