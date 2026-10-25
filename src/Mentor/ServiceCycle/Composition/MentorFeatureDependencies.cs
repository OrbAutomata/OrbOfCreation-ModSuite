using System;
using OrbAutomata;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal sealed class MentorFeatureDependencies
{
    internal MentorFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<MasteryExperienceDomain, bool> captureMutationPermit,
        AutomataFeatureStatusReporter featureStatus,
        Action<string>? warn = null)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ??
                             throw new ArgumentNullException(nameof(readLifecycleEpoch));
        CaptureMutationPermit = captureMutationPermit ??
                                throw new ArgumentNullException(nameof(captureMutationPermit));
        FeatureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        Warn = warn ?? (static _ => { });
    }

    internal Func<long> ReadLifecycleEpoch { get; }
    internal Func<MasteryExperienceDomain, bool> CaptureMutationPermit { get; }
    internal AutomataFeatureStatusReporter FeatureStatus { get; }
    internal Action<string> Warn { get; }
}
