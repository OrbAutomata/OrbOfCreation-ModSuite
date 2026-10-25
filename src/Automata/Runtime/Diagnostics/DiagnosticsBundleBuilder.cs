using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;

namespace OrbAutomata;

internal sealed class DiagnosticsBundleBuildRequest
{
    internal DiagnosticsBundleBuildRequest(
        DateTime utcNow,
        string outputDirectory,
        string configurationPath,
        string saveRoot,
        string logPath,
        string suiteVersion,
        string gameBuildIdentity,
        IReadOnlyList<FeatureStatusSnapshot> features,
        IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> health,
        AutomataDiagnosticsRuntimeEvidence runtimeEvidence,
        DiagnosticsTextRedactor redactor,
        long maximumBytes = DiagnosticsBundleBuilder.MaximumBundleBytes,
        string gameTickFaults = "")
    {
        UtcNow = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        OutputDirectory = Require(outputDirectory, nameof(outputDirectory));
        ConfigurationPath = configurationPath ?? string.Empty;
        SaveRoot = saveRoot ?? string.Empty;
        LogPath = logPath ?? string.Empty;
        SuiteVersion = string.IsNullOrWhiteSpace(suiteVersion) ? "unavailable" : suiteVersion;
        GameBuildIdentity = string.IsNullOrWhiteSpace(gameBuildIdentity) ? "unavailable" : gameBuildIdentity;
        Features = features ?? throw new ArgumentNullException(nameof(features));
        Health = health ?? throw new ArgumentNullException(nameof(health));
        RuntimeEvidence = runtimeEvidence ?? throw new ArgumentNullException(nameof(runtimeEvidence));
        Redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        if (maximumBytes <= 0 || maximumBytes > DiagnosticsBundleBuilder.MaximumBundleBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        MaximumBytes = maximumBytes;
        GameTickFaults = gameTickFaults ?? string.Empty;
    }

    internal DateTime UtcNow { get; }
    internal string OutputDirectory { get; }
    internal string ConfigurationPath { get; }
    internal string SaveRoot { get; }
    internal string LogPath { get; }
    internal string SuiteVersion { get; }
    internal string GameBuildIdentity { get; }
    internal IReadOnlyList<FeatureStatusSnapshot> Features { get; }
    internal IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> Health { get; }
    internal AutomataDiagnosticsRuntimeEvidence RuntimeEvidence { get; }
    internal DiagnosticsTextRedactor Redactor { get; }
    internal long MaximumBytes { get; }

    /// <summary>
    /// The stuck-game report an automatic bundle carries in place of the save files. Empty for a
    /// player-requested bundle, which includes the saves.
    /// </summary>
    internal string GameTickFaults { get; }

    internal bool IncludesSaves => GameTickFaults.Length == 0;

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A directory is required.", parameterName)
            : Path.GetFullPath(value);
}

internal readonly struct DiagnosticsBundleBuildResult
{
    internal DiagnosticsBundleBuildResult(
        string path,
        long bytesWritten,
        int journalSegments,
        TimeSpan journalCoverage)
    {
        Path = path;
        BytesWritten = bytesWritten;
        JournalSegments = journalSegments;
        JournalCoverage = journalCoverage;
    }

    internal string Path { get; }
    internal long BytesWritten { get; }
    internal int JournalSegments { get; }
    internal TimeSpan JournalCoverage { get; }
}

internal static class DiagnosticsBundleBuilder
{
    internal const long MaximumBundleBytes = 10L * 1024 * 1024;
    internal const int MaximumLogInputBytes = 2 * 1024 * 1024;
    internal static readonly TimeSpan MaximumJournalCoverage = TimeSpan.FromHours(4);

    internal const string GameTickFaultMemberName = "game-tick-faults.txt";
    internal const string PlayerBundlePrefix = "orb-modsuite-diagnostics-";
    internal const string AutomaticBundlePrefix = "orb-modsuite-automatic-report-";

    /// <summary>
    /// How many automatic bundles the diagnostics folder keeps; older automatic ones are deleted
    /// after a new one is written. Player-made bundles are never deleted.
    /// </summary>
    internal const int AutomaticBundlesKept = 5;
    internal const string AutomaticBundleSaveIdentity =
        "Save files are not included: this bundle was written automatically when the game stopped advancing.";

