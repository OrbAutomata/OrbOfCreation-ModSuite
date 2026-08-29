using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal enum EntityIdentityCatalogState
{
    Unbound = 0,
    Bound = 1,
    ContractUnavailable = 2,
}

internal readonly struct EntityIdentityName
{
    internal EntityIdentityName(
        Guid entityId,
        string runtimeType,
        string displayName,
        string assetName)
    {
        EntityId = entityId;
        RuntimeType = runtimeType ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        AssetName = assetName ?? string.Empty;
    }

    internal Guid EntityId { get; }
    internal string RuntimeType { get; }
    internal string DisplayName { get; }
    internal string AssetName { get; }
}

/// <summary>
/// One immutable, lifecycle-scoped view of every stable identity loaded by the game.
/// </summary>
/// <remarks>
/// This reference is attached directly to every world published in its lifecycle and also placed in
/// the Common latest-wins holder. The private table copied the capture candidate once; subsequent
/// publications share this object and never copy name rows into gameplay categories.
/// </remarks>
[ServiceCyclePublicationValue]
internal sealed class EntityIdentityCatalogSnapshot
{
    private EntityIdentityCatalogSnapshot(
        long generation,
        EntityIdentityCatalogState state,
        PublicationTable<EntityIdentityName> rows,
        string failureReason)
    {
        Generation = generation;
        State = state;
        Rows = rows;
        FailureReason = failureReason ?? string.Empty;
    }

    internal long Generation { get; }
    internal EntityIdentityCatalogState State { get; }
    internal PublicationTable<EntityIdentityName> Rows { get; }
    internal string FailureReason { get; }
    internal bool IsBound => State == EntityIdentityCatalogState.Bound;

    internal static EntityIdentityCatalogSnapshot Unbound(long generation) =>
        new(generation, EntityIdentityCatalogState.Unbound,
            PublicationTable<EntityIdentityName>.Empty, string.Empty);

    internal static EntityIdentityCatalogSnapshot Bound(
        long generation,
        EntityIdentityName[] rows) =>
        new(generation, EntityIdentityCatalogState.Bound,
            PublicationTable<EntityIdentityName>.Create(rows, rows.Length), string.Empty);

    internal static EntityIdentityCatalogSnapshot ContractUnavailable(
        long generation,
        string reason) =>
        new(generation, EntityIdentityCatalogState.ContractUnavailable,
            PublicationTable<EntityIdentityName>.Empty, reason);

    internal bool TryGet(Guid uuid, out EntityIdentityName identity)
    {
        var rows = Rows.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].EntityId.CompareTo(uuid);
            if (comparison == 0)
            {
                identity = rows[middle];
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        identity = default;
        return false;
    }
}

/// <summary>The non-world consumer view of the exact same immutable catalog reference.</summary>
internal static class EntityIdentityCatalogPublication
{
    private static EntityIdentityCatalogSnapshot _current =
        EntityIdentityCatalogSnapshot.Unbound(0);

    internal static EntityIdentityCatalogSnapshot Current => Volatile.Read(ref _current);

    internal static void Publish(EntityIdentityCatalogSnapshot snapshot) =>
        Volatile.Write(
            ref _current,
            snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
}

/// <summary>
/// Builds the live identity catalog once on the Unity thread in each playable lifecycle.
/// </summary>
internal sealed class EntityIdentityCatalog
{
    private readonly RuntimeIdentityRegistryBinding _binding;
    private readonly Func<GameLifecycleSnapshot> _readLifecycle;
    private readonly Func<int> _readThreadId;
    private readonly Func<object, string> _readDisplayName;
    private readonly Func<object, string> _readAssetName;
    private readonly Action<string> _error;
    private readonly Action<EntityIdentityCatalogSnapshot> _publish;
    private readonly Action<long> _resetFormatter;
    private EntityIdentityCatalogSnapshot _snapshot = EntityIdentityCatalogSnapshot.Unbound(0);
    private int? _mainThreadId;
    private bool _errorReported;

    internal EntityIdentityCatalog(
        RuntimeIdentityRegistryBinding? binding = null,
        Func<GameLifecycleSnapshot>? readLifecycle = null,
        Func<int>? readThreadId = null,
        Func<object, string>? readDisplayName = null,
        Func<object, string>? readAssetName = null,
        Action<string>? error = null,
        Action<EntityIdentityCatalogSnapshot>? publish = null,
        Action<long>? resetFormatter = null)
    {
        _binding = binding ?? RuntimeIdentityRegistryBinding.Shared;
        _readLifecycle = readLifecycle ?? (static () => GameLifecycleMonitor.Shared.Current);
        _readThreadId = readThreadId ?? (static () => Environment.CurrentManagedThreadId);
        _readDisplayName = readDisplayName ?? ReadDisplayName;
        _readAssetName = readAssetName ?? ReadAssetName;
        _error = error ?? EntityIdentityFormatter.ReportCatalogFailure;
        _publish = publish ?? EntityIdentityCatalogPublication.Publish;
        _resetFormatter = resetFormatter ?? EntityIdentityFormatter.Reset;
    }

