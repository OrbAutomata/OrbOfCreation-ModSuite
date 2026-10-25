using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Feature-scoped dependencies for the Auto Cast ServiceCycle contribution. Only the seams the
/// feature owns live here — the live native lifecycle epoch, its action-family lease, the manual
/// pause the toggle and the boundary share, and its feature-status output.
/// </summary>
internal sealed class AutoCastFeatureDependencies
{
    public AutoCastFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        AutoCastManualPauseState manualPause,
        AutomataFeatureStatusReporter featureStatus,
        Action<string>? warn = null)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        ManualPause = manualPause ?? throw new ArgumentNullException(nameof(manualPause));
        FeatureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        Warn = warn ?? (static _ => { });
    }

    public Func<long> ReadLifecycleEpoch { get; }
    public Func<bool> OwnsActionFamily { get; }
    public AutoCastManualPauseState ManualPause { get; }
    public AutomataFeatureStatusReporter FeatureStatus { get; }
    public Action<string> Warn { get; }
}
