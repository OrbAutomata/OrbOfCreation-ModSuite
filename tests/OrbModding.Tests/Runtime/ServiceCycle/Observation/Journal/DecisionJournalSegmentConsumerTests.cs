using OrbModding.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Tools;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;
using static OrbModding.Tests.Runtime.Tracing.BufferedSegments.BufferedSegmentTestWait;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalSegmentConsumerTests
{
    [Fact]
    public void RecoveredOrdinalAndCheckpointProduceContiguousRunSegments()
    {
        var storage = new MemoryStorage(new TraceSegmentStorageRecovery(7, 2, 3, 4));
        var consumer = new DecisionJournalSegmentConsumer(
            storage,
            new DecisionJournalRunId(11),
            maximumCommittedSegments: 5);
        using var sink = CreateSink(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);
        var first = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var second = DecisionJournalRecord.Decision(CreateObservation(2, 20));

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(in first));
        Assert.Equal(BufferedSegmentFlushResult.Flushed, sink.Flush());
        Assert.True(SpinWait.SpinUntil(
            () => sink.Metrics().WrittenBlocks == 1,
            ServiceCycleTestDeadline.Value));
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(in second));
        sink.Stop();
        ForStatus(sink, BufferedSegmentStatus.Stopped);

        Assert.Collection(
            storage.Segments,
            bytes => AssertSegment(bytes, ordinal: 7, sequence: 1, cycle: 1),
            bytes => AssertSegment(bytes, ordinal: 8, sequence: 2, cycle: 2));
        var metrics = consumer.Metrics;
        Assert.Equal(4, metrics.RetainedSegments);
        Assert.Equal(3, metrics.StartupPrunedSegments);
        Assert.Equal(4, metrics.StaleTemporaryFilesRemoved);
        Assert.Equal(DecisionJournalConsumerFaultReason.None, metrics.FaultReason);
    }

    [Fact]
    public void CommitBeyondQuotaDeletesExactlyOneOldestSegment()
    {
        var storage = new MemoryStorage(new TraceSegmentStorageRecovery(5, 2, 0, 0));
        var consumer = new DecisionJournalSegmentConsumer(
            storage,
            new DecisionJournalRunId(12),
            maximumCommittedSegments: 2);
        using var sink = CreateSink(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(in record));
        sink.Stop();
        ForStatus(sink, BufferedSegmentStatus.Stopped);

        Assert.Equal(1, storage.DeleteCalls);
        AssertSegment(Assert.Single(storage.Segments), ordinal: 5, sequence: 1, cycle: 1);
        Assert.Equal(2, consumer.Metrics.RetainedSegments);
        Assert.Equal(1, consumer.Metrics.EvictedSegments);
    }

    [Fact]
    public void PruneFailureKeepsCommittedBlockDurableAndFaultsAtNextSequence()
    {
        var storage = new MemoryStorage(new TraceSegmentStorageRecovery(0, 1, 0, 0))
        {
            FailDelete = true,
        };
        var consumer = new DecisionJournalSegmentConsumer(
            storage,
            new DecisionJournalRunId(13),
            maximumCommittedSegments: 1);
        using var sink = CreateSink(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(in record));
        sink.Stop();
        ForStatus(sink, BufferedSegmentStatus.Faulted);

        Assert.Single(storage.Segments);
        var metrics = sink.Metrics();
        Assert.Equal(1, metrics.WrittenRecords);
        Assert.Equal(2, metrics.FirstIncompleteSequence);
        Assert.Equal(BufferedSegmentFaultReason.CompletionFailed, metrics.FaultReason);
        Assert.Equal(
            DecisionJournalConsumerFaultReason.RetentionFailed,
            consumer.Metrics.FaultReason);
    }

    /// <summary>
    /// A journal that could not prune keeps what the storage said, so its status names the cause
    /// and not only that rolling retention failed.
    /// </summary>
    [Fact]
    public void APruneFailureKeepsWhatTheStorageSaid()
    {
        var storage = new MemoryStorage(new TraceSegmentStorageRecovery(0, 1, 0, 0))
        {
            FailDelete = true,
        };
        var consumer = new DecisionJournalSegmentConsumer(
            storage,
            new DecisionJournalRunId(14),
            maximumCommittedSegments: 1);
        using var sink = CreateSink(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(in record));
        sink.Stop();
        ForStatus(sink, BufferedSegmentStatus.Faulted);

        Assert.Equal("Injected prune failure.", consumer.Metrics.FaultException?.Message);
    }

    [Fact]
    public void FileBackedRunsRetainNewestSegmentsAndRestartRecordSequence()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        WriteFileRun(directory, new DecisionJournalRunId(21), firstCycle: 1);
        WriteFileRun(directory, new DecisionJournalRunId(22), firstCycle: 3);

        var paths = Directory.GetFiles(directory, "journal-*.osjd");
        Array.Sort(paths, StringComparer.Ordinal);
        Assert.Equal(3, paths.Length);
        Assert.Equal("journal-000001.osjd", Path.GetFileName(paths[0]));
        Assert.Equal("journal-000002.osjd", Path.GetFileName(paths[1]));
        Assert.Equal("journal-000003.osjd", Path.GetFileName(paths[2]));

        var prior = DecisionJournalSegmentCodec.Decode(File.ReadAllBytes(paths[0]));
        var restarted = DecisionJournalSegmentCodec.Decode(File.ReadAllBytes(paths[1]));
        var continued = DecisionJournalSegmentCodec.Decode(File.ReadAllBytes(paths[2]));
        Assert.Equal(new DecisionJournalRunId(21), prior.Run);
        Assert.Equal((ulong)2, prior.FirstRecordSequence);
        Assert.Equal(new DecisionJournalRunId(22), restarted.Run);
        Assert.Equal((ulong)1, restarted.FirstRecordSequence);
        Assert.Equal(new DecisionJournalRunId(22), continued.Run);
        Assert.Equal((ulong)2, continued.FirstRecordSequence);


    }

    /// <summary>
    /// A store this build cannot continue is abandoned at startup, and the journal records anyway.
    /// </summary>
    /// <remarks>
    /// The store outlives the process. Refusing it stopped the journal on that machine for good,
    /// which trades every future session for segments no build in the future can read either. The
    /// count is what makes the trade visible.
    /// </remarks>
    [Fact]
    public void AStoreThisBuildCannotContinueIsAbandonedRatherThanRefused()
    {
        using var directory = new DecisionJournalTestDirectory();
        for (var ordinal = 1; ordinal <= 3; ordinal++)
            File.WriteAllBytes(directory.SegmentPath(ordinal), new byte[] { 7, 7, 7, 7 });

        var consumer = Consume(directory, run: 31, out var written);

        Assert.Equal(3, consumer.Metrics.IncompatibleSegmentsPruned);
        Assert.Equal(1, consumer.Metrics.RetainedSegments);
        Assert.Equal("journal-000000.osjd", Path.GetFileName(written));
        var segment = DecisionJournalSegmentCodec.Decode(File.ReadAllBytes(written));
        Assert.Equal(new DecisionJournalRunId(31), segment.Run);
        Assert.Equal((ulong)0, segment.Ordinal);
        Assert.Equal((ulong)1, segment.FirstRecordSequence);
    }

    [Fact]
    public void ASegmentFromAnotherSchemaVersionIsAbandonedRatherThanRefused()
    {
        using var directory = new DecisionJournalTestDirectory();
        var path = directory.WriteSegment(
            1,
            32,
            1,
            DecisionJournalRecord.Decision(CreateObservation(1, 10)));
        var bytes = File.ReadAllBytes(path);
        bytes[4] = DecisionJournalSegmentCodec.SchemaVersion + 1;
        File.WriteAllBytes(path, bytes);

        var consumer = Consume(directory, run: 33, out var written);

        Assert.Equal(1, consumer.Metrics.IncompatibleSegmentsPruned);
        Assert.Equal("journal-000000.osjd", Path.GetFileName(written));
        var segment = DecisionJournalSegmentCodec.Decode(File.ReadAllBytes(written));
        Assert.Equal(new DecisionJournalRunId(33), segment.Run);
        Assert.Equal((ulong)1, segment.FirstRecordSequence);
    }

    /// <summary>
    /// A schema-3 store without action kinds is abandoned before schema 4 starts fresh.
    /// </summary>
    /// <remarks>
    /// Backwards is the direction that actually happens: a schema bump meets stores already on disk.
    /// The journal deliberately has no migration or dual reader, and the discarded count remains loud.
    /// </remarks>
    [Fact]
    public void ASchemaThreeStoreIsAbandonedBeforeSchemaFourStarts()
    {
        using var directory = new DecisionJournalTestDirectory();
        var path = directory.WriteSegment(
            1,
            36,
            1,
            DecisionJournalRecord.Decision(CreateObservation(1, 10)));
        var bytes = File.ReadAllBytes(path);
        bytes[4] = 3;
        bytes[8] = 3;
        File.WriteAllBytes(path, bytes);

        var consumer = Consume(directory, run: 37, out var written);

        Assert.Equal(1, consumer.Metrics.IncompatibleSegmentsPruned);
        Assert.Equal(
            DecisionJournalConsumerFaultReason.None,
            consumer.Metrics.FaultReason);
        Assert.Equal("journal-000000.osjd", Path.GetFileName(written));
    }

    /// <summary>
    /// A readable store from an earlier run is continued, not discarded.
    /// </summary>
    /// <remarks>
    /// The mirror of abandonment: restart evidence is the journal's whole point across a crash, so
    /// a store that a probe accepts must keep its segments and its ordinal.
    /// </remarks>
    [Fact]
    public void AReadableStoreFromAnEarlierRunIsContinued()
    {
        using var directory = new DecisionJournalTestDirectory();
        directory.WriteSegment(0, 34, 1, DecisionJournalRecord.Decision(CreateObservation(1, 10)));
        directory.WriteSegment(1, 34, 2, DecisionJournalRecord.Decision(CreateObservation(2, 20)));

        var consumer = Consume(directory, run: 35, out var written);

        Assert.Equal(0, consumer.Metrics.IncompatibleSegmentsPruned);
        Assert.Equal(3, consumer.Metrics.RetainedSegments);
        Assert.Equal("journal-000002.osjd", Path.GetFileName(written));
        Assert.Equal(3, Directory.GetFiles(directory.Root, "journal-*.osjd").Length);
        var segment = DecisionJournalSegmentCodec.Decode(File.ReadAllBytes(written));
        Assert.Equal(new DecisionJournalRunId(35), segment.Run);
        Assert.Equal((ulong)2, segment.Ordinal);
        Assert.Equal((ulong)1, segment.FirstRecordSequence);
    }

    [Fact]
    public void LastStorageOrdinalRemainsDurableAndFaultsBeforeItsSuccessor()
    {
        var storage = new MemoryStorage(new TraceSegmentStorageRecovery(int.MaxValue, 0, 0, 0));
        var consumer = new DecisionJournalSegmentConsumer(
            storage,
            new DecisionJournalRunId(23),
            maximumCommittedSegments: 2);
        using var sink = CreateSink(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(in record));
        sink.Stop();
        ForStatus(sink, BufferedSegmentStatus.Faulted);

        AssertSegment(
            Assert.Single(storage.Segments),
            ordinal: int.MaxValue,
            sequence: 1,
            cycle: 1);
        Assert.Equal(1, consumer.Metrics.RetainedSegments);
        Assert.Equal(
            DecisionJournalConsumerFaultReason.OrdinalExhausted,
            consumer.Metrics.FaultReason);
        var metrics = sink.Metrics();
        Assert.Equal(1, metrics.WrittenRecords);
        Assert.Equal(2, metrics.FirstIncompleteSequence);
        Assert.Equal(BufferedSegmentFaultReason.CompletionFailed, metrics.FaultReason);
        Assert.Equal(BufferedSegmentAppendResult.Faulted, sink.Append(in record));
        Assert.Single(storage.Segments);
    }

    [Fact]
    public void FacadeBridgesRetentionFaultWithoutAcceptingAnotherRecord()
    {
        var storage = new MemoryStorage(new TraceSegmentStorageRecovery(0, 1, 0, 0))
        {
            FailDelete = true,
        };
        using var sink = new BufferedDecisionJournalRecordSink(
            storage,
            new DecisionJournalRunId(24),
            maximumCommittedSegments: 1,
            blockCount: 3);
        WaitForFacadeStatus(sink, BufferedSegmentStatus.Running);
        var first = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var second = DecisionJournalRecord.Decision(CreateObservation(2, 20));

        Assert.True(sink.TryAppend(in first));
        Assert.True(sink.TryFlush());
        Assert.True(SpinWait.SpinUntil(
            () => sink.ConsumerMetrics.CannotContinue,
            ServiceCycleTestDeadline.Value));

        Assert.False(sink.TryAppend(in second));
        Assert.False(sink.TryFlush());
        WaitForFacadeStatus(sink, BufferedSegmentStatus.Faulted);

        Assert.Single(storage.Segments);
        var metrics = sink.TransportMetrics;
        Assert.Equal(1, metrics.AcceptedRecords);
        Assert.Equal(1, metrics.WrittenRecords);
        Assert.Equal(2, metrics.FirstIncompleteSequence);
        Assert.Equal(BufferedSegmentFaultReason.ProducerFailed, metrics.FaultReason);
        Assert.Equal(2, sink.ConsumerMetrics.RetainedSegments);
        Assert.Equal(
            DecisionJournalConsumerFaultReason.RetentionFailed,
            sink.ConsumerMetrics.FaultReason);
    }

    /// <summary>
    /// Writes one record into the store the directory holds, and names the segment it landed in.
    /// </summary>
    private static DecisionJournalSegmentConsumer Consume(
        DecisionJournalTestDirectory directory,
        ulong run,
        out string written)
    {
        var consumer = new DecisionJournalSegmentConsumer(
            new FileTraceSegmentStorage(directory.Root, "journal", ".osjd"),
            new DecisionJournalRunId(run),
            maximumCommittedSegments: 4);
        using (var sink = CreateSink(consumer))
        {
            ForStatus(sink, BufferedSegmentStatus.Running);
            var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
            Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(in record));
            sink.Stop();
            ForStatus(sink, BufferedSegmentStatus.Stopped);
        }

        var paths = Directory.GetFiles(directory.Root, "journal-*.osjd");
        Array.Sort(paths, StringComparer.Ordinal);
        written = paths[paths.Length - 1];
        return consumer;
    }

    private static BufferedSegmentSink<DecisionJournalRecord> CreateSink(
        DecisionJournalSegmentConsumer consumer) => new(
            consumer,
            new BufferedSegmentOptions(
                3,
                DecisionJournalSegmentCodec.MaximumRecords,
                "Journal consumer test"));

    private static void WriteFileRun(
        string directory,
        DecisionJournalRunId run,
        ulong firstCycle)
    {
        using var sink = new BufferedDecisionJournalRecordSink(
            new FileTraceSegmentStorage(directory, "journal", ".osjd"),
            run,
            maximumCommittedSegments: 3,
            blockCount: 3);
        WaitForFacadeStatus(sink, BufferedSegmentStatus.Running);
        var first = DecisionJournalRecord.Decision(CreateObservation(firstCycle, 10));
        var second = DecisionJournalRecord.Decision(CreateObservation(firstCycle + 1, 20));
        Assert.True(sink.TryAppend(in first));
        Assert.True(sink.TryFlush());
        Assert.True(SpinWait.SpinUntil(
            () => sink.TransportMetrics.WrittenBlocks == 1,
            ServiceCycleTestDeadline.Value));
        Assert.True(sink.TryAppend(in second));
        sink.Stop();
        WaitForFacadeStatus(sink, BufferedSegmentStatus.Stopped);
    }

    private static void WaitForFacadeStatus(
        BufferedDecisionJournalRecordSink sink,
        BufferedSegmentStatus expected)
    {
        Assert.True(SpinWait.SpinUntil(
            () => sink.TransportMetrics.Status == expected,
            ServiceCycleTestDeadline.Value),
            $"Expected {expected}, observed {sink.TransportMetrics.Status}.");
    }

    private static void AssertSegment(
        byte[] bytes,
        ulong ordinal,
        ulong sequence,
        ulong cycle)
    {
        var segment = DecisionJournalSegmentCodec.Decode(bytes);
        Assert.Equal(ordinal, segment.Ordinal);
        Assert.Equal(sequence, segment.FirstRecordSequence);
        Assert.Equal(cycle, Assert.Single(segment.Records).FirstCycle);
    }

    private sealed class MemoryStorage : IRestartAwareTraceSegmentStorage
    {
        private readonly TraceSegmentStorageRecovery _recovery;

        internal MemoryStorage(TraceSegmentStorageRecovery recovery) => _recovery = recovery;

        internal List<byte[]> Segments { get; } = new();
        internal int DeleteCalls { get; private set; }
        internal bool FailDelete { get; init; }

        public TraceSegmentStorageRecovery Reconcile(
            int maximumCommittedSegments,
            ITraceSegmentHeaderProbe? probe = null) => _recovery;

        public object BeginSegment(int ordinal) => new MemorySegment(ordinal);

        public void Append(object segment, ReadOnlySpan<byte> record) =>
            ((MemorySegment)segment).Bytes.AddRange(record.ToArray());

        public void CommitSegment(object segment) =>
            Segments.Add(((MemorySegment)segment).Bytes.ToArray());

        public void DiscardSegment(object segment) { }

        public void DeleteOldestCommitted()
        {
            DeleteCalls++;
            if (FailDelete) throw new InvalidOperationException("Injected prune failure.");
        }

        private sealed class MemorySegment
        {
            internal MemorySegment(int ordinal) => Ordinal = ordinal;
            internal int Ordinal { get; }
            internal List<byte> Bytes { get; } = new();
        }
    }
}
