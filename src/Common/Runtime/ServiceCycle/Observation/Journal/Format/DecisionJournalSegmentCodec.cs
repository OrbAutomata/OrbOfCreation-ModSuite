using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

internal static class DecisionJournalSegmentCodec
{
    /// <summary>
    /// v4 attributes each action outcome to the candidate's action kind as well. Older stores
    /// cannot be continued and are loudly counted by reconciliation before recording starts.
    /// </summary>
    internal const ushort SchemaVersion = 4;
    internal const int HeaderBytes = 80;
    internal const int FooterBytes = 40;
    internal const int MaximumRecords = 128;

    private const int FooterChecksumOffset = 32;
    private static readonly byte[] HeaderMagic = { (byte)'O', (byte)'S', (byte)'J', (byte)'D' };
    private static readonly byte[] FooterMagic = { (byte)'O', (byte)'S', (byte)'J', (byte)'F' };

    /// <summary>
    /// Whether a stored segment's header is one this build can keep writing after.
    /// </summary>
    /// <remarks>
    /// This is the header half of <see cref="DecodeCore"/>: the fields a successor segment has to
    /// agree with. Body and checksum are deliberately not read — the question is whether the store
    /// can be continued, not whether every retained segment is intact.
    /// </remarks>
    internal static bool IsCompatibleHeader(ReadOnlySpan<byte> header) =>
        header.Length >= HeaderBytes &&
        header.Slice(0, 4).SequenceEqual(HeaderMagic) &&
        ReadU16(header, 4) == SchemaVersion &&
        ReadU16(header, 6) == HeaderBytes &&
        ReadU16(header, 8) == SchemaVersion &&
        ReadU16(header, 10) == DecisionJournalRecordCodec.RecordBytes;

