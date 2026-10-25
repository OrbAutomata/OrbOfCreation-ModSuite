using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Feature-scoped dependencies for the Auto Buy ServiceCycle contribution. Only the seams the
/// feature owns live here — the live native lifecycle epoch, the candidate-kind ownership this
/// instance is responsible for, and its diagnostics outputs. The
/// suite-wide seams (frame identity, pump timing, observability) belong to
/// <see cref="AutomataServiceCycleHostDependencies"/>.
/// </summary>
internal sealed class AutoBuyFeatureDependencies
{
    public AutoBuyFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<AutoBuyCandidateKinds> ownershipMask,
        RuntimeDiagnosticsRegistry? runtimeDiagnostics,
        AutomataFeatureStatusReporter featureStatus,
        IAutoBuyRefusalBundlePort refusalBundles,
        Action<string> reportInvariant,
        Action<string> warn
#if SERVICE_CYCLE_PROFILE
        , Func<AutoBuyCandidateKind, bool>? gameMcpOwnership = null
#endif
        , Action<string>? info = null)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnershipMask = ownershipMask ?? throw new ArgumentNullException(nameof(ownershipMask));
        RuntimeDiagnostics = runtimeDiagnostics;
        FeatureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        RefusalBundles = refusalBundles ?? throw new ArgumentNullException(nameof(refusalBundles));
        ReportInvariant = reportInvariant ?? throw new ArgumentNullException(nameof(reportInvariant));
        Warn = warn ?? throw new ArgumentNullException(nameof(warn));
        Info = info ?? (static _ => { });
#if SERVICE_CYCLE_PROFILE
        GameMcpOwnership = gameMcpOwnership ?? (_ => false);
#endif
    }

    public Func<long> ReadLifecycleEpoch { get; }
    public Func<AutoBuyCandidateKinds> OwnershipMask { get; }

    public RuntimeDiagnosticsRegistry? RuntimeDiagnostics { get; }
    public AutomataFeatureStatusReporter FeatureStatus { get; }
    public IAutoBuyRefusalBundlePort RefusalBundles { get; }

    /// <summary>Where the refusal the suite's model says cannot happen is said, at error.</summary>
    public Action<string> ReportInvariant { get; }

    /// <summary>Where the feature's warnings are said: retry episodes and purchase-view blocks.</summary>
    public Action<string> Warn { get; }

    /// <summary>Where topology publications and recovery messages go; omitted by silent hosts.</summary>
    public Action<string> Info { get; }
#if SERVICE_CYCLE_PROFILE
    internal Func<AutoBuyCandidateKind, bool> GameMcpOwnership { get; }
#endif
}
