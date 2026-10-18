using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;

internal enum FullTraceRuntimeSessionState
{
    Idle = 0,
    Arming = 1,
    Recording = 2,
    Stopping = 3,
    Complete = 4,
    Incomplete = 5,
}

internal readonly struct FullTraceRuntimeSessionSnapshot
{
    internal FullTraceRuntimeSessionSnapshot(
        FullTraceRuntimeSessionState state,
        long acceptedRecords,
        long writtenRecords,
        long bytesWritten,
        long segmentCount,
        long firstIncompleteSequence,
        bool manifestCommitted,
        FullTraceTerminalReason terminalReason,
        BufferedSegmentFaultReason faultReason,
        bool storesLost,
        int storeCount,
        string faultCause = "")
    {
        State = state;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        BytesWritten = bytesWritten;
        SegmentCount = segmentCount;
        FirstIncompleteSequence = firstIncompleteSequence;
        ManifestCommitted = manifestCommitted;
        TerminalReason = terminalReason;
        FaultReason = faultReason;
        StoresLost = storesLost;
        StoreCount = storeCount;
        FaultCause = faultCause;
    }

    internal FullTraceRuntimeSessionState State { get; }
    internal long AcceptedRecords { get; }
    internal long WrittenRecords { get; }
    internal long BytesWritten { get; }
    internal long SegmentCount { get; }
    internal long FirstIncompleteSequence { get; }
    internal bool ManifestCommitted { get; }
    internal FullTraceTerminalReason TerminalReason { get; }
    internal BufferedSegmentFaultReason FaultReason { get; }

    /// <summary>What storage threw, when a throw is what stopped the trace; empty otherwise.</summary>
    internal string FaultCause { get; }

    /// <summary>
    /// True once a publication store failed to write. The events are still complete, so this does
    /// not make the session incomplete — but a reader holding a decision that names a generation
    /// will find no file saying what that generation was, and a completion record that did not say
    /// so would let the artifact look whole while missing exactly that.
    /// </summary>
    internal bool StoresLost { get; }

    /// <summary>The publication stores written beside the segments so far.</summary>
    internal int StoreCount { get; }
}
