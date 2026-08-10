using System;
using System.IO;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace;
using OrbModding.ServiceCycleTrace.Dashboard;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class TraceDashboardReaderTests
{
    [Fact]
    public void AttributedActionSurfacesItsExactWireIdentityAndOutcome()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "orb-trace-dashboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = Path.Combine(root, "run-20260101-000000-test");
            var fullSession = Path.Combine(run, "full", "session-000000000000002a");
            Directory.CreateDirectory(fullSession);
            WriteFullTrace(fullSession, CycleStarted(1, lifecycle: 1, cycleId: 1, timestampTicks: 100));

            var candidate = new Guid("11111111-1111-1111-1111-111111111111");
            var list = new Guid("22222222-2222-2222-2222-222222222222");
            var view = new Guid("33333333-3333-3333-3333-333333333333");
            WriteJournal(run, Action(candidate, list, view));

            var document = TraceDashboardReader.Read(TraceCaptureLocator.Locate(run));
            var decision = Assert.Single(document.Decisions);

            Assert.Equal(DecisionJournalRecordKind.Action.ToString(), decision.Kind);
            Assert.Equal(1, decision.ActionOrdinal);
            Assert.Equal(candidate.ToString("D"), decision.CandidateId);
            Assert.Equal(ServiceActionNativeTypeId.UpgradeSO.ToString(), decision.NativeType);
            Assert.Equal(list.ToString("D"), decision.ListId);
            Assert.Equal(view.ToString("D"), decision.ViewId);
            Assert.Equal(ServiceActionRouteStatus.Resolved.ToString(), decision.RouteStatus);
            Assert.Equal("Committed", decision.Outcome);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Cycle ids restart at one in every lifecycle, so a dashboard keyed on service and cycle alone
    /// let a later lifecycle overwrite an earlier one row for row. On one 43-minute capture that lost
    /// 2,782 of World collection's 7,822 cycles and rendered the service as post-prestige-only, in
    /// the same table that rendered Auto Buy as pre-prestige-only, without saying anything.
    /// </summary>
    [Fact]
    public void CyclesRepeatingTheirIdInALaterLifecycleStayDistinctRows()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "orb-trace-dashboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = Path.Combine(root, "run-20260101-000000-test");
            var fullSession = Path.Combine(run, "full", "session-000000000000002a");
            Directory.CreateDirectory(fullSession);
            WriteFullTrace(
                fullSession,
                CycleStarted(1, lifecycle: 9, cycleId: 1, timestampTicks: 100),
                CycleStarted(2, lifecycle: 9, cycleId: 2, timestampTicks: 200),
                CycleStarted(3, lifecycle: 14, cycleId: 1, timestampTicks: 300),
                CycleStarted(4, lifecycle: 14, cycleId: 2, timestampTicks: 400));

            var document = TraceDashboardReader.Read(TraceCaptureLocator.Locate(run));

            Assert.Equal(
                new[] { (9UL, 1UL), (9UL, 2UL), (14UL, 1UL), (14UL, 2UL) },
                document.Cycles
                    .Select(x => (x.Lifecycle, x.Cycle))
                    .OrderBy(x => x.Lifecycle)
                    .ThenBy(x => x.Cycle)
                    .ToArray());
            Assert.Single(
                document.Cycles,
                x => x.Temperature == nameof(ServiceCycleProfileTemperature.ColdProcess));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The reconciliation that makes the key trustworthy rather than merely different: two starts
    /// landing in one row is what the old key did 2,782 times, and it has to be a refusal rather than
    /// a quieter number.
    /// </summary>
    [Fact]
    public void TwoStartsMergingIntoOneCycleRowIsRefused()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "orb-trace-dashboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = Path.Combine(root, "run-20260101-000000-test");
            var fullSession = Path.Combine(run, "full", "session-000000000000002a");
            Directory.CreateDirectory(fullSession);
            WriteFullTrace(
                fullSession,
                CycleStarted(1, lifecycle: 9, cycleId: 1, timestampTicks: 100),
                CycleStarted(2, lifecycle: 9, cycleId: 1, timestampTicks: 200));

            var failure = Assert.Throws<InvalidOperationException>(
                () => TraceDashboardReader.Read(TraceCaptureLocator.Locate(run)));

            Assert.Contains("started 2 cycles", failure.Message);
            Assert.Contains("kept 1 rows", failure.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// What the frame cost is the denominator of every claim about what the suite cost, and it was
    /// only ever recoverable by differencing consecutive pump offsets by hand.
    /// </summary>
    [Fact]
    public void EveryPumpAfterTheFirstCarriesTheFrameItLandedIn()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "orb-trace-dashboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = Path.Combine(root, "run-20260101-000000-test");
            var fullSession = Path.Combine(run, "full", "session-000000000000002a");
            Directory.CreateDirectory(fullSession);
            WriteFullTrace(
                fullSession,
                PumpCompleted(1, frame: 10, timestampTicks: 1_000_000),
                PumpCompleted(2, frame: 11, timestampTicks: 1_250_000),
                PumpCompleted(3, frame: 12, timestampTicks: 1_400_000));

            var document = TraceDashboardReader.Read(TraceCaptureLocator.Locate(run));

            Assert.Equal(
                new double?[] { null, 25, 15 },
                document.Pumps.Select(x => x.AmbientMilliseconds).ToArray());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceCycleSemanticEvent PumpCompleted(
        ulong sequence,
        long frame,
        long timestampTicks)
    {
        var payload = ServiceCycleSemanticPayload.Pump(
            frame,
            accepted: true,
            startingOrdinal: 0,
            responsesAcquired: 0,
            actionsAttempted: 0,
            capturesAttempted: 0,
            cyclesStarted: 0,
            worldGateDeferrals: 0,
            emergencyBatchesRejected: 0,
            lifecycleTransitions: 0,
            responseDuration: 0,
            actionDuration: 0,
            captureDuration: 0,
            totalDuration: 50_000,
            timestampTicks: timestampTicks);
        return new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(new ServiceCycleTraceSessionId(101), sequence),
            default,
            ServiceCycleSemanticEventKind.PumpCompleted,
            in payload);
    }

    private static ServiceCycleSemanticEvent CycleStarted(
        ulong sequence,
        ulong lifecycle,
        ulong cycleId,
        long timestampTicks)
    {
        var cycle = new ServiceCycleTraceCycleIdentity(
            new ServiceCycleTraceServiceId(3),
            lifecycle,
            1,
            1,
            1,
            cycleId);
        var payload = ServiceCycleSemanticPayload.CycleFact(in cycle, 0, timestampTicks, 10);
        return new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(new ServiceCycleTraceSessionId(101), sequence),
            default,
            ServiceCycleSemanticEventKind.CycleStarted,
            in payload);
    }

    private static void WriteFullTrace(string session, params ServiceCycleSemanticEvent[] events)
    {
        var fullSession = new FullTraceSessionId(42);
        var semanticSession = new ServiceCycleTraceSessionId(101);
        var segment = new byte[FullTraceSegmentCodec.GetEncodedLength(events.Length)];
        FullTraceSegmentCodec.Encode(
            fullSession,
            semanticSession,
            0,
            1,
            3,
            events,
            segment);
        File.WriteAllBytes(Path.Combine(session, "segment-00000000.oscs"), segment);

        var manifest = new FullTraceManifestDocument(
            FullTraceCompleteness.Complete,
            FullTraceTerminalReason.UserStopped,
            fullSession,
            semanticSession,
            3,
            1,
            1,
            checked((ulong)events.Length),
            checked((ulong)events.Length),
            0,
            0,
            events[0].Payload.TimestampTicks,
            events[^1].Payload.TimestampTicks,
            checked((ulong)segment.Length));
        var manifestBytes = new byte[FullTraceManifestCodec.ManifestBytes];
        FullTraceManifestCodec.Encode(in manifest, manifestBytes);
        File.WriteAllBytes(Path.Combine(session, "manifest.oscm"), manifestBytes);

        var roster = new ServiceCycleTraceRoster(new[]
        {
            new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.ServiceKind,
                3,
                "orbautomata.auto-buy",
                "Auto Buy"),
        });
        File.WriteAllBytes(
            Path.Combine(session, TraceRosterFormat.FileName),
            TraceRosterFormat.Encode(roster));
    }

    private static DecisionJournalRecord Action(Guid candidate, Guid list, Guid view)
    {
        var cycle = new ServiceCycleIdentity(
            new ServiceId("orbautomata.auto-buy"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceActionContext(
            cycle,
            new BatchId(1),
            new ActionId(1),
            0,
            new MonotonicTimestamp(100));
        var evidence = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));
        var result = ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
        var fact = new ServiceActionFact(
            context,
            result,
            new MonotonicTimestamp(100),
            new MonotonicTimestamp(100));
        var attribution = ServiceActionJournalAttribution.Routed(
            candidate,
            ServiceActionNativeTypeId.UpgradeSO,
            list,
            view);
        var observation = new DecisionJournalActionObservation(
            new ServiceCycleTraceServiceId(3),
            in fact,
            in attribution);
        return DecisionJournalRecord.Action(in observation);
    }

    private static void WriteJournal(string run, DecisionJournalRecord record)
    {
        var directory = Path.Combine(run, "journal");
        Directory.CreateDirectory(directory);
        var bytes = new byte[DecisionJournalSegmentCodec.GetEncodedLength(1)];
        DecisionJournalSegmentCodec.Encode(
            new DecisionJournalRunId(11),
            0,
            1,
            new[] { record },
            bytes);
        File.WriteAllBytes(
            Path.Combine(directory, "journal-000000.osjd"),
            bytes);
    }
}
