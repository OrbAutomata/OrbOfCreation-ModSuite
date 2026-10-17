using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using OrbModding.Common;
using OrbModConfig;
using Xunit;

namespace OrbModding.Tests;

public sealed class SuiteConfigurationSchemaFiveTests
{
    private static readonly (string Section, string Key, string Value)[] Retired =
    {
        ("AutoBuy", "MaxCandidatesPerScan", "1024"),
        ("Diagnostics", "MaxLoggedRejections", "12"),
        ("Diagnostics", "EnableOperationalLogging", "true"),
        ("Diagnostics", "DecisionLogLevel", "Verbose"),
        ("Diagnostics", "DetailedLogging", "true"),
        ("Development", "EventProbe", "true"),
        ("Diagnostics", "VerifyGameMathShortcut", "J + LeftShift"),
    };

    [Fact]
    public void SchemaFiveDiscardsEveryRetiredKeyAndPreservesUnrelatedPlayerValues()
    {
        var file = VersionFourFile();
        file.SeedSerialized("AutoBuy", "LeaveQueueSlots", "7");
        file.SeedSerialized("ThirdParty", "Opaque", "preserve-me");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(4, result.Status.FromVersion);
        Assert.Equal(7, result.Status.ToVersion);
        Assert.Equal(7, result.Diagnostics.Count);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal(
                ConfigurationMigrationDiagnosticKind.DiscardedObsolete,
                diagnostic.Kind));
        foreach (var retired in Retired)
            Assert.False(file.TryGetPersisted(retired.Section, retired.Key, out _));
        Assert.Equal(7, result.Config!.Automata.LeaveQueueSlots.Value);
        Assert.True(file.TryGetPersisted("ThirdParty", "Opaque", out var opaque));
        Assert.Equal("preserve-me", opaque);
    }

    [Fact]
    public void SchemaFiveDiagnosticsNameEveryDiscardedKeyAndReason()
    {
        var result = SuiteConfiguration.TryBind(VersionFourFile());

        Assert.True(result.Success, result.Status.Reason);
        foreach (var retired in Retired)
        {
            Assert.Contains(
                result.Diagnostics,
                diagnostic =>
                    diagnostic.Source.Section == retired.Section &&
                    diagnostic.Source.Key == retired.Key &&
                    !string.IsNullOrWhiteSpace(diagnostic.Detail));
        }
    }

    [Fact]
    public void SchemaFiveSaveFailureRestoresExactBytesAndExposesNoPartialConfiguration()
    {
        const string path = "config/schema-five-save-failure.cfg";
        var original = Encoding.UTF8.GetBytes("exact original schema-four bytes");
        var files = new FakeFileOperations((path, original));
        var file = VersionFourFile(path);
        file.ThrowOnSaveCall = 1;

        var result = SuiteConfiguration.TryBind(file, files);

        Assert.False(result.Success);
        Assert.Equal(ConfigurationSchemaState.Failed, result.Status.State);
        Assert.Null(result.Config);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.Equal(
            original,
            files.ReadAllBytes(ConfigurationSchemaTransaction.GetBackupPath(path, 7)));
        Assert.Empty(file);
    }

    [Fact]
    public void SchemaFiveRebindIsIdempotent()
    {
        var file = VersionFourFile();
        var first = SuiteConfiguration.TryBind(file);
        var savesAfterMigration = file.SaveCalls;

        var second = SuiteConfiguration.TryBind(file);

        Assert.True(first.Success, first.Status.Reason);
        Assert.True(second.Success, second.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Current, second.Status.State);
        Assert.Empty(second.Diagnostics);
        Assert.Equal(savesAfterMigration, file.SaveCalls);
        foreach (var retired in Retired)
            Assert.False(file.TryGetPersisted(retired.Section, retired.Key, out _));
    }

    [Fact]
    public void CurrentCatalogContainsNoRetiredRows()
    {
        var file = new ConfigFile();
        var result = SuiteConfiguration.TryBind(file);
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("suite", "Orb Of Creation ModSuite", "test", file),
        });
        var settings = catalog.Mods.Single().Sections.SelectMany(section => section.Settings).ToArray();

        Assert.True(result.Success, result.Status.Reason);
        foreach (var retired in Retired)
        {
            Assert.DoesNotContain(
                settings,
                setting =>
                    setting.SourceSection == retired.Section &&
                    setting.Key == retired.Key);
        }
    }

    private static ConfigFile VersionFourFile(string path = "")
    {
        var file = new ConfigFile(path);
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "4");
        foreach (var retired in Retired)
            file.SeedSerialized(retired.Section, retired.Key, retired.Value);
        return file;
    }

    private sealed class FakeFileOperations : IConfigurationFileOperations
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public FakeFileOperations(params (string Path, byte[] Contents)[] files)
        {
            foreach (var file in files) _files[file.Path] = file.Contents.ToArray();
        }

        public bool Exists(string path) => _files.ContainsKey(path);

        public byte[] ReadAllBytes(string path) => _files[path].ToArray();

        public void WriteAllBytes(string path, byte[] contents) =>
            _files[path] = contents.ToArray();

        public void Delete(string path) => _files.Remove(path);

        public ConfigurationBackupCreationResult CreateNewBackup(string path, byte[] contents)
        {
            if (_files.ContainsKey(path)) return ConfigurationBackupCreationResult.Collision;
            _files[path] = contents.ToArray();
            return ConfigurationBackupCreationResult.Created;
        }
    }
}
