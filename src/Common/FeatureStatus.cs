using System;
using System.Collections.Generic;
using System.Threading;

namespace OrbModding.Common;

public enum FeatureStatusState
{
    ConfigurationDisabled = 0,
    Locked = 1,
    NotReady = 2,
    Operational = 3,
    TemporarilyBlocked = 4,
    ContractUnavailable = 5,
    Degraded = 6,
    Faulted = 7,
}

public enum FeatureStatusReasonCode
{
    None = 0,
    ConfigurationDisabled = 100,
    ParentFeatureDisabled = 101,
    EmergencyDisabled = 102,
    ProgressionLocked = 200,
    GameplayNotReady = 300,
    RegistryNotReady = 301,
    LifecycleTransition = 302,
    QueueNotReady = 303,
    Initializing = 304,
    TemporarySafetyBlock = 400,
    QueueFull = 401,
    NativeBusy = 402,
    ManualPause = 403,
    TargetingInProgress = 404,
    CapacityExceeded = 405,
    ActionFamilyConflict = 407,
    ContractUnavailable = 500,
    ContractMismatch = 501,
    IdentityMismatch = 502,
    EvidenceUnavailable = 503,
    PartialCapabilityUnavailable = 600,
    NativeMutationFailed = 700,
    PostconditionFailed = 701,
    RuntimeFailure = 702,
    InvariantViolation = 703,
}

public enum FeatureStatusTransitionKind
{
    Added = 0,
    Changed = 1,
    Removed = 2,
}

