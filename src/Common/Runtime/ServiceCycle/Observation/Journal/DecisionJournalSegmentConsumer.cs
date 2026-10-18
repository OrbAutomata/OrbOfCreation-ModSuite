using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed class DecisionJournalSegmentConsumer : IBufferedSegmentConsumer<DecisionJournalRecord>
{
    private readonly IRestartAwareTraceSegmentStorage _storage;
    private readonly DecisionJournalRunId _run;
    private readonly int _maximumCommittedSegments;
    private byte[]? _encodingBuffer;
    private long _nextTransportOrdinal;
    private int _nextStorageOrdinal;
    private ulong _nextRecordSequence = 1;
    private long _writtenRecords;
    private int _retainedSegments;
    private int _startupPrunedSegments;
    private int _incompatibleSegmentsPruned;
    private int _staleTemporaryFilesRemoved;
    private long _evictedSegments;
    private int _faultReason;
    private Exception? _faultException;

    internal DecisionJournalSegmentConsumer(
        IRestartAwareTraceSegmentStorage storage,
        DecisionJournalRunId run,
        int maximumCommittedSegments)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        if (!run.IsValid) throw new ArgumentException("A valid journal run is required.", nameof(run));
        if (maximumCommittedSegments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCommittedSegments));
        _run = run;
        _maximumCommittedSegments = maximumCommittedSegments;
    }

    internal DecisionJournalConsumerMetrics Metrics => new(
        Volatile.Read(ref _retainedSegments),
        Volatile.Read(ref _startupPrunedSegments),
        Volatile.Read(ref _incompatibleSegmentsPruned),
        Volatile.Read(ref _staleTemporaryFilesRemoved),
        Interlocked.Read(ref _evictedSegments),
        (DecisionJournalConsumerFaultReason)Volatile.Read(ref _faultReason),
        Volatile.Read(ref _faultException));

    /// <summary>
    /// Recovers the store, and hands storage the journal's own test for continuing it.
    /// </summary>
    /// <remarks>
    /// A store a newer or older build wrote cannot take this build's segments after it, and a
    /// startup that only refused left the journal permanently dead on that machine. Storage
    /// abandons what it cannot continue and reports the count, which is loud but recoverable.
    /// </remarks>
    public void Initialize()
    {
        _encodingBuffer = new byte[
            DecisionJournalSegmentCodec.GetEncodedLength(DecisionJournalSegmentCodec.MaximumRecords)];
        var recovery = _storage.Reconcile(
            _maximumCommittedSegments,
            DecisionJournalSegmentHeaderProbe.Instance);
        _nextStorageOrdinal = recovery.NextOrdinal;
        Volatile.Write(ref _retainedSegments, recovery.RetainedSegments);
        Volatile.Write(ref _startupPrunedSegments, recovery.StartupPrunedSegments);
        Volatile.Write(ref _incompatibleSegmentsPruned, recovery.IncompatibleSegmentsPruned);
        Volatile.Write(ref _staleTemporaryFilesRemoved, recovery.StaleTemporaryFilesRemoved);
    }

    public int Write(
        long blockOrdinal,
        long firstRecordSequence,
        ReadOnlySpan<DecisionJournalRecord> records)
    {
        var buffer = _encodingBuffer ??
            throw new InvalidOperationException("The decision-journal consumer is not initialized.");
        if (Volatile.Read(ref _faultReason) != 0)
            throw new InvalidOperationException("The decision-journal consumer cannot accept another segment.");
        if (blockOrdinal < 0 || blockOrdinal != _nextTransportOrdinal)
            throw new InvalidOperationException("Decision-journal segment ordinals must be dense.");
        if (firstRecordSequence <= 0 || checked((ulong)firstRecordSequence) != _nextRecordSequence)
            throw new InvalidOperationException("Decision-journal records must be contiguous across segments.");
        if (records.Length is <= 0 or > DecisionJournalSegmentCodec.MaximumRecords)
            throw new InvalidOperationException("A decision-journal segment has an invalid record count.");

        var length = DecisionJournalSegmentCodec.Encode(
            _run,
            checked((ulong)_nextStorageOrdinal),
            _nextRecordSequence,
            records,
            buffer);
        object? segment = null;
        try
        {
            segment = _storage.BeginSegment(_nextStorageOrdinal);
            _storage.Append(segment, buffer.AsSpan(0, length));
            _storage.CommitSegment(segment);
            segment = null;
        }
        catch
        {
            if (segment is not null) _storage.DiscardSegment(segment);
            throw;
        }

        _nextTransportOrdinal++;
        _nextRecordSequence = checked(_nextRecordSequence + (ulong)records.Length);
        _writtenRecords = checked(_writtenRecords + records.Length);
        var retained = checked(Volatile.Read(ref _retainedSegments) + 1);
        Volatile.Write(ref _retainedSegments, retained);
        if (retained > _maximumCommittedSegments)
        {
            try
            {
                _storage.DeleteOldestCommitted();
                Volatile.Write(ref _retainedSegments, retained - 1);
                Interlocked.Increment(ref _evictedSegments);
            }
            catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
            {
                Interlocked.CompareExchange(ref _faultException, exception, null);
                Interlocked.CompareExchange(
                    ref _faultReason,
                    (int)DecisionJournalConsumerFaultReason.RetentionFailed,
                    (int)DecisionJournalConsumerFaultReason.None);
            }
        }
        if (_nextStorageOrdinal == int.MaxValue)
        {
            Interlocked.CompareExchange(
                ref _faultReason,
                (int)DecisionJournalConsumerFaultReason.OrdinalExhausted,
                (int)DecisionJournalConsumerFaultReason.None);
        }
        else
        {
            _nextStorageOrdinal++;
        }
        return length;
    }

    public void Complete(in BufferedSegmentCompletion completion)
    {
        if (completion.WrittenRecords != _writtenRecords)
            throw new InvalidOperationException("Transport and decision-journal durable counts disagree.");
        if (Volatile.Read(ref _faultReason) != 0)
            throw new InvalidOperationException("Decision-journal storage stopped after a durable commit.");
    }
}

internal enum DecisionJournalConsumerFaultReason
{
    None = 0,
    RetentionFailed = 1,
    OrdinalExhausted = 2,
}

internal readonly struct DecisionJournalConsumerMetrics
{
    internal DecisionJournalConsumerMetrics(
        int retainedSegments,
        int startupPrunedSegments,
        int incompatibleSegmentsPruned,
        int staleTemporaryFilesRemoved,
        long evictedSegments,
        DecisionJournalConsumerFaultReason faultReason,
        Exception? faultException = null)
    {
        RetainedSegments = retainedSegments;
        StartupPrunedSegments = startupPrunedSegments;
        IncompatibleSegmentsPruned = incompatibleSegmentsPruned;
        StaleTemporaryFilesRemoved = staleTemporaryFilesRemoved;
        EvictedSegments = evictedSegments;
        FaultReason = faultReason;
        FaultException = faultException;
    }

    internal int RetainedSegments { get; }
    internal int StartupPrunedSegments { get; }
    internal int IncompatibleSegmentsPruned { get; }
    internal int StaleTemporaryFilesRemoved { get; }
    internal long EvictedSegments { get; }
    internal DecisionJournalConsumerFaultReason FaultReason { get; }

    /// <summary>What storage threw when the oldest segment would not delete.</summary>
    internal Exception? FaultException { get; }
    internal bool CannotContinue => FaultReason != DecisionJournalConsumerFaultReason.None;
}
