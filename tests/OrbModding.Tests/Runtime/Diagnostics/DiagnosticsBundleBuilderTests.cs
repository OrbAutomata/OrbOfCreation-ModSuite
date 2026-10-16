using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using Xunit;

namespace OrbModding.Tests.Runtime.Diagnostics;

public sealed class DiagnosticsBundleBuilderTests : IDisposable
{
    private static readonly DateTime CreatedUtc = new(2026, 8, 3, 12, 34, 56, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "orb-diagnostics-bundle-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EveryTextMemberIsRedactedAndTheBundleCapturesOnlyPastEvidence()
    {
        var paths = FixturePaths();
        File.WriteAllText(paths.Config, "Owner=Alice\nPath=/Users/Alice/game/config.cfg\n");
        File.WriteAllText(
            paths.Log,
            "Alice opened C:\\Users\\Alice\\Orb\\LogOutput.log and \\\\server\\Users\\Alice\\trace.log\n");
        File.WriteAllBytes(Path.Combine(paths.Save, "Alice-slot.sav"), Encoding.ASCII.GetBytes("opaque-save"));
        var run = new DecisionJournalRunId(17);
        WriteJournal(paths.Journal, run, ordinal: 0, TimeSpan.FromMinutes(1).Ticks, records: 1);
        WriteJournal(paths.Journal, run, ordinal: 1, TimeSpan.FromMinutes(2).Ticks, records: 1);
        WriteJournal(paths.Journal, run, ordinal: 2, TimeSpan.FromMinutes(3).Ticks, records: 1);

        var feature = new FeatureStatusSnapshot(
            new FeatureStatusKey("orbmodding.suite", "fixture"),
            "Fixture",
            configuredEnabled: true,
            FeatureStatusState.TemporarilyBlocked,
            new FeatureStatusReason(
                FeatureStatusReasonCode.TemporarySafetyBlock,
                "Alice is blocked at /Users/Alice/game"),
            lifecycleGeneration: 4);
        var result = DiagnosticsBundleBuilder.Build(Request(
            paths,
            run,
            journalSegments: 3,
            features: new[] { feature },
            redactor: new DiagnosticsTextRedactor(
                new[] { "/Users/Alice", "C:\\Users\\Alice" },
                new[] { "Alice" })));

        Assert.True(result.BytesWritten <= DiagnosticsBundleBuilder.MaximumBundleBytes);
        using var archive = ZipFile.OpenRead(result.Path);
        Assert.Equal("manifest.txt", archive.Entries[0].FullName);
        Assert.Contains(archive.Entries, entry => entry.FullName == "config/orb-modsuite.cfg");
        Assert.Contains(archive.Entries, entry => entry.FullName == "savegame/identifiable-save-001.sav");
        Assert.Contains(archive.Entries, entry => entry.FullName == "BepInEx/LogOutput.log");
        Assert.Equal(
            new[] { "journal/journal-000002.osjd", "journal/journal-000001.osjd", "journal/journal-000000.osjd" },
            archive.Entries.Where(entry => entry.FullName.StartsWith("journal/", StringComparison.Ordinal))
                .Select(entry => entry.FullName));
        foreach (var entry in archive.Entries)
        {
            Assert.DoesNotContain("Alice", entry.FullName, StringComparison.OrdinalIgnoreCase);
            var bytes = Read(entry);
            var searchable = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain("Alice", searchable, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/Users/Alice", searchable, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C:\\Users\\Alice", searchable, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\\\\server\\Users\\Alice", searchable, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal("opaque-save", Encoding.ASCII.GetString(Read(
            archive.Entries.Single(entry => entry.FullName == "savegame/identifiable-save-001.sav"))));
    }

