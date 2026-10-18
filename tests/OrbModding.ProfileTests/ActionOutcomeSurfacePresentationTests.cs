using System;
using OrbModConfig;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ActionOutcomeSurfacePresentationTests
{
    [Fact]
    public void PerformanceDebugUsesTheSameStableCommittedWorkTimeline()
    {
        var cells = new[]
        {
            new ServiceActionTimelineCellSnapshot(
                minuteKey: 10,
                new ServiceId("orbautomata.world-collection"),
                ServiceShape.Source,
                committed: 50,
                skipped: 0,
                rejected: 0,
                faulted: 0),
            new ServiceActionTimelineCellSnapshot(
                minuteKey: 10,
                new ServiceId("orbautomata.auto-buy"),
                ServiceShape.Ordinary,
                committed: 2,
                skipped: 0,
                rejected: 0,
                faulted: 0),
            new ServiceActionTimelineCellSnapshot(
                minuteKey: 10,
                new ServiceId("orbautomata.auto-cast"),
                ServiceShape.Ordinary,
                committed: 0,
                skipped: 0,
                rejected: 0,
                faulted: 0),
            new ServiceActionTimelineCellSnapshot(
                minuteKey: 10,
                new ServiceId("orbmentor.mastery-sharing"),
                ServiceShape.Ordinary,
                committed: 200,
                skipped: 0,
                rejected: 0,
                faulted: 1),
        };

        var presentation = ActionOutcomeSurfacePresentation.Build(
            cells,
            serviceCount: 4,
            bucketCount: 1,
            ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty);

        Assert.True(presentation.ShowsTimeline);
        Assert.Equal(2, presentation.MaximumCommitted);
        Assert.False(presentation.Buckets[0].HasFault);
        var legend = Assert.Single(presentation.Legend);
        Assert.Equal("Auto Buy", legend.DisplayName);
        Assert.Equal(ActionOutcomeServiceColor.Amber, legend.Color);
    }

    [Fact]
    public void PerformanceDebugMinuteDetailUsesExactOutcomeTerms()
    {
        var cells = new[]
        {
            new ServiceActionTimelineCellSnapshot(
                minuteKey: 12,
                new ServiceId("orbautomata.auto-buy"),
                ServiceShape.Ordinary,
                committed: 4,
                skipped: 2,
                rejected: 1,
                faulted: 3),
        };

        var presentation = ActionOutcomeSurfacePresentation.Build(
            cells,
            serviceCount: 1,
            bucketCount: 1,
            ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty);

        var detail = Assert.Single(presentation.Buckets[0].Details);
        Assert.Equal("Auto Buy · committed 4 · rejected 1 · skipped 2 · faulted 3", detail.Summary);
        Assert.Equal("Completed actions / minute", ActionOutcomeSurfacePresentation.AxisLabel);
    }

    [Fact]
    public void PerformanceDebugKeepsExactQuietAndTimingLines()
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
            totalDuration: MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(0.250)));
        var timing = new ServiceCyclePumpTimingSample(in report);

        var presentation = ActionOutcomeSurfacePresentation.Build(
            ReadOnlySpan<ServiceActionTimelineCellSnapshot>.Empty,
            0,
            0,
            new[] { timing });

        Assert.False(presentation.ShowsTimeline);
        Assert.Equal(
            "No automation activity in the last 30 minutes",
            presentation.QuietMessage);
        Assert.Equal(
            "Recent processing · average 0.250 ms · worst 0.250 ms",
            presentation.TimingSummary);
    }
}
