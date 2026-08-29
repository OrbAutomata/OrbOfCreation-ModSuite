using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed partial class AutoHarvestBindingResolver
{
    private bool TryResolvePair(
        AutoHarvestPair pair,
        in AutoHarvestNativeFailure blocked,
        out AutoHarvestNativeFailure failure)
    {
        var specification = AutoHarvestPairSpecification.For(pair);
        if (blocked.IsValid)
        {
            failure = blocked;
            return false;
        }

        var current = pair == AutoHarvestPair.FruitTree ? _fruit : _treasure;
        if (current is not null &&
            AutoHarvestBindingCoherence.IsCurrent(_registryResolver, _shared!, current))
        {
            failure = default;
            return true;
        }

#if SERVICE_CYCLE_PROFILE
        if (current is not null) _profileTemperature.ObserveUnexpectedDrift();
#endif
        if (pair == AutoHarvestPair.FruitTree) _fruit = null;
        else _treasure = null;

        try
        {
            var plot = Resolve(
                specification.Plot.Uuid,
                _types!.Plot,
                EntityIdentityFormatter.Format(specification.Plot.Uuid),
                out var plotResolution);
            var action = Resolve(
                specification.Action.Uuid,
                _types.Action,
                EntityIdentityFormatter.Format(specification.Action.Uuid),
                out var actionResolution);
            var reward = Resolve(
                specification.RewardPool.Uuid,
                _types.RewardPool,
                EntityIdentityFormatter.Format(specification.RewardPool.Uuid),
                out var rewardResolution);
            RequireCurrentGeneration(
                _shared!.LifecycleGeneration,
                plotResolution,
                actionResolution,
                rewardResolution);

            current = new AutoHarvestPairBinding(
                pair,
                plot,
                action,
                specification.PlotUuid,
                specification.ActionUuid,
                reward,
                plotResolution,
                actionResolution,
                rewardResolution);
            if (pair == AutoHarvestPair.FruitTree) _fruit = current;
            else _treasure = current;
            failure = default;
            return true;
        }
        catch (AutoHarvestRegistryNotReadyException)
        {
            failure = AutoHarvestNativeFailure.Create(
                AutoHarvestRuntimeFailureKind.Retryable,
                AutoHarvestRuntimeFailureScope.Pair);
            return false;
        }
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            var kind = AutoHarvestReflectionAccess.ClassifyExpectedFailure(ex);
            failure = AutoHarvestNativeFailure.Create(
                kind,
                AutoHarvestRuntimeFailureScope.Pair);
            if (kind == AutoHarvestRuntimeFailureKind.Contract)
                _contractCircuit.Block(pair, AutoHarvestRuntimeFailureScope.Pair);
            return false;
        }
    }

    private static void RequireCurrentGeneration(
        long expectedGeneration,
        TypedRegistryResolution first,
        TypedRegistryResolution second,
        TypedRegistryResolution third)
    {
        RequireCurrentGeneration(expectedGeneration, first);
        RequireCurrentGeneration(expectedGeneration, second);
        RequireCurrentGeneration(expectedGeneration, third);
    }
}
