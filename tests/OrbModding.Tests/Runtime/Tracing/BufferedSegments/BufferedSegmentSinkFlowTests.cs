using System;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;
using Xunit;
using static OrbModding.Tests.Runtime.Tracing.BufferedSegments.BufferedSegmentTestWait;

namespace OrbModding.Tests.Runtime.Tracing.BufferedSegments;

public sealed class BufferedSegmentSinkFlowTests
{
    [Fact]
    public void FlushPublishesPartialBlockAndProductionContinues()
    {
        using var consumer = new BufferedSegmentTestConsumer();
        using var sink = Create(consumer, recordsPerBlock: 3);
        ForStatus(sink, BufferedSegmentStatus.Running);

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(41));
        Assert.Equal(BufferedSegmentFlushResult.Flushed, sink.Flush());
        ForWrittenBlocks(sink, 1);
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(42));
        sink.Stop();
        ForSignal(consumer.CompletionObserved, "completion after explicit flush");

        Assert.Collection(
            consumer.Segments,
            first => Assert.Equal(new[] { 41 }, first.Records),
            second => Assert.Equal(new[] { 42 }, second.Records));
    }

    [Fact]
    public void EmptyFlushLeavesProducerBlockWritable()
    {
        using var consumer = new BufferedSegmentTestConsumer();
        using var sink = Create(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        Assert.Equal(BufferedSegmentFlushResult.Empty, sink.Flush());
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(7));
        sink.Stop();
        ForSignal(consumer.CompletionObserved, "completion after empty flush");

        Assert.Equal(new[] { 7 }, Assert.Single(consumer.Segments).Records);
    }

    [Fact]
    public void StopSealsPartialBlockAndCompletesAfterDurableWrite()
    {
        var ownerThread = Environment.CurrentManagedThreadId;
        using var consumer = new BufferedSegmentTestConsumer();
        using var sink = Create(consumer, recordsPerBlock: 3);
        ForStatus(sink, BufferedSegmentStatus.Running);

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(41));
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(42));

        sink.Stop();
        ForSignal(consumer.CompletionObserved, "session completion");
        ForStatus(sink, BufferedSegmentStatus.Stopped);

        var segment = Assert.Single(consumer.Segments);
        Assert.Equal(0, segment.Ordinal);
        Assert.Equal(1, segment.FirstRecordSequence);
        Assert.Equal(new[] { 41, 42 }, segment.Records);
        Assert.NotEqual(ownerThread, segment.ThreadId);
        Assert.True(consumer.Completion.Complete);
        Assert.Equal(2, consumer.Completion.WrittenRecords);
        Assert.Equal(8, sink.Metrics().BytesWritten);
    }

    [Fact]
    public void CoalescedSignalsStillDrainEveryReadyBlockInOrder()
    {
        using var consumer = new BufferedSegmentTestConsumer();
        using var sink = Create(consumer, blockCount: 5, recordsPerBlock: 2);
        ForStatus(sink, BufferedSegmentStatus.Running);

        for (var value = 1; value <= 8; value++)
            Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(value));
        sink.Stop();

        ForSignal(consumer.CompletionObserved, "ordered drain");
        var segments = consumer.Segments;
        Assert.Equal(4, segments.Count);
        for (var index = 0; index < segments.Count; index++)
        {
            Assert.Equal(index, segments[index].Ordinal);
            Assert.Equal(index * 2 + 1, segments[index].FirstRecordSequence);
            Assert.Equal(
                new[] { index * 2 + 1, index * 2 + 2 },
                segments[index].Records);
        }
        Assert.Equal(0, sink.Metrics().PendingBlocks);
    }

    [Fact]
    public void StopReturnsWhileWriterIsBlockedAndCompletesAfterRelease()
    {
        using var consumer = new BufferedSegmentTestConsumer(blockWrites: true);
        using var sink = Create(consumer, recordsPerBlock: 2);
        ForStatus(sink, BufferedSegmentStatus.Running);
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(1));
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(2));
        ForSignal(consumer.WriteEntered, "blocked write");

        sink.Stop();

        Assert.Equal(BufferedSegmentStatus.Stopping, sink.Metrics().Status);
        Assert.False(consumer.CompletionObserved.IsSet);
        consumer.WriteRelease.Set();
        ForSignal(consumer.CompletionObserved, "completion after write release");
        ForStatus(sink, BufferedSegmentStatus.Stopped);
    }

    [Fact]
    public void StopDuringInitializationReturnsAndCompletesAfterInitialization()
    {
        using var consumer = new BufferedSegmentTestConsumer(blockInitialization: true);
        using var sink = Create(consumer);
        ForSignal(consumer.InitializationEntered, "consumer initialization");

        sink.Stop();

        Assert.Equal(BufferedSegmentStatus.Stopping, sink.Metrics().Status);
        consumer.InitializationRelease.Set();
        ForSignal(consumer.CompletionObserved, "empty session completion");
        ForStatus(sink, BufferedSegmentStatus.Stopped);
        Assert.True(consumer.Completion.Complete);
        Assert.Empty(consumer.Segments);
    }

    /// <summary>
    /// The shutdown wait is the point after which the session's completion is on the consumer, not a
    /// promise that it will be.
    /// </summary>
    /// <remarks>
    /// The writer thread is a background thread, so a stop that only signals leaves the last blocks
    /// and the completion racing process exit. A teardown that returns from this wait has already
    /// observed the drain the stop asked for.
    /// </remarks>
    [Fact]
    public void ShutdownWaitReturnsOnlyAfterTheWriterFinishedTheDrainItWasAskedFor()
    {
        using var consumer = new BufferedSegmentTestConsumer();
        using var sink = Create(consumer, recordsPerBlock: 2);
        ForStatus(sink, BufferedSegmentStatus.Running);
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(11));
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(12));

        sink.Stop();

        Assert.True(sink.WaitForWriterExit(TimeSpan.FromSeconds(15)));
        Assert.True(consumer.CompletionObserved.IsSet);
        Assert.True(consumer.Completion.Complete);
        Assert.Equal(2, consumer.Completion.WrittenRecords);
        Assert.Equal(new[] { 11, 12 }, Assert.Single(consumer.Segments).Records);
        Assert.Equal(BufferedSegmentStatus.Stopped, sink.Metrics().Status);
    }

    /// <summary>
    /// A writer that cannot finish costs the wait its bound and nothing else.
    /// </summary>
    /// <remarks>
    /// The bound exists so a writer wedged in a storage call cannot hold the host's quit. Expiring it
    /// publishes nothing and invents nothing: the session is left exactly as an interrupted one is,
    /// without a completion, and the writer stays free to finish behind it.
    /// </remarks>
    [Fact]
    public void ShutdownWaitExpiresWithoutCompletingOrDiscardingTheSession()
    {
        using var consumer = new BufferedSegmentTestConsumer(blockWrites: true);
        using var sink = Create(consumer, recordsPerBlock: 2);
        ForStatus(sink, BufferedSegmentStatus.Running);
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(21));
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(22));
        ForSignal(consumer.WriteEntered, "blocked write");
        sink.Stop();

        Assert.False(sink.WaitForWriterExit(TimeSpan.FromMilliseconds(20)));

        Assert.False(consumer.CompletionObserved.IsSet);
        Assert.Equal(BufferedSegmentStatus.Stopping, sink.Metrics().Status);
        consumer.WriteRelease.Set();
        ForSignal(consumer.CompletionObserved, "completion after the bound expired");
        ForStatus(sink, BufferedSegmentStatus.Stopped);
        Assert.True(consumer.Completion.Complete);
    }

    /// <summary>
    /// A sink still admitting records has no drain to wait for, and says so instead of stalling.
    /// </summary>
    [Fact]
    public void ARunningSinkReportsNoFinishedDrainWithoutSpendingTheBound()
    {
        using var consumer = new BufferedSegmentTestConsumer();
        using var sink = Create(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        Assert.False(sink.WaitForWriterExit(TimeSpan.FromSeconds(15)));

        Assert.Equal(BufferedSegmentStatus.Running, sink.Metrics().Status);
        Assert.False(consumer.CompletionObserved.IsSet);
    }

    private static BufferedSegmentSink<int> Create(
        BufferedSegmentTestConsumer consumer,
        int blockCount = 3,
        int recordsPerBlock = 2) =>
        new(consumer, new BufferedSegmentOptions(
            blockCount,
            recordsPerBlock,
            "Buffered segment flow test"));

    private static void ForWrittenBlocks(BufferedSegmentSink<int> sink, long count) =>
        Assert.True(
            PollUntil(() => sink.Metrics().WrittenBlocks == count),
            $"Expected {count} written blocks; observed {sink.Metrics().WrittenBlocks}.");
}
