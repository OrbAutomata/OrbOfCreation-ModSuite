using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OrbModding.Common;

internal enum AutomaticSaveBackupTrigger
{
    None = 0,
    FreshInstall = 1,
    VersionChanged = 2,
    SaveRootChanged = 3,
    CorruptStamp = 4,
}

internal sealed class AutomaticSaveBackupStatus
{
    private AutomaticSaveBackupStatus(
        bool allowsAutomation,
        bool backupCreated,
        AutomaticSaveBackupTrigger trigger,
        string backupPath,
        int fileCount,
        string failureReason,
        int prunedBackupCount,
        IReadOnlyList<string> retentionFailures)
    {
        AllowsAutomation = allowsAutomation;
        BackupCreated = backupCreated;
        Trigger = trigger;
        BackupPath = backupPath ?? string.Empty;
        FileCount = fileCount;
        FailureReason = failureReason ?? string.Empty;
        PrunedBackupCount = prunedBackupCount;
        RetentionFailures = retentionFailures?.ToArray() ?? Array.Empty<string>();
    }

    internal bool AllowsAutomation { get; }
    internal bool BackupCreated { get; }
    internal AutomaticSaveBackupTrigger Trigger { get; }
    internal string BackupPath { get; }
    internal int FileCount { get; }
    internal string FailureReason { get; }
    internal int PrunedBackupCount { get; }
    internal IReadOnlyList<string> RetentionFailures { get; }
    internal bool HasRetentionFailure => RetentionFailures.Count != 0;

    internal static AutomaticSaveBackupStatus NotRun { get; } = Failed(
        AutomaticSaveBackupTrigger.FreshInstall,
        "The automatic save-backup gate did not run.");

    internal static AutomaticSaveBackupStatus Ready(
        bool backupCreated,
        AutomaticSaveBackupTrigger trigger,
        string backupPath,
        int fileCount,
        int prunedBackupCount,
        IReadOnlyList<string> retentionFailures) =>
        new(
            allowsAutomation: true,
            backupCreated,
            trigger,
            backupPath,
            fileCount,
            string.Empty,
            prunedBackupCount,
            retentionFailures);

    internal static AutomaticSaveBackupStatus Failed(
        AutomaticSaveBackupTrigger trigger,
        string reason) =>
        new(
            allowsAutomation: false,
            backupCreated: false,
            trigger,
            string.Empty,
            fileCount: 0,
            reason,
            prunedBackupCount: 0,
            Array.Empty<string>());
}

internal static class AutomaticSaveBackupPathPolicy
{
    private const string StampFileName = PluginIds.SuiteGuid + ".auto-save-backup.stamp";

    internal static string ResolveStampPath(string configFilePath, string configRoot)
    {
        string directory;
        if (!string.IsNullOrWhiteSpace(configFilePath))
        {
            var fullConfigPath = Path.GetFullPath(configFilePath);
            directory = Path.GetDirectoryName(fullConfigPath) ?? string.Empty;
        }
        else
        {
            directory = string.IsNullOrWhiteSpace(configRoot)
                ? string.Empty
                : Path.GetFullPath(configRoot);
        }

        if (directory.Length == 0)
            throw new InvalidOperationException(
                "The BepInEx configuration directory could not be resolved for the automatic save-backup stamp.");
        return Path.Combine(directory, StampFileName);
    }
}

internal static class SuiteStartupAdmission
{
    internal static bool AllowsRuntime(
        bool buildCompatibilityAllowsRuntime,
        AutomaticSaveBackupStatus automaticSaveBackup)
    {
        if (automaticSaveBackup is null)
            throw new ArgumentNullException(nameof(automaticSaveBackup));
        return buildCompatibilityAllowsRuntime && automaticSaveBackup.AllowsAutomation;
    }
}

internal interface IAutomaticSaveBackupFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IReadOnlyList<string> EnumerateFiles(string path);
    IReadOnlyList<string> EnumerateDirectories(string path);
    void CreateDirectory(string path);
    byte[] ReadAllBytes(string path);
    void WriteNewFile(string path, byte[] contents);
    void WriteStamp(string path, byte[] contents);
    void MoveDirectory(string sourcePath, string destinationPath);
    void DeleteDirectory(string path);
}

