using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoHarvestSharedBinding
{
    public AutoHarvestSharedBinding(
        object activeActions,
        TypedRegistryResolution activeResolution,
        TypedRegistryResolution scalingResolution,
        long lifecycleGeneration)
    {
        ActiveActions = activeActions;
        ActiveResolution = activeResolution;
        ScalingResolution = scalingResolution;
        LifecycleGeneration = lifecycleGeneration;
    }

    public object ActiveActions { get; }
    public TypedRegistryResolution ActiveResolution { get; }
    public TypedRegistryResolution ScalingResolution { get; }
    public long LifecycleGeneration { get; }
}

internal sealed class AutoHarvestPairBinding
{
    public AutoHarvestPairBinding(
        AutoHarvestPair pair,
        object plot,
        object action,
        string plotUuid,
        string actionUuid,
        object rewardPool,
        TypedRegistryResolution plotResolution,
        TypedRegistryResolution actionResolution,
        TypedRegistryResolution rewardResolution)
    {
        Pair = pair;
        Plot = plot;
        Action = action;
        PlotUuid = plotUuid;
        ActionUuid = actionUuid;
        RewardPool = rewardPool;
        PlotResolution = plotResolution;
        ActionResolution = actionResolution;
        RewardResolution = rewardResolution;
    }

    public AutoHarvestPair Pair { get; }
    public object Plot { get; }
    public object Action { get; }
    public string PlotUuid { get; }
    public string ActionUuid { get; }
    public object RewardPool { get; }
    public TypedRegistryResolution PlotResolution { get; }
    public TypedRegistryResolution ActionResolution { get; }
    public TypedRegistryResolution RewardResolution { get; }
}

internal readonly struct ResolvedAutoHarvestPair
{
    public ResolvedAutoHarvestPair(
        AutoHarvestNativeBindings bindings,
        AutoHarvestSharedBinding shared,
        AutoHarvestPairBinding target,
        AutoHarvestPairBinding? fruit,
        AutoHarvestPairBinding? treasure)
    {
        Bindings = bindings;
        Shared = shared;
        Target = target;
        Fruit = fruit;
        Treasure = treasure;
    }

    public AutoHarvestNativeBindings Bindings { get; }
    public AutoHarvestSharedBinding Shared { get; }
    public AutoHarvestPairBinding Target { get; }
    public AutoHarvestPairBinding? Fruit { get; }
    public AutoHarvestPairBinding? Treasure { get; }
    public long LifecycleGeneration => Shared.LifecycleGeneration;
}

internal readonly struct AutoHarvestPairResolution
{
    private AutoHarvestPairResolution(
        bool succeeded,
        in ResolvedAutoHarvestPair pair,
        in AutoHarvestNativeFailure failure)
    {
        Succeeded = succeeded;
        Pair = pair;
        Failure = failure;
    }

    public bool Succeeded { get; }
    public ResolvedAutoHarvestPair Pair { get; }
    public AutoHarvestNativeFailure Failure { get; }

    public static AutoHarvestPairResolution Success(in ResolvedAutoHarvestPair pair) =>
        new(true, pair, default);

    public static AutoHarvestPairResolution Failed(in AutoHarvestNativeFailure failure)
    {
        if (!failure.IsValid)
            throw new System.ArgumentException("A typed native failure is required.", nameof(failure));
        return new AutoHarvestPairResolution(false, default, failure);
    }
}

internal readonly struct AutoHarvestResolvedPairSet
{
    public AutoHarvestResolvedPairSet(
        in AutoHarvestPairResolution fruit,
        in AutoHarvestPairResolution treasure)
    {
        Fruit = fruit;
        Treasure = treasure;
    }

    public AutoHarvestPairResolution Fruit { get; }
    public AutoHarvestPairResolution Treasure { get; }

    public static AutoHarvestResolvedPairSet Create(
        AutoHarvestNativeBindings bindings,
        AutoHarvestSharedBinding shared,
        AutoHarvestPairBinding? fruit,
        in AutoHarvestNativeFailure fruitFailure,
        AutoHarvestPairBinding? treasure,
        in AutoHarvestNativeFailure treasureFailure)
    {
        var fruitResolution = fruit is null
            ? AutoHarvestPairResolution.Failed(fruitFailure)
            : AutoHarvestPairResolution.Success(
                new ResolvedAutoHarvestPair(bindings, shared, fruit, fruit, treasure));
        var treasureResolution = treasure is null
            ? AutoHarvestPairResolution.Failed(treasureFailure)
            : AutoHarvestPairResolution.Success(
                new ResolvedAutoHarvestPair(bindings, shared, treasure, fruit, treasure));
        return new AutoHarvestResolvedPairSet(fruitResolution, treasureResolution);
    }

    public AutoHarvestPairResolution For(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => Fruit,
        AutoHarvestPair.TreasureTree => Treasure,
        _ => throw new System.ArgumentOutOfRangeException(nameof(pair)),
    };
}
