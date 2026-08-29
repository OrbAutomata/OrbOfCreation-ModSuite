using System;
using System.IO;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.ManualTrace;
using OrbModding.Tests.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class ManualFullTraceCategoryViewTests
{
    private static readonly ServiceCycleTraceSessionId Session = new(101);

    private static ServiceCycleTraceRoster Roster() =>
        new(new[]
        {
            new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.ServiceKind, 1, "orbautomata.world-collection", "World collection"),
            new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.WorldCategoryKind, 1, "scribe relations", string.Empty),
            new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.WorldCategoryKind, 2, "effect blocks", string.Empty),
        });

    /// <summary>
    /// The view exists to end the argument about which part of collection is expensive, so the
    /// distribution has to be the session's rather than one pass's: a category whose worst pass is
    /// ten times its median is a category with a spike, and an average alone hides that in both
    /// directions.
    /// </summary>
    [Fact]
    public void ACategorySaysWhatItCostAcrossEveryPassOfTheSession()
    {
        using var fixture = new ManualFullTraceTestDirectory(Passes(), roster: Roster());

        var report = Report(fixture);

        Assert.Contains("5 span(s) over 5 collection pass(es)", report);
        // Total 20 ms over five passes, sorted [1, 2, 3, 4, 10]: average 4, median 3, worst 10.
        Assert.Contains("| scribe relations (1) | 5 | 20.000 | 4.000 | 3.000 | 10.000 | 16 | 2,628 |", report);
    }

    /// <summary>
    /// A structural category is read once per lifecycle epoch, so most of its passes charge nothing
    /// and its rows persist. Both halves have to survive the fold, or the cheapest category in a pass
    /// reads as its dearest.
    /// </summary>
    [Fact]
    public void AReusedCategoryReadsAsFreeWithItsRowsIntact()
    {
        using var fixture = new ManualFullTraceTestDirectory(PassesWithAReusedCategory(), roster: Roster());

        var report = Report(fixture);

        Assert.Contains("| effect blocks (2) | 2 | 9.000 | 4.500 | 4.500 | 9.000 | 180 | 180 |", report);
        // Sorted by total cost, so the expensive category is the first row read.
        Assert.True(
            report.IndexOf("| scribe relations (1) |", StringComparison.Ordinal) <
            report.IndexOf("| effect blocks (2) |", StringComparison.Ordinal));
    }

    /// <summary>
    /// A pass says how many categories it reported, so a pass that shows fewer spans than that is a
    /// pass whose records were lost. It is named rather than thrown on: a capture that ended
    /// incomplete truncates its last pass legitimately, and refusing the other forty minutes over
    /// that would cost strictly more than the half-pass does.
    /// </summary>
    [Fact]
    public void APassMissingSpansIsNamedRatherThanQuietlyUnderCounted()
    {
        using var fixture = new ManualFullTraceTestDirectory(ATruncatedPass(), roster: Roster());

        var report = Report(fixture);

        Assert.Contains("**Incomplete:**", report);
        Assert.Contains("1 of 2 pass(es) carry fewer spans than the categories they reported", report);
    }

    /// <summary>
    /// Every capture recorded before spans existed is one of these, and the record kind is appended
    /// rather than a format change, so they stay entirely readable.
    /// </summary>
    [Fact]
    public void ACaptureRecordedBeforeSpansExistedStillReadsCleanEndToEnd()
    {
        using var fixture = new ManualFullTraceTestDirectory(WithoutSpans(), roster: Roster());

        var report = Report(fixture);

        Assert.Contains("## World collection categories", report);
        Assert.Contains("This capture holds no collection spans", report);
        // The rest of the report is unaffected: the same views, from the same records.
        Assert.Contains("## Service view", report);
        Assert.Contains("# ServiceCycle manual full-trace report", report);
    }

    /// <summary>
    /// A forty-minute capture is a quarter of a million spans, and a timeline carrying them is one
    /// nobody can read. The aggregate above is the form they answer in, and the events that describe a
    /// sequence keep the view that is about sequence.
    /// </summary>
    [Fact]
    public void SpansStayOutOfTheEventTimelineTheyWouldOtherwiseBury()
    {
        using var fixture = new ManualFullTraceTestDirectory(Passes(), roster: Roster());

        var report = Report(fixture);

        Assert.Contains("| scribe relations (1) |", report);
        Assert.DoesNotContain("— WorldCategoryCollected", report);
        Assert.Contains(
            "No semantic events outside the pump and collection-span summaries were recorded.",
            report);
    }

    private static ServiceCycleSemanticEvent[] Passes()
    {
        var milliseconds = new[] { 1d, 3d, 10d, 2d, 4d };
        var sampled = new[] { 2_628, 2_628, 2_628, 16, 16 };
        var events = new ServiceCycleSemanticEvent[milliseconds.Length];
        for (var index = 0; index < milliseconds.Length; index++)
        {
            events[index] = Span(
                sequence: (ulong)index + 1,
                category: 1,
                sampled: sampled[index],
                passCategories: 1,
                frame: 100 + index,
                timestampTicks: 1_000 * (index + 1),
                milliseconds: milliseconds[index]);
        }
        return events;
    }

    private static ServiceCycleSemanticEvent[] PassesWithAReusedCategory() => new[]
    {
        Span(1, category: 1, sampled: 2_628, passCategories: 2, frame: 100, timestampTicks: 1_000, milliseconds: 12),
        Span(2, category: 2, sampled: 180, passCategories: 2, frame: 100, timestampTicks: 1_000, milliseconds: 9),
        Span(3, category: 1, sampled: 2_628, passCategories: 2, frame: 101, timestampTicks: 2_000, milliseconds: 11),
        Span(4, category: 2, sampled: 180, passCategories: 2, frame: 101, timestampTicks: 2_000, milliseconds: 0),
    };

    private static ServiceCycleSemanticEvent[] ATruncatedPass() => new[]
    {
        Span(1, category: 1, sampled: 2_628, passCategories: 2, frame: 100, timestampTicks: 1_000, milliseconds: 12),
        Span(2, category: 2, sampled: 180, passCategories: 2, frame: 100, timestampTicks: 1_000, milliseconds: 9),
        Span(3, category: 1, sampled: 2_628, passCategories: 2, frame: 101, timestampTicks: 2_000, milliseconds: 11),
    };

    private static ServiceCycleSemanticEvent[] WithoutSpans()
    {
        var events = ServiceCycleTraceFixtures.EveryEventKind();
        return Array.FindAll(
            events,
            item => item.Kind != ServiceCycleSemanticEventKind.WorldCategoryCollected);
    }

    private static ServiceCycleSemanticEvent Span(
        ulong sequence,
        int category,
        int sampled,
        int passCategories,
        long frame,
        long timestampTicks,
        double milliseconds)
    {
        var payload = ServiceCycleSemanticPayload.WorldCategoryFact(
            category,
            sampled,
            passCategories,
            lifecycle: 14,
            frameIdentity: frame,
            timestampTicks: timestampTicks,
            durationTicks: (long)(milliseconds * TimeSpan.TicksPerMillisecond));
        return new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(Session, sequence),
            default,
            ServiceCycleSemanticEventKind.WorldCategoryCollected,
            in payload);
    }

    private static string Report(ManualFullTraceTestDirectory fixture)
    {
        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);
        using var writer = new StringWriter();
        ManualFullTraceReport.Write(writer, session);
        return writer.ToString();
    }
}