internal sealed class PhysicalAutomaticSaveBackupFileSystem : IAutomaticSaveBackupFileSystem
{
    internal static PhysicalAutomaticSaveBackupFileSystem Instance { get; } = new();

    private PhysicalAutomaticSaveBackupFileSystem()
    {
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public IReadOnlyList<string> EnumerateFiles(string path) => Directory.EnumerateFiles(path).ToArray();
    public IReadOnlyList<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(path).ToArray();
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public byte[] ReadAllBytes(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        if (stream.Length > int.MaxValue)
            throw new IOException("The file is too large to verify as one automatic save-backup input.");

        var expectedLength = checked((int)stream.Length);
        var first = ReadExactly(stream, expectedLength);
        if (stream.ReadByte() != -1 || stream.Length != expectedLength)
            throw new IOException("The file length changed while the automatic save backup was reading it.");

        stream.Position = 0;
        var second = ReadExactly(stream, expectedLength);
        if (stream.ReadByte() != -1 || stream.Length != expectedLength || !first.SequenceEqual(second))
            throw new IOException("The file changed while the automatic save backup was reading it.");
        return first;
    }

    public void WriteNewFile(string path, byte[] contents)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        stream.Write(contents, 0, contents.Length);
        stream.Flush(flushToDisk: true);
    }

    public void WriteStamp(string path, byte[] contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException("The automatic save-backup stamp has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            WriteNewFile(temporaryPath, contents);
            VerifyExactFile(temporaryPath, contents);
            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, path);
            VerifyExactFile(path, contents);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(sourcePath, destinationPath);

    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);

    private static byte[] ReadExactly(Stream stream, int length)
    {
        var contents = new byte[length];
        var offset = 0;
        while (offset < contents.Length)
        {
            var read = stream.Read(contents, offset, contents.Length - offset);
            if (read == 0)
                throw new EndOfStreamException(
                    "The file ended before the automatic save backup could read its declared length.");
            offset += read;
        }
        return contents;
    }

    private static void VerifyExactFile(string path, byte[] expected)
    {
        var actual = ReadAllBytesOnce(path);
        if (!actual.SequenceEqual(expected))
            throw new IOException("Automatic save-backup file verification failed.");
    }

    private static byte[] ReadAllBytesOnce(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > int.MaxValue)
            throw new IOException("The automatic save-backup file is too large to verify.");
        return ReadExactly(stream, checked((int)stream.Length));
    }
}

internal static class AutomaticSaveBackupStampCodec
{
    private const string Header = "OrbModSuite automatic save backup stamp v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] Encode(AutomaticSaveBackupStamp stamp)
    {
        if (stamp is null) throw new ArgumentNullException(nameof(stamp));
        return StrictUtf8.GetBytes(string.Join("\n", new[]
        {
            Header,
            "suite-version=" + EncodeText(stamp.SuiteVersion),
            "save-root=" + EncodeText(stamp.SaveRoot),
            "backup-path=" + EncodeText(stamp.BackupPath),
            "file-count=" + stamp.FileCount.ToString(CultureInfo.InvariantCulture),
            string.Empty,
        }));
    }

