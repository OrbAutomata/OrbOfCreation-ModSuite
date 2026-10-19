using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptFeatureDependencies
{
    public AutoConceptFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        AutomataFeatureStatusReporter featureStatus,
        Action<string> warn)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        FeatureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        Warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public Func<long> ReadLifecycleEpoch { get; }
    public Func<bool> OwnsActionFamily { get; }
    public AutomataFeatureStatusReporter FeatureStatus { get; }
    public Action<string> Warn { get; }
}
