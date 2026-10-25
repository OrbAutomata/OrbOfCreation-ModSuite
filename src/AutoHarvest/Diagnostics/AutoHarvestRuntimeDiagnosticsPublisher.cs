using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Maps feature-owned pair evidence into the gameplay-neutral diagnostics port.
/// Native objects, UUIDs, reflection details, and the causal ring remain inside
/// Auto Harvest.
/// </summary>
internal sealed class AutoHarvestRuntimeDiagnosticsPublisher : IDisposable
{
    internal const string FruitCapabilityId = "FruitTrees";
    internal const string TreasureCapabilityId = "TreasureTrees";
    private RuntimeDiagnosticsRegistration? _registration;
    private RuntimeServiceDiagnosticsSnapshot _snapshot;
    private readonly string _implementationName;
    private long _lifecycleGeneration;
    private AutoHarvestPairHealth _fruit;
    private AutoHarvestPairHealth _treasure;

    public AutoHarvestRuntimeDiagnosticsPublisher(
        long lifecycleGeneration,
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure,
        string implementationName,
        RuntimeDiagnosticsRegistry registry)
    {
        if (lifecycleGeneration < 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (string.IsNullOrWhiteSpace(implementationName))
            throw new ArgumentException("An implementation name is required.", nameof(implementationName));
        _implementationName = implementationName;
        _lifecycleGeneration = lifecycleGeneration;
        _fruit = fruit;
        _treasure = treasure;
        _snapshot = BuildSnapshot(fruit, treasure);
        _registration = (registry ?? throw new ArgumentNullException(nameof(registry))).Register(_snapshot);
    }

    public void PublishState(
        long lifecycleGeneration,
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure)
    {
        if (lifecycleGeneration < 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        var lifecycleChanged = lifecycleGeneration != _lifecycleGeneration;
        if (!lifecycleChanged && SameHealth(_fruit, fruit) && SameHealth(_treasure, treasure)) return;
        if (lifecycleChanged)
        {
            _lifecycleGeneration = lifecycleGeneration;
        }
        _fruit = fruit;
        _treasure = treasure;
        _snapshot = BuildSnapshot(fruit, treasure);
        Registration.Update(_snapshot);
    }

    public void Dispose()
    {
        var registration = _registration;
        if (registration is null) return;
        _registration = null;
        registration.Dispose();
    }

    private RuntimeDiagnosticsRegistration Registration =>
        _registration ?? throw new ObjectDisposedException(nameof(AutoHarvestRuntimeDiagnosticsPublisher));

    private RuntimeServiceDiagnosticsSnapshot BuildSnapshot(
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure)
    {
        var effectiveFruit = fruit;
        var effectiveTreasure = treasure;
        if (TryGetFeatureScopedFailure(fruit, treasure, out var serviceFailure))
        {
            if (fruit.Selected) effectiveFruit = serviceFailureFor(AutoHarvestPair.FruitTree);
            if (treasure.Selected) effectiveTreasure = serviceFailureFor(AutoHarvestPair.TreasureTree);
        }

        return new RuntimeServiceDiagnosticsSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutoHarvestFeatureContribution.FeatureId),
            "Auto Harvest",
            _implementationName,
            _lifecycleGeneration,
            new[]
            {
                ProjectCapability(FruitCapabilityId, "Fruit trees", effectiveFruit),
                ProjectCapability(TreasureCapabilityId, "Treasure trees", effectiveTreasure),
            });

        AutoHarvestPairHealth serviceFailureFor(AutoHarvestPair pair) => new(
            pair,
            selected: true,
            serviceFailure.Kind,
            featureScoped: true);
    }

    private static bool TryGetFeatureScopedFailure(
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure,
        out AutoHarvestPairHealth failure)
    {
        if (IsFeatureScopedFailure(fruit))
        {
            failure = fruit;
            return true;
        }
        if (IsFeatureScopedFailure(treasure))
        {
            failure = treasure;
            return true;
        }
        failure = default;
        return false;
    }

