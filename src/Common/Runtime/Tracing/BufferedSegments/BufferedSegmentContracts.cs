using System;

namespace OrbModding.Common.Runtime.Tracing.BufferedSegments;

internal enum BufferedSegmentStatus
{
    Initializing = 0,
    Running = 1,
    Stopping = 2,
    Faulting = 3,
    Stopped = 4,
    Faulted = 5,
}

internal enum BufferedSegmentAppendResult
{
    Accepted = 0,
    AcceptedAndBufferExhausted = 1,
    Initializing = 2,
    Stopping = 3,
    Faulting = 4,
    Stopped = 5,
    Faulted = 6,
}

internal enum BufferedSegmentFlushResult
{
    Empty = 0,
    Flushed = 1,
    FlushedAndBufferExhausted = 2,
    Initializing = 3,
    Stopping = 4,
    Faulting = 5,
    Stopped = 6,
    Faulted = 7,
}

internal enum BufferedSegmentFaultReason
{
    None = 0,
    BufferExhausted = 1,
    SequenceExhausted = 2,
    InitializationFailed = 3,
    WriteFailed = 4,
    CompletionFailed = 5,
    ProducerFailed = 6,
    ProducerStopped = 7,
}

internal readonly struct BufferedSegmentOptions
{
    internal BufferedSegmentOptions(int blockCount, int recordsPerBlock, string workerName)
    {
        if (blockCount < 3)
            throw new ArgumentOutOfRangeException(nameof(blockCount), "A buffered sink requires at least three blocks.");
        if (recordsPerBlock <= 0)
            throw new ArgumentOutOfRangeException(nameof(recordsPerBlock));
        if (string.IsNullOrWhiteSpace(workerName))
            throw new ArgumentException("A stable worker name is required.", nameof(workerName));
        _ = checked(blockCount * recordsPerBlock);
        BlockCount = blockCount;
        RecordsPerBlock = recordsPerBlock;
        WorkerName = workerName;
    }

    internal int BlockCount { get; }
    internal int RecordsPerBlock { get; }
    internal string WorkerName { get; }
}

internal readonly struct BufferedSegmentCompletion
{
    internal BufferedSegmentCompletion(
        bool complete,
        BufferedSegmentFaultReason faultReason,
        long acceptedRecords,
        long writtenRecords,
        long firstIncompleteSequence)
    {
        if (complete != (faultReason == BufferedSegmentFaultReason.None))
            throw new ArgumentException("Completion and fault reason disagree.", nameof(faultReason));
        if (acceptedRecords < 0 || writtenRecords < 0 || writtenRecords > acceptedRecords)
            throw new ArgumentOutOfRangeException(nameof(acceptedRecords));
        if (complete && firstIncompleteSequence != 0)
            throw new ArgumentOutOfRangeException(nameof(firstIncompleteSequence));
        if (!complete && firstIncompleteSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(firstIncompleteSequence));
        Complete = complete;
        FaultReason = faultReason;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        FirstIncompleteSequence = firstIncompleteSequence;
    }

    internal bool Complete { get; }
    internal BufferedSegmentFaultReason FaultReason { get; }
    internal long AcceptedRecords { get; }
    internal long WrittenRecords { get; }
    internal long FirstIncompleteSequence { get; }
}

internal readonly struct BufferedSegmentMetrics
{
    internal BufferedSegmentMetrics(
        BufferedSegmentStatus status,
        BufferedSegmentFaultReason faultReason,
        long acceptedRecords,
        long writtenRecords,
        long discardedRecords,
        long bytesWritten,
        long sealedBlocks,
        long writtenBlocks,
        long discardedBlocks,
        int pendingBlocks,
        int peakPendingBlocks,
        long firstIncompleteSequence)
    {
        Status = status;
        FaultReason = faultReason;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        DiscardedRecords = discardedRecords;
        BytesWritten = bytesWritten;
        SealedBlocks = sealedBlocks;
        WrittenBlocks = writtenBlocks;
        DiscardedBlocks = discardedBlocks;
        PendingBlocks = pendingBlocks;
        PeakPendingBlocks = peakPendingBlocks;
        FirstIncompleteSequence = firstIncompleteSequence;
    }

    internal BufferedSegmentStatus Status { get; }
    internal BufferedSegmentFaultReason FaultReason { get; }
    internal long AcceptedRecords { get; }
    internal long WrittenRecords { get; }
    internal long DiscardedRecords { get; }
    internal long BytesWritten { get; }
    internal long SealedBlocks { get; }
    internal long WrittenBlocks { get; }
    internal long DiscardedBlocks { get; }
    internal int PendingBlocks { get; }
    internal int PeakPendingBlocks { get; }
    internal long FirstIncompleteSequence { get; }
}

internal interface IBufferedSegmentConsumer<TRecord> where TRecord : struct
{
    void Initialize();

    int Write(long blockOrdinal, long firstRecordSequence, ReadOnlySpan<TRecord> records);

    void Complete(in BufferedSegmentCompletion completion);
}

internal static class BufferedSegmentFailurePolicy
{
    internal static bool IsProcessFatal(Exception exception) =>
        exception is StackOverflowException or OutOfMemoryException or AccessViolationException;
}

internal static class BufferedSegmentShutdown
{
    /// <summary>
    /// How long a teardown waits for one writer to finish the drain it has already been asked for.
    /// </summary>
    /// <remarks>
    /// The writer thread is a background thread, so process exit kills it wherever it stands. A stop
    /// that only signals therefore leaves the last segments and the whole manifest to a race the
    /// drain loses whenever the host quits promptly — and the profiling session hands its entire
    /// payload over at exactly that moment. Bounded rather than an open join, because a writer wedged
    /// in a storage call must never hold the host's quit; background remains the last-resort net
    /// behind this wait. Two seconds is the bound the suite's own HTTP shutdown already uses, and a
    /// healthy drain returns long before it.
    /// </remarks>
    internal static readonly TimeSpan DrainBound = TimeSpan.FromSeconds(2);
}
