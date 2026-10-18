using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed partial class AutoHarvestBindingResolver
{
    private bool TryResolveShared(out AutoHarvestNativeFailure failure)
    {
        if (_shared is not null)
        {
            if (_registryResolver.IsCurrent(_shared.ActiveResolution) &&
                _registryResolver.IsCurrent(_shared.ScalingResolution))
            {
                failure = default;
                return true;
            }
#if SERVICE_CYCLE_PROFILE
            _profileTemperature.ObserveUnexpectedDrift();
#endif
        }

        // The scaling weight is resolved for its identity's sake, not for the object: the pair's
        // completion effect is audited from the snapshot now, and what this proves is that the
        // weight the audit names still exists in this generation's registry.
        if (!TryResolve(
                KnownEntities.ActivePlotNodeActions.Uuid,
                _bindings!.ActiveActionsType,
                AutoHarvestRuntimeFailureScope.Feature,
                out var activeResolution,
                out failure) ||
            !TryResolve(
                KnownEntities.CompletionScalingWeight.Uuid,
                _bindings.ScalingWeightType,
                AutoHarvestRuntimeFailureScope.Feature,
                out var scalingResolution,
                out failure))
        {
            if (failure.Kind == AutoHarvestRuntimeFailureKind.Contract)
            {
                _contractCircuit.Block(
                    AutoHarvestPair.FruitTree,
                    AutoHarvestRuntimeFailureScope.Feature);
            }
            return false;
        }

        if (scalingResolution.LifecycleGeneration != activeResolution.LifecycleGeneration)
        {
            failure = CrossedGenerations(AutoHarvestRuntimeFailureScope.Feature);
            return false;
        }

        _shared = new AutoHarvestSharedBinding(
            activeResolution.Value!,
            activeResolution,
            scalingResolution,
            activeResolution.LifecycleGeneration);
        failure = default;
        return true;
    }

    private bool TryResolve(
        Guid uuid,
        Type type,
        AutoHarvestRuntimeFailureScope scope,
        out TypedRegistryResolution resolution,
        out AutoHarvestNativeFailure failure)
    {
        resolution = _registryResolver.Resolve(uuid, type);
        if (resolution.IsResolved)
        {
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddStableIdRead();
#endif
            failure = default;
            return true;
        }

        failure = AutoHarvestNativeFailure.Create(
            resolution.IsRetryable
                ? AutoHarvestRuntimeFailureKind.Retryable
                : AutoHarvestRuntimeFailureKind.Contract,
            scope,
            EntityIdentityFormatter.Format(uuid) + " registry identity is unavailable: " + resolution.Format());
        return false;
    }

    private static AutoHarvestNativeFailure CrossedGenerations(AutoHarvestRuntimeFailureScope scope) =>
        AutoHarvestNativeFailure.Create(
            AutoHarvestRuntimeFailureKind.Retryable,
            scope,
            "native Auto Harvest identities crossed lifecycle generations");
}
