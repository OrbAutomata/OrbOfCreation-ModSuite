using System;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>
/// The exact native target one retry episode belongs to: the UUID and native type its actions are
/// attributed to in the decision journal.
/// </summary>
public readonly struct ServiceRetryTarget : IEquatable<ServiceRetryTarget>
{
    public ServiceRetryTarget(Guid id, ServiceActionNativeTypeId nativeType)
    {
        if (id == Guid.Empty) throw new ArgumentException("A retry target needs its stable UUID.", nameof(id));
        if (nativeType is <= ServiceActionNativeTypeId.NotApplicable or > ServiceActionNativeTypeId.EquipmentSO)
            throw new ArgumentOutOfRangeException(nameof(nativeType));
        Id = id;
        NativeType = nativeType;
    }

    public Guid Id { get; }
    public ServiceActionNativeTypeId NativeType { get; }

    public static bool TryFrom(in ServiceActionJournalAttribution attribution, out ServiceRetryTarget target)
    {
        if (attribution.CandidateId == Guid.Empty)
        {
            target = default;
            return false;
        }
        target = new ServiceRetryTarget(attribution.CandidateId, attribution.NativeType);
        return true;
    }

    public bool Equals(ServiceRetryTarget other) => Id == other.Id && NativeType == other.NativeType;
    public override bool Equals(object? obj) => obj is ServiceRetryTarget other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode() * 31 + (int)NativeType;
    public static bool operator ==(ServiceRetryTarget left, ServiceRetryTarget right) => left.Equals(right);
    public static bool operator !=(ServiceRetryTarget left, ServiceRetryTarget right) => !left.Equals(right);
}

/// <summary>One target backing off: why it last failed, how many times in a row, and when it may try again.</summary>
public readonly struct ServiceRetryEntry
{
    internal ServiceRetryEntry(
        ServiceRetryTarget target,
        ServiceActionResultCode code,
        string reason,
        int consecutiveFailures,
        MonotonicTimestamp failedAt,
        MonotonicTimestamp retryDue,
        bool awaitsObservation)
    {
        Target = target;
        Code = code;
        Reason = reason;
        ConsecutiveFailures = consecutiveFailures;
        FailedAt = failedAt;
        RetryDue = retryDue;
        AwaitsObservation = awaitsObservation;
    }

    public ServiceRetryTarget Target { get; }
    public ServiceActionResultCode Code { get; }
    public string Reason { get; }
    public int ConsecutiveFailures { get; }
    public MonotonicTimestamp FailedAt { get; }
    public MonotonicTimestamp RetryDue { get; }
    public MonotonicDuration Backoff => RetryDue - FailedAt;

    /// <summary>
    /// The failure was seen in a later world rather than at the action, so only the evaluator ends it,
    /// when it sees the target work again or stops considering it; a commit alone does not.
    /// </summary>
    public bool AwaitsObservation { get; }
}

/// <summary>
/// An immutable copy of one service's retry gate. Planning reads it to skip a target until it is due;
/// diagnostics read it to say which target is backing off and why.
/// </summary>
public sealed class ServiceRetryGateSnapshot
{
    private readonly ServiceRetryEntry[] _entries;

    internal ServiceRetryGateSnapshot(ServiceRetryEntry[] entries) => _entries = entries;

    public static ServiceRetryGateSnapshot Empty { get; } = new(Array.Empty<ServiceRetryEntry>());

    public int Count => _entries.Length;
    public ServiceRetryEntry this[int index] => _entries[index];

    /// <summary>The entry whose failure was recorded last.</summary>
    public ServiceRetryEntry Latest =>
        _entries.Length == 0
            ? throw new InvalidOperationException("No target is backing off.")
            : _entries[_entries.Length - 1];

    public bool TryFind(ServiceRetryTarget target, out ServiceRetryEntry entry)
    {
        var index = IndexOf(target);
        entry = index < 0 ? default : _entries[index];
        return index >= 0;
    }

    public bool IsWaiting(ServiceRetryTarget target, MonotonicTimestamp now) =>
        TryFind(target, out var entry) && now < entry.RetryDue;

    internal int IndexOf(ServiceRetryTarget target)
    {
        for (var index = 0; index < _entries.Length; index++)
            if (_entries[index].Target == target)
                return index;
        return -1;
    }

    internal bool TryGetNextDueAfter(MonotonicTimestamp now, out MonotonicTimestamp due)
    {
        due = default;
        var found = false;
        for (var index = 0; index < _entries.Length; index++)
        {
            var candidate = _entries[index].RetryDue;
            if (candidate <= now || (found && candidate >= due)) continue;
            due = candidate;
            found = true;
        }
        return found;
    }
}