    internal static int GetEncodedLength(int recordCount)
    {
        if (recordCount is <= 0 or > MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        return checked(HeaderBytes + recordCount * DecisionJournalRecordCodec.RecordBytes + FooterBytes);
    }

    internal static int Encode(
        DecisionJournalRunId run,
        ulong ordinal,
        ulong firstRecordSequence,
        ReadOnlySpan<DecisionJournalRecord> records,
        Span<byte> destination)
    {
        var length = GetEncodedLength(records.Length);
        if (!run.IsValid) throw new ArgumentException("A valid journal run is required.", nameof(run));
        if (firstRecordSequence == 0) throw new ArgumentOutOfRangeException(nameof(firstRecordSequence));
        _ = checked(firstRecordSequence + (ulong)records.Length - 1);
        if (destination.Length < length) throw new ArgumentException("The destination is too small.", nameof(destination));

        var output = destination.Slice(0, length);
        output.Clear();
        HeaderMagic.CopyTo(output);
        WriteU16(output, 4, SchemaVersion);
        WriteU16(output, 6, HeaderBytes);
        WriteU16(output, 8, SchemaVersion);
        WriteU16(output, 10, DecisionJournalRecordCodec.RecordBytes);
        WriteU64(output, 16, run.Value);
        WriteU64(output, 24, ordinal);
        WriteU64(output, 32, firstRecordSequence);
        WriteU64(output, 40, checked((ulong)records.Length));
        TimestampRange(records, out var firstTimestamp, out var lastTimestamp);
        WriteI64(output, 48, firstTimestamp);
        WriteI64(output, 56, lastTimestamp);
        WriteU64(output, 64, checked((ulong)length));

        for (var index = 0; index < records.Length; index++)
            DecisionJournalRecordCodec.Write(
                output.Slice(
                    HeaderBytes + index * DecisionJournalRecordCodec.RecordBytes,
                    DecisionJournalRecordCodec.RecordBytes),
                in records[index]);

        var footerOffset = length - FooterBytes;
        FooterMagic.CopyTo(output.Slice(footerOffset));
        WriteU16(output, footerOffset + 4, SchemaVersion);
        WriteU16(output, footerOffset + 6, FooterBytes);
        WriteU64(output, footerOffset + 8, run.Value);
        WriteU64(output, footerOffset + 16, ordinal);
        WriteU64(
            output,
            footerOffset + 24,
            checked(firstRecordSequence + (ulong)records.Length - 1));
        var checksumOffset = footerOffset + FooterChecksumOffset;
        WriteU32(output, checksumOffset, TraceCrc32.ComputeExcluding(output, checksumOffset, 4));
        return length;
    }

    internal static DecisionJournalSegmentDocument Decode(ReadOnlySpan<byte> source)
    {
        try
        {
            return DecodeCore(source);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }

    private static DecisionJournalSegmentDocument DecodeCore(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderBytes + DecisionJournalRecordCodec.RecordBytes + FooterBytes ||
            !source.Slice(0, 4).SequenceEqual(HeaderMagic) ||
            ReadU16(source, 4) != SchemaVersion || ReadU16(source, 6) != HeaderBytes ||
            ReadU16(source, 8) != SchemaVersion ||
            ReadU16(source, 10) != DecisionJournalRecordCodec.RecordBytes ||
            ReadU32(source, 12) != 0 || ReadU64(source, 72) != 0)
            throw Invalid();

        var runValue = ReadU64(source, 16);
        var ordinal = ReadU64(source, 24);
        var firstRecordSequence = ReadU64(source, 32);
        var countValue = ReadU64(source, 40);
        var firstTimestamp = ReadI64(source, 48);
        var lastTimestamp = ReadI64(source, 56);
        var totalBytes = ReadU64(source, 64);
        if (runValue == 0 || firstRecordSequence == 0 || countValue is 0 or > MaximumRecords ||
            totalBytes != checked((ulong)GetEncodedLength((int)countValue)) ||
            totalBytes != checked((ulong)source.Length) ||
            lastTimestamp < firstTimestamp)
            throw Invalid();

        var footerOffset = source.Length - FooterBytes;
        if (!source.Slice(footerOffset, 4).SequenceEqual(FooterMagic) ||
            ReadU16(source, footerOffset + 4) != SchemaVersion ||
            ReadU16(source, footerOffset + 6) != FooterBytes ||
            ReadU64(source, footerOffset + 8) != runValue ||
            ReadU64(source, footerOffset + 16) != ordinal ||
            ReadU64(source, footerOffset + 24) != checked(firstRecordSequence + countValue - 1) ||
            ReadU32(source, footerOffset + 36) != 0)
            throw Invalid();
        var checksumOffset = footerOffset + FooterChecksumOffset;
        if (ReadU32(source, checksumOffset) !=
            TraceCrc32.ComputeExcluding(source, checksumOffset, 4))
            throw Invalid();

        var records = new DecisionJournalRecord[(int)countValue];
        for (var index = 0; index < records.Length; index++)
            records[index] = DecisionJournalRecordCodec.Read(source.Slice(
                HeaderBytes + index * DecisionJournalRecordCodec.RecordBytes,
                DecisionJournalRecordCodec.RecordBytes));
        TimestampRange(records, out var actualFirstTimestamp, out var actualLastTimestamp);
        if (actualFirstTimestamp != firstTimestamp || actualLastTimestamp != lastTimestamp)
            throw Invalid();
        return new DecisionJournalSegmentDocument(
            new DecisionJournalRunId(runValue),
            ordinal,
            firstRecordSequence,
            records);
    }

    private static void TimestampRange(
        ReadOnlySpan<DecisionJournalRecord> records,
        out long firstTimestamp,
        out long lastTimestamp)
    {
        firstTimestamp = long.MaxValue;
        lastTimestamp = 0;
        for (var index = 0; index < records.Length; index++)
        {
            DecisionJournalRecordValidation.Validate(in records[index]);
            firstTimestamp = Math.Min(firstTimestamp, records[index].FirstTimestampTicks);
            lastTimestamp = Math.Max(lastTimestamp, records[index].LastTimestampTicks);
        }
    }

    private static FormatException Invalid() => new("Invalid decision-journal segment.");
    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static ulong ReadU64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, 8));
    private static long ReadI64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, 8));
    private static void WriteU16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(offset, 2), value);
    private static void WriteU32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(offset, 4), value);
    private static void WriteU64(Span<byte> bytes, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(offset, 8), value);
    private static void WriteI64(Span<byte> bytes, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(offset, 8), value);
}

/// <summary>The journal's answer to storage asking whether a store can still be written to.</summary>
internal sealed class DecisionJournalSegmentHeaderProbe : ITraceSegmentHeaderProbe
{
    internal static readonly DecisionJournalSegmentHeaderProbe Instance = new();

    private DecisionJournalSegmentHeaderProbe() { }

    public int HeaderBytes => DecisionJournalSegmentCodec.HeaderBytes;

    public bool IsCompatible(ReadOnlySpan<byte> header) =>
        DecisionJournalSegmentCodec.IsCompatibleHeader(header);
}