    internal static bool TryDecode(byte[] contents, out AutomaticSaveBackupStamp stamp)
    {
        stamp = null!;
        try
        {
            var lines = StrictUtf8.GetString(contents ?? Array.Empty<byte>()).Split('\n');
            if (lines.Length != 6 || lines[0] != Header || lines[5].Length != 0 ||
                !TryReadText(lines[1], "suite-version=", out var suiteVersion) ||
                !TryReadText(lines[2], "save-root=", out var saveRoot) ||
                !TryReadText(lines[3], "backup-path=", out var backupPath) ||
                !lines[4].StartsWith("file-count=", StringComparison.Ordinal) ||
                !int.TryParse(
                    lines[4].Substring("file-count=".Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var fileCount) ||
                string.IsNullOrWhiteSpace(suiteVersion) ||
                string.IsNullOrWhiteSpace(saveRoot) ||
                string.IsNullOrWhiteSpace(backupPath) ||
                fileCount <= 0)
            {
                return false;
            }

            stamp = new AutomaticSaveBackupStamp(
                suiteVersion,
                saveRoot,
                backupPath,
                fileCount);
            return true;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            return false;
        }
    }

    private static string EncodeText(string value) =>
        Convert.ToBase64String(StrictUtf8.GetBytes(value ?? string.Empty));

    private static bool TryReadText(string line, string prefix, out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var encoded = line.Substring(prefix.Length);
        var bytes = Convert.FromBase64String(encoded);
        if (!string.Equals(Convert.ToBase64String(bytes), encoded, StringComparison.Ordinal))
            return false;
        value = StrictUtf8.GetString(bytes);
        return true;
    }
}

internal sealed class AutomaticSaveBackupStamp
{
    internal AutomaticSaveBackupStamp(
        string suiteVersion,
        string saveRoot,
        string backupPath,
        int fileCount)
    {
        SuiteVersion = suiteVersion;
        SaveRoot = saveRoot;
        BackupPath = backupPath;
        FileCount = fileCount;
    }

    internal string SuiteVersion { get; }
    internal string SaveRoot { get; }
    internal string BackupPath { get; }
    internal int FileCount { get; }
}

internal static class AutomaticSaveBackup
{
    internal const int RetainedBackups = 5;
    internal const string BackupDirectoryName = "backups";
    internal const string BackupPrefix = "auto-modsuite-backup-";
    private const int BackupTimestampLength = 16; // yyyyMMddTHHmmssZ

    internal static AutomaticSaveBackupStatus Run(
        string suiteVersion,
        string saveRoot,
        string stampPath,
        DateTime utcNow,
        IAutomaticSaveBackupFileSystem? fileSystem = null)
    {
        fileSystem ??= PhysicalAutomaticSaveBackupFileSystem.Instance;
        var trigger = AutomaticSaveBackupTrigger.FreshInstall;
        try
        {
            if (string.IsNullOrWhiteSpace(suiteVersion))
                throw new InvalidOperationException("The running suite version is unavailable.");
            if (string.IsNullOrWhiteSpace(saveRoot))
                throw new InvalidOperationException("Unity did not provide a persistent save-data path.");
            if (string.IsNullOrWhiteSpace(stampPath))
                throw new InvalidOperationException("The automatic save-backup stamp path is unavailable.");

            var normalizedSaveRoot = NormalizeDirectory(saveRoot);
            var normalizedStampPath = Path.GetFullPath(stampPath);
            if (!fileSystem.DirectoryExists(normalizedSaveRoot))
                throw new DirectoryNotFoundException(
                    "Unity's persistent save-data directory does not exist: " + normalizedSaveRoot);

            var backupRoot = Path.Combine(normalizedSaveRoot, BackupDirectoryName);
            AutomaticSaveBackupStamp? stamp = null;
            if (fileSystem.FileExists(normalizedStampPath))
            {
                try
                {
                    if (!AutomaticSaveBackupStampCodec.TryDecode(
                            fileSystem.ReadAllBytes(normalizedStampPath),
                            out stamp))
                    {
                        trigger = AutomaticSaveBackupTrigger.CorruptStamp;
                    }
                }
                catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
                {
                    trigger = AutomaticSaveBackupTrigger.CorruptStamp;
                    stamp = null;
                }
            }

            if (stamp is not null)
            {
                if (!string.Equals(stamp.SuiteVersion, suiteVersion, StringComparison.Ordinal))
                {
                    trigger = AutomaticSaveBackupTrigger.VersionChanged;
                }
                else if (!TryNormalizeDirectory(stamp.SaveRoot, out var stampedSaveRoot))
                {
                    trigger = AutomaticSaveBackupTrigger.CorruptStamp;
                }
                else if (!string.Equals(
                             stampedSaveRoot,
                             normalizedSaveRoot,
                             StringComparison.Ordinal))
                {
                    trigger = AutomaticSaveBackupTrigger.SaveRootChanged;
                }
                else if (!IsOwnedBackupDirectory(backupRoot, stamp.BackupPath) ||
                         !fileSystem.DirectoryExists(stamp.BackupPath))
                {
                    trigger = AutomaticSaveBackupTrigger.CorruptStamp;
                }
                else
                {
                    var retention = Prune(fileSystem, backupRoot, stamp.BackupPath);
                    return AutomaticSaveBackupStatus.Ready(
                        backupCreated: false,
                        AutomaticSaveBackupTrigger.None,
                        stamp.BackupPath,
                        stamp.FileCount,
                        retention.PrunedCount,
                        retention.Failures);
                }
            }

            return CreateBackup(
                suiteVersion,
                normalizedSaveRoot,
                normalizedStampPath,
                backupRoot,
                utcNow,
                trigger,
                fileSystem);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            return AutomaticSaveBackupStatus.Failed(trigger, DescribeFailure(exception));
        }
    }

