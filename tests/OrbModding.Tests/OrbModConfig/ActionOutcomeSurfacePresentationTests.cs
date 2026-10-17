using System;
using System.Linq;
using System.Text;
using OrbModConfig;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class ActionOutcomeSurfacePresentationTests
{
    [Fact]
    public void ReleaseTimelineChartsOnlyCommittedAutomationAndLegendsOnlyActiveServices()
    {
        var presentation = Build(
            Bucket(10,
                Cell("orbautomata.world-collection", ServiceShape.Source, 20),
                Cell("orbautomata.auto-buy", ServiceShape.Ordinary, 2, rejected: 1),
                Cell("orbautomata.auto-cast", ServiceShape.Ordinary, 0),
                Cell("orbmentor.mastery-sharing", ServiceShape.Ordinary, 200, faulted: 1)),
            Bucket(11,
                Cell("orbautomata.world-collection", ServiceShape.Source, 40),
                Cell("orbautomata.auto-buy", ServiceShape.Ordinary, 1),
                Cell("orbautomata.auto-cast", ServiceShape.Ordinary, 0),
                Cell("orbmentor.mastery-sharing", ServiceShape.Ordinary, 300)));

        Assert.Equal("Automation activity · last 30 minutes", ActionOutcomeSurfacePresentation.Title);
        Assert.Equal("Completed actions / minute", ActionOutcomeSurfacePresentation.AxisLabel);
        Assert.True(presentation.ShowsTimeline);
        Assert.Equal(2, presentation.MaximumCommitted);
        Assert.Equal(new long[] { 2, 1 }, presentation.Buckets.Select(bucket => bucket.Committed));
        Assert.DoesNotContain(presentation.Buckets, bucket => bucket.HasFault);
        var legend = Assert.Single(presentation.Legend);
        Assert.Equal("Auto Buy", legend.DisplayName);
        Assert.Equal(ActionOutcomeServiceColor.Amber, legend.Color);
        Assert.DoesNotContain(
            presentation.Legend,
            entry => string.Equals(entry.DisplayName, "World collection", StringComparison.Ordinal));
        Assert.DoesNotContain(
            presentation.Legend,
            entry => string.Equals(entry.DisplayName, "Auto Cast", StringComparison.Ordinal));
        Assert.DoesNotContain(
            presentation.Legend,
            entry => string.Equals(entry.DisplayName, "Mentor", StringComparison.Ordinal));
        Assert.DoesNotContain(
            presentation.Buckets.SelectMany(bucket => bucket.Stacks),
            stack => string.Equals(
                stack.Service.Value,
                "orbmentor.mastery-sharing",
                StringComparison.Ordinal));
        var detail = Assert.Single(presentation.Buckets[0].Details);
        Assert.Equal("Auto Buy · 2 completed · 1 not applied", detail.Summary);
        Assert.False(ActionOutcomeTimelineServicePolicy.Includes(
            new ServiceId("orbmentor.mastery-sharing"),
            ServiceShape.Ordinary));
        Assert.False(ActionOutcomeTimelineServicePolicy.Includes(
            new ServiceId("orbautomata.strategist"),
            ServiceShape.Ordinary));
    }

    [Fact]
    public void ReleaseMinuteDetailsLabelEveryAvailableOutcomeWithoutClaimingRejectedWorkLanded()
    {
        var presentation = Build(Bucket(24,
            Cell(
                "orbautomata.auto-buy",
                ServiceShape.Ordinary,
                committed: 4,
                skipped: 2,
                rejected: 1,
                faulted: 3),
            Cell(
                "orbautomata.auto-cast",
                ServiceShape.Ordinary,
                committed: 0,
                rejected: 2),
            Cell(
                "orbmentor.mastery-sharing",
                ServiceShape.Ordinary,
                committed: 100,
                rejected: 9)));

        Assert.Equal(
            new[]
            {
                "Auto Buy · 4 completed · 1 not applied · 2 skipped · 3 failed",
                "Auto Cast · 2 not applied",
            },
            presentation.Buckets[0].Details.Select(detail => detail.Summary));
        Assert.Equal("No automation outcomes in this minute", ActionOutcomeSurfacePresentation.EmptyMinute);
        Assert.DoesNotContain(
            presentation.Buckets[0].Details,
            detail => detail.Service.Value == "orbmentor.mastery-sharing");
    }

    [Fact]
    public void ServiceColorsAndStackOrderAreStableInsteadOfFollowingActivityOrder()
    {
        var buyFirst = Build(Bucket(20,
            Cell("orbautomata.auto-buy", ServiceShape.Ordinary, 2),
            Cell("orbautomata.auto-harvest", ServiceShape.Ordinary, 3)));
        var harvestFirst = Build(Bucket(20,
            Cell("orbautomata.auto-harvest", ServiceShape.Ordinary, 3),
            Cell("orbautomata.auto-buy", ServiceShape.Ordinary, 2)));

        Assert.Equal(ActionOutcomeServiceColor.Leaf,
            ActionOutcomeSurfacePresentation.ColorFor(new ServiceId("orbautomata.auto-harvest")));
        Assert.Equal(ActionOutcomeServiceColor.Amber,
            ActionOutcomeSurfacePresentation.ColorFor(new ServiceId("orbautomata.auto-buy")));
        Assert.Equal(
            buyFirst.Buckets[0].Stacks.Select(stack => (stack.Service.Value, stack.Color)),
            harvestFirst.Buckets[0].Stacks.Select(stack => (stack.Service.Value, stack.Color)));
        Assert.Equal(
            buyFirst.Legend.Select(entry => (entry.DisplayName, entry.Color)),
            harvestFirst.Legend.Select(entry => (entry.DisplayName, entry.Color)));

        var everyService = Build(Bucket(21,
            Cell("orbautomata.auto-harvest", ServiceShape.Ordinary, 1),
            Cell("orbautomata.auto-buy", ServiceShape.Ordinary, 1),
            Cell("orbautomata.spell-level", ServiceShape.Ordinary, 1),
            Cell("orbautomata.auto-cast", ServiceShape.Ordinary, 1),
            Cell("orbautomata.auto-concept", ServiceShape.Ordinary, 1),
            Cell("orbautomata.auto-items", ServiceShape.Ordinary, 1),
            Cell("orbautomata.auto-scribe", ServiceShape.Ordinary, 1),
            Cell("orbmentor.mastery-sharing", ServiceShape.Ordinary, 1)));
        Assert.Equal(
            new[]
            {
                ("Auto Harvest", ActionOutcomeServiceColor.Leaf),
                ("Auto Buy", ActionOutcomeServiceColor.Amber),
                ("Spell Leveling", ActionOutcomeServiceColor.Sky),
                ("Auto Cast", ActionOutcomeServiceColor.Violet),
                ("Auto Concept", ActionOutcomeServiceColor.Cyan),
                ("Auto Items", ActionOutcomeServiceColor.Orange),
                ("Auto Scribe", ActionOutcomeServiceColor.Rose),
            },
            everyService.Legend.Select(entry => (entry.DisplayName, entry.Color)));
        Assert.DoesNotContain(
            everyService.Legend.GroupBy(entry => entry.Color),
            group => group.Skip(1).Any());
    }

    [Fact]
    public void EmptyWindowIsOneCalmLineWhileFaultOnlyBucketsRetainTheirShapeMarker()
    {
        var quiet = Build(Bucket(30,
            Cell("orbautomata.auto-buy", ServiceShape.Ordinary, 0),
            Cell("orbautomata.auto-cast", ServiceShape.Ordinary, 0)));
        var faulted = Build(Bucket(30,
            Cell("orbautomata.auto-buy", ServiceShape.Ordinary, 0, faulted: 1),
            Cell("orbautomata.auto-cast", ServiceShape.Ordinary, 0)));

        Assert.False(quiet.ShowsTimeline);
        Assert.Equal(
            "No automation activity in the last 30 minutes",
            quiet.QuietMessage);
        Assert.DoesNotContain(quiet.Legend, _ => true);
        Assert.True(faulted.ShowsTimeline);
        Assert.True(faulted.Buckets[0].HasFault);
        Assert.Equal(0, faulted.Buckets[0].Committed);
        Assert.DoesNotContain(faulted.Legend, _ => true);
    }

    [Fact]
    public void ANoCommitCycleProducesAByteIdenticalPresentation()
    {
        // The view only rebuilds this presentation when the Common timeline revision changes.
        // Rebuilding the same revision snapshot must remain byte-for-byte stable.
        var timeline = new[]
        {
            Bucket(40,
                Cell("orbautomata.auto-harvest", ServiceShape.Ordinary, 2),
                Cell("orbautomata.auto-buy", ServiceShape.Ordinary, 0)),
        };
        var before = Build(timeline);
        var after = Build(timeline);

        Assert.Equal(Bytes(before), Bytes(after));
    }

    [Fact]
    public void SlimTimingLineRemainsAndEveryCardRailPresentationTypeIsGone()
    {
        var presentation = ActionOutcomeSurfacePresentation.Build(
            ReadOnlySpan<ServiceActionTimelineCellSnapshot>.Empty,
            serviceCount: 0,
            bucketCount: 0,
            new[] { Timing(0.125), Timing(0.500) });
        var assembly = typeof(ActionOutcomeSurfacePresentation).Assembly;

        Assert.Equal(
            "Recent processing · average 0.312 ms · worst 0.500 ms",
            presentation.TimingSummary);
        Assert.Equal(
            ActionOutcomeSurfacePresentation.EmptyTiming,
            ActionOutcomeSurfacePresentation.Build(
                ReadOnlySpan<ServiceActionTimelineCellSnapshot>.Empty,
                0,
                0,
                ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty).TimingSummary);
        Assert.NotNull(assembly.GetType("OrbModConfig.ActionOutcomeView"));
        Assert.NotNull(assembly.GetType("OrbModConfig.ActionOutcomeTimelineGraphic"));
        Assert.Null(assembly.GetType("OrbModConfig.ActionOutcomeRowPresentation"));
        Assert.Null(assembly.GetType("OrbModConfig.ActionOutcomeTone"));
        Assert.Null(assembly.GetType("OrbModConfig.PumpTimingGraphView"));
    }

    private static ActionOutcomeSurfacePresentation Build(params CellSpec[][] buckets)
    {
        if (buckets.Length == 0)
        {
            return ActionOutcomeSurfacePresentation.Build(
                ReadOnlySpan<ServiceActionTimelineCellSnapshot>.Empty,
                0,
                0,
                ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty);
        }
        var serviceCount = buckets[0].Length;
        var cells = new ServiceActionTimelineCellSnapshot[checked(buckets.Length * serviceCount)];
        for (var bucket = 0; bucket < buckets.Length; bucket++)
        {
            Assert.Equal(serviceCount, buckets[bucket].Length);
            for (var service = 0; service < serviceCount; service++)
            {
                var spec = buckets[bucket][service];
                cells[bucket * serviceCount + service] = new ServiceActionTimelineCellSnapshot(
                    spec.Minute,
                    new ServiceId(spec.Service),
                    spec.Shape,
                    spec.Committed,
                    spec.Skipped,
                    spec.Rejected,
                    spec.Faulted);
            }
        }
        return ActionOutcomeSurfacePresentation.Build(
            cells,
            serviceCount,
            buckets.Length,
            ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty);
    }

    private static CellSpec[] Bucket(long minute, params CellSpec[] cells)
    {
        for (var index = 0; index < cells.Length; index++) cells[index].Minute = minute;
        return cells;
    }

    private static CellSpec Cell(
        string service,
        ServiceShape shape,
        long committed,
        long skipped = 0,
        long rejected = 0,
        long faulted = 0) => new(service, shape, committed, skipped, rejected, faulted);

    private static byte[] Bytes(ActionOutcomeSurfacePresentation presentation)
    {
        var text = ActionOutcomeSurfacePresentation.Title + "\n" +
            presentation.ShowsTimeline + "\n" +
            presentation.QuietMessage + "\n" +
            presentation.MaximumCommitted + "\n" +
            presentation.TimingSummary + "\n" +
            string.Join("\n", presentation.Buckets.Select(bucket =>
                bucket.MinuteKey + ":" + bucket.Committed + ":" + bucket.HasFault + ":" +
                string.Join(",", bucket.Stacks.Select(stack =>
                    stack.Service.Value + ":" + stack.Color + ":" + stack.Committed)) + ":" +
                string.Join(",", bucket.Details.Select(detail =>
                    detail.Service.Value + ":" + detail.Color + ":" + detail.Summary)))) + "\n" +
            string.Join("\n", presentation.Legend.Select(entry =>
                entry.Service.Value + ":" + entry.DisplayName + ":" + entry.Color));
        return Encoding.UTF8.GetBytes(text);
    }

    private static ServiceCyclePumpTimingSample Timing(double milliseconds)
    {
        var report = new SuiteFramePumpReport(
            frameIdentity: 1,
            accepted: true,
            startingOrdinal: 0,
            responsesAcquired: 0,
            actionsAttempted: 0,
            capturesAttempted: 0,
            cyclesStarted: 0,
            worldGateDeferrals: 0,
            emergencyBatchesRejected: 0,
            lifecyclePositionTransitions: 0,
            responseDuration: default,
            actionDuration: default,
            captureDuration: default,
            totalDuration: MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)));
        return new ServiceCyclePumpTimingSample(in report);
    }

    private sealed class CellSpec
    {
        internal CellSpec(
            string service,
            ServiceShape shape,
            long committed,
            long skipped,
            long rejected,
            long faulted)
        {
            Service = service;
            Shape = shape;
            Committed = committed;
            Skipped = skipped;
            Rejected = rejected;
            Faulted = faulted;
        }

        internal long Minute { get; set; }
        internal string Service { get; }
        internal ServiceShape Shape { get; }
        internal long Committed { get; }
        internal long Skipped { get; }
        internal long Rejected { get; }
        internal long Faulted { get; }
    }
}
