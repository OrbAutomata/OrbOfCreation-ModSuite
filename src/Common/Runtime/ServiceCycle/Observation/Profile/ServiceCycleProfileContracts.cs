#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal enum ServiceCycleProfileTemperature
{
    ColdProcess = 1,
    LifecycleRebind = 2,
    Warm = 3,
}

internal enum ServiceCycleProfileRecordKind
{
    Aggregate = 1,
    Sample = 2,
}

internal enum ServiceCycleProfileCompleteness : uint
{
    Complete = 1,
    Incomplete = 2,
}

internal enum ServiceCycleProfileTerminalReason : uint
{
    UserStopped = 1,
    RuntimeShutdown = 2,
    BufferExhausted = 3,
    SequenceExhausted = 4,
    WriteFailed = 5,
    ProbeFailed = 6,
}

internal readonly struct ServiceCycleProfileCalibration
{
    internal ServiceCycleProfileCalibration(
        long timestampFrequency,
        long rawTimestamp,
        long monotonicTimestampTicks,
        Guid buildId,
        bool traceActive,
        bool allocationAvailable)
    {
        if (timestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        if (monotonicTimestampTicks < 0) throw new ArgumentOutOfRangeException(nameof(monotonicTimestampTicks));
        if (buildId == Guid.Empty) throw new ArgumentException("A build identity is required.", nameof(buildId));
        TimestampFrequency = timestampFrequency;
        RawTimestamp = rawTimestamp;
        MonotonicTimestampTicks = monotonicTimestampTicks;
        BuildId = buildId;
        TraceActive = traceActive;
        AllocationAvailable = allocationAvailable;
    }

    internal long TimestampFrequency { get; }
    internal long RawTimestamp { get; }
    internal long MonotonicTimestampTicks { get; }
    internal Guid BuildId { get; }
    internal bool TraceActive { get; }
    internal bool AllocationAvailable { get; }
    internal bool IsValid => TimestampFrequency > 0 && MonotonicTimestampTicks >= 0 && BuildId != Guid.Empty;
}

internal readonly struct ServiceCycleProfileOperations : IEquatable<ServiceCycleProfileOperations>
{
    internal ServiceCycleProfileOperations(ulong stableIdReads, ulong listEntries)
    {
        StableIdReads = stableIdReads;
        ListEntries = listEntries;
    }

    internal ulong StableIdReads { get; }
    internal ulong ListEntries { get; }

    public bool Equals(ServiceCycleProfileOperations other) =>
        StableIdReads == other.StableIdReads &&
        ListEntries == other.ListEntries;

    public override bool Equals(object? obj) => obj is ServiceCycleProfileOperations other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(StableIdReads, ListEntries);
}
#endif
