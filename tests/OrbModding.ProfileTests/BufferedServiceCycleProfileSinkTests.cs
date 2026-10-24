using OrbModding.TestSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;
using OrbModding.Common.Runtime.Tracing;
using Xunit;
using static OrbModding.ProfileTests.ServiceCycleProfileTestData;

namespace OrbModding.ProfileTests;

public sealed class BufferedServiceCycleProfileSinkTests
{
    [Fact]
    public void StopDrainsAcceptedRecordsAndCommitsCompleteManifest()
    {
        using var storage = new ProfileStorage();
        var calibration = Calibration();
        using var sink = new BufferedServiceCycleProfileSink(
            storage, new ServiceCycleProfileSessionId(1), in calibration, blockCount: 3, recordsPerBlock: 2);
        WaitUntilReady(sink);

        Assert.Equal(ServiceCycleProfileAppendResult.Accepted, sink.Append(Record(stage: 1)));
        Assert.Equal(ServiceCycleProfileAppendResult.Accepted, sink.Append(Record(stage: 2)));
        Assert.Equal(ServiceCycleProfileAppendResult.Accepted, sink.Append(Record(stage: 3)));
        sink.Stop(ServiceCycleProfileTerminalReason.UserStopped);
        WaitUntilTerminal(sink);

        Assert.Equal(ServiceCycleProfileSinkState.Stopped, sink.Snapshot.State);
        Assert.Equal(2, storage.Segments.Count);
        var manifest = ServiceCycleProfileManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(ServiceCycleProfileCompleteness.Complete, manifest.Completeness);
        Assert.Equal((ulong)3, manifest.WrittenRecords);
        Assert.Equal((ulong)0, manifest.FirstIncompleteSequence);
    }

    [Fact]
    public void FullAcceptedBlockReportsExhaustionWithoutRetryingItsRecord()
    {
        using var storage = new ProfileStorage(blockFirstSegment: true);
        var calibration = Calibration();
        using var sink = new BufferedServiceCycleProfileSink(
            storage, new ServiceCycleProfileSessionId(2), in calibration, blockCount: 3, recordsPerBlock: 1);
        WaitUntilReady(sink);

        Assert.Equal(ServiceCycleProfileAppendResult.Accepted, sink.Append(Record(stage: 1)));
        Assert.True(storage.WriteEntered.Wait(ServiceCycleTestDeadline.Value));
        Assert.Equal(ServiceCycleProfileAppendResult.Accepted, sink.Append(Record(stage: 2)));
        Assert.Equal(
            ServiceCycleProfileAppendResult.AcceptedAndBufferExhausted,
            sink.Append(Record(stage: 3)));
        storage.ReleaseWrite.Set();
        WaitUntilTerminal(sink);

        var manifest = ServiceCycleProfileManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(ServiceCycleProfileCompleteness.Incomplete, manifest.Completeness);
        Assert.Equal(ServiceCycleProfileTerminalReason.BufferExhausted, manifest.Reason);
        Assert.Equal((ulong)3, manifest.AcceptedRecords);
        Assert.Equal((ulong)3, manifest.WrittenRecords);
        Assert.Equal((ulong)4, manifest.FirstIncompleteSequence);
    }

    [Fact]
    public void SegmentWriteFaultPublishesExactMissingSuffix()
    {
        using var storage = new ProfileStorage(failFirstSegment: true);
        var calibration = Calibration();
        using var sink = new BufferedServiceCycleProfileSink(
            storage, new ServiceCycleProfileSessionId(3), in calibration, blockCount: 3, recordsPerBlock: 1);
        WaitUntilReady(sink);

        Assert.Equal(ServiceCycleProfileAppendResult.Accepted, sink.Append(Record()));
        WaitUntilTerminal(sink);

        Assert.Equal(ServiceCycleProfileSinkState.Faulted, sink.Snapshot.State);
        var manifest = ServiceCycleProfileManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(ServiceCycleProfileTerminalReason.WriteFailed, manifest.Reason);
        Assert.Equal((ulong)1, manifest.AcceptedRecords);
        Assert.Equal((ulong)0, manifest.WrittenRecords);
        Assert.Equal((ulong)1, manifest.FirstIncompleteSequence);
    }