    private static AutomaticSaveBackupStatus CreateBackup(
        string suiteVersion,
        string saveRoot,
        string stampPath,
        string backupRoot,
        DateTime utcNow,
        AutomaticSaveBackupTrigger trigger,
        IAutomaticSaveBackupFileSystem fileSystem)
    {
        var sourceFiles = EnumerateSaveFiles(fileSystem, saveRoot);
        if (sourceFiles.Length == 0)
            throw new IOException(
                "No active .sav files or steam_autocloud.vdf were found in Unity's persistent save-data directory.");

        fileSystem.CreateDirectory(backupRoot);
        var timestamp = utcNow.ToUniversalTime()
            .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, BackupPrefix + timestamp);
        var stagingPath = backupPath + ".partial-" + Guid.NewGuid().ToString("N");
        var stagingExists = false;
        var capturedContents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            if (fileSystem.DirectoryExists(backupPath))
                throw new IOException("An automatic save backup already exists for timestamp " + timestamp + ".");
            fileSystem.CreateDirectory(stagingPath);
            stagingExists = true;

            foreach (var sourcePath in sourceFiles)
            {
                var fileName = Path.GetFileName(sourcePath);
                byte[] sourceBytes;
                try
                {
                    sourceBytes = fileSystem.ReadAllBytes(sourcePath);
                }
                catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
                {
                    throw new IOException(
                        "Could not read active save file '" + fileName + "' cleanly.",
                        exception);
                }
                capturedContents.Add(sourcePath, sourceBytes);

                var destinationPath = Path.Combine(stagingPath, fileName);
                fileSystem.WriteNewFile(destinationPath, sourceBytes);
                var copiedBytes = fileSystem.ReadAllBytes(destinationPath);
                if (!sourceBytes.SequenceEqual(copiedBytes))
                    throw new IOException("Backup verification failed for active save file '" + fileName + "'.");
            }

            var confirmedFiles = EnumerateSaveFiles(fileSystem, saveRoot);
            if (!sourceFiles.SequenceEqual(confirmedFiles, StringComparer.Ordinal))
                throw new IOException(
                    "The active save-file set changed while the automatic save backup was being created.");
            foreach (var sourcePath in confirmedFiles)
            {
                var confirmedBytes = fileSystem.ReadAllBytes(sourcePath);
                if (!capturedContents[sourcePath].SequenceEqual(confirmedBytes))
                {
                    throw new IOException(
                        "Active save file '" +
                        Path.GetFileName(sourcePath) +
                        "' changed while the automatic save backup was being created.");
                }
            }

            fileSystem.MoveDirectory(stagingPath, backupPath);
            stagingExists = false;
            var stamp = new AutomaticSaveBackupStamp(
                suiteVersion,
                saveRoot,
                backupPath,
                sourceFiles.Length);
            fileSystem.WriteStamp(stampPath, AutomaticSaveBackupStampCodec.Encode(stamp));

