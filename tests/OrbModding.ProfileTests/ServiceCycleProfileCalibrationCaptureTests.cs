using OrbModding.TestSupport;
using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileCalibrationCaptureTests
{
    [Fact]
    public void CaptureUsesRawMidpointAndSameThreadMonotonicTime()
    {
        var rawClock = new ScriptedProfileRawClock(1_000, new long[] { 100, 106 });
        var capability = UnavailableCapability();

        var point = ServiceCycleProfileCalibrationPoint.Capture(
            rawClock,
            new FixedProfileMonotonicClock(250),
            ServiceCycleProfileTestData.BuildId,
            traceActive: true,
            in capability);

        Assert.Equal(103, point.Calibration.RawTimestamp);
        Assert.Equal(250, point.Calibration.MonotonicTimestampTicks);
        Assert.Equal(1_000, point.Calibration.TimestampFrequency);
        Assert.True(point.Calibration.TraceActive);
        Assert.False(point.Calibration.AllocationAvailable);
        Assert.Equal(Environment.CurrentManagedThreadId, point.OwnerThreadId);
    }

    [Fact]
    public void CaptureRequiresAProvenSameThreadCapability()
    {
        var rawClock = new ScriptedProfileRawClock(1_000, new long[] { 100, 102 });
        var monotonicClock = new FixedProfileMonotonicClock(1);
        var unprobed = default(ServiceCycleProfileAllocationCapability);
        Assert.Throws<ArgumentException>(() => ServiceCycleProfileCalibrationPoint.Capture(
            rawClock,
            monotonicClock,
            ServiceCycleProfileTestData.BuildId,
            traceActive: false,
            in unprobed));

        var foreignCapability = default(ServiceCycleProfileAllocationCapability);
        var thread = new Thread(() =>
        {
            foreignCapability = ServiceCycleProfileAllocationCapability.Probe(
                new ScriptedProfileAllocationCounter(new long[] { 0, 100, 100 }));
        });
        thread.Start();
        Assert.True(thread.Join(ServiceCycleTestDeadline.Value), "The capability probe did not complete.");

        Assert.Throws<ArgumentException>(() => ServiceCycleProfileCalibrationPoint.Capture(
            rawClock,
            monotonicClock,
            ServiceCycleProfileTestData.BuildId,
            traceActive: false,
            in foreignCapability));
    }

    [Fact]
    public void CaptureRejectsBackwardOrUnrepresentableRawRanges()
    {
        var backwards = new ScriptedProfileRawClock(1_000, new long[] { 101, 100 });
        Assert.Throws<InvalidOperationException>(() => Capture(backwards));

        var overflowing = new ScriptedProfileRawClock(1_000, new long[] { -1, long.MaxValue });
        Assert.Throws<OverflowException>(() => Capture(overflowing));
    }

    private static ServiceCycleProfileCalibrationPoint Capture(IServiceCycleProfileRawClock rawClock) =>
        ServiceCycleProfileCalibrationPoint.Capture(
            rawClock,
            new FixedProfileMonotonicClock(1),
            ServiceCycleProfileTestData.BuildId,
            traceActive: false,
            UnavailableCapability());

    private static ServiceCycleProfileAllocationCapability UnavailableCapability() =>
        ServiceCycleProfileAllocationCapability.Probe(
            new ScriptedProfileAllocationCounter(new long[] { 0, 100, 100 }));
}
