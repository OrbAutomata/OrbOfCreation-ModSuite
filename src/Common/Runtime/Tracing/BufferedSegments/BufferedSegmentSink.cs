using System;

namespace OrbModding.Common.Runtime.Tracing.BufferedSegments;

internal sealed class BufferedSegmentSink<TRecord> : IDisposable where TRecord : struct
{
    private readonly BufferedSegmentSessionState<TRecord> _state = new();
    private readonly BufferedSegmentWriter<TRecord> _writer;
    private readonly int _ownerThreadId;

    internal BufferedSegmentSink(
        IBufferedSegmentConsumer<TRecord> consumer,
        in BufferedSegmentOptions options)
    {
        if (consumer is null) throw new ArgumentNullException(nameof(consumer));
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _writer = new BufferedSegmentWriter<TRecord>(_state, consumer, in options);
    }

    internal BufferedSegmentAppendResult Append(in TRecord record)
    {
        EnsureOwner();
        if (!_state.TryBeginOwnerOperation()) return ResultFor(_state.Status);
        try
        {
            if (_state.Status != BufferedSegmentStatus.Running)
                return ResultFor(_state.Status);
            var lane = _state.Lane ??
                throw new InvalidOperationException("A running buffered sink has no producer lane.");
            var result = lane.Append(in record, out var preparedRecordCount);
            if (result == SegmentLaneAppendResult.Accepted)
            {
                _state.RecordAccepted();
                if (preparedRecordCount != 0)
                {
                    _state.RecordSealed();
                    var claimedNextBlock = lane.PublishPreparedBlock(claimNextProducerBlock: true);
                    if (!claimedNextBlock)
                    {
                        _state.LatchFault(
                            BufferedSegmentFaultReason.BufferExhausted,
                            lane.NextRecordSequence);
                        _writer.Signal();
                        return BufferedSegmentAppendResult.AcceptedAndBufferExhausted;
                    }
                    _writer.Signal();
                }
                return BufferedSegmentAppendResult.Accepted;
            }

            if (preparedRecordCount != 0)
            {
                _state.RecordSealed();
                lane.PublishPreparedBlock(claimNextProducerBlock: false);
            }
            _state.LatchFault(
                BufferedSegmentFaultReason.SequenceExhausted,
                lane.NextRecordSequence);
            _writer.Signal();
            return BufferedSegmentAppendResult.Faulted;
        }
        finally { _state.EndOwnerOperation(); }
    }

    internal BufferedSegmentMetrics Metrics() => _state.Metrics();

    internal BufferedSegmentFlushResult Flush()
    {
        EnsureOwner();
        if (!_state.TryBeginOwnerOperation()) return FlushResultFor(_state.Status);
        try
        {
            if (_state.Status != BufferedSegmentStatus.Running)
                return FlushResultFor(_state.Status);
            var lane = _state.Lane ??
                throw new InvalidOperationException("A running buffered sink has no producer lane.");
            if (!lane.HasProducerRecords) return BufferedSegmentFlushResult.Empty;

            var result = lane.PreparePartialBlock(out var preparedRecordCount);
            if (result != SegmentLaneAppendResult.Accepted)
            {
                _state.LatchFault(
                    BufferedSegmentFaultReason.SequenceExhausted,
                    lane.NextRecordSequence);
                _writer.Signal();
                return BufferedSegmentFlushResult.Faulted;
            }

            _state.RecordSealed();
            var claimedNextBlock = lane.PublishPreparedBlock(claimNextProducerBlock: true);
            if (!claimedNextBlock)
            {
                _state.LatchFault(
                    BufferedSegmentFaultReason.BufferExhausted,
                    lane.NextRecordSequence);
                _writer.Signal();
                return BufferedSegmentFlushResult.FlushedAndBufferExhausted;
            }
            _writer.Signal();
            return BufferedSegmentFlushResult.Flushed;
        }
        finally { _state.EndOwnerOperation(); }
    }

