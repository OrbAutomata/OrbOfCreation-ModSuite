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

        if (!TryResolve(
                specification.Plot.Uuid,
                _bindings!.PlotType,
                AutoHarvestRuntimeFailureScope.Pair,
                out var plotResolution,
                out failure) ||
            !TryResolve(
                specification.Action.Uuid,
                _bindings.ActionType,
                AutoHarvestRuntimeFailureScope.Pair,
                out var actionResolution,
                out failure) ||
            !TryResolve(
                specification.RewardPool.Uuid,
                _bindings.RewardPoolType,
                AutoHarvestRuntimeFailureScope.Pair,
                out var rewardResolution,
                out failure))
        {
            if (failure.Kind == AutoHarvestRuntimeFailureKind.Contract)
                _contractCircuit.Block(pair, AutoHarvestRuntimeFailureScope.Pair);
            return false;
        }

        var generation = _shared!.LifecycleGeneration;
        if (plotResolution.LifecycleGeneration != generation ||
            actionResolution.LifecycleGeneration != generation ||
            rewardResolution.LifecycleGeneration != generation)
        {
            failure = CrossedGenerations(AutoHarvestRuntimeFailureScope.Pair);
            return false;
        }

        current = new AutoHarvestPairBinding(
            pair,
            plotResolution.Value!,
            actionResolution.Value!,
            specification.PlotUuid,
            specification.ActionUuid,
            rewardResolution.Value!,
            plotResolution,
            actionResolution,
            rewardResolution);
        if (pair == AutoHarvestPair.FruitTree) _fruit = current;
        else _treasure = current;
        failure = default;
        return true;
    }
}
