using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.ServiceCycleTrace.IO;

namespace OrbModding.ServiceCycleTrace.Journal;

internal sealed class DecisionJournalDirectory
{
    private const string Prefix = "journal-";
    private const string Extension = ".osjd";
    private readonly string _path;

    internal DecisionJournalDirectory(string path)
    {
        _path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(_path))
            throw new DirectoryNotFoundException("The decision-journal directory does not exist.");
    }

    internal DecisionJournalInventory Inventory()
    {
        var count = 0L;
        var minimum = int.MaxValue;
        var maximum = -1;
        foreach (var path in Directory.EnumerateFiles(_path, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (IsOwnedTemporarySegment(name)) continue;
            if (!LooksLikeSegment(name)) continue;
            var ordinal = ParseSegmentName(name);
            count = checked(count + 1);
            minimum = Math.Min(minimum, ordinal);
            maximum = Math.Max(maximum, ordinal);
        }

        if (count == 0) return default;
        if ((long)maximum - minimum + 1 != count)
            throw new InvalidDataException(
                "The retained decision-journal files are not one contiguous storage-ordinal suffix.");
        return new DecisionJournalInventory(minimum, maximum, count);
    }

    internal DecisionJournalSegmentDocument ReadSegment(int ordinal, out int encodedBytes)
    {
        var bytes = BoundedBinaryFile.Read(
            SegmentPath(ordinal),
            DecisionJournalSegmentCodec.GetEncodedLength(1),
            DecisionJournalSegmentCodec.GetEncodedLength(DecisionJournalSegmentCodec.MaximumRecords));
        var segment = DecisionJournalSegmentCodec.Decode(bytes);
        if (segment.Ordinal != checked((ulong)ordinal))
            throw new InvalidDataException(
                "A decision-journal filename does not match its encoded storage ordinal.");
        encodedBytes = bytes.Length;
        return segment;
    }

    internal void EnsureSafeReportPath(string path)
    {
        var name = Path.GetFileName(Path.GetFullPath(path));
        if (LooksLikeSegment(name))
            throw new InvalidOperationException(
                $"A report cannot be named {name}: names starting with {Prefix} or ending with " +
                $"{Extension} are reserved for decision-journal evidence.");
    }

    private string SegmentPath(int ordinal) => Path.Combine(
        _path,
        Prefix + ordinal.ToString("D6", CultureInfo.InvariantCulture) + Extension);

    private static bool LooksLikeSegment(string name) =>
        name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    private static int ParseSegmentName(string name)
    {
        if (!TryParseSegmentName(name, out var ordinal))
            throw new InvalidDataException(
                "The decision-journal directory contains a noncanonical segment name.");
        return ordinal;
    }

    private static bool TryParseSegmentName(string name, out int ordinal)
    {
        ordinal = 0;
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) ||
            !name.EndsWith(Extension, StringComparison.Ordinal))
            return false;
        var value = name.Substring(Prefix.Length, name.Length - Prefix.Length - Extension.Length);
        if (value.Length < 6) return false;
        for (var index = 0; index < value.Length; index++)
            if (value[index] is < '0' or > '9') return false;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ordinal) &&
            ordinal >= 0 &&
            string.Equals(
                name,
                Prefix + ordinal.ToString("D6", CultureInfo.InvariantCulture) + Extension,
                StringComparison.Ordinal);
    }

    private static bool IsOwnedTemporarySegment(string name)
    {
        const int markerLength = 5;
        const int identityLength = 32;
        var marker = name.Length - markerLength - identityLength;
        if (marker <= 0 ||
            !name.AsSpan(marker, markerLength).SequenceEqual(".tmp-".AsSpan()) ||
            !TryParseSegmentName(name.Substring(0, marker), out _))
            return false;
        for (var index = marker + markerLength; index < name.Length; index++)
        {
            var value = name[index];
            if (value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        }
        return true;
    }
}

internal readonly struct DecisionJournalInventory
{
    internal DecisionJournalInventory(int firstOrdinal, int lastOrdinal, long count)
    {
        FirstOrdinal = firstOrdinal;
        LastOrdinal = lastOrdinal;
        Count = count;
    }

    internal bool HasSegments => Count != 0;
    internal int FirstOrdinal { get; }
    internal int LastOrdinal { get; }
    internal long Count { get; }
}
