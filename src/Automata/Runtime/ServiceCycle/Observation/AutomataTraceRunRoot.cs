using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The suite's <c>trace/</c> directory: stable children for the always-on artifacts, and one
    /// <c>run-&lt;timestamp&gt;/</c> folder per process launch for profiling-only artifacts.
/// </summary>
/// <remarks>
/// <para>
/// The decision journal writes to the stable <c>trace/journal</c> directory. Its rolling segment cap
/// and restart reconciliation govern exactly one directory, so minting a fresh folder per launch gave
/// every launch a fresh budget and let the suite's disk use grow without bound — the opposite of what
/// an always-on recorder with a size cap is for.
/// </para>
/// <para>
/// The profiling full trace and the performance profile stay under the per-launch folder, because the
/// analysis tool correlates exactly one full and one profile session per run folder. What accumulates
/// there instead is whole run folders, so a launch prunes all but the newest few — whole folders, so a
/// surviving one is still a complete correlated capture.
/// </para>
/// </remarks>
internal static class AutomataTraceRunRoot
{
    /// <summary>Run folders that survive a launch, counting the one this launch may write.</summary>
    internal const int RetainedRunFolders = 8;

    private const string RunPrefix = "run-";
    private const int RunNameLength = 24; // run-yyyyMMdd-HHmmss-xxxx
    private const string RelativeRoot = "BepInEx/config/OrbOfCreation-ModSuite/trace";

    internal static readonly string RunName = RunPrefix +
        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
        (DateTime.UtcNow.Ticks & 0xFFFF).ToString("x4", CultureInfo.InvariantCulture);

    internal static string Root => Path.Combine(
        Paths.ConfigPath,
        "OrbOfCreation-ModSuite",
        "trace");

    internal static string Child(string name) => Path.Combine(Root, RunName, name);

    internal static string Stable(string name) => Path.Combine(Root, name);

    internal static string FormatRelativePath(string tail) =>
        RelativeRoot + "/" + RunName + "/" + tail;

    internal static string FormatStableRelativePath(string tail) => RelativeRoot + "/" + tail;

    internal static int SweepRunFolders() => SweepRunFolders(Root, RunName, RetainedRunFolders);

    /// <summary>
    /// Deletes the oldest run folders until at most <paramref name="retained"/> remain, counting
    /// <paramref name="currentRun"/>, which is never deleted. Every other entry under
    /// <paramref name="root"/> — the journal directory included — is left alone.
    /// </summary>
    internal static int SweepRunFolders(string root, string currentRun, int retained)
    {
        if (string.IsNullOrEmpty(root)) throw new ArgumentException("A trace root is required.", nameof(root));
        if (retained <= 0) throw new ArgumentOutOfRangeException(nameof(retained));

        var candidates = new List<string>();
        try
        {
            if (!Directory.Exists(root)) return 0;
            foreach (var path in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(path);
                if (IsRunFolderName(name) && !string.Equals(name, currentRun, StringComparison.Ordinal))
                    candidates.Add(name);
            }
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            return 0;
        }

        // The folder name is a fixed-width UTC timestamp, so ordinal order is chronological order
        // without trusting filesystem timestamps a copied or restored directory would not preserve.
        candidates.Sort(StringComparer.Ordinal);
        var removable = candidates.Count - (retained - 1);
        var removed = 0;
        for (var index = 0; index < removable; index++)
        {
            try
            {
                Directory.Delete(Path.Combine(root, candidates[index]), recursive: true);
                removed++;
            }
            catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
            {
                // A folder the user is reading, or one the filesystem refuses, is left for the next
                // launch. Retention is best-effort; it never denies the suite its own recording.
            }
        }
        return removed;
    }

    private static bool IsRunFolderName(string name)
    {
        if (name is null || name.Length != RunNameLength ||
            !name.StartsWith(RunPrefix, StringComparison.Ordinal) ||
            name[12] != '-' || name[19] != '-')
        {
            return false;
        }
        for (var index = 4; index < 12; index++)
            if (!IsDigit(name[index])) return false;
        for (var index = 13; index < 19; index++)
            if (!IsDigit(name[index])) return false;
        for (var index = 20; index < RunNameLength; index++)
            if (!IsLowerHexadecimal(name[index])) return false;
        return true;
    }

    private static bool IsDigit(char value) => value is >= '0' and <= '9';

    private static bool IsLowerHexadecimal(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';
}
