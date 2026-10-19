using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

internal static class DecisionJournalRecordCodec
{
    internal const int RecordBytes = 80;

    internal static void Write(Span<byte> destination, in DecisionJournalRecord record)
    {
        if (destination.Length != RecordBytes)
            throw new ArgumentException("A journal record requires its exact fixed-size destination.", nameof(destination));
        DecisionJournalRecordValidation.Validate(in record);
        destination.Clear();
        destination[0] = (byte)record.Kind;
        switch (record.Kind)
        {
            case DecisionJournalRecordKind.DecisionSpan:
                WriteDecision(destination, in record);
                break;
            case DecisionJournalRecordKind.Action:
                WriteAction(destination, in record);
                break;
            default:
                WriteTransition(destination, in record);
                break;
        }
    }

    internal static DecisionJournalRecord Read(ReadOnlySpan<byte> source)
    {
        try
        {
            if (source.Length != RecordBytes) throw Invalid();
            var kind = (DecisionJournalRecordKind)source[0];
            var record = kind switch
            {
                DecisionJournalRecordKind.DecisionSpan => ReadDecision(source),
                DecisionJournalRecordKind.Action => ReadAction(source),
                _ => ReadTransition(source, kind),
            };
            DecisionJournalRecordValidation.Validate(in record);
            return record;
        }
        catch (FormatException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw Invalid();
        }
    }

    private static void WriteDecision(Span<byte> bytes, in DecisionJournalRecord record)
    {
        bytes[1] = (byte)record.DecisionOutcomeKind;
        WriteU16(bytes, 2, Service(record.Service));
        WriteI32(bytes, 4, record.DecisionOutcomeCode);
        WriteU64(bytes, 8, record.Lifecycle);
        WriteU64(bytes, 16, record.FirstCycle);
        WriteU64(bytes, 24, record.LastCycle);
        WriteI64(bytes, 32, record.FirstTimestampTicks);
        WriteI64(bytes, 40, record.LastTimestampTicks);
        WriteI64(bytes, 48, record.RepeatCount);
        WriteI32(bytes, 56, record.FaultCode);
        WriteU16(bytes, 60, (ushort)record.FaultCategory);
        WriteI32(bytes, 64, record.FirstFaultOccurrence);
        WriteI32(bytes, 68, record.LastFaultOccurrence);
    }

    private static DecisionJournalRecord ReadDecision(ReadOnlySpan<byte> bytes)
    {
        if (!IsZero(bytes.Slice(62, 2)) || !IsZero(bytes.Slice(72, 8))) throw Invalid();
        return DecisionJournalRecord.ReadDecision(
            TraceService(ReadU16(bytes, 2)),
            ReadU64(bytes, 8),
            ReadU64(bytes, 16),
            ReadU64(bytes, 24),
            ReadI64(bytes, 32),
            ReadI64(bytes, 40),
            ReadI64(bytes, 48),
            (DecisionJournalDecisionOutcomeKind)bytes[1],
            ReadI32(bytes, 4),
            (ServiceFaultCategory)ReadU16(bytes, 60),
            ReadI32(bytes, 56),
            ReadI32(bytes, 64),
            ReadI32(bytes, 68));
    }

    private static void WriteAction(Span<byte> bytes, in DecisionJournalRecord record)
    {
        bytes[1] = (byte)record.Attribution.RouteStatus;
        WriteU16(bytes, 2, (ushort)record.Attribution.NativeType);
        WriteU32(bytes, 4, record.ActionOutcome.Value);
        WriteU16(bytes, 8, Service(record.Service));
        WriteU16(bytes, 10, record.ActionOrdinal);
        bytes[12] = record.Attribution.ActionKind;
        WriteU64(bytes, 16, record.FirstCycle);
        WriteI64(bytes, 24, record.FirstTimestampTicks);
        record.Attribution.CandidateId.TryWriteBytes(bytes.Slice(32, 16));
        record.Attribution.ListId.TryWriteBytes(bytes.Slice(48, 16));
        record.Attribution.ViewId.TryWriteBytes(bytes.Slice(64, 16));
    }

    private static DecisionJournalRecord ReadAction(ReadOnlySpan<byte> bytes)
    {
        if (!IsZero(bytes.Slice(13, 3))) throw Invalid();
        var attribution = new ServiceActionJournalAttribution(
            new Guid(bytes.Slice(32, 16)),
            (ServiceActionNativeTypeId)ReadU16(bytes, 2),
            new Guid(bytes.Slice(48, 16)),
            new Guid(bytes.Slice(64, 16)),
            (ServiceActionRouteStatus)bytes[1],
            bytes[12]);
        return DecisionJournalRecord.ReadAction(
            TraceService(ReadU16(bytes, 8)),
            ReadU64(bytes, 16),
            ReadI64(bytes, 24),
            ReadU16(bytes, 10),
            in attribution,
            DecisionJournalActionOutcome.Read(ReadU32(bytes, 4)));
    }

    private static void WriteTransition(Span<byte> bytes, in DecisionJournalRecord record)
    {
        WriteU16(bytes, 2, Service(record.Service));
        WriteI32(bytes, 4, record.TransitionCode);
        WriteU64(bytes, 8, record.Lifecycle);
        WriteU64(bytes, 16, record.Generation);
        WriteI64(bytes, 32, record.FirstTimestampTicks);
    }

    private static DecisionJournalRecord ReadTransition(ReadOnlySpan<byte> bytes, DecisionJournalRecordKind kind)
    {
        if (bytes[1] != 0 || !IsZero(bytes.Slice(24, 8)) || !IsZero(bytes.Slice(40, 40))) throw Invalid();
        return DecisionJournalRecord.ReadTransition(
            kind,
            TraceService(ReadU16(bytes, 2)),
            ReadU64(bytes, 8),
            ReadU64(bytes, 16),
            ReadI64(bytes, 32),
            ReadI32(bytes, 4));
    }

    private static ushort Service(ServiceCycleTraceServiceId service)
    {
        if (service.Value > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(service));
        return checked((ushort)service.Value);
    }

    private static ServiceCycleTraceServiceId TraceService(ushort value) =>
        value == 0 ? default : new ServiceCycleTraceServiceId(value);

    private static bool IsZero(ReadOnlySpan<byte> bytes)
    {
        for (var index = 0; index < bytes.Length; index++) if (bytes[index] != 0) return false;
        return true;
    }

    private static FormatException Invalid() => new("Invalid decision-journal record.");
    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static int ReadI32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
    private static ulong ReadU64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, 8));
    private static long ReadI64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, 8));
    private static void WriteU16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(offset, 2), value);
    private static void WriteU32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(offset, 4), value);
    private static void WriteI32(Span<byte> bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(offset, 4), value);
    private static void WriteU64(Span<byte> bytes, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(offset, 8), value);
    private static void WriteI64(Span<byte> bytes, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(offset, 8), value);
}
