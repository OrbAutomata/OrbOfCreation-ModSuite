#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif
using System;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class ServiceActionTimelineProjectionTests
{
    private const long MinuteTicks = TimeSpan.TicksPerMinute;

    [Fact]
    public void KeysFixedMinutesAndStacksCommittedCountsPerService()
    {
        using var registry = Registry(2, out _);
        using var first = registry.Register(new SyntheticServiceDefinition("timeline.first"));
        using var second = registry.Register(new SyntheticServiceDefinition("timeline.second"));
        var projection = ServiceActionTimelineProjection.Create(registry);

        ObserveCommitted(projection, 1, "timeline.first", cycle: 1, committed: 2, 4 * MinuteTicks + 7);
        ObserveCommitted(projection, 2, "timeline.second", cycle: 1, committed: 3, 4 * MinuteTicks + 9);
        ObserveCommitted(projection, 1, "timeline.first", cycle: 2, committed: 5, 5 * MinuteTicks);

        var cells = Copy(projection);
        Assert.Equal(30, cells.Select(cell => cell.MinuteKey).Distinct().Count());
        Assert.Equal(2, Cell(cells, 4, "timeline.first").Committed);
        Assert.Equal(3, Cell(cells, 4, "timeline.second").Committed);
        Assert.Equal(5, Cell(cells, 5, "timeline.first").Committed);
        Assert.Equal(0, Cell(cells, 5, "timeline.second").Committed);
    }

    [Fact]
    public void RetainsExactlyTheLatestThirtyMinuteBuckets()
    {
        using var registry = Registry(1, out _);
        using var registration = registry.Register(new SyntheticServiceDefinition("timeline.retention"));
        var projection = ServiceActionTimelineProjection.Create(registry);
        for (var minute = 1; minute <= 31; minute++)
            ObserveCommitted(
                projection,
                1,
                "timeline.retention",
                (ulong)minute,
                committed: minute,
                minute * MinuteTicks);

        var cells = Copy(projection);
        Assert.Equal(30, cells.Length);
        Assert.Equal(2, cells[0].MinuteKey);
        Assert.Equal(31, cells[^1].MinuteKey);
        Assert.DoesNotContain(cells, cell => cell.MinuteKey == 1);
        Assert.Equal(31, cells[^1].Committed);
    }

    [Fact]
    public void CurrentBucketGrowsOnlyOnCommitAndClosedBucketsNeverRepaint()
    {
        using var registry = Registry(1, out _);
        using var registration = registry.Register(new SyntheticServiceDefinition("timeline.stable"));
        var projection = ServiceActionTimelineProjection.Create(registry);
        ObserveCommitted(projection, 1, "timeline.stable", cycle: 1, committed: 1, 5 * MinuteTicks + 1);
        var beforeRevision = projection.TimelineRevision;
        var before = Copy(projection);

        ObserveWaiting(projection, 1, 5 * MinuteTicks + 2);
        ObserveRejected(projection, 1, "timeline.stable", cycle: 2, 5 * MinuteTicks + 3);
        ObserveSkipped(projection, 1, "timeline.stable", cycle: 3, 5 * MinuteTicks + 4);

        Assert.Equal(beforeRevision, projection.TimelineRevision);
        var noCommit = Cell(Copy(projection), 5, "timeline.stable");
        Assert.Equal(Cell(before, 5, "timeline.stable").Committed, noCommit.Committed);
        Assert.Equal(1, noCommit.Rejected);
        Assert.Equal(1, noCommit.Skipped);

        ObserveCommitted(projection, 1, "timeline.stable", cycle: 4, committed: 2, 5 * MinuteTicks + 5);
        var refreshed = Cell(Copy(projection), 5, "timeline.stable");
        Assert.Equal(3, refreshed.Committed);
        Assert.Equal(1, refreshed.Rejected);
        Assert.Equal(1, refreshed.Skipped);
        var committedRevision = projection.TimelineRevision;

        projection.Advance(new MonotonicTimestamp(6 * MinuteTicks));
        var advancedRevision = projection.TimelineRevision;
        Assert.Equal(committedRevision + 1, advancedRevision);
        projection.Advance(new MonotonicTimestamp(6 * MinuteTicks + 3));
        Assert.Equal(advancedRevision, projection.TimelineRevision);
        ObserveRejected(projection, 1, "timeline.stable", cycle: 5, 5 * MinuteTicks + 4);
        ObserveSkipped(projection, 1, "timeline.stable", cycle: 6, 5 * MinuteTicks + 5);
        ObserveFault(projection, 1, occurrence: 1, 5 * MinuteTicks + 6);
        ObserveCommitted(projection, 1, "timeline.stable", cycle: 7, committed: 9, 5 * MinuteTicks + 7);

        Assert.Equal(advancedRevision, projection.TimelineRevision);
        var closed = Cell(Copy(projection), 5, "timeline.stable");
        Assert.Equal(3, closed.Committed);
        Assert.Equal(1, closed.Rejected);
        Assert.Equal(1, closed.Skipped);
        Assert.Equal(0, closed.FaultedCount);
    }

    [Fact]
    public void OneFaultMarkerRepresentsAnyFaultInTheBucketWithoutRepeatChurn()
    {
        using var registry = Registry(1, out _);
        using var registration = registry.Register(new SyntheticServiceDefinition("timeline.fault"));
        var projection = ServiceActionTimelineProjection.Create(registry);

        ObserveFault(projection, 1, occurrence: 1, 8 * MinuteTicks + 1);
        var revision = projection.TimelineRevision;
        ObserveFault(projection, 1, occurrence: 2, 8 * MinuteTicks + 2);

        var fault = Cell(Copy(projection), 8, "timeline.fault");
        Assert.True(fault.Faulted);
        Assert.Equal(2, fault.FaultedCount);
        Assert.Equal(0, fault.Committed);
        Assert.Equal(revision, projection.TimelineRevision);
    }

    [Fact]
    public void LifecycleChangeClearsEveryRetainedBucket()
    {
        using var registry = Registry(1, out _);
        using var registration = registry.Register(new SyntheticServiceDefinition("timeline.lifecycle"));
        var projection = ServiceActionTimelineProjection.Create(registry);
        ObserveCommitted(projection, 1, "timeline.lifecycle", cycle: 1, committed: 4, 9 * MinuteTicks);
        var priorRevision = projection.TimelineRevision;
        var transition = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.LifecycleChanged,
            new ServiceCycleTraceServiceId(1),
            generation: 2,
            new MonotonicTimestamp(10 * MinuteTicks),
            code: 1);

        projection.ObserveTransition(in transition);
        projection.Advance(new MonotonicTimestamp(10 * MinuteTicks));

        var cells = Copy(projection);
        Assert.All(cells, cell =>
        {
            Assert.Equal(0, cell.Committed);
            Assert.Equal(0, cell.Skipped);
            Assert.Equal(0, cell.Rejected);
            Assert.Equal(0, cell.FaultedCount);
            Assert.False(cell.Faulted);
        });
        Assert.Equal(priorRevision + 1, projection.TimelineRevision);
    }

    [Fact]
    public void RegisteredRosterIncludesEveryShapeAndNamesInfrastructureExplicitly()
    {
        using var registry = Registry(3, out _);
        using var source = registry.RegisterSource(
            new SourceServiceDefinition("looks.player.meaningful"));
        using var first = registry.Register(
            new SyntheticServiceDefinition("contains.world-collection.but-is-ordinary"));
        using var second = registry.Register(
            new SyntheticServiceDefinition("automation.second"));
        var projection = ServiceActionTimelineProjection.Create(registry);
        ObserveCommitted(projection, 3, "automation.second", cycle: 1, committed: 1, MinuteTicks);

        var minute = Copy(projection).Where(cell => cell.MinuteKey == 1).ToArray();

        Assert.Equal(
            new[]
            {
                "looks.player.meaningful",
                "contains.world-collection.but-is-ordinary",
                "automation.second",
            },
            minute.Select(cell => cell.Service.Value));
        Assert.Equal(ServiceShape.Source, minute[0].Shape);
        Assert.All(minute.Skip(1), cell => Assert.Equal(ServiceShape.Ordinary, cell.Shape));
    }

    [Fact]
    public void LiveTimelinePublishesWithoutAConfiguredDiskJournal()
    {
        using var registry = Registry(1, out _);
        using var registration = registry.Register(new SyntheticServiceDefinition("live.service"));
        registry.Seal();
        var source = new ServiceActionTimelineRegistry();
        using var pump = new SuiteFramePump(registry, null, source
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            );

        Assert.Equal(1, source.TimelineServiceCount);
        Assert.Equal(ServiceActionTimelineProjection.TimelineBucketCount, source.TimelineCellCapacity);
    }

    [Fact]
    public void ThePublishedRevisionNeverRunsBackwardWhenTheTimelineLeaves()
    {
        using var registry = Registry(1, out var clock);
        using var registration = registry.Register(new SyntheticServiceDefinition("timeline.leaves"));
        registry.Seal();
        var source = new ServiceActionTimelineRegistry();
        long whileRegistered;
        using (var pump = new SuiteFramePump(registry, null, source
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            ))
        {
            for (var minute = 1; minute <= 3; minute++)
            {
                clock.AdvanceTo(new MonotonicTimestamp(minute * MinuteTicks));
                pump.PumpFrame(minute);
            }
            whileRegistered = source.TimelineRevision;
        }

        Assert.True(source.TimelineRevision > whileRegistered);
    }

    private static ServiceCycleRegistry Registry(int capacity, out ThreadSafeTestClock clock)
    {
        clock = new ThreadSafeTestClock(1);
        return new ServiceCycleRegistry(capacity, new LifecycleGeneration(1), clock);
    }

    private static void ObserveCommitted(
        ServiceActionTimelineProjection projection,
        ulong traceService,
        string service,
        ulong cycle,
        int committed,
        long observedAt)
    {
        var receipt = BatchReceipt.Completed(
            Identity(service, cycle),
            new BatchId(cycle),
            committed,
            new ServiceNativeCallTotals(committed, committed, committed),
            new MonotonicTimestamp(observedAt));
        Observe(projection, traceService, observedAt, in receipt, default);
    }

    private static void ObserveWaiting(
        ServiceActionTimelineProjection projection,
        ulong traceService,
        long observedAt)
    {
        var receipt = default(BatchReceipt);
        Observe(projection, traceService, observedAt, in receipt, default);
    }

    private static void ObserveRejected(
        ServiceActionTimelineProjection projection,
        ulong traceService,
        string service,
        ulong cycle,
        long observedAt)
    {
        var receipt = BatchReceipt.Terminated(
            Identity(service, cycle),
            new BatchId(cycle),
            actionCount: 1,
            committedCount: 0,
            terminalIndex: 0,
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected),
            default,
            new MonotonicTimestamp(observedAt));
        Observe(projection, traceService, observedAt, in receipt, default);
    }

    private static void ObserveSkipped(
        ServiceActionTimelineProjection projection,
        ulong traceService,
        string service,
        ulong cycle,
        long observedAt)
    {
        var receipt = BatchReceipt.Completed(
            Identity(service, cycle),
            new BatchId(cycle),
            actionCount: 1,
            committedCount: 0,
            default,
            new MonotonicTimestamp(observedAt),
            preNativeSkippedCount: 1);
        Observe(projection, traceService, observedAt, in receipt, default);
    }

    private static void ObserveFault(
        ServiceActionTimelineProjection projection,
        ulong traceService,
        int occurrence,
        long observedAt)
    {
        var receipt = default(BatchReceipt);
        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            occurrence,
            new MonotonicTimestamp(observedAt));
        Observe(projection, traceService, observedAt, in receipt, fault);
    }

    private static void Observe(
        ServiceActionTimelineProjection projection,
        ulong traceService,
        long observedAt,
        in BatchReceipt terminal,
        ServiceFault fault)
    {
        var state = default(ServiceStateProjectionSnapshot);
        var observation = new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(traceService),
            lifecycle: 1,
            configuration: 1,
            strategy: terminal.IsPresent ? 1UL : 0UL,
            cycle: terminal.IsPresent ? terminal.Cycle.Cycle.Value : 0UL,
            new MonotonicTimestamp(observedAt),
            new MonotonicTimestamp(observedAt),
            startDecisionCode: terminal.IsPresent ? CommonServiceDecisionCodes.Ready.Value : 0,
            captureDecisionCode: 0,
            hasWake: false,
            default,
            hasProjection: false,
            in state,
            in fault,
            in terminal);
        projection.Observe(in observation);
    }

    private static ServiceCycleIdentity Identity(string service, ulong cycle) => new(
        new ServiceId(service),
        new LifecycleGeneration(1),
        new ConfigGeneration(1),
        new StrategyGeneration(1),
        new WorldGeneration(1),
        new CycleId(cycle));

    private static ServiceActionTimelineCellSnapshot[] Copy(
        ServiceActionTimelineProjection projection)
    {
        var cells = new ServiceActionTimelineCellSnapshot[projection.TimelineCellCapacity];
        var copied = projection.CopyTimelineTo(cells);
        Assert.True(copied.IsComplete);
        Assert.Equal(projection.TimelineCellCapacity, copied.WrittenCount);
        return cells;
    }

    private static ServiceActionTimelineCellSnapshot Cell(
        ServiceActionTimelineCellSnapshot[] cells,
        long minute,
        string service) => cells.Single(cell =>
            cell.MinuteKey == minute && cell.Service.Value == service);

}
