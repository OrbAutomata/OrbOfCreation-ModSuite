using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileMeasurementRecorderTests
{
    [Fact]
    public void RecorderPreservesReadOrderAndExactDeltas()
    {
        var calls = new List<char>(4);
        var rawClock = new ScriptedProfileRawClock(
            1_000,
            new long[] { 100, 102, 110, 125 },
            calls);
        var allocation = new ScriptedProfileAllocationCounter(
            new long[] { 0, 100, 400, 1_000, 1_064 },
            calls);
        var recorder = Recorder(rawClock, allocation);
        calls.Clear();
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 3, lifecycle: 7);
        var counters = new ServiceCycleProfileOperationCounters();
        counters.AddListEntries(4);

        Assert.True(recorder.TryBegin(in context, out var token));
        Assert.Equal(ServiceCycleProfileMeasurementResult.Accepted, recorder.Complete(in token, in counters));
        Assert.True(recorder.Seal());

        Assert.Equal(new char[] { 'A', 'R', 'R', 'A' }, calls);
        var aggregate = recorder.GetAggregate(0);
        Assert.Equal(110, aggregate.FirstStartedAtRawTicks);
        Assert.Equal((ulong)15, aggregate.TotalElapsedRawTicks);
        Assert.Equal((ulong)64, aggregate.TotalAllocatedBytes);
        Assert.Equal((uint)4, aggregate.Operations.ListEntries);
    }

    [Fact]
    public void UnavailableAllocationNeverReadsACounterAndRecordsZero()
    {
        var rawClock = new ScriptedProfileRawClock(
            1_000,
            new long[] { 100, 102, 110, 120 });
        var allocation = new ScriptedProfileAllocationCounter(new long[] { 0, 100, 100 });
        var recorder = Recorder(rawClock, allocation);
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var counters = default(ServiceCycleProfileOperationCounters);

        Assert.True(recorder.TryBegin(in context, out var token));
        Assert.Equal(ServiceCycleProfileMeasurementResult.Accepted, recorder.Complete(in token, in counters));
        Assert.True(recorder.Seal());

        Assert.Equal((ulong)0, recorder.GetAggregate(0).TotalAllocatedBytes);
        Assert.Equal(3, allocation.ReadCount);
    }

    [Fact]
    public void WarmMeasurementPathAllocatesNoManagedMemory()
    {
        var rawClock = new IncrementingProfileRawClock();
        var allocation = new ProvenIncrementingProfileAllocationCounter();
        var recorder = Recorder(rawClock, allocation);
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var counters = default(ServiceCycleProfileOperationCounters);
        for (var index = 0; index < 16; index++) Record(recorder, in context, in counters);

        var result = ServiceCycleProfileMeasurementResult.Faulted;
        var allocated = AllocationProbe.MeasureRepeated(
            64,
            () => result = Record(recorder, in context, in counters));

        Assert.Equal(ServiceCycleProfileMeasurementResult.Accepted, result);
        Assert.Equal(0, allocated);
    }

    internal static ServiceCycleProfileMeasurementRecorder Recorder(
        IServiceCycleProfileRawClock rawClock,
        IServiceCycleProfileAllocationCounter allocationCounter,
        int maximumGroups = 4,
        int maximumMeasurementDepth = 4)
    {
        var capability = ServiceCycleProfileAllocationCapability.Probe(allocationCounter);
        var point = ServiceCycleProfileCalibrationPoint.Capture(
            rawClock,
            new FixedProfileMonotonicClock(1),
            ServiceCycleProfileTestData.BuildId,
            traceActive: false,
            in capability);
        return new ServiceCycleProfileMeasurementRecorder(
            in point,
            maximumGroups,
            samplesPerGroup: 2,
            maximumMeasurementDepth);
    }

    private static ServiceCycleProfileMeasurementResult Record(
        ServiceCycleProfileMeasurementRecorder recorder,
        in ServiceCycleProfileContext context,
        in ServiceCycleProfileOperationCounters counters)
    {
        if (!recorder.TryBegin(in context, out var token))
            return ServiceCycleProfileMeasurementResult.Faulted;
        return recorder.Complete(in token, in counters);
    }
}
