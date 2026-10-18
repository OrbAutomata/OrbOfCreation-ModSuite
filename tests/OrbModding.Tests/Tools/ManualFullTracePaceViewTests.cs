using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.ManualTrace;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class ManualFullTracePaceViewTests
{
    private static readonly ServiceCycleTraceSessionId Session = new(101);
    private const long Second = TimeSpan.TicksPerSecond;
    private const long Millisecond = TimeSpan.TicksPerMillisecond;

    /// <summary>
    /// The view answers "what did each pace cost", so a settings save that changed something else is
    /// still the same stretch, and a pace change starts a new one at the save that made it.
    /// </summary>
    [Fact]
    public void EachPaceIsOneStretchFromTheSaveThatChoseItToTheSaveThatChangedIt()
    {
        var events = new List<ServiceCycleSemanticEvent>();
        Configuration(events, generation: 1, at: 0);
        Pump(events, at: 1 * Second, captures: 1, captureMs: 30, totalMs: 40);
        Pump(events, at: 2 * Second, captures: 1, captureMs: 30, totalMs: 40);
        Configuration(events, generation: 2, at: 2_500 * Millisecond);
        Pump(events, at: 3 * Second, captures: 1, captureMs: 30, totalMs: 40);
        Configuration(events, generation: 3, at: 4 * Second);
        Pump(events, at: 5 * Second, captures: 0, captureMs: 0, totalMs: 1);
        Pump(events, at: 6 * Second, captures: 0, captureMs: 0, totalMs: 1);
        Pump(events, at: 14 * Second, captures: 1, captureMs: 30, totalMs: 31);
        using var fixture = new ManualFullTraceTestDirectory(events.ToArray());
        Store(fixture, 1, "Fastest");
        Store(fixture, 2, "Fastest");
        Store(fixture, 3, "CpuSaver");

        var report = Report(fixture);

        Assert.Contains("| 0.000 | Fastest | 3.0 | 3 | 1.00 | 30.000 | 1.00 | 40.000 | — | — | — |", report);
        Assert.Contains("| 4000.000 | CpuSaver | 10.0 | 1 | 0.10 | 30.000 | 0.30 | 11.000 | — | — | — |", report);
    }

    /// <summary>
    /// A capture from a build without the setting has no pace to name, and still has its cost.
    /// </summary>
    [Fact]
    public void ACaptureWithoutThePaceStillReportsWhatCollectionCost()
    {
        var events = new List<ServiceCycleSemanticEvent>();
        Configuration(events, generation: 1, at: 0);
        Pump(events, at: 1 * Second, captures: 1, captureMs: 30, totalMs: 40);
        using var fixture = new ManualFullTraceTestDirectory(events.ToArray());

        var report = Report(fixture);

        Assert.Contains("| 0.000 | unrecorded | 1.0 | 1 | 1.00 | 30.000 | 1.00 | 40.000 | — | — | — |", report);
    }

    private static void Configuration(List<ServiceCycleSemanticEvent> events, ulong generation, long at)
    {
        var payload = ServiceCycleSemanticPayload.Publication(false, generation, at);
        events.Add(new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(Session, (ulong)events.Count + 1),
            default,
            ServiceCycleSemanticEventKind.ConfigurationPublished,
            in payload));
    }

    private static void Pump(
        List<ServiceCycleSemanticEvent> events,
        long at,
        int captures,
        double captureMs,
        double totalMs)
    {
        var payload = ServiceCycleSemanticPayload.Pump(
            frameIdentity: events.Count + 1,
            accepted: true,
            startingOrdinal: 0,
            responsesAcquired: 0,
            actionsAttempted: 0,
            capturesAttempted: captures,
            cyclesStarted: captures,
            worldGateDeferrals: 0,
            emergencyBatchesRejected: 0,
            lifecycleTransitions: 0,
            responseDuration: 0,
            actionDuration: 0,
            captureDuration: (long)(captureMs * Millisecond),
            totalDuration: (long)(totalMs * Millisecond),
            timestampTicks: at);
        events.Add(new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(Session, (ulong)events.Count + 1),
            default,
            ServiceCycleSemanticEventKind.PumpCompleted,
            in payload));
    }

    private static void Store(ManualFullTraceTestDirectory fixture, ulong generation, string pace)
    {
        var hex = generation.ToString("x16", CultureInfo.InvariantCulture);
        File.WriteAllText(
            Path.Combine(fixture.SessionPath, "configuration-" + hex + ".oscv"),
            $"OSCV 1 configuration {hex}\nGeneral.Enabled = true\nGeneral.Pace = {pace}\n");
    }

    private static string Report(ManualFullTraceTestDirectory fixture)
    {
        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);
        using var writer = new StringWriter();
        ManualFullTraceReport.Write(writer, session);
        return writer.ToString();
    }
}
