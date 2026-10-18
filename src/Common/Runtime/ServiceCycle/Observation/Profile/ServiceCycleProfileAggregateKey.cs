#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal readonly struct ServiceCycleProfileAggregateKey : IEquatable<ServiceCycleProfileAggregateKey>
{
    internal ServiceCycleProfileAggregateKey(in ServiceCycleProfileMeasurement measurement)
    {
        var context = measurement.Context;
        StageCode = context.StageCode;
        ServiceOrdinal = context.ServiceOrdinal;
        Lifecycle = context.Lifecycle;
        Temperature = context.Temperature;
        Operations = measurement.Operations;
    }

    internal int StageCode { get; }
    internal int ServiceOrdinal { get; }
    internal ulong Lifecycle { get; }
    internal ServiceCycleProfileTemperature Temperature { get; }
    internal ServiceCycleProfileOperations Operations { get; }

    public bool Equals(ServiceCycleProfileAggregateKey other) =>
        StageCode == other.StageCode &&
        ServiceOrdinal == other.ServiceOrdinal &&
        Lifecycle == other.Lifecycle &&
        Temperature == other.Temperature &&
        Operations.Equals(other.Operations);

    internal ulong StableHash()
    {
        var hash = 14695981039346656037ul;
        Mix(ref hash, checked((uint)StageCode));
        Mix(ref hash, checked((uint)ServiceOrdinal));
        Mix(ref hash, unchecked((uint)Lifecycle));
        Mix(ref hash, unchecked((uint)(Lifecycle >> 32)));
        Mix(ref hash, checked((uint)Temperature));
        Mix(ref hash, unchecked((uint)Operations.StableIdReads));
        Mix(ref hash, unchecked((uint)(Operations.StableIdReads >> 32)));
        Mix(ref hash, unchecked((uint)Operations.ListEntries));
        Mix(ref hash, unchecked((uint)(Operations.ListEntries >> 32)));
        return hash;
    }

    private static void Mix(ref ulong hash, uint value)
    {
        hash ^= value;
        hash = unchecked(hash * 1099511628211ul);
    }
}
#endif
