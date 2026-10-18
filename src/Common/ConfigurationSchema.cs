using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx.Configuration;

namespace OrbModding.Common;

public readonly struct ConfigurationKey : IEquatable<ConfigurationKey>
{
    public ConfigurationKey(string section, string key)
    {
        Section = section ?? throw new ArgumentNullException(nameof(section));
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public string Section { get; }

    public string Key { get; }

    public ConfigDefinition ToDefinition() => new ConfigDefinition(Section, Key);

    public bool Equals(ConfigurationKey other) =>
        string.Equals(Section, other.Section, StringComparison.Ordinal) &&
        string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ConfigurationKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Section, Key);

    public static bool operator ==(ConfigurationKey left, ConfigurationKey right) => left.Equals(right);

    public static bool operator !=(ConfigurationKey left, ConfigurationKey right) => !left.Equals(right);

    public override string ToString() => $"[{Section}] {Key}";
}

public enum ConfigurationMigrationDiagnosticKind
{
    Mapped,
    DiscardedObsolete,
}

public readonly struct ConfigurationMigrationDiagnostic
{
    public ConfigurationMigrationDiagnostic(
        ConfigurationMigrationDiagnosticKind kind,
        ConfigurationKey source,
        ConfigurationKey? destination,
        string detail)
    {
        Kind = kind;
        Source = source;
        Destination = destination;
        Detail = detail;
    }

    public ConfigurationMigrationDiagnosticKind Kind { get; }

    public ConfigurationKey Source { get; }

    public ConfigurationKey? Destination { get; }

    public string Detail { get; }
}

public enum ConfigurationMigrationFailureCode
{
    InvalidKnownMode,
    InvalidKnownIntervalSeconds,
    InvalidKnownIntervalMinutes,
    KnownIntervalOutsideFiniteRange,
    DestinationNotBound,
}

public sealed class ConfigurationMigrationException : Exception
{
    public ConfigurationMigrationException(ConfigurationMigrationFailureCode code)
        : base(RenderSafeReason(code))
    {
        Code = code;
    }

    public ConfigurationMigrationFailureCode Code { get; }

    internal static string RenderSafeReason(ConfigurationMigrationFailureCode code) => code switch
    {
        ConfigurationMigrationFailureCode.InvalidKnownMode =>
            "Known configuration mode is outside the reviewed Disabled, Active, or BalanceMastery contract.",
        ConfigurationMigrationFailureCode.InvalidKnownIntervalSeconds =>
            "Known configuration interval seconds are malformed or negative.",
        ConfigurationMigrationFailureCode.InvalidKnownIntervalMinutes =>
            "Known configuration interval minutes are malformed, non-finite, negative, or not invariant-formatted.",
        ConfigurationMigrationFailureCode.KnownIntervalOutsideFiniteRange =>
            "Known configuration interval is outside the supported finite range.",
        ConfigurationMigrationFailureCode.DestinationNotBound =>
            "A reviewed configuration migration destination was not bound by the current schema.",
        _ => "Reviewed configuration migration validation failed.",
    };
}

public sealed class ConfigurationMigrationContext
{
    private readonly IReadOnlyDictionary<ConfigurationKey, string> _sourceValues;
    private readonly Dictionary<ConfigurationKey, string> _destinationValues = new();
    private readonly List<ConfigurationMigrationDiagnostic> _diagnostics = new();

    internal ConfigurationMigrationContext(IReadOnlyDictionary<ConfigurationKey, string> sourceValues)
    {
        _sourceValues = sourceValues;
    }

    public IReadOnlyList<ConfigurationMigrationDiagnostic> Diagnostics => _diagnostics;

    internal IReadOnlyDictionary<ConfigurationKey, string> DestinationValues => _destinationValues;

    public bool TryGet(ConfigurationKey key, out string value) => _sourceValues.TryGetValue(key, out value!);

