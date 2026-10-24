using OrbModding.TestSupport;
using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileMeasurementFailureTests
{
    [Fact]
    public void SourceFailuresLatchOnceWithoutEscaping()
    {
        var rawClock = new ScriptedProfileRawClock(
            1_000,
            new long[] { 100, 102 },
            terminalFailure: new InvalidOperationException("clock failed"));
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            rawClock,
            UnavailableCounter());
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);

        Assert.False(recorder.TryBegin(in context, out _));
        Assert.Equal(ServiceCycleProfileMeasurementFault.RawClockFailed, recorder.Fault);
        Assert.False(recorder.TryBegin(in context, out _));
        Assert.Equal(3, rawClock.ReadCount);

        var allocation = new ScriptedProfileAllocationCounter(
            new long[] { 0, 100, 400 },
            terminalFailure: new InvalidOperationException("counter failed"));
        var allocationRecorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102 }),
            allocation);
        Assert.False(allocationRecorder.TryBegin(in context, out _));
        Assert.Equal(
            ServiceCycleProfileMeasurementFault.AllocationCounterFailed,
            allocationRecorder.Fault);
    }

    [Fact]
    public void BackwardRawOrAllocationEvidenceFaultsWithoutPublishing()
    {
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var counters = default(ServiceCycleProfileOperationCounters);
        var backwardClock = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102, 110, 109 }),
            UnavailableCounter());
        Assert.True(backwardClock.TryBegin(in context, out var clockToken));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Faulted,
            backwardClock.Complete(in clockToken, in counters));
        Assert.Equal(ServiceCycleProfileMeasurementFault.RawClockRegressed, backwardClock.Fault);
        Assert.Equal(0, backwardClock.GroupCount);

        var overflowingClock = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(
                1_000,
                new long[] { 100, 102, long.MinValue, long.MaxValue }),
            UnavailableCounter());
        Assert.True(overflowingClock.TryBegin(in context, out var overflowToken));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Faulted,
            overflowingClock.Complete(in overflowToken, in counters));
        Assert.Equal(
            ServiceCycleProfileMeasurementFault.MeasurementArithmeticExhausted,
            overflowingClock.Fault);

        var backwardAllocation = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102, 110, 120 }),
            new ScriptedProfileAllocationCounter(new long[] { 0, 100, 400, 10, 9 }));
        Assert.True(backwardAllocation.TryBegin(in context, out var allocationToken));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Faulted,
            backwardAllocation.Complete(in allocationToken, in counters));
        Assert.Equal(
            ServiceCycleProfileMeasurementFault.AllocationCounterRegressed,
            backwardAllocation.Fault);
        Assert.Equal(0, backwardAllocation.GroupCount);
    }

    [Fact]
    public void AggregatorExhaustionInvalidatesRecorder()
    {
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var capacityRecorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(
                1_000,
                new long[] { 100, 102, 110, 120, 130, 140 }),
            UnavailableCounter(),
            maximumGroups: 1);
        var emptyCounters = default(ServiceCycleProfileOperationCounters);
        Assert.True(capacityRecorder.TryBegin(in context, out var first));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            capacityRecorder.Complete(in first, in emptyCounters));
        var otherContext = ServiceCycleProfileAggregatorTests.Context(stage: 2, lifecycle: 1);
        Assert.True(capacityRecorder.TryBegin(in otherContext, out var second));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Faulted,
            capacityRecorder.Complete(in second, in emptyCounters));
        Assert.Equal(ServiceCycleProfileMeasurementFault.AggregationFailed, capacityRecorder.Fault);
    }

    [Fact]
    public void ForeignThreadUseFaultsWithoutThrowing()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102 }),
            UnavailableCounter());
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        Exception? failure = null;
        var began = true;
        var thread = new Thread(() =>
        {
            try
            {
                began = recorder.TryBegin(in context, out _);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.Start();
        Assert.True(thread.Join(ServiceCycleTestDeadline.Value), "The foreign-thread probe did not complete.");

        Assert.Null(failure);
        Assert.False(began);
        Assert.Equal(ServiceCycleProfileMeasurementFault.OwnerThreadRejected, recorder.Fault);
    }

    [Fact]
    public void ForeignOrSealedTokensAreRejectedBeforeReadingSources()
    {
        var firstClock = new ScriptedProfileRawClock(
            1_000,
            new long[] { 100, 102, 110, 120 });
        var secondClock = new ScriptedProfileRawClock(1_000, new long[] { 200, 202 });
        var first = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            firstClock,
            UnavailableCounter());
        var second = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            secondClock,
            UnavailableCounter());
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var counters = default(ServiceCycleProfileOperationCounters);
        Assert.True(first.TryBegin(in context, out var token));

        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Faulted,
            second.Complete(in token, in counters));
        Assert.Equal(ServiceCycleProfileMeasurementFault.TokenRejected, second.Fault);
        Assert.Equal(2, secondClock.ReadCount);

        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            first.Complete(in token, in counters));
        Assert.True(first.Seal());
        Assert.False(first.TryBegin(in context, out _));
        Assert.Equal(ServiceCycleProfileMeasurementFault.AggregatorSealed, first.Fault);
        Assert.Equal(4, firstClock.ReadCount);
        Assert.Throws<InvalidOperationException>(() => first.GetAggregate(0));
    }

    [Fact]
    public void NestedTokensAreLifoOneShotAndMustFinishBeforeSeal()
    {
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var counters = default(ServiceCycleProfileOperationCounters);
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(
                1_000,
                new long[] { 100, 102, 110, 115, 120, 130 }),
            UnavailableCounter());
        Assert.True(recorder.TryBegin(in context, out var outer));
        Assert.True(recorder.TryBegin(in context, out var inner));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in inner, in counters));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in outer, in counters));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Faulted,
            recorder.Complete(in outer, in counters));
        Assert.Equal(ServiceCycleProfileMeasurementFault.TokenRejected, recorder.Fault);

        var outOfOrder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102, 110, 115 }),
            UnavailableCounter());
        Assert.True(outOfOrder.TryBegin(in context, out var first));
        Assert.True(outOfOrder.TryBegin(in context, out _));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Faulted,
            outOfOrder.Complete(in first, in counters));
        Assert.Equal(ServiceCycleProfileMeasurementFault.TokenRejected, outOfOrder.Fault);

        var activeAtSeal = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102, 110 }),
            UnavailableCounter());
        Assert.True(activeAtSeal.TryBegin(in context, out _));
        Assert.False(activeAtSeal.Seal());
        Assert.Equal(
            ServiceCycleProfileMeasurementFault.ActiveMeasurementAtSeal,
            activeAtSeal.Fault);

        var depthBound = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102, 110 }),
            UnavailableCounter(),
            maximumMeasurementDepth: 1);
        Assert.True(depthBound.TryBegin(in context, out _));
        Assert.False(depthBound.TryBegin(in context, out _));
        Assert.Equal(
            ServiceCycleProfileMeasurementFault.MeasurementDepthExhausted,
            depthBound.Fault);
    }

    private static ScriptedProfileAllocationCounter UnavailableCounter() =>
        new(new long[] { 0, 100, 100 });
}
