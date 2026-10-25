using System;
using System.Collections.Generic;
using System.IO;

namespace OrbModding.Common.Runtime.Tracing;

/// <summary>
/// Real file-backed <see cref="ITraceSegmentStorage"/>. Each segment is written to a temporary file in the
/// target directory and published with a write-then-atomic-rename (<see cref="File.Move(string,string)"/>
/// within the same directory), so a reader never observes a partially written segment. Segment file names
/// are ordinal-numbered; the oldest committed file is deleted first to honor the writer's rolling cap. Its
/// methods are invoked only on the writer's dedicated low-priority thread.
/// </summary>
public sealed class FileTraceSegmentStorage : IRestartAwareTraceSegmentStorage
{
    private readonly Queue<string> _committed = new();
    private readonly string _directory;
    private readonly string _prefix;
    private readonly string _extension;

    public FileTraceSegmentStorage(string directory, string filePrefix = "octr-trace", string extension = ".octr")
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("A trace directory is required.", nameof(directory));
        _directory = Path.GetFullPath(directory);
        _prefix = string.IsNullOrEmpty(filePrefix) ? "octr-trace" : filePrefix;
        _extension = extension ?? string.Empty;
        ValidateFileComponent(_prefix, nameof(filePrefix), requireLeadingDot: false);
        ValidateFileComponent(_extension, nameof(extension), requireLeadingDot: _extension.Length != 0);
    }

    public TraceSegmentStorageRecovery Reconcile(
        int maximumCommittedSegments,
        ITraceSegmentHeaderProbe? probe = null)
    {
        if (maximumCommittedSegments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCommittedSegments));

        Directory.CreateDirectory(_directory);
        // Retain only the newest configured K paths while inventorying. Directory size therefore
        // cannot inflate managed memory beyond the explicit retention target.
        var retained = new List<(int Ordinal, string Path)>(maximumCommittedSegments);
        var committedCount = 0;
        var maximumOrdinal = -1;
        var staleTemporaryFilesRemoved = 0;
        var unusable = false;
        foreach (var path in Directory.EnumerateFiles(_directory, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (IsPotentialTemporaryFileName(fileName))
            {
                if (!IsOwnedTemporaryFileName(fileName))
                {
                    unusable = true;
                    continue;
                }
                File.Delete(EnsureContained(path));
                staleTemporaryFilesRemoved++;
                continue;
            }
            if (!IsPotentialCommittedFileName(fileName)) continue;
            if (TryParseCommittedFileName(fileName, out var ordinal))
            {
                committedCount++;
                maximumOrdinal = Math.Max(maximumOrdinal, ordinal);
                InsertNewest(retained, (ordinal, EnsureContained(path)), maximumCommittedSegments);
                continue;
            }
            unusable = true;
        }

        // Exhaustion is a non-destructive startup failure. Do not prune valid evidence and only then
        // discover that no collision-free successor can be named.
        if (maximumOrdinal == int.MaxValue)
            throw new TraceSegmentOrdinalExhaustedException();

        if (unusable || retained.Count != 0 && !CanContinueFrom(probe, retained[retained.Count - 1].Path))
            return Quarantine(staleTemporaryFilesRemoved);

        var startupPruned = 0;
        if (committedCount > retained.Count)
        {
            var firstRetainedOrdinal = retained[0].Ordinal;
            foreach (var path in Directory.EnumerateFiles(_directory, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(path);
                if (!IsPotentialCommittedFileName(fileName)) continue;
                if (!TryParseCommittedFileName(fileName, out var ordinal))
                    throw new IOException("Trace storage changed to an invalid committed artifact during reconciliation.");
                if (ordinal >= firstRetainedOrdinal) continue;
                File.Delete(EnsureContained(path));
                startupPruned++;
            }
        }

        _committed.Clear();
        for (var index = 0; index < retained.Count; index++)
            _committed.Enqueue(retained[index].Path);

        var nextOrdinal = maximumOrdinal < 0
            ? 0
            : maximumOrdinal + 1;
        return new TraceSegmentStorageRecovery(
            nextOrdinal,
            retained.Count,
            startupPruned,
            staleTemporaryFilesRemoved);
    }

    /// <summary>
    /// Abandons a store this writer cannot continue, and counts what it discarded.
    /// </summary>
    /// <remarks>
    /// Evidence that cannot be continued is worth less than a recording of the session in front of
    /// it, and the count says plainly what was traded away.
    /// </remarks>
    private TraceSegmentStorageRecovery Quarantine(int staleTemporaryFilesRemoved)
    {
        var incompatiblePruned = 0;
        foreach (var path in Directory.EnumerateFiles(_directory, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (IsPotentialTemporaryFileName(fileName))
            {
                File.Delete(EnsureContained(path));
                staleTemporaryFilesRemoved++;
                continue;
            }
            if (!IsPotentialCommittedFileName(fileName)) continue;
            File.Delete(EnsureContained(path));
            incompatiblePruned++;
        }

        _committed.Clear();
        return new TraceSegmentStorageRecovery(
            0,
            0,
            0,
            staleTemporaryFilesRemoved,
            incompatiblePruned);
    }

    private static bool CanContinueFrom(ITraceSegmentHeaderProbe? probe, string path)
    {
        if (probe is null) return true;
        var length = probe.HeaderBytes;
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(probe), "A header probe must read at least one byte.");
        var header = new byte[length];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var read = 0;
        while (read < length)
        {
            var count = stream.Read(header, read, length - read);
            if (count == 0) return false;
            read += count;
        }
        return probe.IsCompatible(header);
    }

    public object BeginSegment(int ordinal)
    {
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        Directory.CreateDirectory(_directory);
        var finalPath = EnsureContained(Path.Combine(_directory, $"{_prefix}-{ordinal:D6}{_extension}"));
        var tempPath = EnsureContained(finalPath + ".tmp-" + Guid.NewGuid().ToString("N"));
        var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return new FileSegment(tempPath, finalPath, stream);
    }

    public void Append(object segment, ReadOnlySpan<byte> record)
    {
        var s = (FileSegment)segment;
        s.Stream.Write(record);
    }

    public void CommitSegment(object segment)
    {
        var s = (FileSegment)segment;
        s.Stream.Flush(flushToDisk: true);
        s.Stream.Dispose();
        File.Move(s.TempPath, s.FinalPath); // no-overwrite atomic rename within the directory
        _committed.Enqueue(s.FinalPath);
    }

    public void DiscardSegment(object segment)
    {
        var s = (FileSegment)segment;
        try { s.Stream.Dispose(); }
        catch { /* best-effort cleanup */ }
        try { if (File.Exists(s.TempPath)) File.Delete(s.TempPath); }
        catch { /* best-effort cleanup */ }
    }

    public void DeleteOldestCommitted()
    {
        if (_committed.Count == 0) return;
        var path = _committed.Peek();
        if (File.Exists(path)) File.Delete(path);
        _committed.Dequeue();
    }

    private bool TryParseCommittedFileName(string fileName, out int ordinal)
    {
        ordinal = 0;
        var start = _prefix + "-";
        if (!fileName.StartsWith(start, StringComparison.Ordinal) ||
            !fileName.EndsWith(_extension, StringComparison.Ordinal))
            return false;
        var ordinalLength = fileName.Length - start.Length - _extension.Length;
        if (ordinalLength < 6) return false;
        var value = fileName.Substring(start.Length, ordinalLength);
        for (var index = 0; index < value.Length; index++)
            if (value[index] is < '0' or > '9') return false;
        if (!int.TryParse(value, out ordinal) || ordinal < 0) return false;
        return fileName == $"{_prefix}-{ordinal:D6}{_extension}";
    }

    private bool IsPotentialCommittedFileName(string fileName) =>
        fileName.StartsWith(_prefix + "-", StringComparison.Ordinal) &&
        fileName.EndsWith(_extension, StringComparison.Ordinal);

    private bool IsPotentialTemporaryFileName(string fileName) =>
        fileName.StartsWith(_prefix + "-", StringComparison.Ordinal) &&
        fileName.Contains(_extension + ".tmp-", StringComparison.Ordinal);

    private bool IsOwnedTemporaryFileName(string fileName)
    {
        const int suffixLength = 5 + 32; // .tmp- plus a Guid in N format
        if (fileName.Length <= suffixLength) return false;
        var suffixStart = fileName.Length - suffixLength;
        if (!fileName.AsSpan(suffixStart, 5).SequenceEqual(".tmp-".AsSpan())) return false;
        if (!TryParseCommittedFileName(fileName.Substring(0, suffixStart), out _)) return false;
        for (var index = suffixStart + 5; index < fileName.Length; index++)
        {
            var c = fileName[index];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        }
        return true;
    }

    private static void InsertNewest(
        List<(int Ordinal, string Path)> retained,
        (int Ordinal, string Path) candidate,
        int capacity)
    {
        var index = retained.BinarySearch(candidate, OrdinalComparer.Instance);
        if (index >= 0) throw new IOException("Trace storage contains duplicate committed ordinals.");
        retained.Insert(~index, candidate);
        if (retained.Count > capacity) retained.RemoveAt(0);
    }

    private string EnsureContained(string path)
    {
        var full = Path.GetFullPath(path);
        var root = _directory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? _directory
            : _directory + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Trace path escapes the configured directory.", nameof(path));
        return full;
    }

    private static void ValidateFileComponent(string value, string parameterName, bool requireLeadingDot)
    {
        if (requireLeadingDot && (value.Length < 2 || value[0] != '.'))
            throw new ArgumentException("A trace extension must be empty or begin with a dot.", parameterName);
        if (value.Length == 0 && !requireLeadingDot) throw new ArgumentException("A trace file prefix is required.", parameterName);
        if (Path.IsPathRooted(value) || value.Contains("..", StringComparison.Ordinal) ||
            value.IndexOf(':') >= 0 || value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 ||
            value.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            value.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Trace file names must be single safe path components.", parameterName);
    }

    private sealed class OrdinalComparer : IComparer<(int Ordinal, string Path)>
    {
        internal static readonly OrdinalComparer Instance = new();
        public int Compare((int Ordinal, string Path) x, (int Ordinal, string Path) y) =>
            x.Ordinal.CompareTo(y.Ordinal);
    }

    private sealed class FileSegment
    {
        public FileSegment(string tempPath, string finalPath, FileStream stream)
        {
            TempPath = tempPath;
            FinalPath = finalPath;
            Stream = stream;
        }

        public string TempPath { get; }
        public string FinalPath { get; }
        public FileStream Stream { get; }
    }
}