    internal void FailProducer(BufferedSegmentFaultReason reason = BufferedSegmentFaultReason.ProducerFailed)
    {
        EnsureOwner();
        if (reason is not (BufferedSegmentFaultReason.ProducerFailed or BufferedSegmentFaultReason.ProducerStopped))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (!_state.TryBeginOwnerOperation()) return;
        try
        {
            if (_state.Status != BufferedSegmentStatus.Running) return;
            var lane = _state.Lane ??
                throw new InvalidOperationException("A running buffered sink has no producer lane.");
            var result = lane.PreparePartialBlock(out var preparedRecordCount);
            if (result != SegmentLaneAppendResult.Accepted)
            {
                _state.LatchFault(BufferedSegmentFaultReason.SequenceExhausted, lane.NextRecordSequence);
                _writer.Signal();
                return;
            }
            if (preparedRecordCount != 0)
            {
                _state.RecordSealed();
                lane.PublishPreparedBlock(claimNextProducerBlock: false);
            }
            _state.LatchFault(reason, lane.NextRecordSequence);
            _writer.Signal();
        }
        finally { _state.EndOwnerOperation(); }
    }

    internal void Stop()
    {
        EnsureOwner();
        if (!_state.TryBeginOwnerOperation()) return;
        try
        {
            var wasRunning = _state.Status == BufferedSegmentStatus.Running;
            if (!_state.TryRequestStop()) return;
            if (wasRunning)
            {
                var lane = _state.Lane ??
                    throw new InvalidOperationException("A running buffered sink has no producer lane.");
                var result = lane.PreparePartialBlock(out var preparedRecordCount);
                if (result != SegmentLaneAppendResult.Accepted)
                {
                    _state.LatchFault(BufferedSegmentFaultReason.SequenceExhausted, lane.NextRecordSequence);
                }
                else if (preparedRecordCount != 0)
                {
                    _state.RecordSealed();
                    lane.PublishPreparedBlock(claimNextProducerBlock: false);
                }
            }
            _writer.Signal();
        }
        finally { _state.EndOwnerOperation(); }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Waits, bounded, for the writer to finish a drain that has already been asked for, and answers
    /// whether it finished.
    /// </summary>
    /// <remarks>
    /// A sink still admitting records has no drain to finish, and a teardown that blocked on one
    /// would stall for the whole bound over a stop its caller never issued; that answers false
    /// immediately instead, and every caller reports a false. Nothing here writes: whatever the
    /// writer publishes it publishes on its own thread, so a wait that expires leaves exactly what an
    /// interrupted session leaves and never a manifest this thread invented.
    /// </remarks>
    internal bool WaitForWriterExit(TimeSpan timeout)
    {
        EnsureOwner();
        if (_state.Status is BufferedSegmentStatus.Initializing or BufferedSegmentStatus.Running)
            return false;
        return _writer.WaitForExit(timeout);
    }

    private static BufferedSegmentAppendResult ResultFor(BufferedSegmentStatus status) => status switch
    {
        BufferedSegmentStatus.Initializing => BufferedSegmentAppendResult.Initializing,
        BufferedSegmentStatus.Stopping => BufferedSegmentAppendResult.Stopping,
        BufferedSegmentStatus.Faulting => BufferedSegmentAppendResult.Faulting,
        BufferedSegmentStatus.Stopped => BufferedSegmentAppendResult.Stopped,
        BufferedSegmentStatus.Faulted => BufferedSegmentAppendResult.Faulted,
        _ => throw new InvalidOperationException("The buffered segment sink has an invalid lifecycle state."),
    };

    private static BufferedSegmentFlushResult FlushResultFor(BufferedSegmentStatus status) => status switch
    {
        BufferedSegmentStatus.Initializing => BufferedSegmentFlushResult.Initializing,
        BufferedSegmentStatus.Stopping => BufferedSegmentFlushResult.Stopping,
        BufferedSegmentStatus.Faulting => BufferedSegmentFlushResult.Faulting,
        BufferedSegmentStatus.Stopped => BufferedSegmentFlushResult.Stopped,
        BufferedSegmentStatus.Faulted => BufferedSegmentFlushResult.Faulted,
        _ => throw new InvalidOperationException("The buffered segment sink has an invalid lifecycle state."),
    };

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Buffered segment production is owner-thread affine.");
    }
}