    internal static EntityIdentityCatalog Shared { get; } = new();

    internal EntityIdentityCatalogSnapshot Current => _snapshot;

    /// <summary>
    /// Drops every prior-lifecycle reference immediately. Called from the suite's Unity-thread
    /// lifecycle transition handler before any replacement world can be captured.
    /// </summary>
    internal void Reset(long generation)
    {
        _mainThreadId = _readThreadId();
        _errorReported = false;
        _snapshot = EntityIdentityCatalogSnapshot.Unbound(generation);
        _publish(_snapshot);
        _resetFormatter(generation);
    }

    /// <summary>
    /// Returns the current lifecycle snapshot, attempting its one native enumeration only while the
    /// lifecycle is Playing. Instability leaves the unbound snapshot in place so the next ordinary
    /// world capture retries.
    /// </summary>
    internal EntityIdentityCatalogSnapshot Capture(long expectedGeneration)
    {
        if (_snapshot.Generation != expectedGeneration) Reset(expectedGeneration);
        if (_snapshot.State != EntityIdentityCatalogState.Unbound) return _snapshot;
        if (_mainThreadId.HasValue && _mainThreadId.Value != _readThreadId())
            return _snapshot;

        var beforeLifecycle = _readLifecycle();
        if (!beforeLifecycle.IsGameplayReady ||
            beforeLifecycle.Generation != expectedGeneration)
            return _snapshot;

        TypedRegistrySourceSnapshot source;
        try
        {
            source = _binding.Read();
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return Fail(expectedGeneration,
                "runtime identity registry binding failed: " +
                exception.GetBaseException().Message);
        }
        if (source.Status == TypedRegistryResolutionStatus.ContractUnavailable)
            return Fail(expectedGeneration, source.Reason);
        if (!source.IsReady) return _snapshot;

        var registry = source.Registry!;
        int countBefore;
        var candidate = new List<EntityIdentityName>();
        try
        {
            countBefore = registry.Count;
            candidate.Capacity = countBefore;
            foreach (DictionaryEntry entry in registry)
            {
                if (entry.Key is not Guid uuid || uuid == Guid.Empty || entry.Value is null)
                    return Fail(expectedGeneration,
                        "runtime identity registry contained an empty, non-Guid, or null entry");

                var stableUuid = _binding.ReadStableUuid(entry.Value);
                if (!stableUuid.HasValue || stableUuid.Value != uuid)
                    return Fail(expectedGeneration,
                        "runtime identity registry key disagreed with IdScriptableObject.GetGuid() for " +
                        uuid.ToString("D"));

                var displayName = ReadName(_readDisplayName, entry.Value);
                var assetName = ReadName(_readAssetName, entry.Value);
                var runtimeType = entry.Value.GetType().FullName ?? entry.Value.GetType().Name;
                candidate.Add(new EntityIdentityName(
                    uuid, runtimeType, displayName, assetName));
            }
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            // A registry mutation normally invalidates IDictionary enumeration. Treat the whole
            // candidate as transient and retry on the next capture; no partial table is published.
            return _snapshot;
        }

        var afterLifecycle = _readLifecycle();
        int countAfter;
        try
        {
            countAfter = registry.Count;
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return _snapshot;
        }
        if (!afterLifecycle.IsGameplayReady ||
            afterLifecycle.Generation != beforeLifecycle.Generation ||
            countBefore != countAfter ||
            countAfter != candidate.Count)
            return _snapshot;

        candidate.Sort(static (left, right) => left.EntityId.CompareTo(right.EntityId));
        var built = EntityIdentityCatalogSnapshot.Bound(
            expectedGeneration, candidate.ToArray());
        _snapshot = built;
        _publish(built);
        return built;
    }

    private EntityIdentityCatalogSnapshot Fail(long generation, string reason)
    {
        var failed = EntityIdentityCatalogSnapshot.ContractUnavailable(generation, reason);
        _snapshot = failed;
        _publish(failed);
        if (!_errorReported)
        {
            _errorReported = true;
            _error("Live entity-name catalog unavailable for lifecycle " +
                generation + ": " + reason);
        }
        return failed;
    }

    private static string ReadName(Func<object, string> read, object value)
    {
        try
        {
            var name = read(value);
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }
        catch (Exception)
        {
            // Names are diagnostic metadata. One broken/localization-sensitive asset never discards
            // an otherwise identity-correct catalog or blocks world publication.
            return string.Empty;
        }
    }

    private static string ReadDisplayName(object value) =>
        value is TooltipableObject tooltip ? tooltip.GetName() ?? string.Empty : string.Empty;

    private static string ReadAssetName(object value) =>
        value is UnityEngine.Object asset ? asset.name ?? string.Empty : string.Empty;

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is InvalidOperationException or
        ArgumentException or
        NotSupportedException or
        TargetException or
        TargetInvocationException or
        TargetParameterCountException or
        MethodAccessException or
        FieldAccessException or
        MissingMemberException or
        TypeLoadException;
}
