using System;
using System.Buffers.Binary;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

internal static class ServiceCycleSemanticEventV7Codec
{
    internal const int RecordBytes = 288;

    internal static void Write(Span<byte> bytes, in ServiceCycleSemanticEvent item)
    {
        if (bytes.Length != RecordBytes)
            throw new ArgumentException("A semantic event record must have the canonical schema-v7 length.", nameof(bytes));

        WriteU64(bytes, 0, item.Id.Session.Value);
        WriteU64(bytes, 8, item.Id.Sequence);
        WriteU64(bytes, 16, item.Parent.Session.Value);
        WriteU64(bytes, 24, item.Parent.Sequence);
        WriteI32(bytes, 32, (int)item.Kind);
        var payload = item.Payload;
        // Retired publication accounting. Per-action effect/evidence is authoritative; new records
        // burn the old batch-ledger slot to zero.
        WriteU32(bytes, 36, 0);
        WriteU64(bytes, 40, (ulong)payload.Fields);
        WriteU64(bytes, 48, payload.Service);
        WriteU64(bytes, 56, payload.Lifecycle);
        WriteU64(bytes, 64, payload.Configuration);
        WriteU64(bytes, 72, payload.Strategy);
        WriteU64(bytes, 80, payload.Capture);
        WriteU64(bytes, 88, payload.Cycle);
        WriteU64(bytes, 96, payload.Batch);
        WriteU64(bytes, 104, payload.Action);
        WriteU64(bytes, 112, payload.StatePublication);
        WriteI64(bytes, 120, payload.TimestampTicks);
        WriteI64(bytes, 128, payload.DurationTicks);
        WriteI64(bytes, 136, payload.DeadlineTicks);
        WriteI64(bytes, 144, payload.FrameIdentity);
        WriteU64(bytes, 152, payload.Fingerprint);
        WriteI32(bytes, 160, payload.Code);
        WriteI32(bytes, 164, payload.Disposition);
        WriteI32(bytes, 168, payload.ActionIndex);
        WriteI32(bytes, 172, payload.ActionCount);
        WriteI32(bytes, 176, payload.CommittedCount);
        WriteI32(bytes, 180, payload.UntouchedSuffixCount);
        WriteI32(bytes, 184, payload.OccurrenceCount);
        WriteI32(bytes, 188, payload.NativeOutcomeCode);
        WriteI64(bytes, 192, payload.NativeCallsAttempted);
        WriteI64(bytes, 200, payload.MutationAttempts);
        WriteI64(bytes, 208, payload.MutationsCommitted);
        WriteI32(bytes, 216, payload.ResponsesAcquired);
        WriteI32(bytes, 220, payload.ActionsAttempted);
        WriteI32(bytes, 224, payload.CapturesAttempted);
        WriteI32(bytes, 228, payload.EmergencyBatchesRejected);
        WriteI64(bytes, 232, payload.LifecycleTransitions);
        WriteI64(bytes, 240, payload.ResponseDurationTicks);
        WriteI64(bytes, 248, payload.ActionDurationTicks);
        WriteI64(bytes, 256, payload.CaptureDurationTicks);
        WriteI64(bytes, 264, payload.TotalDurationTicks);
        // v6 ran contiguously to 272 with nothing spare, so v7 appends: the fourth pinned generation
        // a cycle ran against, and the two pump counters that tell a stalled frame from an idle one.
        WriteU64(bytes, 272, payload.World);
        WriteI32(bytes, 280, payload.CyclesStarted);
        WriteI32(bytes, 284, payload.WorldGateDeferrals);
    }

    internal static ServiceCycleSemanticEvent Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != RecordBytes) throw Invalid();
        var id = ReadIdentity(bytes, 0, 8, required: true);
        var parent = ReadIdentity(bytes, 16, 24, required: false);
        var kindValue = ReadI32(bytes, 32);
        if (kindValue is < (int)ServiceCycleSemanticEventKind.ConfigurationPublished or
            > (int)ServiceCycleSemanticEventKind.WorldCategoryCollected) throw Invalid();
        var kind = (ServiceCycleSemanticEventKind)kindValue;
        if (ReadU32(bytes, 36) != 0) throw Invalid();
        var payload = new ServiceCycleSemanticPayload(
            (ServiceCycleSemanticFields)ReadU64(bytes, 40),
            ReadU64(bytes, 48), ReadU64(bytes, 56), ReadU64(bytes, 64), ReadU64(bytes, 72),
            ReadU64(bytes, 80), ReadU64(bytes, 88), ReadU64(bytes, 96), ReadU64(bytes, 104), ReadU64(bytes, 112),
            ReadI64(bytes, 120), ReadI64(bytes, 128), ReadI64(bytes, 136), ReadI64(bytes, 144), ReadU64(bytes, 152),
            ReadI32(bytes, 160), ReadI32(bytes, 164), ReadI32(bytes, 168), ReadI32(bytes, 172), ReadI32(bytes, 176),
            ReadI32(bytes, 180), ReadI32(bytes, 184), ReadI64(bytes, 192), ReadI64(bytes, 200), ReadI64(bytes, 208),
            ReadI32(bytes, 216), ReadI32(bytes, 220), ReadI32(bytes, 224), ReadI32(bytes, 228), ReadI64(bytes, 232),
            ReadI64(bytes, 240), ReadI64(bytes, 248), ReadI64(bytes, 256), ReadI64(bytes, 264), ReadI32(bytes, 188),
            ReadU64(bytes, 272),
            ReadI32(bytes, 280),
            ReadI32(bytes, 284));
        try
        {
            return new ServiceCycleSemanticEvent(id, parent, kind, in payload);
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
    }

    private static ServiceCycleTraceEventId ReadIdentity(
        ReadOnlySpan<byte> bytes,
        int sessionOffset,
        int sequenceOffset,
        bool required)
    {
        var session = ReadU64(bytes, sessionOffset);
        var sequence = ReadU64(bytes, sequenceOffset);
        if (session == 0 && sequence == 0 && !required) return default;
        if (session == 0 || sequence == 0 || sequence > ServiceCycleTraceEventId.MaximumSequence) throw Invalid();
        return new ServiceCycleTraceEventId(new ServiceCycleTraceSessionId(session), sequence);
    }

    private static FormatException Invalid() => new("Invalid service-cycle semantic event record.");
    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static int ReadI32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
    private static ulong ReadU64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, 8));
    private static long ReadI64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, 8));
    private static void WriteI32(Span<byte> bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(offset, 4), value);
    private static void WriteU32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(offset, 4), value);
    private static void WriteU64(Span<byte> bytes, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(offset, 8), value);
    private static void WriteI64(Span<byte> bytes, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(offset, 8), value);
}