    private const long ManifestReserveBytes = 64 * 1024;
    private const long EndOfCentralDirectoryBytes = 22;
    private static readonly UTF8Encoding Utf8 = new(false);

    internal static DiagnosticsBundleBuildResult Build(DiagnosticsBundleBuildRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var notices = new List<string>();
        if (request.RuntimeEvidence.UnavailableReason.Length != 0)
            notices.Add(request.RuntimeEvidence.UnavailableReason);

        var configuration = CaptureConfiguration(request, notices);
        var saveIdentity = AutomaticBundleSaveIdentity;
        var saves = request.IncludesSaves
            ? CaptureSaves(request, notices, out saveIdentity)
            : new List<BundleMember>
            {
                BundleMember.Text(GameTickFaultMemberName, request.GameTickFaults, request.Redactor),
            };
        var log = CaptureLog(request, notices, out var logDroppedBytes);
        var mandatory = new List<BundleMember>(2 + saves.Count) { configuration };
        mandatory.AddRange(saves);
        mandatory.Add(log);

        var timestamp = ZipTimestamp(request.UtcNow);
        var mandatoryContribution = Measure(mandatory, timestamp);
        var manifestReserve = Math.Min(ManifestReserveBytes, Math.Max(4096, request.MaximumBytes / 8));
        if (checked(EndOfCentralDirectoryBytes + mandatoryContribution + manifestReserve) > request.MaximumBytes)
        {
            throw new IOException(
                "The configuration, identifiable save files, and log cannot fit inside the 10 MB sharing limit.");
        }

        var selected = new List<BundleMember>(mandatory);
        var selectedContribution = mandatoryContribution;
        var host = CaptureHostTrace(request, notices);
        if (host.Count != 0)
        {
            var hostContribution = Measure(host, timestamp);
            if (Fits(request.MaximumBytes, selectedContribution, hostContribution, manifestReserve))
            {
                selected.AddRange(host);
                selectedContribution = checked(selectedContribution + hostContribution);
            }
            else
            {
                notices.Add("The recent-event buffer was dropped because it would exceed the 10 MB sharing limit.");
            }
        }

        var journalInventory = CaptureJournal(request, notices);
        var chosenJournal = new List<JournalMember>();
        for (var index = 0; index < journalInventory.Count; index++)
        {
            var candidate = journalInventory[index];
            var contribution = Measure(candidate.Member, timestamp);
            if (!Fits(request.MaximumBytes, selectedContribution, contribution, manifestReserve))
            {
                notices.Add(
                    (journalInventory.Count - index).ToString(CultureInfo.InvariantCulture) +
                    " older journal segment(s) were dropped at the 10 MB sharing limit.");
                break;
            }
            selected.Add(candidate.Member);
            chosenJournal.Add(candidate);
            selectedContribution = checked(selectedContribution + contribution);
        }

        var coverage = JournalCoverage(chosenJournal, out var firstJournalTick, out var lastJournalTick);
        var manifestText = BuildManifest(
            request,
            selected,
            notices,
            saveIdentity,
            logDroppedBytes,
            chosenJournal.Count,
            journalInventory.Count,
            firstJournalTick,
            lastJournalTick,
            coverage,
            selectedContribution);
        var manifest = BundleMember.Text("manifest.txt", manifestText, request.Redactor);
        var manifestContribution = Measure(manifest, timestamp);
        var predicted = checked(
            EndOfCentralDirectoryBytes + selectedContribution + manifestContribution);
        if (predicted > request.MaximumBytes)
            throw new IOException("The diagnostics manifest cannot fit inside the 10 MB sharing limit.");

        Directory.CreateDirectory(request.OutputDirectory);
        var finalPath = ResolveOutputPath(
            request.OutputDirectory,
            request.UtcNow,
            request.IncludesSaves ? PlayerBundlePrefix : AutomaticBundlePrefix);
        var temporaryPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    Write(archive, manifest, timestamp);
                    for (var index = 0; index < selected.Count; index++)
                        Write(archive, selected[index], timestamp);
                }
                stream.Flush(flushToDisk: true);
            }