    [Fact]
    public void ProbeFailurePublishesIncompleteManifestWithoutASecondShutdownPath()
    {
        using var storage = new ProfileStorage();
        var calibration = Calibration();
        using var sink = new BufferedServiceCycleProfileSink(
            storage, new ServiceCycleProfileSessionId(5), in calibration, blockCount: 3, recordsPerBlock: 2);
        WaitUntilReady(sink);

        Assert.Equal(ServiceCycleProfileAppendResult.Accepted, sink.Append(Record()));
        sink.Stop(ServiceCycleProfileTerminalReason.ProbeFailed);
        WaitUntilTerminal(sink);

        var manifest = ServiceCycleProfileManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(ServiceCycleProfileCompleteness.Incomplete, manifest.Completeness);
        Assert.Equal(ServiceCycleProfileTerminalReason.ProbeFailed, manifest.Reason);
    }

    [Fact]
    public void DisposalSuppliesRuntimeShutdownWithoutExternalOrdering()
    {
        using var storage = new ProfileStorage();
        var calibration = Calibration();
        var sink = new BufferedServiceCycleProfileSink(
            storage, new ServiceCycleProfileSessionId(4), in calibration, blockCount: 3, recordsPerBlock: 2);
        try
        {
            WaitUntilReady(sink);
            Assert.Equal(ServiceCycleProfileAppendResult.Accepted, sink.Append(Record()));

            sink.Dispose();

            Assert.Equal(ServiceCycleProfileSinkState.Stopped, sink.Snapshot.State);
            var manifest = ServiceCycleProfileManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
            Assert.Equal(ServiceCycleProfileCompleteness.Complete, manifest.Completeness);
            Assert.Equal(ServiceCycleProfileTerminalReason.RuntimeShutdown, manifest.Reason);
        }
        finally
        {
            sink.Dispose();
        }
    }


    private static void WaitUntilReady(BufferedServiceCycleProfileSink sink) =>
        Assert.True(ServiceCycleTestDeadline.PollUntil(
            () => sink.Snapshot.State != ServiceCycleProfileSinkState.Initializing));

    private static void WaitUntilTerminal(BufferedServiceCycleProfileSink sink) =>
        Assert.True(ServiceCycleTestDeadline.PollUntil(
            () => sink.Snapshot.State is ServiceCycleProfileSinkState.Stopped or
                ServiceCycleProfileSinkState.Faulted));


    internal sealed class ProfileStorage : ISegmentSessionStorage, IDisposable
    {
        private readonly bool _blockFirstSegment;
        private readonly bool _failFirstSegment;

        internal ProfileStorage(bool blockFirstSegment = false, bool failFirstSegment = false)
        {
            _blockFirstSegment = blockFirstSegment;
            _failFirstSegment = failFirstSegment;
        }

        internal List<byte[]> Segments { get; } = new();
        internal byte[]? Manifest { get; private set; }
        internal ManualResetEventSlim WriteEntered { get; } = new(false);
        internal ManualResetEventSlim ReleaseWrite { get; } = new(false);

        public void Initialize() { }

        public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes)
        {
            Assert.Equal(Segments.Count, ordinal);
            if (ordinal == 0)
            {
                WriteEntered.Set();
                if (_blockFirstSegment) ReleaseWrite.Wait(TimeSpan.FromSeconds(2));
                if (_failFirstSegment) throw new InvalidOperationException("Expected write failure.");
            }
            Segments.Add(bytes.ToArray());
        }

        public void CommitManifest(ReadOnlySpan<byte> bytes) => Manifest = bytes.ToArray();

        public void Dispose()
        {
            ReleaseWrite.Set();
            WriteEntered.Dispose();
            ReleaseWrite.Dispose();
        }
    }
}
