using OrbModding.Common.Runtime.Tracing.BufferedSegments;
using Xunit;
using static OrbModding.Tests.Runtime.Tracing.BufferedSegments.BufferedSegmentTestWait;

namespace OrbModding.Tests.Runtime.Tracing.BufferedSegments;

public sealed class BufferedSegmentSinkFailureTests
{
    [Fact]
    public void PartialFlushExhaustionDrainsEveryAcceptedBlockExactlyOnce()
    {
        using var consumer = new BufferedSegmentTestConsumer(blockWrites: true);
        using var sink = Create(consumer, recordsPerBlock: 4);
        ForStatus(sink, BufferedSegmentStatus.Running);

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(1));
        Assert.Equal(BufferedSegmentFlushResult.Flushed, sink.Flush());
        ForSignal(consumer.WriteEntered, "blocked partial write");
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(2));
        Assert.Equal(BufferedSegmentFlushResult.Flushed, sink.Flush());
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(3));

        Assert.Equal(BufferedSegmentFlushResult.FlushedAndBufferExhausted, sink.Flush());
        Assert.Equal(BufferedSegmentAppendResult.Faulting, sink.Append(4));
        var faulting = sink.Metrics();
        Assert.Equal(BufferedSegmentStatus.Faulting, faulting.Status);
        Assert.Equal(BufferedSegmentFaultReason.BufferExhausted, faulting.FaultReason);
        Assert.Equal(4, faulting.FirstIncompleteSequence);
        Assert.Equal(3, faulting.AcceptedRecords);
        Assert.Equal(3, faulting.SealedBlocks);

        consumer.WriteRelease.Set();
        ForSignal(consumer.CompletionObserved, "partial-flush exhaustion drain");
        ForStatus(sink, BufferedSegmentStatus.Faulted);

        Assert.Collection(
            consumer.Segments,
            first => Assert.Equal(new[] { 1 }, first.Records),
            second => Assert.Equal(new[] { 2 }, second.Records),
            third => Assert.Equal(new[] { 3 }, third.Records));
        var completed = sink.Metrics();
        Assert.Equal(3, completed.WrittenRecords);
        Assert.Equal(3, completed.WrittenBlocks);
        Assert.Equal(0, completed.PendingBlocks);
        Assert.False(consumer.Completion.Complete);
        Assert.Equal(BufferedSegmentFaultReason.BufferExhausted, consumer.Completion.FaultReason);
        Assert.Equal(4, consumer.Completion.FirstIncompleteSequence);
    }

    [Fact]
    public void ExhaustionIsImmediateAndAcceptedBlocksFinishAsIncomplete()
    {
        using var consumer = new BufferedSegmentTestConsumer(blockWrites: true);
        using var sink = Create(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        AppendBlock(sink, 1);
        ForSignal(consumer.WriteEntered, "writer ownership");
        AppendBlock(sink, 3);
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(5));

        Assert.Equal(BufferedSegmentAppendResult.AcceptedAndBufferExhausted, sink.Append(6));
        Assert.Equal(BufferedSegmentStatus.Faulting, sink.Metrics().Status);
        Assert.Equal(7, sink.Metrics().FirstIncompleteSequence);

        consumer.WriteRelease.Set();
        ForSignal(consumer.CompletionObserved, "incomplete drain");
        ForStatus(sink, BufferedSegmentStatus.Faulted);
        Assert.False(consumer.Completion.Complete);
        Assert.Equal(BufferedSegmentFaultReason.BufferExhausted, consumer.Completion.FaultReason);
        Assert.Equal(6, consumer.Completion.AcceptedRecords);
        Assert.Equal(6, consumer.Completion.WrittenRecords);
        Assert.Equal(0, sink.Metrics().PendingBlocks);
    }

    [Fact]
    public void WriteFailureDiscardsOwnedBlockWithoutCorruptingPendingCount()
    {
        using var consumer = new BufferedSegmentTestConsumer(failWrite: true);
        using var sink = Create(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        AppendBlock(sink, 1);

        ForSignal(consumer.CompletionObserved, "write-failure completion");
        ForStatus(sink, BufferedSegmentStatus.Faulted);
        var metrics = sink.Metrics();
        Assert.Equal(BufferedSegmentFaultReason.WriteFailed, metrics.FaultReason);
        Assert.Equal(2, metrics.AcceptedRecords);
        Assert.Equal(0, metrics.WrittenRecords);
        Assert.Equal(2, metrics.DiscardedRecords);
        Assert.Equal(1, metrics.DiscardedBlocks);
        Assert.Equal(0, metrics.PendingBlocks);
        Assert.Equal(1, metrics.FirstIncompleteSequence);
    }

    [Fact]
    public void InitializationFailureFaultsAdmissionWithoutCallingCompletion()
    {
        using var consumer = new BufferedSegmentTestConsumer(failInitialization: true);
        using var sink = Create(consumer);

        ForStatus(sink, BufferedSegmentStatus.Faulted);

        Assert.Equal(BufferedSegmentAppendResult.Faulted, sink.Append(1));
        Assert.Equal(BufferedSegmentFaultReason.InitializationFailed, sink.Metrics().FaultReason);
        Assert.False(consumer.CompletionObserved.IsSet);
    }

    [Fact]
    public void CompletionFailureCannotRelabelSuccessfullyWrittenRecords()
    {
        using var consumer = new BufferedSegmentTestConsumer(failCompletion: true);
        using var sink = Create(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(1));

        sink.Stop();

        ForSignal(consumer.CompletionObserved, "failing completion");
        ForStatus(sink, BufferedSegmentStatus.Faulted);
        var metrics = sink.Metrics();
        Assert.Equal(BufferedSegmentFaultReason.CompletionFailed, metrics.FaultReason);
        Assert.Equal(1, metrics.AcceptedRecords);
        Assert.Equal(1, metrics.WrittenRecords);
        Assert.Equal(2, metrics.FirstIncompleteSequence);
    }

    [Fact]
    public void EarlierWriteFailureReplacesLaterBufferExhaustionEvidenceAsOnePair()
    {
        using var consumer = new BufferedSegmentTestConsumer(blockWrites: true, failWrite: true);
        using var sink = Create(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        AppendBlock(sink, 1);
        ForSignal(consumer.WriteEntered, "blocked failing write");
        AppendBlock(sink, 3);
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(5));
        Assert.Equal(BufferedSegmentAppendResult.AcceptedAndBufferExhausted, sink.Append(6));
        Assert.Equal(BufferedSegmentFaultReason.BufferExhausted, sink.Metrics().FaultReason);
        Assert.Equal(7, sink.Metrics().FirstIncompleteSequence);

        consumer.WriteRelease.Set();

        ForSignal(consumer.CompletionObserved, "combined-failure completion");
        ForStatus(sink, BufferedSegmentStatus.Faulted);
        Assert.Equal(BufferedSegmentFaultReason.WriteFailed, consumer.Completion.FaultReason);
        Assert.Equal(1, consumer.Completion.FirstIncompleteSequence);
        Assert.Equal(0, consumer.Completion.WrittenRecords);
        Assert.Equal(6, sink.Metrics().DiscardedRecords);
    }

    /// <summary>
    /// The producer stopping is how a recording ends when the runtime shuts down. A 43-minute
    /// capture whose every one of 486,377 accepted records was durable still published
    /// <c>Incomplete</c> with a first-missing sequence of 486,378 — one past the last record it had
    /// written — and the next reader has to disprove that by arithmetic before trusting anything in
    /// the session.
    /// </summary>
    [Fact]
    public void AProducerStopThatLostNothingCompletesInsteadOfClaimingTruncation()
    {
        using var consumer = new BufferedSegmentTestConsumer();
        using var sink = Create(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        AppendBlock(sink, 1);
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(3));

        sink.FailProducer(BufferedSegmentFaultReason.ProducerStopped);

        ForSignal(consumer.CompletionObserved, "producer-stop completion");
        ForStatus(sink, BufferedSegmentStatus.Stopped);
        Assert.True(consumer.Completion.Complete);
        Assert.Equal(BufferedSegmentFaultReason.None, consumer.Completion.FaultReason);
        Assert.Equal(0, consumer.Completion.FirstIncompleteSequence);
        Assert.Equal(3, consumer.Completion.AcceptedRecords);
        Assert.Equal(3, consumer.Completion.WrittenRecords);
        var metrics = sink.Metrics();
        Assert.Equal(BufferedSegmentFaultReason.None, metrics.FaultReason);
        Assert.Equal(0, metrics.FirstIncompleteSequence);
    }

    /// <summary>
    /// Every accepted record being durable is not by itself completeness: a producer that faulted
    /// stopped producing records the session was meant to hold, and the counters cannot see the ones
    /// that were never accepted.
    /// </summary>
    [Fact]
    public void AProducerFaultStaysIncompleteWithEveryAcceptedRecordDurable()
    {
        using var consumer = new BufferedSegmentTestConsumer();
        using var sink = Create(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        AppendBlock(sink, 1);
        sink.FailProducer();

        ForSignal(consumer.CompletionObserved, "producer-fault completion");
        ForStatus(sink, BufferedSegmentStatus.Faulted);
        Assert.False(consumer.Completion.Complete);
        Assert.Equal(BufferedSegmentFaultReason.ProducerFailed, consumer.Completion.FaultReason);
        Assert.Equal(2, consumer.Completion.AcceptedRecords);
        Assert.Equal(2, consumer.Completion.WrittenRecords);
    }

    private static void AppendBlock(BufferedSegmentSink<int> sink, int first)
    {
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(first));
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(first + 1));
    }

    private static BufferedSegmentSink<int> Create(
        BufferedSegmentTestConsumer consumer,
        int recordsPerBlock = 2) =>
        new(consumer, new BufferedSegmentOptions(3, recordsPerBlock, "Buffered segment failure test"));
}