    public void Preserve(ConfigurationKey key, string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        _destinationValues[key] = value;
    }

    public void Map(ConfigurationKey source, ConfigurationKey destination, string value, string detail)
    {
        Preserve(destination, value);
        _diagnostics.Add(new ConfigurationMigrationDiagnostic(
            ConfigurationMigrationDiagnosticKind.Mapped,
            source,
            destination,
            SanitizeDetail(detail)));
    }

    public void DiscardObsolete(ConfigurationKey source, string detail)
    {
        if (!_sourceValues.ContainsKey(source)) return;
        _diagnostics.Add(new ConfigurationMigrationDiagnostic(
            ConfigurationMigrationDiagnosticKind.DiscardedObsolete,
            source,
            null,
            SanitizeDetail(detail)));
    }

    private static string SanitizeDetail(string value) =>
        ConfigurationSchemaStatus.SanitizeReason(value, "Configuration migration diagnostic recorded.");
}

public sealed class ConfigurationMigrationStep
{
    public ConfigurationMigrationStep(
        int fromVersion,
        int toVersion,
        IReadOnlyList<ConfigurationKey> knownKeys,
        Action<ConfigurationMigrationContext> execute)
    {
        if (fromVersion < 0) throw new ArgumentOutOfRangeException(nameof(fromVersion));
        if (toVersion != fromVersion + 1) throw new ArgumentOutOfRangeException(nameof(toVersion));
        FromVersion = fromVersion;
        ToVersion = toVersion;
        KnownKeys = knownKeys ?? throw new ArgumentNullException(nameof(knownKeys));
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public int FromVersion { get; }

    public int ToVersion { get; }

    public IReadOnlyList<ConfigurationKey> KnownKeys { get; }

    internal Action<ConfigurationMigrationContext> Execute { get; }
}

public sealed class ConfigurationSchemaPlan
{
    private readonly IReadOnlyDictionary<int, ConfigurationMigrationStep> _steps;

    public ConfigurationSchemaPlan(int currentVersion, IReadOnlyList<ConfigurationMigrationStep> steps)
    {
        if (currentVersion < 1) throw new ArgumentOutOfRangeException(nameof(currentVersion));
        CurrentVersion = currentVersion;
        var byVersion = new Dictionary<int, ConfigurationMigrationStep>();
        foreach (var step in steps ?? throw new ArgumentNullException(nameof(steps)))
        {
            if (step.ToVersion > currentVersion || !byVersion.TryAdd(step.FromVersion, step))
                throw new ArgumentException("Configuration migration steps must be unique and end at the current version.", nameof(steps));
        }

        for (var version = 0; version < currentVersion; version++)
        {
            if (!byVersion.ContainsKey(version))
                throw new ArgumentException($"Configuration migration step {version} -> {version + 1} is missing.", nameof(steps));
        }

        _steps = byVersion;
    }

    public int CurrentVersion { get; }

    internal IReadOnlyList<ConfigurationMigrationStep> GetStepsFrom(int version)
    {
        var result = new List<ConfigurationMigrationStep>(CurrentVersion - version);
        for (var current = version; current < CurrentVersion; current++) result.Add(_steps[current]);
        return result;
    }
}

public enum ConfigurationSchemaState
{
    Current,
    Migrated,
    Failed,
    Future,
}

public readonly struct ConfigurationSchemaStatus : IEquatable<ConfigurationSchemaStatus>
{
    public ConfigurationSchemaStatus(
        string pluginId,
        ConfigurationSchemaState state,
        int fromVersion,
        int toVersion,
        bool saved,
        bool loaded,
        string reason,
        bool backupCreated)
    {
        PluginId = pluginId;
        State = state;
        FromVersion = fromVersion;
        ToVersion = toVersion;
        Saved = saved;
        Loaded = loaded;
        Reason = reason;
        BackupCreated = backupCreated;
    }

    public string PluginId { get; }

    public ConfigurationSchemaState State { get; }

    public int FromVersion { get; }

    public int ToVersion { get; }

    public bool Saved { get; }

    public bool Loaded { get; }

    public string Reason { get; }

    public bool BackupCreated { get; }

    public bool Equals(ConfigurationSchemaStatus other) =>
        string.Equals(PluginId, other.PluginId, StringComparison.Ordinal) &&
        State == other.State &&
        FromVersion == other.FromVersion &&
        ToVersion == other.ToVersion &&
        Saved == other.Saved &&
        Loaded == other.Loaded &&
        string.Equals(Reason, other.Reason, StringComparison.Ordinal) &&
        BackupCreated == other.BackupCreated;

    public override bool Equals(object? obj) => obj is ConfigurationSchemaStatus other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        PluginId,
        State,
        FromVersion,
        ToVersion,
        Saved,
        Loaded,
        Reason,
        BackupCreated);