    private static bool IsFeatureScopedFailure(in AutoHarvestPairHealth health) =>
        health.Selected && health.FeatureScoped && health.Kind is
            AutoHarvestPairHealthKind.RegistryNotReady or
            AutoHarvestPairHealthKind.ContractUnavailable;

    private static bool SameHealth(in AutoHarvestPairHealth left, in AutoHarvestPairHealth right) =>
        left.Pair == right.Pair &&
        left.Selected == right.Selected &&
        left.Kind == right.Kind &&
        left.FeatureScoped == right.FeatureScoped;

    private static RuntimeCapabilityDiagnostics ProjectCapability(
        string capabilityId,
        string displayName,
        in AutoHarvestPairHealth health)
    {
        if (!health.Selected)
        {
            return new RuntimeCapabilityDiagnostics(
                capabilityId,
                displayName,
                configuredEnabled: false,
                FeatureStatusState.ConfigurationDisabled,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.ConfigurationDisabled,
                    displayName + " collection is disabled by configuration."));
        }

        var (state, reason, summary) = health.Kind switch
        {
            AutoHarvestPairHealthKind.Eligible =>
                (FeatureStatusState.Operational, FeatureStatusReasonCode.None, string.Empty),
            AutoHarvestPairHealthKind.ProgressionLocked =>
                (FeatureStatusState.Locked, FeatureStatusReasonCode.ProgressionLocked,
                    "This harvest action's prerequisites have not been met."),
            AutoHarvestPairHealthKind.PlotNotVisible =>
                (FeatureStatusState.Locked, FeatureStatusReasonCode.ProgressionLocked,
                    "The plot this harvest belongs to is not visible yet."),
            // Not locked either, and for a sharper reason than the one below it: this is not a state
            // of the world, it is the absence of an answer about one. The game sets the latch when it
            // happens to check, so saying "you have not met these" would be telling the player about
            // progression on the strength of a field that has never been written.
            AutoHarvestPairHealthKind.PrerequisitesNotConfirmed =>
                (FeatureStatusState.NotReady, FeatureStatusReasonCode.GameplayNotReady,
                    "The game has not confirmed this harvest action's prerequisites yet."),
            // Not locked. The plot is reachable and its prerequisites are met; it is simply not
            // offering the action this minute, which is what a harvested-out tree looks like.
            AutoHarvestPairHealthKind.ActionNotOffered =>
                (FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.NativeBusy,
                    "The plot is unlocked but is not offering this harvest action right now."),
            AutoHarvestPairHealthKind.NativeBusy =>
                (FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.NativeBusy,
                    "The harvest action is unlocked but not currently ready."),
            AutoHarvestPairHealthKind.QueueBlocked =>
                (FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.QueueFull,
                    "The native plot-action list has no free action entry."),
            AutoHarvestPairHealthKind.ContractUnavailable =>
                (FeatureStatusState.ContractUnavailable, FeatureStatusReasonCode.ContractUnavailable,
                    health.FeatureScoped
                        ? "The audited native contract is unavailable for the Auto Harvest service."
                        : "The audited native contract is unavailable for this capability."),
            AutoHarvestPairHealthKind.RegistryNotReady =>
                (FeatureStatusState.NotReady, FeatureStatusReasonCode.RegistryNotReady,
                    health.FeatureScoped
                        ? "The Auto Harvest service is waiting for authoritative native registry evidence."
                        : "This capability is waiting for authoritative native registry evidence."),
            _ => (FeatureStatusState.NotReady, FeatureStatusReasonCode.Initializing,
                "This capability has not completed its first bounded evaluation."),
        };
        return new RuntimeCapabilityDiagnostics(
            capabilityId,
            displayName,
            configuredEnabled: true,
            state,
            reason == FeatureStatusReasonCode.None ? default : new FeatureStatusReason(reason, summary));
    }
}
