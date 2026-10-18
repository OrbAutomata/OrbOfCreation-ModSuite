using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class ConfigurationSchemaTests
{
    private const string PluginId = "test.schema.plugin";
    private static readonly ConfigurationKey Legacy = new("Legacy", "Value");
    private static readonly ConfigurationKey Current = new("General", "Value");

    [Fact]
    public void FreshMarkerOnlyMigrationCreatesBackupBindsMarkerLastAndSavesOnce()
    {
        var path = "config/test.cfg";
        var original = Encoding.UTF8.GetBytes("unknown original bytes");
        var files = new FakeFileOperations((path, original));
        var config = new ConfigFile(path);
        config.SeedSerialized("ThirdParty", "Orphan", "preserve-me");
        var registry = new ConfigurationSchemaStatusRegistry();

        var result = ConfigurationSchemaTransaction.Bind<TestConfig>(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            files,
            registry);

        Assert.True(result.Success);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.True(result.Status.BackupCreated);
        Assert.True(result.Status.Saved);
        Assert.True(result.Status.Loaded);
        Assert.Equal(1, config.SaveCalls);
        Assert.Equal(original, files.ReadAllBytes(ConfigurationSchemaTransaction.GetBackupPath(path, 1)));
        Assert.True(config.TryGetPersisted("ThirdParty", "Orphan", out var orphan));
        Assert.Equal("preserve-me", orphan);
        var marker = Assert.Single(config, pair =>
            pair.Key.Section == ConfigurationSchemaTransaction.MarkerSection &&
            pair.Key.Key == ConfigurationSchemaTransaction.MarkerKey);
        Assert.Equal(1, Assert.IsType<ConfigEntry<int>>(marker.Value).Value);
        Assert.Contains(marker.Value.Description.Tags, tag => tag is ModConfigMetadata { Hidden: true });
    }

    [Fact]
    public void OrderedMigrationReadsOnlyKnownKeysAndAppliesTypedDestination()
    {
        var config = new ConfigFile();
        config.SeedSerialized(Legacy.Section, Legacy.Key, "41");
        config.SeedSerialized("Unknown", "LeaveAlone", "opaque");
        var plan = new ConfigurationSchemaPlan(1, new[]
        {
            new ConfigurationMigrationStep(0, 1, new[] { Legacy }, context =>
            {
                Assert.True(context.TryGet(Legacy, out var value));
                context.Map(Legacy, Current, (int.Parse(value) + 1).ToString(), "mapped known value");
            }),
        });

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            plan,
            BindCurrent,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(42, result.Config!.Value.Value);
        Assert.Single(result.Diagnostics);
        Assert.False(config.TryGetPersisted(Legacy.Section, Legacy.Key, out _));
        Assert.True(config.TryGetPersisted("Unknown", "LeaveAlone", out var unknown));
        Assert.Equal("opaque", unknown);
    }

    [Fact]
    public void InitiallyBoundKnownKeyIsConsumedBeforeCurrentTypedBinding()
    {
        var config = new ConfigFile();
        config.Bind(Legacy.Section, Legacy.Key, "41", "Previously bound legacy value.");

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MappingPlan(),
            BindCurrent,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(41, result.Config!.Value.Value);
        Assert.DoesNotContain(config, pair => pair.Key.Equals(Legacy.ToDefinition()));
        Assert.False(config.TryGetPersisted(Legacy.Section, Legacy.Key, out _));
    }

    [Fact]
    public void CurrentSchemaIsIdempotentWithoutStepBackupOrSave()
    {
        var path = "config/current.cfg";
        var files = new FakeFileOperations((path, Encoding.UTF8.GetBytes("current")));
        var config = new ConfigFile(path);
        config.SeedSerialized(ConfigurationSchemaTransaction.MarkerSection, ConfigurationSchemaTransaction.MarkerKey, "1");
        config.SeedSerialized(Current.Section, Current.Key, "7");
        var executions = 0;
        var plan = new ConfigurationSchemaPlan(1, new[]
        {
            new ConfigurationMigrationStep(0, 1, Array.Empty<ConfigurationKey>(), _ => executions++),
        });

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            plan,
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(ConfigurationSchemaState.Current, result.Status.State);
        Assert.Equal(7, result.Config!.Value.Value);
        Assert.Equal(0, executions);
        Assert.Equal(0, config.SaveCalls);
        Assert.Empty(files.BackupPaths);

        var repeated = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            plan,
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());
        Assert.True(repeated.Success);
        Assert.Equal(ConfigurationSchemaState.Current, repeated.Status.State);
        Assert.Equal(0, config.SaveCalls);
        Assert.Empty(files.BackupPaths);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("-1")]
    public void MalformedOrNegativeMarkerFailsClosedAndReloads(string marker)
    {
        var config = new ConfigFile("config/malformed.cfg");
        config.SeedSerialized(ConfigurationSchemaTransaction.MarkerSection, ConfigurationSchemaTransaction.MarkerKey, marker);

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            new FakeFileOperations((config.ConfigFilePath, Encoding.UTF8.GetBytes(marker))),
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Equal(ConfigurationSchemaState.Failed, result.Status.State);
        Assert.False(result.Status.Loaded);
        Assert.True(config.ReloadCalls >= 1);
        Assert.Empty(config);
    }

    [Fact]
    public void FutureSchemaIsReadOnlyWithoutBackupBindOrSave()
    {
        var path = "config/future.cfg";
        var files = new FakeFileOperations((path, Encoding.UTF8.GetBytes("future")));
        var config = new ConfigFile(path);
        config.SeedSerialized(ConfigurationSchemaTransaction.MarkerSection, ConfigurationSchemaTransaction.MarkerKey, "2");

        var result = ConfigurationSchemaTransaction.Bind<TestConfig>(
            PluginId,
            config,
            MarkerOnlyPlan(),
            _ => throw new Xunit.Sdk.XunitException("Current binder must not run for a future schema."),
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Equal(ConfigurationSchemaState.Future, result.Status.State);
        Assert.True(result.Status.Saved);
        Assert.False(result.Status.Loaded);
        Assert.Empty(files.BackupPaths);
        Assert.Equal(0, config.SaveCalls);
        Assert.True(config.ReloadCalls >= 1);
    }

    [Fact]
    public void SaveFailureRemovesAddedEntriesRestoresExactBytesReloadsAndReturnsNoConfig()
    {
        var path = "config/save-failure.cfg";
        var original = new byte[] { 0, 1, 2, 255, 10 };
        var files = new FakeFileOperations((path, original));
        var config = new ConfigFile(path) { ThrowOnSaveCall = 1 };
        config.SeedSerialized(Legacy.Section, Legacy.Key, "9");
        config.SeedSerialized("Unknown", "Orphan", "untouched");

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MappingPlan(),
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Equal(ConfigurationSchemaState.Failed, result.Status.State);
        Assert.True(result.Status.BackupCreated);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.Equal(original, files.ReadAllBytes(ConfigurationSchemaTransaction.GetBackupPath(path, 1)));
        Assert.Empty(config);
        Assert.True(config.ReloadCalls >= 1);
        Assert.True(config.SaveOnConfigSet);
    }

    [Fact]
    public void BindFailureRestoresExactBytesAndDoesNotExposePartialConfig()
    {
        var path = "config/bind-failure.cfg";
        var original = Encoding.UTF8.GetBytes("before");
        var config = new ConfigFile(path)
        {
            ThrowOnBindDefinition = Current.ToDefinition(),
        };
        var files = new FakeFileOperations((path, original));

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.Empty(config);
        Assert.Equal(0, config.SaveCalls);
        Assert.True(config.ReloadCalls >= 1);
    }

    [Fact]
    public void AbsentOriginalCreatedDuringFailedMigrationIsDeleted()
    {
        var path = "config/new-file.cfg";
        var files = new FakeFileOperations();
        var config = new ConfigFile(path) { ThrowOnSaveCall = 1 };

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            file =>
            {
                files.WriteAllBytes(path, Encoding.UTF8.GetBytes("partial"));
                return BindCurrent(file);
            },
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.False(files.Exists(path));
        Assert.Empty(files.BackupPaths);
        Assert.Equal(1, config.ReloadCalls);
    }

    [Fact]
    public void BackupCollisionPreservesExistingBackupAndUsesFirstFreeSuffix()
    {
        var path = "config/collision.cfg";
        var backupPath = ConfigurationSchemaTransaction.GetBackupPath(path, 1);
        var original = Encoding.UTF8.GetBytes("original");
        var existingBackup = Encoding.UTF8.GetBytes("existing backup");
        var files = new FakeFileOperations((path, original), (backupPath, existingBackup));
        var config = new ConfigFile(path);
        var bindCalls = 0;

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            file =>
            {
                bindCalls++;
                return BindCurrent(file);
            },
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(1, bindCalls);
        Assert.Equal(existingBackup, files.ReadAllBytes(backupPath));
        Assert.Equal(original, files.ReadAllBytes(ConfigurationSchemaTransaction.GetBackupPath(path, 1, 2)));
        Assert.True(result.Status.BackupCreated);
        Assert.Contains(ConfigurationSchemaTransaction.GetBackupPath(path, 1, 2), files.BackupPaths);
    }

    [Theory]
    [InlineData(BackupFailureStage.PartialWrite)]
    [InlineData(BackupFailureStage.Flush)]
    public void OwnedPartialBackupFailureIsDeletedAndMigrationFailsClosed(BackupFailureStage stage)
    {
        var path = "C:/Users/private/config-partial.cfg";
        var original = Encoding.UTF8.GetBytes("original bytes with private-value");
        var files = new FakeFileOperations((path, original)) { BackupFailure = stage };
        var config = new ConfigFile(path);

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        var candidate = ConfigurationSchemaTransaction.GetBackupPath(path, 1);
        Assert.False(result.Success);
        Assert.Null(result.Config);
        Assert.Equal(ConfigurationSchemaState.Failed, result.Status.State);
        Assert.False(result.Status.BackupCreated);
        Assert.False(result.Status.Loaded);
        Assert.True(files.OwnedPartialDeleted);
        Assert.False(files.Exists(candidate));
        Assert.Empty(files.BackupPaths);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.DoesNotContain(path, result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-value", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("simulated", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RaceCollisionRetriesFirstFreeSuffixWithoutOverwritingCompetitor()
    {
        var path = "config/race-collision.cfg";
        var original = Encoding.UTF8.GetBytes("original");
        var competitor = Encoding.UTF8.GetBytes("race winner");
        var files = new FakeFileOperations((path, original)) { RaceCollisionContents = competitor };
        var config = new ConfigFile(path);

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(competitor, files.ReadAllBytes(ConfigurationSchemaTransaction.GetBackupPath(path, 1)));
        Assert.Equal(original, files.ReadAllBytes(ConfigurationSchemaTransaction.GetBackupPath(path, 1, 2)));
        Assert.Single(files.BackupPaths);
    }

    [Fact]
    public void RealFirstProbeSentinelValueIsPreservedAfterReloadBackedSecondProbe()
    {
        const string sentinel = "\u001eOrbModding.Configuration.Missing\u001e";
        var path = "config/sentinel.cfg";
        var files = new FakeFileOperations((path, Encoding.UTF8.GetBytes("sentinel config")));
        var config = new ConfigFile(path);
        config.SeedSerialized(Legacy.Section, Legacy.Key, sentinel);
        var plan = new ConfigurationSchemaPlan(1, new[]
        {
            new ConfigurationMigrationStep(0, 1, new[] { Legacy }, context =>
            {
                Assert.True(context.TryGet(Legacy, out var value));
                Assert.Equal(sentinel, value);
                context.Map(Legacy, new ConfigurationKey("General", "Text"), value, "preserve sentinel");
            }),
        });

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            plan,
            file => new TextConfig(file.Bind("General", "Text", string.Empty, "Text")),
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(sentinel, result.Config!.Value.Value);
        Assert.True(config.ReloadCalls >= 1);
    }

    [Fact]
    public void ThrowingStatusSubscriberCannotRollBackSuccessfulMigration()
    {
        var registry = new ConfigurationSchemaStatusRegistry();
        var observed = 0;
        registry.Transitioned += _ => throw new InvalidOperationException("subscriber failure");
        registry.Transitioned += _ => observed++;
        var config = new ConfigFile();

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            new FakeFileOperations(),
            registry);

        Assert.True(result.Success);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(1, observed);
        Assert.True(registry.TryGet(PluginId, out var published));
        Assert.Equal(result.Status, published);
    }

    [Fact]
    public void LaterMigrationOverwritesReloadResurrectedMarkerWithTargetVersion()
    {
        var path = "config/v1-to-v2.cfg";
        var files = new FakeFileOperations((path, Encoding.UTF8.GetBytes("v1")));
        var config = new ConfigFile(path);
        config.SeedSerialized(ConfigurationSchemaTransaction.MarkerSection, ConfigurationSchemaTransaction.MarkerKey, "1");
        var missingKnown = new ConfigurationKey("Legacy", "MissingInV1");
        var plan = new ConfigurationSchemaPlan(2, new[]
        {
            new ConfigurationMigrationStep(0, 1, Array.Empty<ConfigurationKey>(), _ => { }),
            new ConfigurationMigrationStep(1, 2, new[] { missingKnown }, _ => { }),
        });

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            plan,
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(1, result.Status.FromVersion);
        Assert.Equal(2, result.Status.ToVersion);
        Assert.True(config.TryGetPersisted(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            out var persistedMarker));
        Assert.Equal("2", persistedMarker);
    }

    [Fact]
    public void StatusRegistryPublishesExactGuidTransitionsWithoutPaths()
    {
        var path = "C:/Users/private/config.cfg";
        var registry = new ConfigurationSchemaStatusRegistry();
        var transitions = new List<ConfigurationSchemaStatusTransition>();
        registry.Transitioned += transitions.Add;
        var config = new ConfigFile(path) { ThrowOnSaveCall = 1 };

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            new FakeFileOperations((path, Encoding.UTF8.GetBytes("before"))),
            registry);

        Assert.False(result.Success);
        Assert.True(registry.TryGet(PluginId, out var status));
        Assert.Equal(result.Status, status);
        Assert.Equal(PluginId, status.PluginId);
        Assert.DoesNotContain("C:/", status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Single(transitions);
    }

    /// <summary>
    /// A migration step that breaks keeps the short sentence for the player and hands the log what
    /// broke. The sentence alone left a step that could not parse an old value indistinguishable
    /// from every other unexpected failure.
    /// </summary>
    [Fact]
    public void AMigrationStepThatThrowsKeepsWhatThrew()
    {
        var config = new ConfigFile();
        config.SeedSerialized(Legacy.Section, Legacy.Key, "forty-one");
        var plan = new ConfigurationSchemaPlan(1, new[]
        {
            new ConfigurationMigrationStep(0, 1, new[] { Legacy }, context =>
            {
                Assert.True(context.TryGet(Legacy, out var value));
                context.Map(Legacy, Current, (int.Parse(value) + 1).ToString(), "mapped known value");
            }),
        });

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            plan,
            BindCurrent,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Equal(
            "Configuration migration failed; the prior file was restored and runtime configuration was not loaded.",
            result.Status.Reason);
        Assert.StartsWith("System.FormatException: ", result.FailureCause);
    }

    [Fact]
    public void SnapshotReadFailureLeavesOriginalFileUntouchedAndFailsClosed()
    {
        var path = "config/unreadable.cfg";
        var original = Encoding.UTF8.GetBytes("do not alter");
        var files = new FakeFileOperations((path, original)) { ThrowOnReadPath = path };
        var config = new ConfigFile(path);

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        files.ThrowOnReadPath = null;
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.Empty(config);
        Assert.Contains("snapshot", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirstReloadFailureRestoresExactBytesAndPublishesGenericFailure()
    {
        var path = "C:/Users/private/first-reload.cfg";
        var original = Encoding.UTF8.GetBytes("private-reload-value");
        var config = new ConfigFile(path) { ThrowOnReloadCall = 1 };
        var files = new FakeFileOperations((path, original));

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Null(result.Config);
        Assert.False(result.Status.Loaded);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.Equal(2, config.ReloadCalls);
        Assert.DoesNotContain(path, result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-reload-value", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("simulated", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepeatedReloadFailureRestoresExactBytesAndPublishesGenericFailure()
    {
        var path = "C:/Users/private/repeated-reload.cfg";
        var original = Encoding.UTF8.GetBytes("private-repeated-value");
        var config = new ConfigFile(path) { ThrowOnEveryReload = true };
        var files = new FakeFileOperations((path, original));

        var result = ConfigurationSchemaTransaction.Bind(
            PluginId,
            config,
            MarkerOnlyPlan(),
            BindCurrent,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Null(result.Config);
        Assert.False(result.Status.Loaded);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.Equal(2, config.ReloadCalls);
        Assert.DoesNotContain(path, result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-repeated-value", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("simulated", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static ConfigurationSchemaPlan MarkerOnlyPlan() => new(1, new[]
    {
        new ConfigurationMigrationStep(0, 1, Array.Empty<ConfigurationKey>(), _ => { }),
    });

    private static ConfigurationSchemaPlan MappingPlan() => new(1, new[]
    {
        new ConfigurationMigrationStep(0, 1, new[] { Legacy }, context =>
        {
            if (context.TryGet(Legacy, out var value)) context.Map(Legacy, Current, value, "mapped");
        }),
    });

    private static TestConfig BindCurrent(ConfigFile file) => new(
        file.Bind(Current.Section, Current.Key, 5, "Current value."));

    private sealed record TestConfig(ConfigEntry<int> Value);

    private sealed record TextConfig(ConfigEntry<string> Value);

    public enum BackupFailureStage
    {
        PartialWrite,
        Flush,
    }

    private sealed class FakeFileOperations : IConfigurationFileOperations
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private readonly List<string> _backups = new();

        public FakeFileOperations(params (string Path, byte[] Contents)[] files)
        {
            foreach (var file in files) _files[file.Path] = file.Contents.ToArray();
        }

        public IReadOnlyList<string> BackupPaths => _backups;

        public string? ThrowOnReadPath { get; set; }

        public BackupFailureStage? BackupFailure { get; set; }

        public byte[]? RaceCollisionContents { get; set; }

        public bool OwnedPartialDeleted { get; private set; }

        public bool Exists(string path) => _files.ContainsKey(path);

        public byte[] ReadAllBytes(string path)
        {
            if (string.Equals(path, ThrowOnReadPath, StringComparison.Ordinal))
                throw new InvalidOperationException("simulated read failure");
            return _files[path].ToArray();
        }

        public void WriteAllBytes(string path, byte[] contents) => _files[path] = contents.ToArray();

        public void Delete(string path) => _files.Remove(path);

        public ConfigurationBackupCreationResult CreateNewBackup(string path, byte[] contents)
        {
            if (_files.ContainsKey(path)) return ConfigurationBackupCreationResult.Collision;
            if (RaceCollisionContents is not null)
            {
                _files[path] = RaceCollisionContents.ToArray();
                RaceCollisionContents = null;
                return ConfigurationBackupCreationResult.Collision;
            }
            if (BackupFailure.HasValue)
            {
                _files[path] = BackupFailure == BackupFailureStage.PartialWrite
                    ? contents.Take(Math.Max(1, contents.Length / 2)).ToArray()
                    : contents.ToArray();
                _files.Remove(path);
                OwnedPartialDeleted = true;
                throw new InvalidOperationException("simulated private backup write or flush failure");
            }
            _files[path] = contents.ToArray();
            _backups.Add(path);
            return ConfigurationBackupCreationResult.Created;
        }
    }
}