    public static bool operator ==(ConfigurationSchemaStatus left, ConfigurationSchemaStatus right) => left.Equals(right);

    public static bool operator !=(ConfigurationSchemaStatus left, ConfigurationSchemaStatus right) => !left.Equals(right);

    internal static string SanitizeReason(string? reason, string fallback)
    {
        var normalized = string.Join(
            " ",
            (reason ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized)) normalized = fallback;
        if (normalized.Length > 240) normalized = normalized.Substring(0, 240);
        return normalized;
    }
}

public readonly struct ConfigurationSchemaStatusTransition
{
    public ConfigurationSchemaStatusTransition(
        ConfigurationSchemaStatus? previous,
        ConfigurationSchemaStatus current)
    {
        Previous = previous;
        Current = current;
    }

    public ConfigurationSchemaStatus? Previous { get; }

    public ConfigurationSchemaStatus Current { get; }
}

public interface IConfigurationSchemaStatusSource
{
    event Action<ConfigurationSchemaStatusTransition>? Transitioned;

    bool TryGet(string pluginId, out ConfigurationSchemaStatus status);
}

public sealed class ConfigurationSchemaStatusRegistry : IConfigurationSchemaStatusSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ConfigurationSchemaStatus> _statuses = new(StringComparer.Ordinal);

    public static ConfigurationSchemaStatusRegistry Shared { get; } = new();

    public event Action<ConfigurationSchemaStatusTransition>? Transitioned;

    public bool TryGet(string pluginId, out ConfigurationSchemaStatus status)
    {
        lock (_gate) return _statuses.TryGetValue(pluginId ?? string.Empty, out status);
    }

    internal void Publish(ConfigurationSchemaStatus status)
    {
        ConfigurationSchemaStatus? previous;
        lock (_gate)
        {
            previous = _statuses.TryGetValue(status.PluginId, out var value) ? value : null;
            if (previous.HasValue && previous.Value == status) return;
            _statuses[status.PluginId] = status;
        }

        var transition = new ConfigurationSchemaStatusTransition(previous, status);
        var handlers = Transitioned?.GetInvocationList();
        if (handlers is null) return;
        foreach (var handler in handlers)
        {
            try { ((Action<ConfigurationSchemaStatusTransition>)handler)(transition); }
            catch { }
        }
    }

    internal void ClearForTests()
    {
        lock (_gate) _statuses.Clear();
    }
}

public interface IConfigurationFileOperations
{
    bool Exists(string path);

    byte[] ReadAllBytes(string path);

    void WriteAllBytes(string path, byte[] contents);

    void Delete(string path);

    ConfigurationBackupCreationResult CreateNewBackup(string path, byte[] contents);
}

public enum ConfigurationBackupCreationResult
{
    Created,
    Collision,
}

public sealed class PhysicalConfigurationFileOperations : IConfigurationFileOperations
{
    public static PhysicalConfigurationFileOperations Instance { get; } = new();

