using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;
using Xunit;
using static OrbModding.ProfileTests.ServiceCycleProfileTestData;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileRecordCodecTests
{
    [Fact]
    public void FixedRecordRoundTripsEveryField()
    {
        var expected = Record();
        var bytes = new byte[ServiceCycleProfileRecordCodec.RecordBytes];

        ServiceCycleProfileRecordCodec.Write(bytes, in expected);
        var actual = ServiceCycleProfileRecordCodec.Read(bytes);

        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.StageCode, actual.StageCode);
        Assert.Equal(expected.ServiceOrdinal, actual.ServiceOrdinal);
        Assert.Equal(expected.Lifecycle, actual.Lifecycle);
        Assert.Equal(expected.Cycle, actual.Cycle);
        Assert.Equal(expected.Frame, actual.Frame);
        Assert.Equal(expected.FirstStartedAtRawTicks, actual.FirstStartedAtRawTicks);
        Assert.Equal(expected.LastStartedAtRawTicks, actual.LastStartedAtRawTicks);
        Assert.Equal(expected.OccurrenceCount, actual.OccurrenceCount);
        Assert.Equal(expected.TotalElapsedRawTicks, actual.TotalElapsedRawTicks);
        Assert.Equal(expected.MinimumElapsedRawTicks, actual.MinimumElapsedRawTicks);
        Assert.Equal(expected.MaximumElapsedRawTicks, actual.MaximumElapsedRawTicks);
        Assert.Equal(expected.TotalAllocatedBytes, actual.TotalAllocatedBytes);
        Assert.Equal(expected.Temperature, actual.Temperature);
        Assert.Equal(expected.Operations, actual.Operations);
        Assert.Equal((ushort)4, ServiceCycleProfileRecordCodec.SchemaVersion);
        Assert.Equal(5_000_000_000UL, ServiceCycleProfileBinary.U64(bytes, 96));
        Assert.Equal(6_000_000_001UL, ServiceCycleProfileBinary.U64(bytes, 104));
        Assert.True(ServiceCycleProfileBinary.AllZero(bytes.AsSpan(112)));
    }

    [Fact]
    public void ReservedBytesAndDefaultRecordsFailClosed()
    {
        var record = Record();
        var bytes = new byte[ServiceCycleProfileRecordCodec.RecordBytes];
        ServiceCycleProfileRecordCodec.Write(bytes, in record);
        bytes[128] = 1;

        Assert.Throws<FormatException>(() => ServiceCycleProfileRecordCodec.Read(bytes));
        Assert.Throws<ArgumentException>(() =>
            ServiceCycleProfileRecordCodec.Write(new byte[ServiceCycleProfileRecordCodec.RecordBytes], default));
    }

    [Fact]
    public void AggregateRoundTripsSummaryAndOperationSignature()
    {
        var operations = new ServiceCycleProfileOperations(stableIdReads: 3, listEntries: 4);
        var expected = ServiceCycleProfileRecord.Aggregate(
            stageCode: 7,
            serviceOrdinal: 2,
            lifecycle: 9,
            firstStartedAtRawTicks: 100,
            lastStartedAtRawTicks: 200,
            occurrenceCount: 3,
            totalElapsedRawTicks: 24,
            minimumElapsedRawTicks: 5,
            maximumElapsedRawTicks: 11,
            totalAllocatedBytes: 32,
            ServiceCycleProfileTemperature.LifecycleRebind,
            in operations);
        var bytes = new byte[ServiceCycleProfileRecordCodec.RecordBytes];

        ServiceCycleProfileRecordCodec.Write(bytes, in expected);
        var actual = ServiceCycleProfileRecordCodec.Read(bytes);

        Assert.Equal(ServiceCycleProfileRecordKind.Aggregate, actual.Kind);
        Assert.Equal((ulong)3, actual.OccurrenceCount);
        Assert.Equal((ulong)24, actual.TotalElapsedRawTicks);
        Assert.Equal(5, actual.MinimumElapsedRawTicks);
        Assert.Equal(11, actual.MaximumElapsedRawTicks);
        Assert.Equal(expected.Operations, actual.Operations);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(48, 1)]
    [InlineData(56, 2)]
    public void SampleSemanticMutationsFailClosed(int offset, byte value)
    {
        var record = Record();
        var bytes = new byte[ServiceCycleProfileRecordCodec.RecordBytes];
        ServiceCycleProfileRecordCodec.Write(bytes, in record);
        bytes[offset] = value;

        Assert.Throws<FormatException>(() => ServiceCycleProfileRecordCodec.Read(bytes));
    }

    [Fact]
    public void ImpossibleAggregateTotalsAreRejected()
    {
        var operations = default(ServiceCycleProfileOperations);

        Assert.Throws<ArgumentException>(() => ServiceCycleProfileRecord.Aggregate(
            1, 0, 0, 1, 2, occurrenceCount: 10, totalElapsedRawTicks: 100,
            minimumElapsedRawTicks: 100, maximumElapsedRawTicks: 100,
            totalAllocatedBytes: 0, ServiceCycleProfileTemperature.Warm, in operations));
    }
}