            var retention = Prune(fileSystem, backupRoot, backupPath);
            return AutomaticSaveBackupStatus.Ready(
                backupCreated: true,
                trigger,
                backupPath,
                sourceFiles.Length,
                retention.PrunedCount,
                retention.Failures);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            if (stagingExists)
            {
                try
                {
                    fileSystem.DeleteDirectory(stagingPath);
                }
                catch (Exception cleanupException) when (!ExceptionDetail.IsProcessFatal(cleanupException))
                {
                    throw new IOException(
                        DescribeFailure(exception) +
                        " The incomplete staging directory also could not be removed: " +
                        DescribeFailure(cleanupException),
                        exception);
                }
            }
            throw;
        }
    }

    private static RetentionOutcome Prune(
        IAutomaticSaveBackupFileSystem fileSystem,
        string backupRoot,
        string currentBackupPath)
    {
        var failures = new List<string>();
        IReadOnlyList<string> directories;
        try
        {
            if (!fileSystem.DirectoryExists(backupRoot))
                return new RetentionOutcome(0, failures);
            directories = fileSystem.EnumerateDirectories(backupRoot);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            failures.Add("Could not enumerate automatic backup retention: " + DescribeFailure(exception));
            return new RetentionOutcome(0, failures);
        }

        var candidates = directories
            .Where(path => IsOwnedBackupDirectory(backupRoot, path))
            .Where(path => !SameDirectory(path, currentBackupPath))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        var removable = Math.Max(0, candidates.Length - (RetainedBackups - 1));
        var pruned = 0;
        for (var index = 0; index < candidates.Length && pruned < removable; index++)
        {
            try
            {
                fileSystem.DeleteDirectory(candidates[index]);
                pruned++;
            }
            catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
            {
                failures.Add(
                    "Could not prune owned automatic backup '" +
                    Path.GetFileName(candidates[index]) +
                    "': " +
                    DescribeFailure(exception));
            }
        }
        return new RetentionOutcome(pruned, failures);
    }

    private static bool IsSaveFileName(string name) =>
        name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "steam_autocloud.vdf", StringComparison.OrdinalIgnoreCase);

    private static string[] EnumerateSaveFiles(
        IAutomaticSaveBackupFileSystem fileSystem,
        string saveRoot) =>
        fileSystem.EnumerateFiles(saveRoot)
            .Where(path => IsSaveFileName(Path.GetFileName(path)))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

    private static bool IsOwnedBackupDirectory(string backupRoot, string path)
    {
        try
        {
            var fullRoot = NormalizeDirectory(backupRoot);
            var fullPath = NormalizeDirectory(path);
            var parent = Path.GetDirectoryName(fullPath) ?? string.Empty;
            return string.Equals(NormalizeDirectory(parent), fullRoot, StringComparison.Ordinal) &&
                   IsOwnedBackupName(Path.GetFileName(fullPath));
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            return false;
        }
    }

    internal static bool IsOwnedBackupName(string name)
    {
        if (name is null ||
            name.Length != BackupPrefix.Length + BackupTimestampLength ||
            !name.StartsWith(BackupPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var timestamp = name.Substring(BackupPrefix.Length);
        return DateTime.TryParseExact(
            timestamp,
            "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
    }

    private static bool SameDirectory(string left, string right) =>
        string.Equals(NormalizeDirectory(left), NormalizeDirectory(right), StringComparison.Ordinal);

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool TryNormalizeDirectory(string path, out string normalized)
    {
        try
        {
            normalized = NormalizeDirectory(path);
            return normalized.Length != 0;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static string DescribeFailure(Exception exception)
    {
        var message = exception.Message?.Trim();
        var root = exception.GetBaseException();
        var rootMessage = root.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message)) message = exception.GetType().Name;
        if (!ReferenceEquals(root, exception) &&
            !string.IsNullOrWhiteSpace(rootMessage) &&
            !string.Equals(message, rootMessage, StringComparison.Ordinal))
        {
            message += " " + rootMessage;
        }
        return message;
    }

    private readonly struct RetentionOutcome
    {
        internal RetentionOutcome(int prunedCount, IReadOnlyList<string> failures)
        {
            PrunedCount = prunedCount;
            Failures = failures;
        }

        internal int PrunedCount { get; }
        internal IReadOnlyList<string> Failures { get; }
    }
}