    [Fact]
    public void JournalCoverageNeverExceedsFourHoursAndOlderRunsAreNotGuessedIntoTheWindow()
    {
        var paths = FixturePaths();
        WriteMinimumFixedInputs(paths);
        var oldRun = new DecisionJournalRunId(11);
        var currentRun = new DecisionJournalRunId(12);
        WriteJournal(paths.Journal, oldRun, ordinal: 0, TimeSpan.FromHours(8).Ticks, records: 1);
        for (var hour = 0; hour <= 5; hour++)
            WriteJournal(paths.Journal, currentRun, ordinal: hour + 1, TimeSpan.FromHours(hour).Ticks, records: 1);

        var result = DiagnosticsBundleBuilder.Build(Request(paths, currentRun, journalSegments: 7));

        Assert.Equal(TimeSpan.FromHours(4), result.JournalCoverage);
        using var archive = ZipFile.OpenRead(result.Path);
        var journals = archive.Entries
            .Where(entry => entry.FullName.StartsWith("journal/", StringComparison.Ordinal))
            .Select(entry => entry.FullName)
            .ToArray();
        Assert.Equal(5, journals.Length);
        Assert.Equal("journal/journal-000006.osjd", journals[0]);
        Assert.Equal("journal/journal-000002.osjd", journals[^1]);
        var manifest = Encoding.UTF8.GetString(Read(archive.Entries[0]));
        Assert.Contains("journal-window-covered=04:00:00", manifest, StringComparison.Ordinal);
        Assert.Contains("older game runs were not included", manifest, StringComparison.Ordinal);
        Assert.Contains("fell outside the four-hour limit", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableSaveCandidateCannotSuppressAnIdentifiableReadableSave()
    {
        var paths = FixturePaths();
        File.WriteAllText(paths.Config, "Enabled=true\n");
        File.WriteAllText(paths.Log, "ready\n");
        File.WriteAllBytes(Path.Combine(paths.Save, "readable.sav"), new byte[] { 1, 2, 3, 4 });
        var lockedPath = Path.Combine(paths.Save, "locked.sav");
        File.WriteAllBytes(lockedPath, new byte[] { 5, 6, 7, 8 });
        using var locked = new FileStream(
            lockedPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var result = DiagnosticsBundleBuilder.Build(Request(
            paths,
            new DecisionJournalRunId(1),
            journalSegments: 0));

        using var archive = ZipFile.OpenRead(result.Path);
        Assert.Contains(archive.Entries, entry => entry.FullName == "savegame/identifiable-save-002.sav");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "savegame/identifiable-save-001.sav");
        var manifest = Encoding.UTF8.GetString(Read(archive.Entries[0]));
        Assert.Contains("1 of 2 identifiable", manifest, StringComparison.Ordinal);
        Assert.Contains("Identifiable save locked.sav could not be copied", manifest, StringComparison.Ordinal);
        Assert.Contains(
            "Identifiable save readable.sav was copied as savegame/identifiable-save-002.sav",
            manifest,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NewestJournalSegmentsStopBeforeTheMeasuredArchiveBudget()
    {
        var paths = FixturePaths();
        WriteMinimumFixedInputs(paths);
        var run = new DecisionJournalRunId(23);
        const int available = 200;
        for (var ordinal = 0; ordinal < available; ordinal++)
            WriteJournal(
                paths.Journal,
                run,
                ordinal,
                TimeSpan.FromMinutes(ordinal).Ticks,
                DecisionJournalSegmentCodec.MaximumRecords);

        const long cap = 96 * 1024;
        var result = DiagnosticsBundleBuilder.Build(Request(
            paths,
            run,
            available,
            maximumBytes: cap));

        Assert.True(result.BytesWritten <= cap);
        Assert.InRange(result.JournalSegments, 1, available - 1);
        using var archive = ZipFile.OpenRead(result.Path);
        var journals = archive.Entries
            .Where(entry => entry.FullName.StartsWith("journal/", StringComparison.Ordinal))
            .Select(entry => entry.FullName)
            .ToArray();
        Assert.Equal("journal/journal-000199.osjd", journals[0]);
        var manifest = Encoding.UTF8.GetString(Read(archive.Entries[0]));
        Assert.Contains("older journal segment(s) were dropped", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void JournalRecordsHaveNoReferenceTypedPayloadForTextToRideAlong()
    {
        AssertValueOnly(typeof(DecisionJournalRecord), new HashSet<Type>());
    }

    [Fact]
    public void AnAutomaticBundleKeepsTheNewestFewAutomaticOnesAndNeverTouchesAPlayersBundle()
    {
        var paths = FixturePaths();
        WriteMinimumFixedInputs(paths);
        Directory.CreateDirectory(paths.Output);
        var player = Path.Combine(paths.Output, DiagnosticsBundleBuilder.PlayerBundlePrefix + "20200101-000000Z.zip");
        File.WriteAllText(player, "player");
        File.SetLastWriteTimeUtc(player, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var older = new List<string>();
        for (var index = 0; index < DiagnosticsBundleBuilder.AutomaticBundlesKept + 2; index++)
        {
            var path = Path.Combine(
                paths.Output,
                DiagnosticsBundleBuilder.AutomaticBundlePrefix + "2026080" + index + "-000000Z.zip");
            File.WriteAllText(path, "automatic");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 8, 1 + index, 0, 0, 0, DateTimeKind.Utc));
            older.Add(path);
        }

        var result = DiagnosticsBundleBuilder.Build(Request(
            paths, new DecisionJournalRunId(1), journalSegments: 0, gameTickFaults: "fault"));

        Assert.StartsWith(DiagnosticsBundleBuilder.AutomaticBundlePrefix, Path.GetFileName(result.Path));
        Assert.True(File.Exists(player));
        var kept = Directory.GetFiles(paths.Output, DiagnosticsBundleBuilder.AutomaticBundlePrefix + "*.zip");
        Assert.Equal(DiagnosticsBundleBuilder.AutomaticBundlesKept, kept.Length);
        Assert.Contains(result.Path, kept);
        Assert.Equal(
            older.Skip(older.Count - (DiagnosticsBundleBuilder.AutomaticBundlesKept - 1)).OrderBy(path => path, StringComparer.Ordinal),
            kept.Where(path => path != result.Path).OrderBy(path => path, StringComparer.Ordinal));
    }

    [Fact]
    public void APlayerBundleNeverPrunesAutomaticOnes()
    {
        var paths = FixturePaths();
        WriteMinimumFixedInputs(paths);
        Directory.CreateDirectory(paths.Output);
        for (var index = 0; index < DiagnosticsBundleBuilder.AutomaticBundlesKept + 2; index++)
        {
            File.WriteAllText(
                Path.Combine(paths.Output, DiagnosticsBundleBuilder.AutomaticBundlePrefix + index + ".zip"),
                "automatic");
        }

        var result = DiagnosticsBundleBuilder.Build(Request(paths, new DecisionJournalRunId(1), journalSegments: 0));

        Assert.StartsWith(DiagnosticsBundleBuilder.PlayerBundlePrefix, Path.GetFileName(result.Path));
        Assert.Equal(
            DiagnosticsBundleBuilder.AutomaticBundlesKept + 2,
            Directory.GetFiles(paths.Output, DiagnosticsBundleBuilder.AutomaticBundlePrefix + "*.zip").Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private Fixture FixturePaths()
    {
        var fixture = new Fixture(
            Path.Combine(_root, "out"),
            Path.Combine(_root, "suite.cfg"),
            Path.Combine(_root, "save"),
            Path.Combine(_root, "LogOutput.log"),
            Path.Combine(_root, "journal"));
        Directory.CreateDirectory(fixture.Save);
        Directory.CreateDirectory(fixture.Journal);
        return fixture;
    }

    private static void WriteMinimumFixedInputs(Fixture paths)
    {
        File.WriteAllText(paths.Config, "Enabled=true\n");
        File.WriteAllText(paths.Log, "ready\n");
        File.WriteAllBytes(Path.Combine(paths.Save, "ooc_save_0.sav"), new byte[] { 1, 2, 3, 4 });
    }

    private static DiagnosticsBundleBuildRequest Request(
        Fixture paths,
        DecisionJournalRunId run,
        int journalSegments,
        IReadOnlyList<FeatureStatusSnapshot>? features = null,
        DiagnosticsTextRedactor? redactor = null,
        long maximumBytes = DiagnosticsBundleBuilder.MaximumBundleBytes,
        string gameTickFaults = "")
    {
        var status = new DecisionJournalStatus(
            DecisionJournalStatusState.Recording,
            acceptedRecords: journalSegments,
            writtenRecords: journalSegments,
            discardedRecords: 0,
            bytesWritten: journalSegments,
            writtenSegments: journalSegments,
            retainedSegments: journalSegments,
            evictedSegments: 0,
            startupPrunedSegments: 0,
            incompatibleSegmentsPruned: 0,
            staleTemporaryFilesRemoved: 0,
            pendingBlocks: 0,
            peakPendingBlocks: 0,
            firstIncompleteSequence: 0,
            DecisionJournalStatusResult.None,
            "journal");
        return new DiagnosticsBundleBuildRequest(
            CreatedUtc,
            paths.Output,
            paths.Config,
            paths.Save,
            paths.Log,
            "1.2.3",
            "audited fixture",
            features ?? Array.Empty<FeatureStatusSnapshot>(),
            Array.Empty<RuntimeServiceDiagnosticsSnapshot>(),
            new AutomataDiagnosticsRuntimeEvidence(null, status, paths.Journal, string.Empty),
            redactor ?? new DiagnosticsTextRedactor(),
            maximumBytes,
            gameTickFaults);
    }

    private static void WriteJournal(
        string directory,
        DecisionJournalRunId run,
        int ordinal,
        long firstTick,
        int records)
    {
        var values = new DecisionJournalRecord[records];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = DecisionJournalRecord.Transition(
                DecisionJournalRecordKind.ConfigurationChanged,
                default,
                checked((ulong)ordinal * (ulong)records + (ulong)index + 1),
                new MonotonicTimestamp(checked(firstTick + index)));
        }
        var bytes = new byte[DecisionJournalSegmentCodec.GetEncodedLength(records)];
        DecisionJournalSegmentCodec.Encode(
            run,
            checked((ulong)ordinal),
            checked((ulong)ordinal * (ulong)records + 1),
            values,
            bytes);
        File.WriteAllBytes(
            Path.Combine(directory, "journal-" + ordinal.ToString("D6") + ".osjd"),
            bytes);
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var destination = new MemoryStream();
        source.CopyTo(destination);
        return destination.ToArray();
    }

    private static void AssertValueOnly(Type type, ISet<Type> visited)
    {
        Assert.True(type.IsValueType, type.FullName + " is reference-typed.");
        if (!visited.Add(type) || type.IsPrimitive || type.IsEnum || type.IsPointer) return;
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotEmpty(fields);
        foreach (var field in fields)
        {
            Assert.True(
                field.FieldType.IsValueType,
                type.FullName + "." + field.Name + " carries reference-typed data: " +
                field.FieldType.FullName);
            AssertValueOnly(field.FieldType, visited);
        }
    }

    private readonly record struct Fixture(
        string Output,
        string Config,
        string Save,
        string Log,
        string Journal);
}
