using System;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;

/// <summary>
/// Commits a session's roster beside its segments, once, before the manifest seals the session.
/// </summary>
/// <remarks>
/// A roster that cannot be written costs a reader the names and nothing else, so a failure is
/// swallowed the way a publication store's is: the recording continues and the artifact is simply one
/// whose numbers stay numbers. Losing the events too would cost strictly more than the failure did.
/// </remarks>
internal static class TraceRosterWriter
{
    /// <summary>
    /// True when a roster was written. False covers every reason there is no roster to read: no sink,
    /// nothing to name, or a write that failed.
    /// </summary>
    internal static bool TryWrite(ISegmentSessionStorage storage, ServiceCycleTraceRoster? roster)
    {
        if (roster is null || roster.Count == 0) return false;
        if (storage is not ISessionSideArtifactSink sink) return false;
        try
        {
            sink.CommitSideArtifact(TraceRosterFormat.FileName, TraceRosterFormat.Encode(roster));
            return true;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            return false;
        }
    }
}
