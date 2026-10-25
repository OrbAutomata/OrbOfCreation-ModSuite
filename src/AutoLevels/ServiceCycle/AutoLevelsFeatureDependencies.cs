using System;
using OrbModding.Common;
namespace OrbAutomata;
internal sealed record AutoLevelsFeatureDependencies(TypedRegistryResolver Registry,
    Func<long> ReadLifecycleEpoch, Func<bool> OwnsActionFamily, Func<bool> TryPermit,
    Func<string> OwnershipFailure, AutomataFeatureStatusReporter Status, Action<string> Warn);
