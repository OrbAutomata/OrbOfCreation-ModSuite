using System;
using System.Diagnostics;
using System.Linq;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.WorldCollection;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.WorldCollection;

public sealed class WorldCollectionSpanRegistryTests
{
    /// <summary>
    /// Collection runs four times a second for the whole life of the process and the trace is a
    /// profiling-build session that may never start. Nothing may reach the wire without one.
    /// </summary>
    [Fact]
    public void APassObservedWithNoRecordingSessionRecordsNothing()
    {
        var registry = new WorldCollectionSpanRegistry();
        var recorder = Recorder(out _);

        registry.Observe(Report(("resources", 4, RawTicks(1))), frameIdentity: 7, lifecycleEpoch: 2, At(100));

        Assert.False(registry.IsRecording);
        Assert.Equal(0, recorder.Count);
    }

    [Fact]
    public void EveryCategoryOfAPassBecomesOneSpanCarryingWhatThatPassSpentOnIt()
    {
        var registry = new WorldCollectionSpanRegistry();
        var recorder = Recorder(out var trace);
        Assert.True(registry.TryStartRecording(trace, out var recording));
        using var _ = recording;

        registry.Observe(
            Report(
                ("resources", 40, RawTicks(1)),
                ("scribe relations", 2_628, RawTicks(4)),
                ("effect blocks", 180, 0)),
            frameIdentity: 48_815,
            lifecycleEpoch: 14,
            At(1_000));

        var spans = Spans(recorder);
        Assert.Equal(3, spans.Length);
        Assert.Equal(new[] { 1, 2, 3 }, spans.Select(x => x.Payload.WorldCategory).ToArray());
        Assert.Equal(new[] { 40, 2_628, 180 }, spans.Select(x => x.Payload.WorldCategorySampled).ToArray());
        Assert.All(spans, span => Assert.Equal(3, span.Payload.WorldPassCategories));
        Assert.All(spans, span => Assert.Equal(48_815, span.Payload.FrameIdentity));
        Assert.All(spans, span => Assert.Equal(14UL, span.Payload.Lifecycle));
        Assert.All(spans, span => Assert.Equal(1_000, span.Payload.TimestampTicks));

        // The wire measures every other duration in hundred-nanosecond ticks, so a category that took
        // a millisecond of raw timestamp ticks says a millisecond in those.
        Assert.Equal(TimeSpan.TicksPerMillisecond, spans[0].Payload.DurationTicks);
        Assert.Equal(4 * TimeSpan.TicksPerMillisecond, spans[1].Payload.DurationTicks);
    }

    /// <summary>
    /// A structural category is read once per lifecycle epoch and reused after, and the pass that
    /// reused it spent nothing getting it. Charging it the read that filled the buffer would make the
    /// cheapest categories of a pass render as its dearest — and the category still has rows, so it
    /// is recorded rather than skipped.
    /// </summary>
    [Fact]
    public void AReusedStructuralCategoryIsRecordedAtNothingRatherThanRecharged()
    {
        var registry = new WorldCollectionSpanRegistry();
        var recorder = Recorder(out var trace);
        Assert.True(registry.TryStartRecording(trace, out var recording));
        using var _ = recording;

        registry.Observe(Report(("effect blocks", 180, RawTicks(9))), 1, 1, At(100));
        registry.Observe(Report(("effect blocks", 180, 0)), 2, 1, At(200));

        var spans = Spans(recorder);
        Assert.Equal(2, spans.Length);
        Assert.Equal(9 * TimeSpan.TicksPerMillisecond, spans[0].Payload.DurationTicks);
        Assert.Equal(0, spans[1].Payload.DurationTicks);
        Assert.Equal(180, spans[1].Payload.WorldCategorySampled);
    }

    [Fact]
    public void ASessionThatStoppedRecordsNoFurtherPass()
    {
        var registry = new WorldCollectionSpanRegistry();
        var recorder = Recorder(out var trace);
        Assert.True(registry.TryStartRecording(trace, out var recording));

        registry.Observe(Report(("resources", 1, RawTicks(1))), 1, 1, At(100));
        recording!.Dispose();
        registry.Observe(Report(("resources", 1, RawTicks(1))), 2, 1, At(200));

        Assert.False(registry.IsRecording);
        Assert.Single(Spans(recorder));
    }

    [Fact]
    public void ASecondSessionCannotRecordWhileOneAlreadyIs()
    {
        var registry = new WorldCollectionSpanRegistry();
        Recorder(out var trace);
        Assert.True(registry.TryStartRecording(trace, out var first));
        using var _ = first;

        Assert.False(registry.TryStartRecording(trace, out var second));
        Assert.Null(second);
    }

    /// <summary>
    /// The collector owns the names, so a category added to it is named by that alone. The pseudo
    /// category a degraded pass appends is named too, rather than appearing as a bare identity the
    /// first time a modifier fold has to reconstruct an input.
    /// </summary>
    [Fact]
    public void TheCollectorNamesEveryCategoryASpanCanIdentify()
    {
        var collector = new GameWorldCollector(static (string _) => (Type?)null);
        var registry = new WorldCollectionSpanRegistry();

        registry.PublishCategories(collector.CategoryNames());

        var names = registry.Categories.ToArray();
        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal("modifier folding", names[^1]);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        // Every category a pass can report has a name at the identity its spans carry: a full pass
        // reports one row per reader, and a degraded one appends the pseudo category last.
        Assert.Equal(collector.Collect().Categories.Length, names.Length - 1);
    }

    private static long RawTicks(int milliseconds) => Stopwatch.Frequency * milliseconds / 1000;

    private static MonotonicTimestamp At(long ticks) => new(ticks);

    private static WorldCollectionReport Report(params (string Category, int Sampled, long Ticks)[] categories)
    {
        var reports = new WorldCategoryReport[categories.Length];
        for (var index = 0; index < categories.Length; index++)
        {
            reports[index] = new WorldCategoryReport(
                categories[index].Category,
                WorldCategoryOutcome.Collected,
                categories[index].Sampled,
                skipped: 0,
                firstFailure: string.Empty,
                categories[index].Ticks);
        }
        return new WorldCollectionReport(reports);
    }

    private static ServiceCycleSemanticRecorder Recorder(out ServiceCycleSemanticRuntimeTrace trace)
    {
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(101),
            eventCapacity: 64,
            serviceCapacity: 1,
            enabled: true);
        trace = new ServiceCycleSemanticRuntimeTrace(recorder, 1);
        return recorder;
    }

    private static ServiceCycleSemanticEvent[] Spans(ServiceCycleSemanticRecorder recorder)
    {
        var buffer = new ServiceCycleSemanticEvent[recorder.Count];
        var drain = recorder.DrainSince(default, buffer);
        return buffer
            .Take(drain.Copied)
            .Where(x => x.Kind == ServiceCycleSemanticEventKind.WorldCategoryCollected)
            .ToArray();
    }
}
