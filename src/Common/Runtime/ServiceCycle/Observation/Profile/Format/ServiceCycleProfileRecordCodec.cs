#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;

internal static class ServiceCycleProfileRecordCodec
{
    internal const ushort SchemaVersion = 4;
    internal const int RecordBytes = 144;

    internal static void Write(Span<byte> destination, in ServiceCycleProfileRecord record)
    {
        if (destination.Length != RecordBytes)
            throw new ArgumentException("A profile record requires its exact fixed-size destination.", nameof(destination));
        ServiceCycleProfileRecordValidation.Validate(in record);
        var output = destination;
        output.Clear();
        ServiceCycleProfileBinary.I32(output, 0, (int)record.Kind);
        ServiceCycleProfileBinary.I32(output, 4, record.StageCode);
        ServiceCycleProfileBinary.I32(output, 8, record.ServiceOrdinal);
        ServiceCycleProfileBinary.I32(output, 12, (int)record.Temperature);
        ServiceCycleProfileBinary.U64(output, 16, record.Lifecycle);
        ServiceCycleProfileBinary.U64(output, 24, record.Cycle);
        ServiceCycleProfileBinary.U64(output, 32, record.Frame);
        ServiceCycleProfileBinary.I64(output, 40, record.FirstStartedAtRawTicks);
        ServiceCycleProfileBinary.I64(output, 48, record.LastStartedAtRawTicks);
        ServiceCycleProfileBinary.U64(output, 56, record.OccurrenceCount);
        ServiceCycleProfileBinary.U64(output, 64, record.TotalElapsedRawTicks);
        ServiceCycleProfileBinary.I64(output, 72, record.MinimumElapsedRawTicks);
        ServiceCycleProfileBinary.I64(output, 80, record.MaximumElapsedRawTicks);
        ServiceCycleProfileBinary.U64(output, 88, record.TotalAllocatedBytes);
        var operations = record.Operations;
        ServiceCycleProfileBinary.U64(output, 96, operations.StableIdReads);
        ServiceCycleProfileBinary.U64(output, 104, operations.ListEntries);
    }

    internal static ServiceCycleProfileRecord Read(ReadOnlySpan<byte> source)
    {
        if (source.Length != RecordBytes || !ServiceCycleProfileBinary.AllZero(source.Slice(112, 32)))
            throw Invalid();
        try
        {
            var operations = new ServiceCycleProfileOperations(
                ServiceCycleProfileBinary.U64(source, 96),
                ServiceCycleProfileBinary.U64(source, 104));
            return new ServiceCycleProfileRecord(
                (ServiceCycleProfileRecordKind)ServiceCycleProfileBinary.I32(source, 0),
                ServiceCycleProfileBinary.I32(source, 4),
                ServiceCycleProfileBinary.I32(source, 8),
                ServiceCycleProfileBinary.U64(source, 16),
                ServiceCycleProfileBinary.U64(source, 24),
                ServiceCycleProfileBinary.U64(source, 32),
                ServiceCycleProfileBinary.I64(source, 40),
                ServiceCycleProfileBinary.I64(source, 48),
                ServiceCycleProfileBinary.U64(source, 56),
                ServiceCycleProfileBinary.U64(source, 64),
                ServiceCycleProfileBinary.I64(source, 72),
                ServiceCycleProfileBinary.I64(source, 80),
                ServiceCycleProfileBinary.U64(source, 88),
                (ServiceCycleProfileTemperature)ServiceCycleProfileBinary.I32(source, 12),
                in operations);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw Invalid();
        }
    }

    private static FormatException Invalid() => new("Invalid service-cycle profile record.");
}
#endif
