using OrbModding.TestSupport;
using System;
using System.Globalization;
using System.IO;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Tests.Tools;

internal sealed class ManualFullTraceTestDirectory : IDisposable
{
    private readonly TempDirectory _temporary = new(create: true);
    private static readonly FullTraceSessionId Session = new(0x2a);
    private readonly string _root;

    internal ManualFullTraceTestDirectory(
        ServiceCycleSemanticEvent[] events,
        bool writeManifest = true,
        ServiceCycleTraceRoster? roster = null)
    {
        _root = _temporary.Path;
        SessionPath = Path.Combine(
            _root,
            "session-" + Session.Value.ToString("x16", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(SessionPath);
        if (roster is not null)
        {
            File.WriteAllBytes(
                Path.Combine(SessionPath, TraceRosterFormat.FileName),
                TraceRosterFormat.Encode(roster));
        }
        if (events.Length == 0)
        {
            if (writeManifest) WriteManifest(events, 0, 0);
            return;
        }

        var bytes = new byte[FullTraceSegmentCodec.GetEncodedLength(events.Length)];
        FullTraceSegmentCodec.Encode(
            Session,
            events[0].Id.Session,
            0,
            1,
            7,
            events,
            bytes);
        File.WriteAllBytes(Path.Combine(SessionPath, "segment-00000000.oscs"), bytes);
        if (writeManifest) WriteManifest(events, 1, (ulong)bytes.Length);
    }

    internal string SessionPath { get; }

    public void Dispose()
    {
        _temporary.Dispose();
    }

    private void WriteManifest(ServiceCycleSemanticEvent[] events, ulong segmentCount, ulong segmentBytes)
    {
        var semanticSession = events.Length == 0
            ? new ServiceCycleTraceSessionId(101)
            : events[0].Id.Session;
        var firstSequence = events.Length == 0 ? 1UL : events[0].Id.Sequence;
        var firstTimestamp = events.Length == 0 ? 0 : events[0].Payload.TimestampTicks;
        var lastTimestamp = events.Length == 0 ? 0 : events[^1].Payload.TimestampTicks;
        var document = new FullTraceManifestDocument(
            FullTraceCompleteness.Complete,
            FullTraceTerminalReason.UserStopped,
            Session,
            semanticSession,
            7,
            segmentCount,
            firstSequence,
            (ulong)events.Length,
            (ulong)events.Length,
            0,
            0,
            firstTimestamp,
            lastTimestamp,
            segmentBytes);
        var bytes = new byte[FullTraceManifestCodec.ManifestBytes];
        FullTraceManifestCodec.Encode(in document, bytes);
        File.WriteAllBytes(Path.Combine(SessionPath, "manifest.oscm"), bytes);
    }
}
