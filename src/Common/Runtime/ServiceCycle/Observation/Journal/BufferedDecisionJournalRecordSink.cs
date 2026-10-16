using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed class BufferedDecisionJournalRecordSink : IDecisionJournalRecordSink, IDisposable
{
    private readonly DecisionJournalSegmentConsumer _consumer;
    private readonly BufferedSegmentSink<DecisionJournalRecord> _sink;

    internal BufferedDecisionJournalRecordSink(
        IRestartAwareTraceSegmentStorage storage,
        DecisionJournalRunId run,
        int maximumCommittedSegments,
        int blockCount)
    {
        if (blockCount < 3) throw new ArgumentOutOfRangeException(nameof(blockCount));
        _consumer = new DecisionJournalSegmentConsumer(
            storage,
            run,
            maximumCommittedSegments);
        _sink = new BufferedSegmentSink<DecisionJournalRecord>(
            _consumer,
            new BufferedSegmentOptions(
                blockCount,
                DecisionJournalSegmentCodec.MaximumRecords,
                "ServiceCycle decision journal writer"));
    }

    internal BufferedSegmentMetrics TransportMetrics => _sink.Metrics();
    internal DecisionJournalConsumerMetrics ConsumerMetrics => _consumer.Metrics;

    public bool TryAppend(in DecisionJournalRecord record)
    {
        if (!CanContinue()) return false;
        return _sink.Append(in record) == BufferedSegmentAppendResult.Accepted;
    }

    public bool TryFlush()
    {
        if (!CanContinue()) return false;
        var result = _sink.Flush();
        return result is BufferedSegmentFlushResult.Empty or BufferedSegmentFlushResult.Flushed;
    }

    public void Stop()
    {
        if (!CanContinue()) return;
        _sink.Stop();
    }

    internal void FailProducer() => _sink.FailProducer();

    internal bool WaitForDrain(TimeSpan timeout) => _sink.WaitForWriterExit(timeout);

    public void Dispose() => Stop();

    private bool CanContinue()
    {
        if (!_consumer.Metrics.CannotContinue) return true;
        if (_sink.Metrics().Status == BufferedSegmentStatus.Running)
            _sink.FailProducer();
        return false;
    }
}
