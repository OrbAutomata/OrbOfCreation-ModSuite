using System;
using System.Collections.Generic;
using System.IO;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.ManualTrace;
using OrbModding.Tests.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class ManualFullTraceHeapGrowthViewTests
{
    private const long Second = TimeSpan.TicksPerSecond;
    private const long Minute = 60 * Second;
    private const long Megabyte = 1_048_576;
    private const long Start = 1_000 * TimeSpan.TicksPerMillisecond;
    private const int RunningMinutes = 70;
    private const int StoppedMinutes = 15;
    private static readonly ServiceCycleTraceCycleIdentity Mentor = new(new ServiceCycleTraceServiceId(6), 2, 3, 4, 11, 6);

    /// <summary>
    /// An emergency stop pauses every suite cycle, so a floor that stops rising with it follows what
    /// the suite does, and one that keeps rising is the game's own.
    /// </summary>
    [Fact]
    public void AFloorThatStopsRisingWithTheSuiteStoppedIsNotTheGamesOwn()
    {
        var report = Report(Run(stoppedFloorMegabytesPerMinute: 0));

        Assert.Contains("| Running | 0.0 | 70.0 |", report);
        Assert.Contains("| Stopped | 70.0 | 15.0 | 380 → 380 | 0 |", report);
        Assert.Contains(
            "- **The game grows its own heap:** Refuted: the floor stops rising when the suite stops, so it " +
            "follows what the suite does (stopped 0 MB/h over 15 min,",
            report);
    }

    [Fact]
    public void AFloorThatKeepsRisingWithTheSuiteStoppedIsTheGamesOwn()
    {
        var report = Report(Run(stoppedFloorMegabytesPerMinute: 7));

        Assert.Contains("| Stopped | 70.0 | 15.0 | 380 → 485 | 420 |", report);
        Assert.Contains("- **The game grows its own heap:** Confirmed: the floor keeps rising with the suite stopped", report);
    }

    /// <summary>
    /// Each census says what the heap held, so a suspect whose count holds still is cleared by the same
    /// run that convicts the one whose count climbs.
    /// </summary>
    [Fact]
    public void EachCensusSuspectIsConvictedOrClearedByItsOwnSeries()
    {
        var report = Report(Run(stoppedFloorMegabytesPerMinute: 0));

        Assert.Contains(
            "- **The suite holds old world readings:** Refuted: at most 2 reachable over 86 censuses, " +
            "2.0 on average in the first third and 2.0 in the last.",
            report);
        Assert.Contains(
            "- **Host objects nobody destroys:** Confirmed: ScriptableObjects 9,000 → 9,000 (+0/h), " +
            "GameObjects 3,000 → 4,380 (+1,200/h), MonoBehaviours 2,500 → 2,500 (+0/h).",
            report);
        Assert.Contains("- **The reservation outgrows its use:** Refuted: ", report);
        Assert.Contains("unused part of it +0 MB/h", report);
        Assert.Contains(
            "- **Captures cost more as the game grows:** Confirmed, as garbage per reading rather than anything kept:",
            report);
    }

    /// <summary>
    /// The window-by-window fit is what names a lead: the floor's rise per window against what each
    /// service committed in it.
    /// </summary>
    [Fact]
    public void TheFloorsRiseIsFittedAgainstEachServicesCommitsPerWindow()
    {
        var report = Report(Run(stoppedFloorMegabytesPerMinute: 0));

        Assert.Contains("| Auto Buy (7) | 7 | 1.00 | 501.5 |", report);
    }

    /// <summary>
    /// Turning a busy service off while the rest of the suite runs, here a busier Mentor, separates a heap that grows
    /// with what it commits from one that grows with play regardless.
    /// </summary>
    [Fact]
    public void AFloorThatStopsWhileTheBusiestServiceIsOffFollowsItsCommits()
    {
        var report = Report(Run(stoppedFloorMegabytesPerMinute: 0, quietMinutes: 15, quietFloorMegabytesPerMinute: 0));

        Assert.Contains("| Running | 70.0 | 15.0 | 380 → 380 | 0 |", report);
        Assert.Contains(
            "- **Auto Buy's commits grow the heap:** Confirmed: the floor stops rising while Auto Buy commits " +
            "nothing and the rest of the suite runs (without its commits 0 MB/h over 15 min,",
            report);
    }

    [Fact]
    public void AFloorThatKeepsRisingWhileTheBusiestServiceIsOffGrowsWithPlay()
    {
        var report = Report(Run(stoppedFloorMegabytesPerMinute: 0, quietMinutes: 15, quietFloorMegabytesPerMinute: 7));

        Assert.Contains(
            "- **Auto Buy's commits grow the heap:** Refuted: the floor keeps rising without Auto Buy's commits",
            report);
    }

    /// <summary>Without a stretch to compare, the verdict names the busiest service to turn off.</summary>
    [Fact]
    public void WithoutAQuietStretchTheCommitVerdictSaysWhatToRecord()
    {
        var report = Report(Run(stoppedFloorMegabytesPerMinute: 0));

        Assert.Contains(
            "- **Mentor's commits grow the heap:** Unanswered: no running stretch of 10 minutes or more in " +
            "which Mentor committed nothing.",
            report);
    }

    [Fact]
    public void ACaptureWithoutHeapSamplesSaysTheSectionIsUnavailable()
    {
        var events = new List<ServiceCycleSemanticEvent>();
        Pump(events, 1, Start);
        var report = Report(events);

        Assert.Contains(
            "## Heap growth\n\nUnavailable: this capture holds no heap samples across a collection.",
            report.Replace("\r\n", "\n"));
    }

    /// <summary>
    /// Seventy running minutes whose floor rises one megabyte a minute faster each ten-minute window,
    /// in step with Auto Buy's commits while Mentor commits more at a steady rate, then an applied
    /// configuration under which Auto Buy commits nothing, then a stretch under an emergency stop.
    /// </summary>
    private static List<ServiceCycleSemanticEvent> Run(
        int stoppedFloorMegabytesPerMinute,
        int quietMinutes = 0,
        int quietFloorMegabytesPerMinute = 0)
    {
        var events = new List<ServiceCycleSemanticEvent>();
        var lifecycle = ServiceCycleSemanticPayload.LifecycleFact(ServiceCycleTraceFixtures.Service, 2, 0, Start);
        Add(events, ServiceCycleSemanticEventKind.LifecycleActivated, in lifecycle);
        var floor = 100.0;
        var collections = 10;
        long frame = 100;
        var stoppedFrom = RunningMinutes + quietMinutes;
        for (var halfMinute = 0; halfMinute <= 2 * (stoppedFrom + StoppedMinutes); halfMinute++)
        {
            var at = Start + halfMinute * 30 * Second;
            var minute = halfMinute / 2;
            var quiet = minute >= RunningMinutes && minute < stoppedFrom;
            var stopped = minute >= stoppedFrom;
            if (quiet && halfMinute == 2 * RunningMinutes)
            {
                var applied = ServiceCycleSemanticPayload.Publication(false, 2, at);
                Add(events, ServiceCycleSemanticEventKind.ConfigurationPublished, in applied);
            }
            if (stopped && halfMinute == 2 * stoppedFrom)
            {
                var emergency = ServiceCycleSemanticPayload.Emergency((int)EmergencyStopReason.UserRequested, 1, at);
                Add(events, ServiceCycleSemanticEventKind.EmergencyEntered, in emergency);
            }
            var window = minute / 10;
            if (!stopped)
            {
                var steady = ServiceCycleSemanticPayload.ActionFact(
                    Mentor, 9, 11, 0, (int)ServiceActionDisposition.Committed,
                    CommonActionResultCodes.Committed.Value, NativeMutationOutcome.Verified, 1, 1, 1, at, 10, frame);
                for (var commit = 0; commit < 6; commit++)
                    Add(events, ServiceCycleSemanticEventKind.ActionCommitted, in steady);
            }
            if (!stopped && !quiet)
            {
                for (var commit = 0; commit <= window; commit++)
                {
                    var timed = ServiceCycleSemanticPayload.ActionFact(
                        ServiceCycleTraceFixtures.Cycle, 8, 10, 0, (int)ServiceActionDisposition.Committed,
                        CommonActionResultCodes.Committed.Value, NativeMutationOutcome.Verified, 1, 1, 1, at, 10, frame);
                    Add(events, ServiceCycleSemanticEventKind.ActionCommitted, in timed);
                }
            }
            frame++;
            Pump(events, frame, at);
            var floorBytes = (long)(floor * Megabyte);
            collections++;
            var census = halfMinute % 2 == 0
                ? new PumpHeapCensus(
                    floorBytes + 51 * Megabyte, 2, 9_000, 3_000 + 20 * minute, 2_500)
                : (PumpHeapCensus?)null;
            var heap = ServiceCycleSemanticPayload.PumpHeapFact(
                frame, at, collections, collections, floorBytes, floorBytes + Megabyte,
                captureHeapGrowth: (64 + minute) * 1_024L, capturesMeasured: 1, capturesAcrossCollection: 0,
                previousFrameAllocated: null, census: census);
            Add(events, ServiceCycleSemanticEventKind.PumpHeapSampled, in heap);
            floor += (stopped ? stoppedFloorMegabytesPerMinute : quiet ? quietFloorMegabytesPerMinute : window + 1) / 2.0;
        }
        return events;
    }

    private static void Pump(List<ServiceCycleSemanticEvent> events, long frame, long at)
    {
        var payload = ServiceCycleSemanticPayload.Pump(
            frameIdentity: frame,
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
            totalDuration: TimeSpan.TicksPerMillisecond,
            timestampTicks: at);
        Add(events, ServiceCycleSemanticEventKind.PumpCompleted, in payload);
    }

    private static void Add(
        List<ServiceCycleSemanticEvent> events,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload) =>
        events.Add(new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, (ulong)events.Count + 1),
            default,
            kind,
            in payload));

    private static string Report(List<ServiceCycleSemanticEvent> events)
    {
        var roster = new ServiceCycleTraceRoster(new[]
        {
            new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.ServiceKind, ServiceCycleTraceFixtures.Service.Value, "auto-buy", "Auto Buy"),
            new ServiceCycleTraceRosterEntry(ServiceCycleTraceRoster.ServiceKind, 6, "mentor", "Mentor"),
        });
        using var fixture = new ManualFullTraceTestDirectory(events.ToArray(), roster: roster);
        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);
        using var writer = new StringWriter();
        ManualFullTraceHeapGrowthView.Write(writer, session);
        return writer.ToString();
    }
}