            var actualBytes = new FileInfo(temporaryPath).Length;
            if (actualBytes > request.MaximumBytes)
                throw new IOException("The completed diagnostics file exceeded the 10 MB sharing limit.");
            File.Move(temporaryPath, finalPath);
            if (!request.IncludesSaves) PruneAutomaticBundles(request.OutputDirectory, finalPath);
            return new DiagnosticsBundleBuildResult(
                finalPath,
                actualBytes,
                chosenJournal.Count,
                coverage);
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
            throw;
        }
    }

    private static BundleMember CaptureConfiguration(
        DiagnosticsBundleBuildRequest request,
        List<string> notices)
    {
        try
        {
            if (request.ConfigurationPath.Length == 0 || !File.Exists(request.ConfigurationPath))
                throw new FileNotFoundException("The suite configuration file could not be identified.");
            return BundleMember.Text(
                "config/orb-modsuite.cfg",
                ReadAllBytes(request.ConfigurationPath, FileShare.ReadWrite),
                request.Redactor);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            var reason = "The suite configuration was unavailable: " + Describe(exception);
            notices.Add(reason);
            return BundleMember.Text("config/orb-modsuite.cfg", "UNAVAILABLE: " + reason + "\n", request.Redactor);
        }
    }

    private static List<BundleMember> CaptureSaves(
        DiagnosticsBundleBuildRequest request,
        List<string> notices,
        out string identity)
    {
        var members = new List<BundleMember>();
        try
        {
            if (request.SaveRoot.Length == 0 || !Directory.Exists(request.SaveRoot))
                throw new DirectoryNotFoundException("Unity's save directory is unavailable.");
            var candidates = Directory.EnumerateFiles(request.SaveRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
                throw new FileNotFoundException("No top-level .sav file could be identified.");

            var unavailable = 0;
            for (var index = 0; index < candidates.Length; index++)
            {
                var path = candidates[index];
                try
                {
                    var memberName = "savegame/identifiable-save-" +
                        (index + 1).ToString("D3", CultureInfo.InvariantCulture) + ".sav";
                    members.Add(new BundleMember(
                        memberName,
                        ReadStableSave(path),
                        isText: false));
                    notices.Add(
                        "Identifiable save " + Path.GetFileName(path) +
                        " was copied as " + memberName + ".");
                }
                catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
                {
                    unavailable++;
                    notices.Add(
                        "Identifiable save " + Path.GetFileName(path) +
                        " could not be copied: " + Describe(exception));
                }
            }

            if (members.Count == 0)
                throw new IOException("None of the identifiable top-level .sav files could be copied.");

            identity = "The game did not expose an active slot identity; " +
                members.Count.ToString(CultureInfo.InvariantCulture) + " of " +
                candidates.Length.ToString(CultureInfo.InvariantCulture) +
                " identifiable top-level .sav file(s) were included without guessing" +
                (unavailable == 0 ? "." : "; unavailable candidates are listed below.");
            notices.Add(identity);
            return members;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            identity = "The active save could not be identified or copied: " + Describe(exception);
            notices.Add(identity);
            members.Add(BundleMember.Text(
                "savegame/UNAVAILABLE.txt",
                "UNAVAILABLE: " + identity + "\n",
                request.Redactor));
            return members;
        }
    }

    private static BundleMember CaptureLog(
        DiagnosticsBundleBuildRequest request,
        List<string> notices,
        out long droppedBytes)
    {
        droppedBytes = 0;
        try
        {
            if (request.LogPath.Length == 0 || !File.Exists(request.LogPath))
                throw new FileNotFoundException("The BepInEx log file could not be identified.");
            var bytes = ReadTail(request.LogPath, MaximumLogInputBytes, out droppedBytes);
            if (droppedBytes != 0)
            {
                notices.Add(
                    droppedBytes.ToString(CultureInfo.InvariantCulture) +
                    " older BepInEx log byte(s) were dropped before redaction to keep the bundle shareable.");
            }
            return BundleMember.Text("BepInEx/LogOutput.log", bytes, request.Redactor);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            var reason = "The BepInEx log was unavailable: " + Describe(exception);
            notices.Add(reason);
            return BundleMember.Text("BepInEx/LogOutput.log", "UNAVAILABLE: " + reason + "\n", request.Redactor);
        }
    }

    private static List<BundleMember> CaptureHostTrace(
        DiagnosticsBundleBuildRequest request,
        List<string> notices)
    {
        var snapshot = request.RuntimeEvidence.HostTrace;
        if (snapshot is null)
        {
            notices.Add("The recent-event buffer was unavailable.");
            return new List<BundleMember>();
        }
        if (snapshot.WrittenEvents == 0)
        {
            notices.Add("The recent-event buffer was empty.");
            return new List<BundleMember>();
        }
        if (snapshot.OverwrittenEvents != 0)
        {
            notices.Add(
                snapshot.OverwrittenEvents.ToString(CultureInfo.InvariantCulture) +
                " older recent events had already been overwritten before the bundle was requested.");
        }

        var result = new List<BundleMember>(snapshot.Members.Count);
        for (var index = 0; index < snapshot.Members.Count; index++)
        {
            var member = snapshot.Members[index];
            result.Add(member.IsText
                ? BundleMember.Text("recent-events/" + member.Name, member.Bytes, request.Redactor)
                : new BundleMember("recent-events/" + member.Name, member.Bytes, isText: false));
        }
        return result;
    }

    private static List<JournalMember> CaptureJournal(
        DiagnosticsBundleBuildRequest request,
        List<string> notices)
    {
        var status = request.RuntimeEvidence.Journal;
        if (status.State == DecisionJournalStatusState.Unavailable)
            notices.Add("The decision journal was unavailable.");
        else
        {
            if (status.PendingBlocks != 0)
                notices.Add("The decision journal still had " + status.PendingBlocks +
                    " pending block(s) after its flush wedge guard; only durable segments were included.");
            if (status.DiscardedRecords != 0)
                notices.Add(status.DiscardedRecords + " decision-journal record(s) had been discarded.");
            if (status.EvictedSegments != 0)
                notices.Add(status.EvictedSegments + " oldest decision-journal segment(s) had already been evicted.");
            if (status.IncompatibleSegmentsPruned != 0)
                notices.Add(status.IncompatibleSegmentsPruned +
                    " incompatible decision-journal segment(s) had been removed at startup.");
            if (status.State == DecisionJournalStatusState.Faulted)
                notices.Add("The decision journal had faulted: " + status.Result + ".");
        }

        var directory = request.RuntimeEvidence.JournalDirectory;
        if (directory.Length == 0 || !Directory.Exists(directory)) return new List<JournalMember>();
        var files = Directory.EnumerateFiles(directory, "journal-*.osjd", SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, Ordinal = ParseJournalOrdinal(Path.GetFileName(path)) })
            .Where(item => item.Ordinal >= 0)
            .OrderByDescending(item => item.Ordinal)
            .ToArray();
        var candidates = new List<JournalMember>();
        DecisionJournalRunId currentRun = default;
        var haveRun = false;
        long newestLastTick = 0;
        var olderRuns = 0;
        var outsideWindow = 0;
        for (var index = 0; index < files.Length; index++)
        {
            try
            {
                var bytes = ReadAllBytes(files[index].Path, FileShare.ReadWrite);
                var decoded = DecisionJournalSegmentCodec.Decode(bytes);
                TimestampRange(decoded, out var firstTick, out var lastTick);
                if (!haveRun)
                {
                    currentRun = decoded.Run;
                    newestLastTick = lastTick;
                    haveRun = true;
                }
                if (decoded.Run != currentRun)
                {
                    olderRuns++;
                    continue;
                }
                var cutoff = Math.Max(0, newestLastTick - MaximumJournalCoverage.Ticks);
                if (firstTick < cutoff)
                {
                    outsideWindow++;
                    continue;
                }
                candidates.Add(new JournalMember(
                    new BundleMember("journal/" + Path.GetFileName(files[index].Path), bytes, isText: false),
                    firstTick,
                    lastTick));
            }
            catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
            {
                notices.Add("Journal segment " + Path.GetFileName(files[index].Path) +
                    " was unavailable or invalid: " + Describe(exception));
            }
        }
        if (olderRuns != 0)
            notices.Add(olderRuns +
                " segment(s) from older game runs were not included because their wall-time continuity cannot be proven.");
        if (outsideWindow != 0)
            notices.Add(outsideWindow + " current-run journal segment(s) fell outside the four-hour limit.");
        if (candidates.Count == 0)
            notices.Add("No durable current-run decision-journal segment was available.");
        return candidates;
    }

    private static string BuildManifest(
        DiagnosticsBundleBuildRequest request,
        IReadOnlyList<BundleMember> selected,
        IReadOnlyList<string> notices,
        string saveIdentity,
        long logDroppedBytes,
        int journalIncluded,
        int journalAvailable,
        long firstJournalTick,
        long lastJournalTick,
        TimeSpan journalCoverage,
        long selectedContribution)
    {
        var text = new StringBuilder();
        text.AppendLine("Orb Of Creation ModSuite diagnostics bundle v1");
        text.Append("created-utc=").AppendLine(request.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        text.Append("suite-version=").AppendLine(request.SuiteVersion);
        text.Append("game-build=").AppendLine(request.GameBuildIdentity);
        text.Append("archive-limit-bytes=").AppendLine(request.MaximumBytes.ToString(CultureInfo.InvariantCulture));
        text.Append("selected-zip-contribution-before-manifest-bytes=")
            .AppendLine(selectedContribution.ToString(CultureInfo.InvariantCulture));
        text.Append("enabled-features=")
            .AppendLine(request.Features.Count(feature => feature.ConfiguredEnabled)
                .ToString(CultureInfo.InvariantCulture));
        text.Append("save-identification=").AppendLine(saveIdentity);
        text.Append("log-older-bytes-dropped=").AppendLine(logDroppedBytes.ToString(CultureInfo.InvariantCulture));
        text.Append("recent-events=")
            .Append(request.RuntimeEvidence.HostTrace?.WrittenEvents ?? 0)
            .Append(" held, ")
            .Append(request.RuntimeEvidence.HostTrace?.OverwrittenEvents ?? 0)
            .AppendLine(" older overwritten");
        text.Append("journal-state=").AppendLine(request.RuntimeEvidence.Journal.State.ToString());
        text.Append("journal-segments=").Append(journalIncluded).Append(" included newest-first of ")
            .Append(journalAvailable).AppendLine(" eligible");
        text.Append("journal-window-first-monotonic-tick=").AppendLine(firstJournalTick.ToString(CultureInfo.InvariantCulture));
        text.Append("journal-window-last-monotonic-tick=").AppendLine(lastJournalTick.ToString(CultureInfo.InvariantCulture));
        text.Append("journal-window-covered=").AppendLine(FormatDuration(journalCoverage));
        text.AppendLine();
        text.AppendLine("FEATURE HEALTH");
        for (var index = 0; index < request.Features.Count; index++)
        {
            var feature = request.Features[index];
            text.Append("- ").Append(feature.Key).Append(" | ").Append(feature.DisplayName)
                .Append(" | configured=").Append(feature.ConfiguredEnabled ? "on" : "off")
                .Append(" | state=").Append(feature.State)
                .Append(" | code=").Append(feature.Reason.Code)
                .Append(" | generation=").Append(feature.LifecycleGeneration);
            if (!feature.Reason.IsEmpty) text.Append(" | reason=").Append(feature.Reason.Summary);
            if (!string.IsNullOrWhiteSpace(feature.Detail)) text.Append(" | detail=").Append(feature.Detail);
            text.AppendLine();
        }
        text.AppendLine();
        text.AppendLine("RUNTIME HEALTH");
        for (var index = 0; index < request.Health.Count; index++)
        {
            var health = request.Health[index];
            text.Append("- ").Append(health.Key).Append(" | ").Append(health.DisplayName)
                .Append(" | implementation=").Append(health.Implementation)
                .Append(" | generation=").Append(health.LifecycleGeneration).AppendLine();
            for (var capabilityIndex = 0; capabilityIndex < health.Capabilities.Count; capabilityIndex++)
            {
                var capability = health.Capabilities[capabilityIndex];
                text.Append("  - ").Append(capability.CapabilityId)
                    .Append(" | configured=").Append(capability.ConfiguredEnabled ? "on" : "off")
                    .Append(" | state=").Append(capability.State)
                    .Append(" | code=").Append(capability.Reason.Code);
                if (!capability.Reason.IsEmpty) text.Append(" | reason=").Append(capability.Reason.Summary);
                text.AppendLine();
            }
        }
        text.AppendLine();
        text.AppendLine("BUNDLE MEMBERS");
        text.AppendLine("- manifest.txt | text | redacted");
        for (var index = 0; index < selected.Count; index++)
        {
            var member = selected[index];
            text.Append("- ").Append(member.Name).Append(" | ")
                .Append(member.IsText ? "text | redacted" : "binary")
                .Append(" | input-bytes=").Append(member.Bytes.Length).AppendLine();
        }
        text.AppendLine();
        text.AppendLine("DROPPED OR UNAVAILABLE");
        if (notices.Count == 0) text.AppendLine("- none");
        else
            for (var index = 0; index < notices.Count; index++)
                text.Append("- ").AppendLine(notices[index]);
        return text.ToString();
    }

    private static long Measure(IReadOnlyList<BundleMember> members, DateTimeOffset timestamp)
    {
        var total = 0L;
        for (var index = 0; index < members.Count; index++)
            total = checked(total + Measure(members[index], timestamp));
        return total;
    }

    private static long Measure(BundleMember member, DateTimeOffset timestamp)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            Write(archive, member, timestamp);
        if (stream.Length < EndOfCentralDirectoryBytes)
            throw new InvalidOperationException("The zip entry measurement was incomplete.");
        return stream.Length - EndOfCentralDirectoryBytes;
    }

    private static bool Fits(long maximum, long selected, long candidate, long manifestReserve) =>
        checked(EndOfCentralDirectoryBytes + selected + candidate + manifestReserve) <= maximum;

    private static void Write(ZipArchive archive, BundleMember member, DateTimeOffset timestamp)
    {
        var entry = archive.CreateEntry(member.Name, CompressionLevel.Optimal);
        entry.LastWriteTime = timestamp;
        using var destination = entry.Open();
        destination.Write(member.Bytes, 0, member.Bytes.Length);
    }

    private static byte[] ReadStableSave(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > int.MaxValue) throw new IOException("An identifiable save file is too large to capture.");
        var length = checked((int)stream.Length);
        var first = ReadExactly(stream, length);
        if (stream.ReadByte() != -1 || stream.Length != length)
            throw new IOException("An identifiable save file changed while it was being read.");
        stream.Position = 0;
        var second = ReadExactly(stream, length);
        if (stream.ReadByte() != -1 || stream.Length != length || !first.SequenceEqual(second))
            throw new IOException("An identifiable save file changed while it was being read.");
        return first;
    }

    private static byte[] ReadAllBytes(string path, FileShare share)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, share);
        if (stream.Length > int.MaxValue) throw new IOException("A diagnostics input is too large to capture.");
        var bytes = ReadExactly(stream, checked((int)stream.Length));
        if (stream.ReadByte() != -1) throw new IOException("A diagnostics input changed while it was being read.");
        return bytes;
    }

    private static byte[] ReadTail(string path, int maximumBytes, out long droppedBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        var start = Math.Max(0, length - maximumBytes);
        droppedBytes = start;
        stream.Position = start;
        var bytes = ReadExactly(stream, checked((int)(length - start)));
        if (start == 0) return bytes;
        var firstLineEnd = Array.IndexOf(bytes, (byte)'\n');
        if (firstLineEnd < 0)
        {
            droppedBytes = length;
            return Array.Empty<byte>();
        }
        droppedBytes = checked(droppedBytes + firstLineEnd + 1);
        var tail = new byte[bytes.Length - firstLineEnd - 1];
        Array.Copy(bytes, firstLineEnd + 1, tail, 0, tail.Length);
        return tail;
    }

    private static byte[] ReadExactly(Stream stream, int length)
    {
        var result = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(result, offset, length - offset);
            if (read == 0) throw new EndOfStreamException("A diagnostics input ended before its declared length.");
            offset += read;
        }
        return result;
    }

    private static void TimestampRange(
        DecisionJournalSegmentDocument segment,
        out long first,
        out long last)
    {
        first = long.MaxValue;
        last = 0;
        for (var index = 0; index < segment.Records.Length; index++)
        {
            first = Math.Min(first, segment.Records[index].FirstTimestampTicks);
            last = Math.Max(last, segment.Records[index].LastTimestampTicks);
        }
    }

    private static TimeSpan JournalCoverage(
        IReadOnlyList<JournalMember> members,
        out long first,
        out long last)
    {
        if (members.Count == 0)
        {
            first = 0;
            last = 0;
            return TimeSpan.Zero;
        }
        first = members.Min(member => member.FirstTick);
        last = members.Max(member => member.LastTick);
        return TimeSpan.FromTicks(checked(last - first));
    }

    private static int ParseJournalOrdinal(string name)
    {
        const string prefix = "journal-";
        const string extension = ".osjd";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
            !name.EndsWith(extension, StringComparison.Ordinal))
            return -1;
        var value = name.Substring(prefix.Length, name.Length - prefix.Length - extension.Length);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) && ordinal >= 0
            ? ordinal
            : -1;
    }

    /// <remarks>
    /// A bundle that cannot be deleted stays; the new report was written either way.
    /// </remarks>
    private static void PruneAutomaticBundles(string directory, string written)
    {
        var older = Directory.GetFiles(directory, AutomaticBundlePrefix + "*.zip")
            .Where(path => !string.Equals(path, written, StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(path => path, StringComparer.Ordinal)
            .Skip(AutomaticBundlesKept - 1);
        foreach (var path in older)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string ResolveOutputPath(string directory, DateTime utcNow, string prefix)
    {
        var stem = prefix +
            utcNow.ToString("yyyyMMdd-HHmmss'Z'", CultureInfo.InvariantCulture);
        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var name = suffix == 0
                ? stem + ".zip"
                : stem + "-" + suffix.ToString("D2", CultureInfo.InvariantCulture) + ".zip";
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) return path;
        }
        throw new IOException("A unique diagnostics filename could not be allocated for this timestamp.");
    }

    private static DateTimeOffset ZipTimestamp(DateTime utcNow)
    {
        var value = utcNow < new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            ? new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : utcNow;
        return new DateTimeOffset(value);
    }

    private static string FormatDuration(TimeSpan value) => string.Format(
        CultureInfo.InvariantCulture,
        "{0:00}:{1:00}:{2:00}",
        (long)value.TotalHours,
        value.Minutes,
        value.Seconds);

    private static string Describe(Exception exception)
    {
        var message = exception.GetBaseException().Message?.Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    private sealed class BundleMember
    {
        internal BundleMember(string name, byte[] bytes, bool isText)
        {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("/", StringComparison.Ordinal) ||
                name.Contains("..", StringComparison.Ordinal) || name.Contains('\\'))
                throw new ArgumentException("A safe relative zip member name is required.", nameof(name));
            Name = name;
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            IsText = isText;
        }

        internal string Name { get; }
        internal byte[] Bytes { get; }
        internal bool IsText { get; }

        internal static BundleMember Text(
            string name,
            string text,
            DiagnosticsTextRedactor redactor) => new(
            name,
            Utf8.GetBytes(redactor.Redact(text)),
            isText: true);

        internal static BundleMember Text(
            string name,
            byte[] bytes,
            DiagnosticsTextRedactor redactor) => new(
            name,
            redactor.Redact(bytes),
            isText: true);
    }

    private readonly struct JournalMember
    {
        internal JournalMember(BundleMember member, long firstTick, long lastTick)
        {
            Member = member;
            FirstTick = firstTick;
            LastTick = lastTick;
        }

        internal BundleMember Member { get; }
        internal long FirstTick { get; }
        internal long LastTick { get; }
    }
}