public readonly struct FeatureStatusKey : IEquatable<FeatureStatusKey>
{
    public FeatureStatusKey(string pluginId, string featureId)
    {
        PluginId = Normalize(pluginId, nameof(pluginId));
        FeatureId = Normalize(featureId, nameof(featureId));
    }

    public string PluginId { get; }
    public string FeatureId { get; }

    public bool Equals(FeatureStatusKey other) =>
        string.Equals(PluginId, other.PluginId, StringComparison.Ordinal) &&
        string.Equals(FeatureId, other.FeatureId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FeatureStatusKey other && Equals(other);
    public override int GetHashCode() => unchecked(
        (StringComparer.Ordinal.GetHashCode(PluginId ?? string.Empty) * 397) ^
        StringComparer.Ordinal.GetHashCode(FeatureId ?? string.Empty));
    public override string ToString() => (PluginId ?? string.Empty) + "/" + (FeatureId ?? string.Empty);

    private static string Normalize(string value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("A stable status key is required.", parameterName);
        return normalized;
    }
}

public readonly struct FeatureStatusReason : IEquatable<FeatureStatusReason>
{
    public FeatureStatusReason(
        FeatureStatusReasonCode code,
        string summary)
    {
        if (!Enum.IsDefined(typeof(FeatureStatusReasonCode), code))
            throw new ArgumentOutOfRangeException(nameof(code));
        var normalizedSummary = (summary ?? string.Empty).Trim();
        if (code == FeatureStatusReasonCode.None && normalizedSummary.Length > 0)
            throw new ArgumentException("An operational status without a reason cannot carry a reason summary.", nameof(summary));
        if (code != FeatureStatusReasonCode.None && normalizedSummary.Length == 0)
            throw new ArgumentException("A non-empty reason summary is required.", nameof(summary));
        Code = code;
        Summary = normalizedSummary;
    }

    public FeatureStatusReasonCode Code { get; }
    public string Summary { get; }
    public bool IsEmpty => Code == FeatureStatusReasonCode.None;

    public bool Equals(FeatureStatusReason other) =>
        Code == other.Code &&
        string.Equals(Summary, other.Summary, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is FeatureStatusReason other && Equals(other);
    public override int GetHashCode() => unchecked(
        ((int)Code * 397) ^ StringComparer.Ordinal.GetHashCode(Summary ?? string.Empty));
}

public readonly struct FeatureStatusSnapshot : IEquatable<FeatureStatusSnapshot>
{
    public FeatureStatusSnapshot(
        FeatureStatusKey key,
        string displayName,
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReason reason = default,
        long lifecycleGeneration = 0,
        string detail = "")
    {
        if (string.IsNullOrWhiteSpace(key.PluginId) || string.IsNullOrWhiteSpace(key.FeatureId))
            throw new ArgumentException("An initialized feature key is required.", nameof(key));
        var normalizedDisplayName = (displayName ?? string.Empty).Trim();
        if (normalizedDisplayName.Length == 0) throw new ArgumentException("A feature display name is required.", nameof(displayName));
        if (!Enum.IsDefined(typeof(FeatureStatusState), state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (lifecycleGeneration < 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (state == FeatureStatusState.ConfigurationDisabled && configuredEnabled)
            throw new ArgumentException("A configuration-disabled status cannot be configured enabled.", nameof(configuredEnabled));
        if (state != FeatureStatusState.ConfigurationDisabled && !configuredEnabled)
            throw new ArgumentException("Only configuration-disabled status can be configured disabled.", nameof(configuredEnabled));
        if (state == FeatureStatusState.Operational && !reason.IsEmpty)
            throw new ArgumentException("Operational status cannot carry a blocking reason.", nameof(reason));
        if (state != FeatureStatusState.Operational && reason.IsEmpty)
            throw new ArgumentException("A non-operational status requires a structured reason.", nameof(reason));

        Key = key;
        DisplayName = normalizedDisplayName;
        ConfiguredEnabled = configuredEnabled;
        State = state;
        Reason = reason;
        LifecycleGeneration = lifecycleGeneration;
        Detail = (detail ?? string.Empty).Trim();
    }

    public FeatureStatusKey Key { get; }
    public string DisplayName { get; }
    public bool ConfiguredEnabled { get; }
    public FeatureStatusState State { get; }
    public FeatureStatusReason Reason { get; }
    public long LifecycleGeneration { get; }
    /// <summary>Current explanatory context, independent of whether the feature is healthy or blocked.</summary>
    public string Detail { get; }
    public FeatureStatusConditionKey ConditionKey => new(this);

    public bool Equals(FeatureStatusSnapshot other) =>
        Key.Equals(other.Key) &&
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
        ConfiguredEnabled == other.ConfiguredEnabled && State == other.State &&
        Reason.Equals(other.Reason) && LifecycleGeneration == other.LifecycleGeneration &&
        string.Equals(Detail, other.Detail, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is FeatureStatusSnapshot other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Key.GetHashCode();
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(DisplayName ?? string.Empty);
            hash = (hash * 397) ^ ConfiguredEnabled.GetHashCode();
            hash = (hash * 397) ^ (int)State;
            hash = (hash * 397) ^ Reason.GetHashCode();
            hash = (hash * 397) ^ LifecycleGeneration.GetHashCode();
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Detail ?? string.Empty);
            return hash;
        }
    }
}

public readonly struct FeatureStatusConditionKey : IEquatable<FeatureStatusConditionKey>
{
    private readonly FeatureStatusSnapshot _status;
    internal FeatureStatusConditionKey(FeatureStatusSnapshot status) => _status = status;
    public bool Equals(FeatureStatusConditionKey other) =>
        _status.Key.Equals(other._status.Key) &&
        _status.ConfiguredEnabled == other._status.ConfiguredEnabled &&
        _status.State == other._status.State &&
        _status.Reason.Code == other._status.Reason.Code &&
        _status.LifecycleGeneration == other._status.LifecycleGeneration;
    public override bool Equals(object? obj) => obj is FeatureStatusConditionKey other && Equals(other);
    public override int GetHashCode() => unchecked(
        ((((_status.Key.GetHashCode() * 397) ^ _status.ConfiguredEnabled.GetHashCode()) * 397 ^
            (int)_status.State) * 397 ^ (int)_status.Reason.Code) * 397 ^
        _status.LifecycleGeneration.GetHashCode());
}

public readonly struct FeatureStatusTransition
{
    internal FeatureStatusTransition(
        FeatureStatusTransitionKind kind,
        FeatureStatusSnapshot? previous,
        FeatureStatusSnapshot? current,
        long sequence)
    {
        Kind = kind;
        Previous = previous;
        Current = current;
        Sequence = sequence;
    }

    public FeatureStatusTransitionKind Kind { get; }
    public FeatureStatusSnapshot? Previous { get; }
    public FeatureStatusSnapshot? Current { get; }
    public long Sequence { get; }
}

public interface IFeatureStatusSource
{
    event Action<FeatureStatusTransition>? Transitioned;
    /// <summary>Signals additions, removals and changed details; identical snapshots stay quiet.</summary>
    event Action? SnapshotChanged;
    IReadOnlyList<FeatureStatusSnapshot> GetSnapshot();
}

public sealed class FeatureStatusRegistry : IFeatureStatusSource
{
    private readonly int _ownerThreadId;
    private readonly Dictionary<FeatureStatusKey, FeatureStatusSnapshot> _statuses = new();
    private long _sequence;

    public FeatureStatusRegistry() => _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

    public static FeatureStatusRegistry Shared { get; } = new();

    public event Action<FeatureStatusTransition>? Transitioned;
    public event Action? SnapshotChanged;

    public FeatureStatusRegistration Register(FeatureStatusSnapshot initialStatus)
    {
        AssertOwnerThread();
        if (_statuses.ContainsKey(initialStatus.Key))
            throw new InvalidOperationException("A feature status publisher already owns " + initialStatus.Key + ".");
        _statuses.Add(initialStatus.Key, initialStatus);
        Publish(new FeatureStatusTransition(
            FeatureStatusTransitionKind.Added,
            null,
            initialStatus,
            checked(++_sequence)));
        return new FeatureStatusRegistration(this, initialStatus.Key);
    }

    public IReadOnlyList<FeatureStatusSnapshot> GetSnapshot()
    {
        AssertOwnerThread();
        var result = new List<FeatureStatusSnapshot>(_statuses.Values);
        result.Sort(StatusComparer.Instance);
        return result;
    }

    public bool TryGet(FeatureStatusKey key, out FeatureStatusSnapshot status)
    {
        AssertOwnerThread();
        return _statuses.TryGetValue(key, out status);
    }

    internal bool Update(FeatureStatusKey key, FeatureStatusSnapshot status)
    {
        AssertOwnerThread();
        if (!key.Equals(status.Key)) throw new ArgumentException("A registration cannot change its feature key.", nameof(status));
        if (!_statuses.TryGetValue(key, out var previous))
            throw new ObjectDisposedException(nameof(FeatureStatusRegistration));
        if (previous.Equals(status)) return false;
        _statuses[key] = status;
        // Detail text stays current even when the canonical condition has not transitioned.
        if (previous.ConditionKey.Equals(status.ConditionKey))
        {
            PublishSnapshotChanged();
            return false;
        }
        Publish(new FeatureStatusTransition(
            FeatureStatusTransitionKind.Changed,
            previous,
            status,
            checked(++_sequence)));
        return true;
    }

    internal void Remove(FeatureStatusKey key)
    {
        AssertOwnerThread();
        if (!_statuses.Remove(key, out var previous)) return;
        Publish(new FeatureStatusTransition(
            FeatureStatusTransitionKind.Removed,
            previous,
            null,
            checked(++_sequence)));
    }

    private void Publish(FeatureStatusTransition transition)
    {
        var handlers = Transitioned;
        if (handlers is not null)
        {
            foreach (Action<FeatureStatusTransition> handler in handlers.GetInvocationList())
            {
                try { handler(transition); }
                catch { }
            }
        }
        PublishSnapshotChanged();
    }

    private void PublishSnapshotChanged()
    {
        var handlers = SnapshotChanged;
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch { }
        }
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Feature status registry access must remain on its owning main thread.");
    }

    private sealed class StatusComparer : IComparer<FeatureStatusSnapshot>
    {
        public static readonly StatusComparer Instance = new();
        public int Compare(FeatureStatusSnapshot left, FeatureStatusSnapshot right)
        {
            var plugin = string.Compare(left.Key.PluginId, right.Key.PluginId, StringComparison.Ordinal);
            return plugin != 0
                ? plugin
                : string.Compare(left.Key.FeatureId, right.Key.FeatureId, StringComparison.Ordinal);
        }
    }
}

public sealed class FeatureStatusRegistration : IDisposable
{
    private FeatureStatusRegistry? _registry;
    private readonly FeatureStatusKey _key;

    internal FeatureStatusRegistration(FeatureStatusRegistry registry, FeatureStatusKey key)
    {
        _registry = registry;
        _key = key;
    }

    public bool Update(FeatureStatusSnapshot status)
    {
        var registry = _registry ?? throw new ObjectDisposedException(nameof(FeatureStatusRegistration));
        return registry.Update(_key, status);
    }

    public void Dispose()
    {
        var registry = _registry;
        if (registry is null) return;
        _registry = null;
        registry.Remove(_key);
    }
}

public enum FeatureConfiguredPresentationState
{
    Off = 0,
    On = 1,
}

public enum FeatureRuntimePresentationState
{
    Off = 0,
    Operational = 1,
    Waiting = 2,
    Blocked = 3,
    Degraded = 4,
    Unavailable = 5,
    Faulted = 6,
}

public readonly struct FeatureStatusPresentation : IEquatable<FeatureStatusPresentation>
{
    internal FeatureStatusPresentation(
        FeatureConfiguredPresentationState configuredState,
        FeatureRuntimePresentationState runtimeState,
        FeatureStatusReason reason)
    {
        ConfiguredState = configuredState;
        RuntimeState = runtimeState;
        Reason = reason;
    }

    public FeatureConfiguredPresentationState ConfiguredState { get; }
    public FeatureRuntimePresentationState RuntimeState { get; }
    public FeatureStatusReason Reason { get; }
    public bool IsConfiguredOn => ConfiguredState == FeatureConfiguredPresentationState.On;
    public string ConfiguredLabel => IsConfiguredOn ? "ON" : "OFF";
    public string RuntimeLabel => FeatureStatusPresenter.RuntimeLabel(RuntimeState);

    public bool Equals(FeatureStatusPresentation other) =>
        ConfiguredState == other.ConfiguredState &&
        RuntimeState == other.RuntimeState &&
        Reason.Equals(other.Reason);
    public override bool Equals(object? obj) => obj is FeatureStatusPresentation other && Equals(other);
    public override int GetHashCode() => unchecked(
        (((int)ConfiguredState * 397) ^ (int)RuntimeState) * 397 ^ Reason.GetHashCode());
}

public static class FeatureStatusPresenter
{
    public static FeatureStatusPresentation Present(in FeatureStatusSnapshot status) => new(
        status.ConfiguredEnabled
            ? FeatureConfiguredPresentationState.On
            : FeatureConfiguredPresentationState.Off,
        RuntimeState(status),
        status.Reason);

    public static string Format(in FeatureStatusSnapshot status)
        => string.Join("\n", FormatLines(status));

    public static IReadOnlyList<string> FormatLines(
        in FeatureStatusSnapshot status,
        int lineWidth = 80)
    {
        if (lineWidth < 24) throw new ArgumentOutOfRangeException(nameof(lineWidth));
        var presentation = Present(status);
        var configured = presentation.IsConfiguredOn ? "Enabled" : "Disabled";
        var lines = new List<string>(status.Reason.IsEmpty ? 2 : 4)
        {
            $"Configured: {configured}",
            $"Runtime: {presentation.RuntimeLabel}",
        };
        if (!status.Reason.IsEmpty)
            AppendWrappedLines(lines, "Reason: ", status.Reason.Summary, lineWidth, 320);
        AppendDetailLines(lines, "Details: ", status.Detail, lineWidth);
        return lines;
    }

    public static IReadOnlyList<string> FormatCompactLines(
        string label,
        in FeatureStatusSnapshot status,
        int lineWidth = 80)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A feature label is required.", nameof(label));
        if (lineWidth < 24) throw new ArgumentOutOfRangeException(nameof(lineWidth));
        var presentation = Present(status);
        var configured = presentation.IsConfiguredOn ? "Enabled" : "Disabled";
        var lines = new List<string>(status.Reason.IsEmpty ? 1 : 3)
        {
            $"{label}: {configured} | {presentation.RuntimeLabel}",
        };
        if (!status.Reason.IsEmpty)
            AppendWrappedLines(lines, $"{label} reason: ", status.Reason.Summary, lineWidth, 320);
        AppendDetailLines(lines, $"{label} details: ", status.Detail, lineWidth);
        return lines;
    }

    public static FeatureRuntimePresentationState RuntimeState(in FeatureStatusSnapshot status) => status.State switch
    {
        FeatureStatusState.ConfigurationDisabled => FeatureRuntimePresentationState.Off,
        FeatureStatusState.Operational => FeatureRuntimePresentationState.Operational,
        FeatureStatusState.Locked or FeatureStatusState.NotReady => FeatureRuntimePresentationState.Waiting,
        FeatureStatusState.TemporarilyBlocked when status.Reason.Code is
            FeatureStatusReasonCode.QueueFull or
            FeatureStatusReasonCode.NativeBusy or
            FeatureStatusReasonCode.ManualPause or
            FeatureStatusReasonCode.TargetingInProgress => FeatureRuntimePresentationState.Waiting,
        FeatureStatusState.TemporarilyBlocked => FeatureRuntimePresentationState.Blocked,
        FeatureStatusState.ContractUnavailable => FeatureRuntimePresentationState.Unavailable,
        FeatureStatusState.Degraded => FeatureRuntimePresentationState.Degraded,
        FeatureStatusState.Faulted => FeatureRuntimePresentationState.Faulted,
        _ => FeatureRuntimePresentationState.Unavailable,
    };

    public static string RuntimeLabel(FeatureRuntimePresentationState state) => state switch
    {
        FeatureRuntimePresentationState.Off => "Off",
        FeatureRuntimePresentationState.Operational => "Operational",
        FeatureRuntimePresentationState.Waiting => "Waiting",
        FeatureRuntimePresentationState.Blocked => "Blocked",
        FeatureRuntimePresentationState.Degraded => "Degraded",
        FeatureRuntimePresentationState.Unavailable => "Unavailable",
        FeatureRuntimePresentationState.Faulted => "Faulted",
        _ => "Unavailable",
    };

    public static string Label(FeatureStatusState state) => state switch
    {
        FeatureStatusState.ConfigurationDisabled => "Off",
        FeatureStatusState.Locked => "Locked",
        FeatureStatusState.NotReady => "Not ready",
        FeatureStatusState.Operational => "Operational",
        FeatureStatusState.TemporarilyBlocked => "Temporarily blocked",
        FeatureStatusState.ContractUnavailable => "Contract unavailable",
        FeatureStatusState.Degraded => "Degraded",
        FeatureStatusState.Faulted => "Faulted",
        _ => "Unknown",
    };

    public static string BoundAndWrap(string? text, int maximumCharacters = 320, int lineWidth = 72)
    {
        if (maximumCharacters < 16) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        if (lineWidth < 16) throw new ArgumentOutOfRangeException(nameof(lineWidth));
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var builder = new System.Text.StringBuilder(Math.Min(maximumCharacters, text.Length));
        var lineLength = 0;
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            var separatorLength = builder.Length == 0 ? 0 : 1;
            if (builder.Length + separatorLength + word.Length > maximumCharacters)
            {
                if (builder.Length == 0)
                {
                    builder.Append(word, 0, maximumCharacters - 3);
                    builder.Append("...");
                    break;
                }
                if (builder.Length + 3 <= maximumCharacters) builder.Append("...");
                break;
            }

            if (lineLength > 0 && lineLength + 1 + word.Length > lineWidth)
            {
                builder.Append('\n');
                lineLength = 0;
            }
            else if (builder.Length > 0)
            {
                builder.Append(' ');
                lineLength++;
            }

            builder.Append(word);
            lineLength += word.Length;
        }
        return builder.ToString();
    }

    private static void AppendDetailLines(List<string> lines, string prefix, string? detail, int lineWidth)
    {
        if (string.IsNullOrWhiteSpace(detail)) return;
        // Each authored row is one explanation (for example, one dial). Preserve that structure
        // while still giving the native tooltip one physical line per node.
        foreach (var row in detail!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(row)) continue;
            AppendWrappedLines(lines, prefix, row, lineWidth, int.MaxValue);
            prefix = new string(' ', prefix.Length);
        }
    }

    private static void AppendWrappedLines(
        List<string> lines,
        string prefix,
        string? text,
        int lineWidth,
        int maximumCharacters)
    {
        var contentWidth = Math.Max(16, lineWidth - prefix.Length);
        var wrapped = BoundAndWrap(text, maximumCharacters, contentWidth);
        if (wrapped.Length == 0) return;
        var segments = wrapped.Split('\n');
        for (var index = 0; index < segments.Length; index++)
            lines.Add((index == 0 ? prefix : new string(' ', prefix.Length)) + segments[index]);
    }
}
