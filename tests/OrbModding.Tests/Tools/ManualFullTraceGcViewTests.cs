using System;
using System.Collections.Generic;
using System.IO;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.ManualTrace;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class ManualFullTraceGcViewTests
{
    private static readonly ServiceCycleTraceSessionId Session = new(101);
    private const long Millisecond = TimeSpan.TicksPerMillisecond;
    private const long Kilobyte = 1_024;
    private const long Megabyte = 1_048_576;

    /// <summary>
    /// Where a collection completed is who paid for its pause: between the previous pump's end and
    /// this one's start is the game's part of the frame, between this pump's ends is the suite's.
    /// </summary>
    [Fact]
    public void EachHitchSaysWhichSideOfTheFrameItsCollectionCompletedIn()
    {
        var report = Report(TwoHitches());

        Assert.Contains("- Collections: 2 (42.9 per minute) — 1 completed inside a suite pump, 1 in the game's part of a frame", report);
        Assert.Contains("| During the suite's pump | 1 | 1300.0 |", report);
        Assert.Contains("| During the game's part of the frame | 1 | 1300.0 |", report);
        Assert.Contains("| No collection | 0 | 0.0 |", report);
        Assert.Contains("| 2800.0 | 5 | 1300.0 | 1200.0 | During the suite's pump |", report);
        Assert.Contains("| 1400.0 | 3 | 1300.0 | 6.0 | During the game's part of the frame |", report);
    }

    /// <summary>
    /// The suite's share of what was allocated is its share of how often collections run. Only frames
    /// no collection touched say what was allocated, because a collection frees the game's garbage and
    /// the suite's alike.
    /// </summary>
    [Fact]
    public void TheSuitesShareComesOnlyFromFramesNoCollectionTouched()
    {
        var report = Report(TwoHitches());

        Assert.Contains(
            "- Heap growth over 2 frames with no collection: 5.00 MB/s in all, of which the suite 1.25 MB/s (25.0%)",
            report);
        Assert.Contains(
            "  - inside suite pumps 1.25 MB/s, collection's worker between pumps 0.00 MB/s, " +
            "other services' workers between pumps 0.00 MB/s",
            report);
        Assert.Contains("- Captures: 0.1 MB over 1 measured (64.0 KB each); 1 crossed a collection and are unknown", report);
        Assert.Contains("- Unity's per-frame allocation counter: unavailable in this player", report);
        Assert.Contains(
            "- Frame time with a collection: 1300.0 ms average over 2 frames, worst 1300.0 ms; " +
            "without: 100.0 ms average over 2 frames, worst 100.0 ms",
            report);
        Assert.Contains("| 42.9 | 5.00 | 1.25 |", report);
    }

    /// <summary>
    /// The suite's workers allocate between pumps, in the game's part of the frame. Whatever that part
    /// grew beyond the game's own rate while a worker was evaluating is the worker's, and an evaluation
    /// is recorded only after the frames it overlapped.
    /// </summary>
    [Fact]
    public void GrowthBetweenPumpsBeyondTheGamesOwnRateIsChargedToTheWorkerThatWasEvaluating()
    {
        var events = new List<ServiceCycleSemanticEvent>();
        var collection = new ServiceCycleTraceServiceId(1);
        var other = new ServiceCycleTraceServiceId(2);
        var start = 100 * Megabyte;
        Frame(events, frame: 1, atMs: 1_000, pumpMs: 5, collections: (10, 10), heap: (start, start));
        Frame(events, frame: 2, atMs: 1_100, pumpMs: 5, collections: (10, 10),
            heap: (start + 95 * Kilobyte, start + 95 * Kilobyte));
        Capture(events, collection, atMs: 1_110);
        Frame(events, frame: 3, atMs: 1_200, pumpMs: 5, collections: (10, 10),
            heap: (start + 490 * Kilobyte, start + 490 * Kilobyte));
        Frame(events, frame: 4, atMs: 1_300, pumpMs: 5, collections: (10, 10),
            heap: (start + 685 * Kilobyte, start + 685 * Kilobyte));
        Frame(events, frame: 5, atMs: 1_400, pumpMs: 5, collections: (10, 10),
            heap: (start + 780 * Kilobyte, start + 780 * Kilobyte));
        Evaluation(events, collection, fromMs: 1_120, toMs: 1_180);
        Evaluation(events, other, fromMs: 1_210, toMs: 1_250);

        var report = Report(events);

        Assert.Contains(
            "- Heap growth over 4 frames with no collection: 1.90 MB/s in all, of which the suite 0.98 MB/s (51.3%)",
            report);
        Assert.Contains(
            "  - inside suite pumps 0.00 MB/s, collection's worker between pumps 0.73 MB/s, " +
            "other services' workers between pumps 0.24 MB/s",
            report);
        Assert.Contains("  - the game alone 0.98 MB/s, read from the game's part of frames no suite worker overlapped", report);
        Assert.Contains("| 1.90 | 0.98 |", report);
    }

    /// <summary>
    /// A build that samples no heap, or a capture from before it did, has no answer rather than an
    /// answer of zero.
    /// </summary>
    [Fact]
    public void ACaptureWithoutHeapSamplesSaysTheSectionIsUnavailable()
    {
        var events = new List<ServiceCycleSemanticEvent>();
        Pump(events, frame: 1, at: 1_000 * Millisecond, pumpMs: 5);

        var report = Report(events);

        Assert.Contains("Unavailable: this capture holds no heap samples.", report);
        Assert.Contains("| — | — | — |", report);
    }

    private static List<ServiceCycleSemanticEvent> TwoHitches()
    {
        var events = new List<ServiceCycleSemanticEvent>();
        Frame(events, frame: 1, atMs: 1_000, pumpMs: 5, collections: (10, 10),
            heap: (100 * Megabyte, 100 * Megabyte + 128 * Kilobyte), captureGrowth: 64 * Kilobyte, measured: 1);
        Frame(events, frame: 2, atMs: 1_100, pumpMs: 5, collections: (10, 10),
            heap: (100 * Megabyte + 512 * Kilobyte, 100 * Megabyte + 640 * Kilobyte));
        Frame(events, frame: 3, atMs: 2_400, pumpMs: 6, collections: (11, 11),
            heap: (60 * Megabyte, 60 * Megabyte + 128 * Kilobyte));
        Frame(events, frame: 4, atMs: 2_500, pumpMs: 5, collections: (11, 11),
            heap: (60 * Megabyte + 512 * Kilobyte, 60 * Megabyte + 640 * Kilobyte));
        Frame(events, frame: 5, atMs: 3_800, pumpMs: 1_200, collections: (11, 12),
            heap: (61 * Megabyte, 50 * Megabyte), across: 1);
        return events;
    }

    private static void Frame(
        List<ServiceCycleSemanticEvent> events,
        long frame,
        long atMs,
        double pumpMs,
        (int Start, int End) collections,
        (long Start, long End) heap,
        long captureGrowth = 0,
        int measured = 0,
        int across = 0)
    {
        var at = atMs * Millisecond;
        Pump(events, frame, at, pumpMs);
        var payload = ServiceCycleSemanticPayload.PumpHeapFact(
            frame, at, collections.Start, collections.End, heap.Start, heap.End, captureGrowth, measured, across,
            previousFrameAllocated: null);
        Add(events, ServiceCycleSemanticEventKind.PumpHeapSampled, in payload);
    }

    private static void Capture(List<ServiceCycleSemanticEvent> events, ServiceCycleTraceServiceId service, long atMs)
    {
        var identity = new ServiceCycleTraceCaptureIdentity(service, 1, 1, 1, 1);
        var payload = ServiceCycleSemanticPayload.CaptureFact(
            in identity, strategyGeneration: 1, CommonServiceDecisionCodes.Captured.Value, atMs * Millisecond, durationTicks: Millisecond, frameIdentity: 2);
        Add(events, ServiceCycleSemanticEventKind.CaptureCompleted, in payload);
    }

    private static void Evaluation(
        List<ServiceCycleSemanticEvent> events,
        ServiceCycleTraceServiceId service,
        long fromMs,
        long toMs)
    {
        var identity = new ServiceCycleTraceCycleIdentity(service, 1, 1, 1, 1, 1);
        var payload = ServiceCycleSemanticPayload.EvaluationCompleted(
            in identity, 0, WakePolicy.Immediate, toMs * Millisecond, (toMs - fromMs) * Millisecond);
        Add(events, ServiceCycleSemanticEventKind.EvaluationCompleted, in payload);
    }

    private static void Pump(List<ServiceCycleSemanticEvent> events, long frame, long at, double pumpMs)
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
            totalDuration: (long)(pumpMs * Millisecond),
            timestampTicks: at);
        Add(events, ServiceCycleSemanticEventKind.PumpCompleted, in payload);
    }

    private static void Add(
        List<ServiceCycleSemanticEvent> events,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload) =>
        events.Add(new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(Session, (ulong)events.Count + 1),
            default,
            kind,
            in payload));

    private static string Report(List<ServiceCycleSemanticEvent> events)
    {
        using var fixture = new ManualFullTraceTestDirectory(events.ToArray());
        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);
        using var writer = new StringWriter();
        ManualFullTraceReport.Write(writer, session);
        return writer.ToString();
    }
}
