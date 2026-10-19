using System;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Feature-scoped dependencies for the Auto Harvest ServiceCycle contribution. Only
/// the seams the feature itself owns live here — native registry resolution,
/// action-family ownership, mutation permitting, and its diagnostics outputs. The
/// suite-wide seams (frame identity, lifecycle epoch, pump timing, observability)
/// belong to <see cref="AutomataServiceCycleHostDependencies"/>.
/// </summary>
internal sealed class AutoHarvestFeatureDependencies
{
    public AutoHarvestFeatureDependencies(
        TypedRegistryResolver registryResolver,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit,
        RuntimeDiagnosticsRegistry? runtimeDiagnostics,
        AutomataFeatureStatusReporter featureStatus,
        Action<string>? warn = null)
    {
        RegistryResolver = registryResolver ?? throw new ArgumentNullException(nameof(registryResolver));
        OwnsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        TryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        RuntimeDiagnostics = runtimeDiagnostics;
        FeatureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        Warn = warn ?? LogWarning;
    }

    public TypedRegistryResolver RegistryResolver { get; }
    public Func<bool> OwnsActionFamily { get; }
    public Func<bool> TryCaptureMutationPermit { get; }
    public RuntimeDiagnosticsRegistry? RuntimeDiagnostics { get; }
    public AutomataFeatureStatusReporter FeatureStatus { get; }

    /// <summary>Where the feature's warnings go: the suite log unless a host names another.</summary>
    public Action<string> Warn { get; }

    internal static void LogWarning(string message) => Plugin.Log?.LogAutomataWarning(message);
}
