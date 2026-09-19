using System;
using System.Threading;

namespace OrbModding.Common.Runtime.Tracing.BufferedSegments;

internal sealed class BufferedSegmentWriter<TRecord> where TRecord : struct
{
    private readonly BufferedSegmentSessionState<TRecord> _state;
    private readonly IBufferedSegmentConsumer<TRecord> _consumer;
    private readonly BufferedSegmentOptions _options;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private bool _consumerInitialized;
    private bool _completionAttempted;

    internal BufferedSegmentWriter(
        BufferedSegmentSessionState<TRecord> state,
        IBufferedSegmentConsumer<TRecord> consumer,
        in BufferedSegmentOptions options)
    {
        _state = state;
        _consumer = consumer;
        _options = options;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = options.WorkerName,
            Priority = ThreadPriority.Lowest,
        };
        _thread.Start();
    }

    internal void Signal() => _wake.Set();

    internal bool WaitForExit(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout), "A finite bounded timeout is required.");
        return _thread.Join(timeout);
    }

    private void Run()
    {
        ReusableSegmentBlockLane<TRecord>? lane = null;
        try
        {
            lane = new ReusableSegmentBlockLane<TRecord>(
                _options.BlockCount,
                _options.RecordsPerBlock);
            _consumer.Initialize();
            _consumerInitialized = true;
            if (!_state.TryPublishRunning(lane))
            {
                FinishStopping(lane);
                return;
            }

            while (true)
            {
                if (!DrainReady(lane)) return;
                var status = _state.Status;
                if (status == BufferedSegmentStatus.Running)
                {
                    _wake.WaitOne();
                    continue;
                }
                if (status == BufferedSegmentStatus.Stopping)
                {
                    FinishStopping(lane);
                    return;
                }
                if (status == BufferedSegmentStatus.Faulting)
                {
                    FinishFaulted(lane);
                    return;
                }
                return;
            }
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            var reason = _consumerInitialized
                ? BufferedSegmentFaultReason.WriteFailed
                : BufferedSegmentFaultReason.InitializationFailed;
            _state.LatchFault(reason, _state.Metrics().WrittenRecords + 1);
            _state.CloseAdmissionAndWait();
            if (lane is not null) Discard(lane);
            TryComplete(complete: false);
            _state.PublishFaulted();
        }
        finally
        {
            _state.CloseAdmissionAndWait();
            _wake.Dispose();
        }
    }

    private bool DrainReady(ReusableSegmentBlockLane<TRecord> lane)
    {
        while (lane.TryTakeNextReady(out var candidate))
        {
            var block = candidate!;
            try
            {
                var bytes = _consumer.Write(
                    block.Ordinal,
                    block.FirstRecordSequence,
                    block.Records.AsSpan(0, block.Count));
                if (bytes < 0)
                    throw new InvalidOperationException("A buffered segment consumer returned a negative byte count.");
                var count = block.Count;
                lane.ReleaseWritten(block);
                _state.RecordWritten(count, bytes);
            }
            catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
            {
                _state.LatchFault(BufferedSegmentFaultReason.WriteFailed, block.FirstRecordSequence);
                _state.CloseAdmissionAndWait();
                Discard(lane);
                TryComplete(complete: false);
                _state.PublishFaulted();
                return false;
            }
        }
        return true;
    }

    private void FinishStopping(ReusableSegmentBlockLane<TRecord> lane)
    {
        _state.CloseAdmissionAndWait();
        if (!DrainReady(lane)) return;
        if (!TryComplete(complete: true)) return;
        _state.PublishStopped();
    }

    private void FinishFaulted(ReusableSegmentBlockLane<TRecord> lane)
    {
        _state.CloseAdmissionAndWait();
        if (_state.FaultReason is BufferedSegmentFaultReason.BufferExhausted or
            BufferedSegmentFaultReason.SequenceExhausted or
            BufferedSegmentFaultReason.ProducerFailed or
            BufferedSegmentFaultReason.ProducerStopped)
        {
            if (!DrainReady(lane)) return;
        }
        Discard(lane);
        if (!TryComplete(complete: false)) return;
        if (_state.TryPublishStoppedWithoutLoss()) return;
        _state.PublishFaulted();
    }

    private bool TryComplete(bool complete)
    {
        if (!_consumerInitialized || _completionAttempted) return true;
        _completionAttempted = true;
        try
        {
            var completion = _state.Completion(complete);
            _consumer.Complete(in completion);
            return true;
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            var firstUnaccepted = _state.Metrics().WrittenRecords + 1;
            _state.LatchFault(BufferedSegmentFaultReason.CompletionFailed, firstUnaccepted);
            _state.PublishFaulted();
            return false;
        }
    }

    private void Discard(ReusableSegmentBlockLane<TRecord> lane)
    {
        lane.DiscardAll(out var blocks, out var pendingBlocks, out var records);
        if (blocks != 0 || pendingBlocks != 0)
            _state.RecordDiscarded(blocks, pendingBlocks, records);
    }
}
