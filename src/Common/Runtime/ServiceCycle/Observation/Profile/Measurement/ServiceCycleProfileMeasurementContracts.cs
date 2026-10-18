#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal enum ServiceCycleProfileMeasurementResult
{
    Accepted = 0,
    Faulted = 1,
}

internal enum ServiceCycleProfileProbeFault
{
    None = 0,
    ContextRejected = 1,
    MeasurementPortRejected = 2,
    MeasurementPortFailed = 3,
    StageOverlapRejected = 4,
}

internal enum ServiceCycleProfileMeasurementFault
{
    None = 0,
    OwnerThreadRejected = 1,
    TokenRejected = 2,
    RawClockFailed = 3,
    RawClockRegressed = 4,
    AllocationCounterFailed = 5,
    AllocationCounterRegressed = 6,
    MeasurementArithmeticExhausted = 8,
    AggregationFailed = 9,
    AggregatorSealed = 10,
    MeasurementDepthExhausted = 11,
    TokenSequenceExhausted = 12,
    ActiveMeasurementAtSeal = 13,
}

internal readonly struct ServiceCycleProfileMeasurementToken
{
    private readonly object? _owner;

    internal ServiceCycleProfileMeasurementToken(
        object owner,
        ulong sequence,
        in ServiceCycleProfileContext context,
        long startedAtRawTicks,
        long allocatedBytes)
    {
        _owner = owner;
        Sequence = sequence;
        Context = context;
        StartedAtRawTicks = startedAtRawTicks;
        AllocatedBytes = allocatedBytes;
    }

    internal ServiceCycleProfileContext Context { get; }
    internal ulong Sequence { get; }
    internal long StartedAtRawTicks { get; }
    internal long AllocatedBytes { get; }
    internal bool IsOwnedBy(object owner) => ReferenceEquals(_owner, owner) && Sequence != 0;
}
#endif