    private PhysicalConfigurationFileOperations()
    {
    }

    public bool Exists(string path) => File.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] contents) => File.WriteAllBytes(path, contents);

    public void Delete(string path) => File.Delete(path);

    public ConfigurationBackupCreationResult CreateNewBackup(string path, byte[] contents)
    {
        var temporaryPath = path + ".tmp." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var ownsTemporary = false;
        var ownsCandidate = false;
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                ownsTemporary = true;
                stream.Write(contents, 0, contents.Length);
                stream.Flush(true);
            }
            VerifyExactBackup(temporaryPath, contents);

            try
            {
                File.Move(temporaryPath, path);
                ownsTemporary = false;
                ownsCandidate = true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return ConfigurationBackupCreationResult.Collision;
            }

            VerifyExactBackup(path, contents);
            return ConfigurationBackupCreationResult.Created;
        }
        catch
        {
            if (ownsCandidate) DeleteOwnedFile(path);
            throw;
        }
        finally
        {
            if (ownsTemporary) DeleteOwnedFile(temporaryPath);
        }
    }

    private static void VerifyExactBackup(string path, byte[] expected)
    {
        var actual = File.ReadAllBytes(path);
        if (new FileInfo(path).Length != expected.LongLength ||
            actual.LongLength != expected.LongLength ||
            !actual.SequenceEqual(expected) ||
            !HashesMatch(actual, expected))
        {
            throw new IOException("Configuration backup verification failed.");
        }
    }

    private static void DeleteOwnedFile(string path)
    {
        try
        {
            File.Delete(path);
            if (File.Exists(path))
                throw new IOException("Owned configuration backup cleanup could not be confirmed.");
        }
        catch (Exception ex) when (ex is not IOException || File.Exists(path))
        {
            throw new IOException("Owned configuration backup cleanup failed.", ex);
        }
    }

    private static bool HashesMatch(byte[] actual, byte[] expected)
    {
        using var sha256 = SHA256.Create();
        var actualHash = sha256.ComputeHash(actual);
        var expectedHash = sha256.ComputeHash(expected);
        return actualHash.SequenceEqual(expectedHash);
    }
}

public sealed class ConfigurationSchemaBindResult<T> where T : class
{
    internal ConfigurationSchemaBindResult(
        T? config,
        ConfigurationSchemaStatus status,
        IReadOnlyList<ConfigurationMigrationDiagnostic> diagnostics,
        string failureCause = "")
    {
        Config = config;
        Status = status;
        Diagnostics = diagnostics;
        FailureCause = failureCause;
    }

    public bool Success => Config is not null;

    public T? Config { get; }

    public ConfigurationSchemaStatus Status { get; }

    public IReadOnlyList<ConfigurationMigrationDiagnostic> Diagnostics { get; }

    /// <summary>
    /// What threw, when an unexpected exception is why the bind failed; empty otherwise. It is for
    /// the log, beside the status sentence the player reads.
    /// </summary>
    public string FailureCause { get; }
}

public static class ConfigurationSchemaTransaction
{
    public const string MarkerSection = "OrbModding";
    public const string MarkerKey = "ConfigurationSchemaVersion";
    private const string MissingSentinel = "\u001eOrbModding.Configuration.Missing\u001e";
    private const string SecondMissingSentinel = "\u001eOrbModding.Configuration.Missing.SecondProbe\u001e";
    private static readonly ConfigurationKey Marker = new(MarkerSection, MarkerKey);

