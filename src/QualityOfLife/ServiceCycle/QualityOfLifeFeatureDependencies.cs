using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class QualityOfLifeFeatureDependencies
{
    internal QualityOfLifeFeatureDependencies(TypedRegistryResolver registry, Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily, Func<bool> tryPermit, Func<string> ownershipFailure,
        AutomataFeatureStatusReporter status, Action<string> warn)
    {
        Registry = registry;
        ReadLifecycleEpoch = readLifecycleEpoch;
        OwnsActionFamily = ownsActionFamily;
        TryPermit = tryPermit;
        OwnershipFailure = ownershipFailure;
        Status = status;
        Warn = warn;
    }
    internal TypedRegistryResolver Registry { get; }
    internal Func<long> ReadLifecycleEpoch { get; }
    internal Func<bool> OwnsActionFamily { get; }
    internal Func<bool> TryPermit { get; }
    internal Func<string> OwnershipFailure { get; }
    internal AutomataFeatureStatusReporter Status { get; }
    internal Action<string> Warn { get; }
}
