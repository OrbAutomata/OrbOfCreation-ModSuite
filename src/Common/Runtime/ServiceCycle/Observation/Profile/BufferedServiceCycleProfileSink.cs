#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal enum ServiceCycleProfileSinkState
{
    Initializing = 0,
    Running = 1,
    Stopping = 2,
    Stopped = 3,
    Faulted = 4,
}

internal enum ServiceCycleProfileAppendResult
{
    Accepted = 0,
    AcceptedAndBufferExhausted = 1,
    Unavailable = 2,
    Faulted = 3,
}

internal readonly struct ServiceCycleProfileSinkSnapshot
{
    internal ServiceCycleProfileSinkSnapshot(
        ServiceCycleProfileSinkState state,
        long acceptedRecords,
        long writtenRecords,
        long bytesWritten,
        int pendingBlocks,
        int peakPendingBlocks,
        long firstIncompleteSequence)
    {
        State = state;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        BytesWritten = bytesWritten;
        PendingBlocks = pendingBlocks;
        PeakPendingBlocks = peakPendingBlocks;
        FirstIncompleteSequence = firstIncompleteSequence;
    }

    internal ServiceCycleProfileSinkState State { get; }
    internal long AcceptedRecords { get; }
    internal long WrittenRecords { get; }
    internal long BytesWritten { get; }
    internal int PendingBlocks { get; }
    internal int PeakPendingBlocks { get; }
    internal long FirstIncompleteSequence { get; }
}

internal sealed class BufferedServiceCycleProfileSink : IDisposable
{
    private readonly BufferedSegmentSink<ServiceCycleProfileRecord> _sink;
    private readonly ServiceCycleProfileTerminalRequest _terminalRequest;
    private readonly ServiceCycleProfileSegmentConsumer _consumer;
    private bool _stopRequested;

    internal BufferedServiceCycleProfileSink(
        ISegmentSessionStorage storage,
        ServiceCycleProfileSessionId session,
        in ServiceCycleProfileCalibration calibration,
        int blockCount,
        int recordsPerBlock)
    {
        if (recordsPerBlock is <= 0 or > ServiceCycleProfileSegmentCodec.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(recordsPerBlock));
        _terminalRequest = new ServiceCycleProfileTerminalRequest();
        _consumer = new ServiceCycleProfileSegmentConsumer(
            storage,
            _terminalRequest,
            session,
            in calibration);
        _sink = new BufferedSegmentSink<ServiceCycleProfileRecord>(
            _consumer,
            new BufferedSegmentOptions(blockCount, recordsPerBlock, "ServiceCycle profile writer"));
    }

    internal ServiceCycleProfileAppendResult Append(in ServiceCycleProfileRecord record) =>
        _sink.Append(in record) switch
        {
            BufferedSegmentAppendResult.Accepted => ServiceCycleProfileAppendResult.Accepted,
            BufferedSegmentAppendResult.AcceptedAndBufferExhausted =>
                ServiceCycleProfileAppendResult.AcceptedAndBufferExhausted,
            BufferedSegmentAppendResult.Faulted => ServiceCycleProfileAppendResult.Faulted,
            _ => ServiceCycleProfileAppendResult.Unavailable,
        };

    internal ServiceCycleProfileSinkSnapshot Snapshot
    {
        get
        {
            var metrics = _sink.Metrics();
            return new ServiceCycleProfileSinkSnapshot(
                State(metrics.Status),
                metrics.AcceptedRecords,
                metrics.WrittenRecords,
                metrics.BytesWritten,
                metrics.PendingBlocks,
                metrics.PeakPendingBlocks,
                metrics.FirstIncompleteSequence);
        }
    }

    internal bool ManifestCommitted => _consumer.ManifestCommitted;
    internal ServiceCycleProfileTerminalReason TerminalReason => _consumer.TerminalReason;

    internal void Stop(ServiceCycleProfileTerminalReason reason)
    {
        if (_stopRequested) throw new InvalidOperationException("The profile sink is already stopping.");
        _terminalRequest.Set(reason);
        _stopRequested = true;
        _sink.Stop();
    }

    /// <summary>
    /// Stops the session and waits, bounded, for its writer to publish.
    /// </summary>
    /// <remarks>
    /// A profiling session hands its whole payload to the transport during its stop, so disposal is
    /// where every one of its segments and its manifest is written. Returning the moment the stop was
    /// signalled left all of it racing the host's exit.
    /// </remarks>
    public void Dispose()
    {
        if (!_stopRequested && _sink.Metrics().Status is
            BufferedSegmentStatus.Initializing or BufferedSegmentStatus.Running)
            Stop(ServiceCycleProfileTerminalReason.RuntimeShutdown);
        _sink.Dispose();
        _sink.WaitForWriterExit(BufferedSegmentShutdown.DrainBound);
    }

    private static ServiceCycleProfileSinkState State(BufferedSegmentStatus status) => status switch
    {
        BufferedSegmentStatus.Initializing => ServiceCycleProfileSinkState.Initializing,
        BufferedSegmentStatus.Running => ServiceCycleProfileSinkState.Running,
        BufferedSegmentStatus.Stopping or BufferedSegmentStatus.Faulting => ServiceCycleProfileSinkState.Stopping,
        BufferedSegmentStatus.Stopped => ServiceCycleProfileSinkState.Stopped,
        BufferedSegmentStatus.Faulted => ServiceCycleProfileSinkState.Faulted,
        _ => throw new InvalidOperationException("The profile transport state is invalid."),
    };

}
#endif