    public static ConfigurationSchemaBindResult<T> Bind<T>(
        string pluginId,
        ConfigFile file,
        ConfigurationSchemaPlan plan,
        Func<ConfigFile, T> bindCurrent,
        IConfigurationFileOperations? fileOperations = null,
        ConfigurationSchemaStatusRegistry? statuses = null)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(pluginId)) throw new ArgumentException("Plugin ID is required.", nameof(pluginId));
        if (file is null) throw new ArgumentNullException(nameof(file));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (bindCurrent is null) throw new ArgumentNullException(nameof(bindCurrent));

        fileOperations ??= PhysicalConfigurationFileOperations.Instance;
        statuses ??= ConfigurationSchemaStatusRegistry.Shared;
        var originalSaveOnConfigSet = file.SaveOnConfigSet;
        var initialKeys = new HashSet<ConfigurationKey>(
            file.Select(pair => new ConfigurationKey(pair.Key.Section, pair.Key.Key)));
        var path = file.ConfigFilePath ?? string.Empty;
        var originalExisted = false;
        var originalBytes = Array.Empty<byte>();
        var snapshotAvailable = string.IsNullOrWhiteSpace(path);
        var backupCreated = false;
        var fromVersion = 0;
        var diagnostics = Array.Empty<ConfigurationMigrationDiagnostic>();

        try
        {
            file.SaveOnConfigSet = false;
            if (!string.IsNullOrWhiteSpace(path) && fileOperations.Exists(path))
            {
                originalBytes = fileOperations.ReadAllBytes(path);
                originalExisted = true;
            }
            snapshotAvailable = true;

            var markerPresent = TryReadTemporary(
                file,
                Marker,
                initialKeys,
                originalExisted,
                out var serializedMarker);
            if (markerPresent &&
                (!int.TryParse(serializedMarker, NumberStyles.Integer, CultureInfo.InvariantCulture, out fromVersion) ||
                 fromVersion < 0))
            {
                return Fail<T>(
                    pluginId,
                    file,
                    fileOperations,
                    statuses,
                    initialKeys,
                    path,
                    originalExisted,
                    originalBytes,
                    snapshotAvailable,
                    0,
                    plan.CurrentVersion,
                    backupCreated,
                    "Configuration schema marker is malformed or negative.");
            }

            if (!markerPresent) fromVersion = 0;
            if (fromVersion > plan.CurrentVersion)
            {
                TryReload(file);
                var future = Publish(
                    statuses,
                    pluginId,
                    ConfigurationSchemaState.Future,
                    fromVersion,
                    plan.CurrentVersion,
                    saved: true,
                    loaded: false,
                    backupCreated: false,
                    "Configuration uses a newer schema and was left read-only.");
                return new ConfigurationSchemaBindResult<T>(null, future, diagnostics);
            }

            if (fromVersion == plan.CurrentVersion)
            {
                var currentConfig = bindCurrent(file);
                BindMarker(file, plan.CurrentVersion);
                var current = Publish(
                    statuses,
                    pluginId,
                    ConfigurationSchemaState.Current,
                    fromVersion,
                    plan.CurrentVersion,
                    saved: true,
                    loaded: true,
                    backupCreated: false,
                    "Configuration schema is current and loaded.");
                return new ConfigurationSchemaBindResult<T>(currentConfig, current, diagnostics);
            }

            if (originalExisted)
            {
                CreateFirstAvailableBackup(fileOperations, path, plan.CurrentVersion, originalBytes);
                backupCreated = true;
            }

            var steps = plan.GetStepsFrom(fromVersion);
            var knownKeys = steps
                .SelectMany(step => step.KnownKeys)
                .Distinct()
                .ToArray();
            var sourceValues = new Dictionary<ConfigurationKey, string>();
            foreach (var key in knownKeys)
            {
                if (TryReadTemporary(file, key, initialKeys, originalExisted, out var value))
                    sourceValues[key] = value;
            }
            ConsumeKnownKeys(file, knownKeys, sourceValues, initialKeys);

            var context = new ConfigurationMigrationContext(sourceValues);
            foreach (var step in steps) step.Execute(context);
            var migratedConfig = bindCurrent(file);
            ApplyDestinations(file, context.DestinationValues);
            BindMarker(file, plan.CurrentVersion);
            file.Save();
            diagnostics = context.Diagnostics.ToArray();
            var mapped = diagnostics.Count(item => item.Kind == ConfigurationMigrationDiagnosticKind.Mapped);
            var discarded = diagnostics.Count(item => item.Kind == ConfigurationMigrationDiagnosticKind.DiscardedObsolete);
            var migrated = Publish(
                statuses,
                pluginId,
                ConfigurationSchemaState.Migrated,
                fromVersion,
                plan.CurrentVersion,
                saved: true,
                loaded: true,
                backupCreated,
                $"Configuration migrated from schema {fromVersion} to {plan.CurrentVersion}; mapped {mapped}, discarded {discarded} obsolete.");
            return new ConfigurationSchemaBindResult<T>(migratedConfig, migrated, diagnostics);
        }
        catch (ConfigurationMigrationException ex)
        {
            return Fail<T>(
                pluginId,
                file,
                fileOperations,
                statuses,
                initialKeys,
                path,
                originalExisted,
                originalBytes,
                snapshotAvailable,
                fromVersion,
                plan.CurrentVersion,
                backupCreated,
                ConfigurationMigrationException.RenderSafeReason(ex.Code));
        }
        catch (Exception ex)
        {
            return Fail<T>(
                pluginId,
                file,
                fileOperations,
                statuses,
                initialKeys,
                path,
                originalExisted,
                originalBytes,
                snapshotAvailable,
                fromVersion,
                plan.CurrentVersion,
                backupCreated,
                "Configuration migration failed; the prior file was restored and runtime configuration was not loaded.",
                ExceptionDetail.Describe(ex));
        }
        finally
        {
            file.SaveOnConfigSet = originalSaveOnConfigSet;
        }
    }

    public static string GetBackupPath(string configPath, int targetVersion) =>
        configPath + $".pre-schema-v{targetVersion}.bak";

    public static string GetBackupPath(string configPath, int targetVersion, int sequence) =>
        sequence <= 1 ? GetBackupPath(configPath, targetVersion) : GetBackupPath(configPath, targetVersion) + "." + sequence;

    public static ConfigDescription CreateMarkerDescription() => new(
        "Internal configuration schema marker.",
        null,
        new ModConfigMetadata(int.MaxValue, int.MaxValue, hidden: true));

    private static bool TryReadTemporary(
        ConfigFile file,
        ConfigurationKey key,
        ISet<ConfigurationKey> initialKeys,
        bool originalExisted,
        out string value)
    {
        var definition = key.ToDefinition();
        if (initialKeys.Contains(key))
        {
            var existing = file.First(pair => pair.Key.Equals(definition)).Value;
            value = existing.GetSerializedValue();
            file.Remove(definition);
            return true;
        }

        var probe = file.Bind(key.Section, key.Key, MissingSentinel, "Temporary configuration schema read.");
        value = probe.Value;
        file.Remove(definition);
        if (!string.Equals(value, MissingSentinel, StringComparison.Ordinal)) return true;
        if (!originalExisted) return false;

        file.Reload();
        var secondProbe = file.Bind(
            key.Section,
            key.Key,
            SecondMissingSentinel,
            "Temporary configuration schema second read.");
        value = secondProbe.Value;
        file.Remove(definition);
        return !string.Equals(value, SecondMissingSentinel, StringComparison.Ordinal);
    }

    private static void CreateFirstAvailableBackup(
        IConfigurationFileOperations fileOperations,
        string configPath,
        int targetVersion,
        byte[] contents)
    {
        for (var sequence = 1; sequence <= 10_000; sequence++)
        {
            var candidate = GetBackupPath(configPath, targetVersion, sequence);
            if (fileOperations.Exists(candidate)) continue;
            var result = fileOperations.CreateNewBackup(candidate, contents);
            if (result == ConfigurationBackupCreationResult.Created) return;
            if (result != ConfigurationBackupCreationResult.Collision)
                throw new IOException("Configuration backup creation returned an unsupported result.");
        }

        throw new IOException("No available non-overwriting configuration backup name remains.");
    }

    private static void ConsumeKnownKeys(
        ConfigFile file,
        IReadOnlyList<ConfigurationKey> knownKeys,
        IReadOnlyDictionary<ConfigurationKey, string> sourceValues,
        ISet<ConfigurationKey> initialKeys)
    {
        foreach (var key in knownKeys)
        {
            if (initialKeys.Contains(key)) continue;
            var fallback = sourceValues.TryGetValue(key, out var value) ? value : SecondMissingSentinel;
            file.Bind(key.Section, key.Key, fallback, "Temporary configuration schema cleanup.");
            file.Remove(key.ToDefinition());
        }
    }

    private static void ApplyDestinations(ConfigFile file, IReadOnlyDictionary<ConfigurationKey, string> destinations)
    {
        foreach (var destination in destinations)
        {
            var definition = destination.Key.ToDefinition();
            var entry = file.FirstOrDefault(pair => pair.Key.Equals(definition)).Value;
            if (entry is null)
                throw new ConfigurationMigrationException(ConfigurationMigrationFailureCode.DestinationNotBound);
            entry.SetSerializedValue(destination.Value);
        }
    }

    private static void BindMarker(ConfigFile file, int version)
    {
        var marker = file.Bind(MarkerSection, MarkerKey, version, CreateMarkerDescription());
        marker.Value = version;
    }

    private static ConfigurationSchemaBindResult<T> Fail<T>(
        string pluginId,
        ConfigFile file,
        IConfigurationFileOperations fileOperations,
        ConfigurationSchemaStatusRegistry statuses,
        ISet<ConfigurationKey> initialKeys,
        string path,
        bool originalExisted,
        byte[] originalBytes,
        bool snapshotAvailable,
        int fromVersion,
        int toVersion,
        bool backupCreated,
        string reason,
        string failureCause = "")
        where T : class
    {
        try
        {
            var added = file
                .Select(pair => new ConfigurationKey(pair.Key.Section, pair.Key.Key))
                .Where(key => !initialKeys.Contains(key))
                .ToArray();
            foreach (var key in added) file.Remove(key.ToDefinition());

            if (snapshotAvailable && !string.IsNullOrWhiteSpace(path))
            {
                if (originalExisted) fileOperations.WriteAllBytes(path, originalBytes);
                else if (fileOperations.Exists(path)) fileOperations.Delete(path);
            }
            else if (!snapshotAvailable)
            {
                reason = "Configuration migration failed before an exact file snapshot could be captured; runtime configuration was not loaded.";
            }
        }
        catch
        {
            reason = "Configuration migration failed and exact file restoration could not be confirmed.";
        }

        TryReload(file);
        var failed = Publish(
            statuses,
            pluginId,
            ConfigurationSchemaState.Failed,
            fromVersion,
            toVersion,
            saved: false,
            loaded: false,
            backupCreated,
            reason);
        return new ConfigurationSchemaBindResult<T>(
            null, failed, Array.Empty<ConfigurationMigrationDiagnostic>(), failureCause);
    }

    private static void TryReload(ConfigFile file)
    {
        try { file.Reload(); }
        catch { }
    }

    private static ConfigurationSchemaStatus Publish(
        ConfigurationSchemaStatusRegistry statuses,
        string pluginId,
        ConfigurationSchemaState state,
        int fromVersion,
        int toVersion,
        bool saved,
        bool loaded,
        bool backupCreated,
        string reason)
    {
        var status = new ConfigurationSchemaStatus(
            pluginId,
            state,
            fromVersion,
            toVersion,
            saved,
            loaded,
            ConfigurationSchemaStatus.SanitizeReason(reason, "Configuration schema status unavailable."),
            backupCreated);
        statuses.Publish(status);
        return status;
    }
}
