using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileAggregatorTests
{
    [Fact]
    public void EquivalentMeasurementsProduceExactAggregate()
    {
        var aggregator = new ServiceCycleProfileAggregator(8, 2, allocationAvailable: true);
        var context = Context(stage: 2, lifecycle: 7);
        var operations = Operations(listEntries: 4);

        AssertAccepted(aggregator.Record(Measurement(in context, 100, 10, 2, in operations)));
        AssertAccepted(aggregator.Record(Measurement(in context, 90, 5, 3, in operations)));
        AssertAccepted(aggregator.Record(Measurement(in context, 130, 20, 4, in operations)));
        aggregator.Seal();

        var aggregate = aggregator.GetAggregate(0);
        Assert.Equal(1, aggregator.GroupCount);
        Assert.Equal(ServiceCycleProfileRecordKind.Aggregate, aggregate.Kind);
        Assert.Equal((ulong)3, aggregate.OccurrenceCount);
        Assert.Equal((ulong)35, aggregate.TotalElapsedRawTicks);
        Assert.Equal(5, aggregate.MinimumElapsedRawTicks);
        Assert.Equal(20, aggregate.MaximumElapsedRawTicks);
        Assert.Equal((ulong)9, aggregate.TotalAllocatedBytes);
        Assert.Equal(90, aggregate.FirstStartedAtRawTicks);
        Assert.Equal(130, aggregate.LastStartedAtRawTicks);
    }

    [Fact]
    public void FirstSeenOrderIsStableAcrossHashSlots()
    {
        var aggregator = new ServiceCycleProfileAggregator(4, 1, allocationAvailable: true);
        var first = Context(stage: 9, lifecycle: 1);
        var second = Context(stage: 1, lifecycle: 1);
        var operations = Operations(listEntries: 1);

        AssertAccepted(aggregator.Record(Measurement(in first, 1, 1, 0, in operations)));
        AssertAccepted(aggregator.Record(Measurement(in second, 2, 1, 0, in operations)));
        AssertAccepted(aggregator.Record(Measurement(in first, 3, 1, 0, in operations)));
        aggregator.Seal();

        Assert.Equal(9, aggregator.GetAggregate(0).StageCode);
        Assert.Equal(1, aggregator.GetAggregate(1).StageCode);
    }

    [Fact]
    public void ExactKeyDifferencesConsumeCapacityAndFaultClosed()
    {
        var aggregator = new ServiceCycleProfileAggregator(4, 1, allocationAvailable: true);
        var warm = Context(stage: 1, lifecycle: 1);
        var rebind = new ServiceCycleProfileContext(
            1, 2, 1, 3, 4, ServiceCycleProfileTemperature.LifecycleRebind);
        var nextLifecycle = Context(stage: 1, lifecycle: 2);
        var firstOperations = Operations(listEntries: 1);
        var secondOperations = Operations(listEntries: 2);

        AssertAccepted(aggregator.Record(Measurement(in warm, 1, 1, 0, in firstOperations)));
        AssertAccepted(aggregator.Record(Measurement(in warm, 2, 1, 0, in secondOperations)));
        AssertAccepted(aggregator.Record(Measurement(in rebind, 3, 1, 0, in firstOperations)));
        AssertAccepted(aggregator.Record(Measurement(in nextLifecycle, 4, 1, 0, in firstOperations)));
        var fifth = Context(stage: 2, lifecycle: 1);

        Assert.Equal(
            ServiceCycleProfileAggregationResult.Faulted,
            aggregator.Record(Measurement(in fifth, 5, 1, 0, in firstOperations)));
        Assert.Equal(ServiceCycleProfileAggregationFault.GroupCapacityExhausted, aggregator.Fault);
        Assert.Equal(4, aggregator.GroupCount);
        Assert.Equal(
            ServiceCycleProfileAggregationResult.Faulted,
            aggregator.Record(Measurement(in warm, 6, 1, 0, in firstOperations)));
        aggregator.Seal();
        Assert.Throws<InvalidOperationException>(() => aggregator.GetAggregate(0));
    }

    [Fact]
    public void ElapsedAndAllocationArithmeticExhaustionFaultTheWholeProfile()
    {
        AssertArithmeticFault(long.MaxValue, allocatedBytes: 0);
        AssertArithmeticFault(elapsedRawTicks: 0, allocatedBytes: long.MaxValue);
    }

    private static void AssertArithmeticFault(long elapsedRawTicks, long allocatedBytes)
    {
        var aggregator = new ServiceCycleProfileAggregator(1, 1, allocationAvailable: true);
        var context = Context(stage: 1, lifecycle: 1);
        var operations = default(ServiceCycleProfileOperations);

        AssertAccepted(aggregator.Record(Measurement(
            in context, 1, elapsedRawTicks, allocatedBytes, in operations)));
        AssertAccepted(aggregator.Record(Measurement(
            in context, 2, elapsedRawTicks, allocatedBytes, in operations)));
        Assert.Equal(
            ServiceCycleProfileAggregationResult.Faulted,
            aggregator.Record(Measurement(
                in context, 3, elapsedRawTicks, allocatedBytes, in operations)));

        Assert.Equal(ServiceCycleProfileAggregationFault.ArithmeticExhausted, aggregator.Fault);
        aggregator.Seal();
        Assert.Throws<InvalidOperationException>(() => aggregator.GetAggregate(0));
    }

    [Fact]
    public void UnavailableAllocationProbeRejectsNonzeroEvidence()
    {
        var aggregator = new ServiceCycleProfileAggregator(2, 1, allocationAvailable: false);
        var context = Context(stage: 1, lifecycle: 1);
        var operations = default(ServiceCycleProfileOperations);

        Assert.Equal(
            ServiceCycleProfileAggregationResult.Faulted,
            aggregator.Record(Measurement(in context, 1, 1, 1, in operations)));

        Assert.Equal(ServiceCycleProfileAggregationFault.AllocationUnavailable, aggregator.Fault);
        Assert.Equal(0, aggregator.GroupCount);
    }

    [Fact]
    public void ReadsRequireSealingAndRecordRequiresOwnerThread()
    {
        var aggregator = new ServiceCycleProfileAggregator(2, 1, allocationAvailable: true);
        var context = Context(stage: 1, lifecycle: 1);
        var operations = default(ServiceCycleProfileOperations);
        AssertAccepted(aggregator.Record(Measurement(in context, 1, 1, 0, in operations)));
        Assert.Throws<InvalidOperationException>(() => aggregator.GetAggregate(0));
        var foreignMeasurement = Measurement(in context, 2, 1, 0, in operations);

        Exception? foreignFailure = null;
        var thread = new Thread(() =>
        {
            try
            {
                aggregator.Record(in foreignMeasurement);
            }
            catch (Exception exception)
            {
                foreignFailure = exception;
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "The owner-thread check did not complete.");

        Assert.IsType<InvalidOperationException>(foreignFailure);
        aggregator.Seal();
        Assert.Equal((ulong)1, aggregator.GetAggregate(0).OccurrenceCount);
    }

    [Fact]
    public void WarmRecordAndSealAllocateNoManagedMemory()
    {
        var context = Context(stage: 1, lifecycle: 1);
        var operations = Operations(listEntries: 4);
        var aggregator = new ServiceCycleProfileAggregator(8, 4, allocationAvailable: true);
        for (var index = 0; index < 16; index++)
            AssertAccepted(aggregator.Record(Measurement(in context, index, 1, 0, in operations)));

        var result = ServiceCycleProfileAggregationResult.Faulted;
        var startedAt = 0L;
        var recordAllocated = AllocationProbe.MeasureRepeated(64, () =>
        {
            result = aggregator.Record(Measurement(in context, startedAt, 1, 0, in operations));
            startedAt++;
        });

        AssertAccepted(result);
        Assert.Equal(0, recordAllocated);

        // Sealing is a one-shot per aggregator, so each pass gets a fresh one; building it is
        // preparation and stays outside the measured window.
        var sealTarget = null as ServiceCycleProfileAggregator;
        var sealAllocated = AllocationProbe.MeasureRepeated(
            1,
            () => sealTarget!.Seal(),
            prepare: () => sealTarget = new ServiceCycleProfileAggregator(1, 1, allocationAvailable: true));

        Assert.Equal(0, sealAllocated);
    }

    internal static ServiceCycleProfileContext Context(int stage, ulong lifecycle) => new(
        stage,
        serviceOrdinal: 2,
        lifecycle,
        cycle: 3,
        frame: 4,
        ServiceCycleProfileTemperature.Warm);

    internal static ServiceCycleProfileMeasurement Measurement(
        in ServiceCycleProfileContext context,
        long startedAtRawTicks,
        long elapsedRawTicks,
        long allocatedBytes,
        in ServiceCycleProfileOperations operations) =>
        new(in context, startedAtRawTicks, elapsedRawTicks, allocatedBytes, in operations);

    internal static ServiceCycleProfileOperations Operations(uint listEntries) => new(
        reflectedFieldReads: 1,
        reflectedMethodCalls: 2,
        stableIdReads: 3,
        listEntries,
        invocationArgumentArrays: 0,
        recordCopies: 0);

    internal static void AssertAccepted(ServiceCycleProfileAggregationResult result) =>
        Assert.Equal(ServiceCycleProfileAggregationResult.Accepted, result);
}
