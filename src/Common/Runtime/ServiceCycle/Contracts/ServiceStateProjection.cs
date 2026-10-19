using System;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public readonly struct ServiceProjectionKey : IEquatable<ServiceProjectionKey>
{
    public ServiceProjectionKey(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public int Value { get; }
    public bool IsValid => Value > 0;
    public bool Equals(ServiceProjectionKey other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ServiceProjectionKey other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(ServiceProjectionKey left, ServiceProjectionKey right) => left.Equals(right);
    public static bool operator !=(ServiceProjectionKey left, ServiceProjectionKey right) => !left.Equals(right);
}

public enum ServiceProjectionValueKind
{
    Boolean = 1,
    Integer = 2,
    FloatingPoint = 3,
}

public readonly struct ServiceProjectionValue
{
    private ServiceProjectionValue(ServiceProjectionValueKind kind, long integer, double floatingPoint)
    {
        Kind = kind;
        Integer = integer;
        FloatingPoint = floatingPoint;
    }

    public ServiceProjectionValueKind Kind { get; }
    public long Integer { get; }
    public double FloatingPoint { get; }
    public bool Boolean => Integer != 0;
    public bool IsValid => Kind is ServiceProjectionValueKind.Boolean or
        ServiceProjectionValueKind.Integer or
        ServiceProjectionValueKind.FloatingPoint;

    public static ServiceProjectionValue FromBoolean(bool value) =>
        new(ServiceProjectionValueKind.Boolean, value ? 1 : 0, default);
    public static ServiceProjectionValue FromInteger(long value) =>
        new(ServiceProjectionValueKind.Integer, value, default);
    public static ServiceProjectionValue FromFloatingPoint(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        return new ServiceProjectionValue(ServiceProjectionValueKind.FloatingPoint, default, value);
    }
}

internal sealed class ServiceStateProjectionWriteBuffer
{
    internal const int MaximumCapacity = 16;
    private readonly ServiceProjectionKey[] _keys;
    private readonly ServiceProjectionValue[] _values;

    internal ServiceStateProjectionWriteBuffer(int capacity)
    {
        if (capacity <= 0 || capacity > MaximumCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        _keys = new ServiceProjectionKey[capacity];
        _values = new ServiceProjectionValue[capacity];
    }

    internal int Count { get; private set; }
    internal int Capacity => _keys.Length;

    internal void Add(ServiceProjectionKey key, ServiceProjectionValue value)
    {
        for (var index = 0; index < Count; index++)
        {
            if (_keys[index] == key)
                throw new InvalidOperationException(
                    $"State projection key '{key.Value}' was added more than once.");
        }
        if (Count == Capacity)
            throw new InvalidOperationException("The bounded state projection capacity is exhausted.");
        _keys[Count] = key;
        _values[Count] = value;
        Count++;
    }

    internal void Reset() => Count = 0;

    internal ServiceStateProjectionSnapshot CreateSnapshot()
        => ServiceStateProjectionSnapshot.CopyFrom(_keys, _values, Count);
}

/// <summary>A non-escapable bounded Common-owned semantic projection writer.</summary>
public readonly ref struct ServiceStateProjectionBuilder
{
    private readonly ServiceStateProjectionWriteBuffer _buffer;

    internal ServiceStateProjectionBuilder(ServiceStateProjectionWriteBuffer buffer) =>
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

    public int Count => _buffer.Count;
    public int Capacity => _buffer.Capacity;
    public void Add(ServiceProjectionKey key, ServiceProjectionValue value)
    {
        if (!key.IsValid) throw new ArgumentException("A stable projection key is required.", nameof(key));
        if (!value.IsValid) throw new ArgumentException("A valid projection value is required.", nameof(value));
        _buffer.Add(key, value);
    }

    internal ServiceStateProjectionSnapshot CaptureSnapshot() => _buffer.CreateSnapshot();
}

public readonly struct ServiceProjectionEntry
{
    internal ServiceProjectionEntry(ServiceProjectionKey key, ServiceProjectionValue value)
    {
        Key = key;
        Value = value;
    }

    public ServiceProjectionKey Key { get; }
    public ServiceProjectionValue Value { get; }
}

/// <summary>
/// A bounded value snapshot. The fixed fields deliberately avoid publishing an array that the worker
/// could later reuse or mutate. This is copied across the handoff as an immutable value.
/// </summary>
public readonly struct ServiceStateProjectionSnapshot
{
    public const int MaximumEntryCount = ServiceStateProjectionWriteBuffer.MaximumCapacity;

    private readonly ServiceProjectionEntry _e0;
    private readonly ServiceProjectionEntry _e1;
    private readonly ServiceProjectionEntry _e2;
    private readonly ServiceProjectionEntry _e3;
    private readonly ServiceProjectionEntry _e4;
    private readonly ServiceProjectionEntry _e5;
    private readonly ServiceProjectionEntry _e6;
    private readonly ServiceProjectionEntry _e7;
    private readonly ServiceProjectionEntry _e8;
    private readonly ServiceProjectionEntry _e9;
    private readonly ServiceProjectionEntry _e10;
    private readonly ServiceProjectionEntry _e11;
    private readonly ServiceProjectionEntry _e12;
    private readonly ServiceProjectionEntry _e13;
    private readonly ServiceProjectionEntry _e14;
    private readonly ServiceProjectionEntry _e15;

    private ServiceStateProjectionSnapshot(
        ServiceProjectionKey[] keys,
        ServiceProjectionValue[] values,
        int count)
    {
        Count = count;
        _e0 = EntryAt(keys, values, count, 0);
        _e1 = EntryAt(keys, values, count, 1);
        _e2 = EntryAt(keys, values, count, 2);
        _e3 = EntryAt(keys, values, count, 3);
        _e4 = EntryAt(keys, values, count, 4);
        _e5 = EntryAt(keys, values, count, 5);
        _e6 = EntryAt(keys, values, count, 6);
        _e7 = EntryAt(keys, values, count, 7);
        _e8 = EntryAt(keys, values, count, 8);
        _e9 = EntryAt(keys, values, count, 9);
        _e10 = EntryAt(keys, values, count, 10);
        _e11 = EntryAt(keys, values, count, 11);
        _e12 = EntryAt(keys, values, count, 12);
        _e13 = EntryAt(keys, values, count, 13);
        _e14 = EntryAt(keys, values, count, 14);
        _e15 = EntryAt(keys, values, count, 15);
    }

    public int Count { get; }

    public ServiceProjectionEntry GetEntry(int index) => index switch
    {
        0 when index < Count => _e0,
        1 when index < Count => _e1,
        2 when index < Count => _e2,
        3 when index < Count => _e3,
        4 when index < Count => _e4,
        5 when index < Count => _e5,
        6 when index < Count => _e6,
        7 when index < Count => _e7,
        8 when index < Count => _e8,
        9 when index < Count => _e9,
        10 when index < Count => _e10,
        11 when index < Count => _e11,
        12 when index < Count => _e12,
        13 when index < Count => _e13,
        14 when index < Count => _e14,
        15 when index < Count => _e15,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    internal static ServiceStateProjectionSnapshot CopyFrom(
        ServiceProjectionKey[] keys,
        ServiceProjectionValue[] values,
        int count)
    {
        if (keys is null) throw new ArgumentNullException(nameof(keys));
        if (values is null) throw new ArgumentNullException(nameof(values));
        if (keys.Length != MaximumEntryCount || values.Length != MaximumEntryCount)
            throw new ArgumentException("The transient projection has the wrong shape.");
        if ((uint)count > MaximumEntryCount) throw new ArgumentOutOfRangeException(nameof(count));
        return new ServiceStateProjectionSnapshot(keys, values, count);
    }

    private static ServiceProjectionEntry EntryAt(
        ServiceProjectionKey[] keys,
        ServiceProjectionValue[] values,
        int count,
        int index) => index < count
            ? new ServiceProjectionEntry(keys[index], values[index])
            : default;
}

public readonly struct ServiceProjectionContext
{
    public ServiceProjectionContext(
        ServiceCycleIdentity cycle,
        StatePublicationId publication,
        MonotonicTimestamp projectedAt)
    {
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        if (!publication.IsValid) throw new ArgumentException("A valid publication identity is required.", nameof(publication));
        Cycle = cycle;
        Publication = publication;
        ProjectedAt = projectedAt;
    }

    public ServiceCycleIdentity Cycle { get; }
    public StatePublicationId Publication { get; }
    public MonotonicTimestamp ProjectedAt { get; }
}

public readonly struct ServiceFaultRecoveryPolicy
{
    public ServiceFaultRecoveryPolicy(
        MonotonicDuration initialBackoff,
        MonotonicDuration maximumBackoff)
    {
        if (initialBackoff.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(initialBackoff));
        if (maximumBackoff < initialBackoff) throw new ArgumentOutOfRangeException(nameof(maximumBackoff));
        InitialBackoff = initialBackoff;
        MaximumBackoff = maximumBackoff;
    }

    public MonotonicDuration InitialBackoff { get; }
    public MonotonicDuration MaximumBackoff { get; }
    public bool IsValid =>
        InitialBackoff.Ticks > 0 &&
        MaximumBackoff >= InitialBackoff;

    /// <summary>The wait after this many consecutive failures: the initial backoff, doubled per failure.</summary>
    public MonotonicDuration BackoffAfter(int consecutiveFailures) =>
        BackoffAfter(consecutiveFailures, MaximumBackoff);

    /// <summary>
    /// A caller's own first wait and cap may sit above the policy's, never below its first wait, and
    /// the cap, the policy maximum when absent, never below the first wait.
    /// </summary>
    internal bool Allows(MonotonicDuration? initial, MonotonicDuration? maximum)
    {
        var first = initial ?? InitialBackoff;
        return first >= InitialBackoff && (maximum ?? MaximumBackoff) >= first;
    }

    /// <summary>The same doubling, held at a cap of the caller's own instead of the policy's.</summary>
    public MonotonicDuration BackoffAfter(int consecutiveFailures, MonotonicDuration maximum) =>
        BackoffAfter(consecutiveFailures, InitialBackoff, maximum);

    /// <summary>The same doubling from a first wait of the caller's own.</summary>
    internal MonotonicDuration BackoffAfter(
        int consecutiveFailures,
        MonotonicDuration initial,
        MonotonicDuration maximum)
    {
        if (consecutiveFailures < 1) throw new ArgumentOutOfRangeException(nameof(consecutiveFailures));
        if (!Allows(initial, maximum)) throw new ArgumentOutOfRangeException(nameof(maximum));
        var ticks = initial.Ticks;
        for (var index = 1; index < consecutiveFailures && ticks < maximum.Ticks; index++)
        {
            if (ticks > maximum.Ticks / 2)
                return maximum;
            ticks *= 2;
        }
        return new MonotonicDuration(Math.Min(ticks, maximum.Ticks));
    }
}
