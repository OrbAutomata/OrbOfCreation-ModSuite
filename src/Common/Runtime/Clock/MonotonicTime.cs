using System;

namespace OrbModding.Common.Runtime;

public readonly struct MonotonicTimestamp : IComparable<MonotonicTimestamp>, IEquatable<MonotonicTimestamp>
{
    /// <param name="ticks">TimeSpan ticks (100 nanoseconds) from an arbitrary monotonic origin.</param>
    public MonotonicTimestamp(long ticks)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        Ticks = ticks;
    }

    public long Ticks { get; }

    public int CompareTo(MonotonicTimestamp other) => Ticks.CompareTo(other.Ticks);
    public bool Equals(MonotonicTimestamp other) => Ticks == other.Ticks;
    public override bool Equals(object? obj) => obj is MonotonicTimestamp other && Equals(other);
    public override int GetHashCode() => Ticks.GetHashCode();
    public override string ToString() => Ticks.ToString();
    public static bool operator <(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks < right.Ticks;
    public static bool operator >(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks > right.Ticks;
    public static bool operator <=(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks <= right.Ticks;
    public static bool operator >=(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks >= right.Ticks;
    public static bool operator ==(MonotonicTimestamp left, MonotonicTimestamp right) => left.Equals(right);
    public static bool operator !=(MonotonicTimestamp left, MonotonicTimestamp right) => !left.Equals(right);
    public static MonotonicTimestamp operator +(MonotonicTimestamp timestamp, MonotonicDuration duration) =>
        new(checked(timestamp.Ticks + duration.Ticks));
    public static MonotonicTimestamp operator -(MonotonicTimestamp timestamp, MonotonicDuration duration) =>
        new(checked(timestamp.Ticks - duration.Ticks));

    /// <summary>This timestamp plus a duration, holding at the end of time instead of overflowing.</summary>
    public MonotonicTimestamp AddSaturated(MonotonicDuration duration) =>
        duration.Ticks > long.MaxValue - Ticks
            ? new MonotonicTimestamp(long.MaxValue)
            : new MonotonicTimestamp(Ticks + duration.Ticks);
    public static MonotonicDuration operator -(MonotonicTimestamp end, MonotonicTimestamp start)
    {
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end), "Elapsed monotonic time cannot be negative.");
        return new MonotonicDuration(checked(end.Ticks - start.Ticks));
    }
}

/// <summary>A non-negative duration measured in TimeSpan ticks (100 nanoseconds).</summary>
public readonly struct MonotonicDuration : IComparable<MonotonicDuration>, IEquatable<MonotonicDuration>
{
    public const long TicksPerSecond = TimeSpan.TicksPerSecond;
    public const int NanosecondsPerTick = 100;

    public MonotonicDuration(long ticks)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        Ticks = ticks;
    }

    public long Ticks { get; }
    public static MonotonicDuration Zero => default;
    public static MonotonicDuration FromTimeSpan(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        return new MonotonicDuration(duration.Ticks);
    }

    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(Ticks);
    public int CompareTo(MonotonicDuration other) => Ticks.CompareTo(other.Ticks);
    public bool Equals(MonotonicDuration other) => Ticks == other.Ticks;
    public override bool Equals(object? obj) => obj is MonotonicDuration other && Equals(other);
    public override int GetHashCode() => Ticks.GetHashCode();
    public override string ToString() => ToTimeSpan().ToString();
    public static bool operator <(MonotonicDuration left, MonotonicDuration right) => left.Ticks < right.Ticks;
    public static bool operator >(MonotonicDuration left, MonotonicDuration right) => left.Ticks > right.Ticks;
    public static bool operator <=(MonotonicDuration left, MonotonicDuration right) => left.Ticks <= right.Ticks;
    public static bool operator >=(MonotonicDuration left, MonotonicDuration right) => left.Ticks >= right.Ticks;
    public static bool operator ==(MonotonicDuration left, MonotonicDuration right) => left.Equals(right);
    public static bool operator !=(MonotonicDuration left, MonotonicDuration right) => !left.Equals(right);
    public static MonotonicDuration operator +(MonotonicDuration left, MonotonicDuration right) =>
        new(checked(left.Ticks + right.Ticks));
    public static MonotonicDuration operator -(MonotonicDuration left, MonotonicDuration right) =>
        new(checked(left.Ticks - right.Ticks));
}

public interface IMonotonicClock
{
    MonotonicTimestamp Now { get; }
}
