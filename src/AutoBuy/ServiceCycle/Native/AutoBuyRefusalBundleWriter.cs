using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OrbAutomata;

/// <summary>
/// Where a refusal bundle is put. Separated from the responder so retry and invariant policy
/// can be tested without a filesystem, and so a failure to write one is a value the responder handles
/// rather than an exception thrown into the action path.
/// </summary>
internal interface IAutoBuyRefusalBundlePort
{
    /// <summary>
    /// Writes one bundle and answers where it went. False means it could not be written; the caller
    /// still applies its classified policy, and still says so, without a path.
    /// </summary>
    bool TryWrite(string contents, DateTime utcNow, out string path);
}

/// <summary>
/// Writes invariant-refusal bundles to a stable directory under the suite's trace root and keeps
/// the newest evidence within fixed count and byte budgets.
/// </summary>
/// <remarks>
/// <para>
/// A stable directory rather than a per-launch one, because a bundle is written at the moment Auto
/// Buy records a refusal and is read some time later, by someone who was told a path — the folder
/// they were told about has to be the folder it is still in after a restart.
/// </para>
/// <para>
/// Nothing here throws. The bundle exists so a person can find out why Auto Buy held off; a
/// writer that could fault the action path would turn a diagnosis into a second fault.
/// </para>
/// </remarks>
internal sealed class AutoBuyRefusalBundleWriter : IAutoBuyRefusalBundlePort
{
    /// <summary>Bundles that survive a write, counting the one being written.</summary>
    internal const int RetainedBundles = 8;
    internal const long RetainedBytes = 1024L * 1024L;

    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

    private readonly Func<string> _directory;

    public AutoBuyRefusalBundleWriter(Func<string> directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public bool TryWrite(string contents, DateTime utcNow, out string path)
    {
        path = string.Empty;
        try
        {
            var directory = _directory();
            if (string.IsNullOrWhiteSpace(directory)) return false;
            var bytes = Utf8WithoutBom.GetBytes(contents ?? string.Empty);
            if (bytes.LongLength > RetainedBytes) return false;
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, AutoBuyRefusalBundle.FileName(utcNow));
            if (File.Exists(target)) return false;
            if (!MakeRoom(directory, Path.GetFileName(target), bytes.LongLength)) return false;
            File.WriteAllBytes(target, bytes);
            path = target;
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes oldest owned bundles before the write until both count and byte budgets have room.
    /// The file name carries a fixed-width UTC timestamp, so ordinal order is chronological without
    /// trusting filesystem timestamps a copied directory would not preserve.
    /// </summary>
    private static bool MakeRoom(string directory, string current, long incomingBytes)
    {
        var existing = new List<BundleFile>();
        foreach (var file in Directory.EnumerateFiles(
                     directory,
                     AutoBuyRefusalBundle.FileNamePrefix + "*.txt"))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, current, StringComparison.Ordinal)) continue;
            existing.Add(new BundleFile(name, new FileInfo(file).Length));
        }

        existing.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        var totalBytes = incomingBytes;
        foreach (var bundle in existing) totalBytes = checked(totalBytes + bundle.Bytes);
        var index = 0;
        while (existing.Count - index + 1 > RetainedBundles || totalBytes > RetainedBytes)
        {
            if (index >= existing.Count) return false;
            var bundle = existing[index++];
            File.Delete(Path.Combine(directory, bundle.Name));
            totalBytes -= bundle.Bytes;
        }
        return true;
    }

    private readonly struct BundleFile
    {
        public BundleFile(string name, long bytes)
        {
            Name = name;
            Bytes = bytes;
        }

        public string Name { get; }
        public long Bytes { get; }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or OverflowException or System.Security.SecurityException;
}
